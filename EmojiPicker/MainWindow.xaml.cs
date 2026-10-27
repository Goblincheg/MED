using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfEmojiData = Emoji.Wpf.EmojiData;

namespace EmojiPicker
{
    public partial class MainWindow : Window
    {
        private const int MaxRecentEmojis = 24;
        private const string RecentCategoryKey = "Recent";
        private const string SearchHeader = "Search results";

        // Emoji cell footprint in DIPs (40x40 border + 1px margin each side);
        // used to derive the grid's current column count for keyboard nav
        private const double ItemCellWidth = 42.0;

        // How long to wait after the last keystroke before filtering, so typing
        // stays smooth instead of re-rendering the grid on every character
        private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(120);

        private static readonly string RecentEmojisFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClassicEmojiPicker",
            "recent.json");

        // Windows 10's seven picker categories, keyed by the Unicode group names
        // that Emoji.Wpf's EmojiData exposes ("Component" and "Flags" are excluded,
        // as in the original picker)
        private static readonly Dictionary<string, string> GroupToCategory = new Dictionary<string, string>
        {
            ["Smileys & Emotion"] = "Smileys",
            ["Animals & Nature"] = "Smileys",
            ["People & Body"] = "People",
            ["Activities"] = "Celebrations",
            ["Objects"] = "Celebrations",
            ["Food & Drink"] = "Food",
            ["Travel & Places"] = "Transport",
            ["Symbols"] = "Symbols",
        };

        private static readonly List<EmojiCategory> Categories = new List<EmojiCategory>
        {
            new EmojiCategory(RecentCategoryKey, "🕒", "Most recently used"),
            new EmojiCategory("Smileys", "😀", "Smiley faces and animals"),
            new EmojiCategory("People", "🧑", "People"),
            new EmojiCategory("Celebrations", "🎉", "Celebrations and objects"),
            new EmojiCategory("Food", "🍕", "Food and plants"),
            new EmojiCategory("Transport", "🚗", "Transportation and places"),
            new EmojiCategory("Symbols", "♥️", "Symbols"),
        };

        private const string DefaultCategoryKey = "Smileys";

        private readonly DispatcherTimer searchTimer;
        private List<Emoji> allEmojis = new List<Emoji>();
        private List<Emoji> recentEmojis = new List<Emoji>();
        private string currentCategory = DefaultCategoryKey;
        private bool isShowing;
        private bool recentsDirty;
        private CancellationTokenSource? insertionSession;
        private TextInjector.Target insertionTarget;
        private PickerDismissMonitor? dismissMonitor;
        private readonly InsertionQueue insertionQueue = new();
        private bool insertionRunning;
        private bool restoringPickerFocus;
        private bool queueFullReported;
        internal PickerLifecycleTrace LifecycleTrace { get; } = new();
        internal bool IsPickerOpen => insertionSession != null && !insertionSession.IsCancellationRequested && IsVisible;

        public MainWindow()
        {
            InitializeComponent();
            SourceInitialized += (_, _) =>
                HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(PickerWindowProc);
            PreviewGotKeyboardFocus += (_, e) =>
            {
                TraceLifecycle("wpf-preview-keyboard-focus");
                if (insertionRunning && !restoringPickerFocus) { e.Handled = true; }
            };
            LostFocus += (_, _) => TraceLifecycle("wpf-lost-focus");
            LostKeyboardFocus += (_, _) => TraceLifecycle("wpf-lost-keyboard-focus");
            Activated += (_, _) => TraceLifecycle("wpf-activated");
            IsVisibleChanged += (_, _) =>
            {
                TraceLifecycle(IsVisible ? "visibility-shown" : "visibility-hidden");
                if (!IsVisible) { LifecycleTrace.Finish(); }
            };
            Closing += (_, _) => TraceLifecycle("window-closing");
            Closed += (_, _) =>
            {
                TraceLifecycle("window-closed");
                LifecycleTrace.Finish();
            };
            InitializeEmojis();
            LoadRecentEmojis();

            searchTimer = new DispatcherTimer { Interval = SearchDebounce };
            searchTimer.Tick += (_, _) => RunSearch();

            CategoryTabs.ItemsSource = Categories;
            CategoryTabs.SelectedIndex = Categories.FindIndex(category => category.Key == currentCategory);
        }

        // The items panel hosting the emoji cells; cached after the first lookup.
        // Its ActualWidth is the viewport content width (scrollbar excluded),
        // which is stable regardless of scroll position or container recycling.
        private WpfToolkit.Controls.VirtualizingWrapPanel? emojiPanel;

        // Columns currently shown by the virtualizing wrap panel, derived from
        // the panel's width so Up/Down move exactly one visual row. Geometry of
        // realized containers is NOT used: recycled containers report garbage
        // positions once the grid has scrolled, which broke row navigation.
        private int ColumnsPerRow
        {
            get
            {
                if (EmojiGrid.Items.Count == 0)
                {
                    return 1;
                }

                emojiPanel ??= FindVisualChild<WpfToolkit.Controls.VirtualizingWrapPanel>(EmojiGrid);
                if (emojiPanel != null && emojiPanel.ActualWidth > 0)
                {
                    return Math.Max(1, (int)(emojiPanel.ActualWidth / ItemCellWidth));
                }

                // Before first layout: estimate from the ListBox width, allowing
                // for the 10px themed scrollbar (or the estimate lands one high)
                return Math.Max(1, (int)((EmojiGrid.ActualWidth - 12) / ItemCellWidth));
            }
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T match)
                {
                    return match;
                }

                if (FindVisualChild<T>(child) is T nested)
                {
                    return nested;
                }
            }

            return null;
        }

        /// <summary>
        /// Renders the window once off-screen at startup so the WPF visual tree and
        /// Emoji.Wpf glyph path are JIT-warmed; the first real hotkey open is then
        /// as fast as subsequent ones instead of paying a cold-start cost.
        /// </summary>
        public void PreWarm()
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -32000;
            Top = -32000;
            ShowActivated = false;
            Show();

            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    // Only hide if we're still the off-screen prewarm window. If a
                    // hotkey ShowPicker ran first (during startup), it repositioned
                    // us on-screen - don't yank it back closed.
                    if (Left <= -30000)
                    {
                        TraceLifecycle("hide-prewarm-offscreen");
                        Hide();
                    }

                    ShowActivated = true;

                    // Startup allocates heavily (emoji database, glyph warm-up);
                    // return it before settling into the tray
                    Dispatcher.BeginInvoke(new Action(MemoryTrimmer.Trim), DispatcherPriority.ContextIdle);
                }),
                DispatcherPriority.Loaded);
        }

        /// <summary>
        /// Brings the picker up ready to search, positioned near the cursor, and
        /// takes foreground. The window is reused across hotkey presses rather
        /// than recreated, so this resets it to a clean state each time.
        /// </summary>
        public void ShowPicker()
        {
            if (IsPickerOpen)
            {
                TraceLifecycle("show-ignored-already-open");
                return;
            }
            LifecycleTrace.Start();
            TraceLifecycle("show-begin diagnostic=MED-4.3-v1");
            insertionSession?.Dispose();
            insertionSession = new CancellationTokenSource();
            insertionTarget = TextInjector.Target.Capture(App.PreviousForegroundWindow, App.PreviousFocusWindow);
            // Ignore focus-loss triggered while we are bringing the window up
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            isShowing = true;

            SearchBox.Clear();

            // Open on Recent when there is history (like the Windows 10 picker),
            // otherwise the first content tab
            var openKey = recentEmojis.Count > 0 ? RecentCategoryKey : DefaultCategoryKey;
            var openIndex = Categories.FindIndex(category => category.Key == openKey);
            if (CategoryTabs.SelectedIndex == openIndex)
            {
                LoadCategory(openKey); // no SelectionChanged will fire; refresh directly
            }
            else
            {
                CategoryTabs.SelectedIndex = openIndex; // fires SelectionChanged -> LoadCategory
            }

            PositionNearCursor();

            Show();
            EnsureOnScreen();
            Activate();
            var handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
            {
                dismissMonitor?.Dispose();
                try
                {
                    dismissMonitor = new PickerDismissMonitor(handle, RequestDismiss,
                        () => Dispatcher.BeginInvoke(new Action(CommitSelectedEmoji)), () => insertionRunning, TraceLifecycle);
                }
                catch (Exception ex)
                {
                    Logger.LogAlways($"Picker input monitor failed: {ex}");
                    DismissPicker("input-monitor-failure");
                    isShowing = false;
                    ((App)Application.Current).ReportInsertionFailure();
                    return;
                }
                TraceLifecycle("show-focus-request");
                ForceForeground(handle);
                TraceLifecycle("show-focus-return");
            }

            SearchBox.Focus();
            Keyboard.Focus(SearchBox);

            Logger.Log($"ShowPicker done in {stopwatch.ElapsedMilliseconds}ms: Left={Left:F0} Top={Top:F0} " +
                $"W={Width} H={Height} foreground={NativeMethods.GetForegroundWindow()} thisHwnd={handle}");

            // Clear the guard once the show/activation storm has settled
            Dispatcher.BeginInvoke(new Action(() =>
            {
                isShowing = false;
                TraceLifecycle("show-guard-cleared");
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>
        /// Brings our window to the foreground even though the hotkey fired from
        /// another app. A background process can't normally steal focus, so we
        /// briefly attach to the current foreground thread's input queue.
        /// </summary>
        private static void ForceForeground(IntPtr hwnd)
        {
            var foregroundThread = NativeMethods.GetWindowThreadProcessId(NativeMethods.GetForegroundWindow(), out _);
            var thisThread = NativeMethods.GetCurrentThreadId();

            if (foregroundThread != thisThread && foregroundThread != 0)
            {
                NativeMethods.AttachThreadInput(foregroundThread, thisThread, true);
                NativeMethods.SetForegroundWindow(hwnd);
                NativeMethods.AttachThreadInput(foregroundThread, thisThread, false);
            }
            else
            {
                NativeMethods.SetForegroundWindow(hwnd);
            }
        }

        private void PositionNearCursor()
        {
            // Anchor at the target app's text caret when it exposed one at hotkey
            // time (like the Windows 10 panel); otherwise at the mouse pointer.
            // The anchor is a small rectangle: the picker opens below its bottom
            // edge, or above its top edge when there's no room below.
            int anchorX, anchorTop, anchorBottom;
            string anchor;
            if (App.PreviousCaretRect is System.Drawing.Rectangle caret)
            {
                anchorX = caret.Left;
                anchorTop = caret.Top;
                anchorBottom = caret.Bottom;
                anchor = "caret";
            }
            else if (NativeMethods.GetCursorPos(out var cursor))
            {
                anchorX = cursor.X;
                anchorTop = cursor.Y;
                anchorBottom = cursor.Y;
                anchor = "mouse";
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
                return;
            }

            // The anchor and Screen.WorkingArea are in physical pixels, but WPF's
            // Left/Top are in device-independent units. Convert with the window's DPI
            // scale, or the panel lands off-screen on scaled/high-DPI displays.
            var hwnd = new WindowInteropHelper(this).EnsureHandle();
            double scale = NativeMethods.GetDpiForWindow(hwnd) / 96.0;
            if (scale <= 0)
            {
                scale = 1.0;
            }

            var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(anchorX, anchorBottom));
            var area = screen.WorkingArea;

            // Work in physical pixels, then convert the top-left to DIPs for WPF.
            var physicalWidth = Width * scale;
            var physicalHeight = Height * scale;
            const int gap = 8;

            // Horizontal: align the picker's left edge with the anchor, clamped
            // within the anchor's monitor.
            double left = Math.Max(area.Left, Math.Min(anchorX + gap, area.Right - physicalWidth));

            // Vertical: prefer below the anchor; if it won't fit (e.g. the caret is
            // near the bottom of the screen), open *above* it like the Windows 10
            // picker; otherwise clamp within the monitor.
            double top;
            string vplace;
            if (anchorBottom + gap + physicalHeight <= area.Bottom)
            {
                top = anchorBottom + gap;
                vplace = "below";
            }
            else if (anchorTop - gap - physicalHeight >= area.Top)
            {
                top = anchorTop - gap - physicalHeight;
                vplace = "above";
            }
            else
            {
                top = Math.Max(area.Top, Math.Min(anchorBottom + gap, area.Bottom - physicalHeight));
                vplace = "clamped";
            }

            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left / scale;
            Top = top / scale;

            Logger.Log($"PositionNearCursor: anchor={anchor}({anchorX},{anchorTop}-{anchorBottom}) scale={scale} vplace={vplace} " +
                $"area=[{area.Left},{area.Top},{area.Right},{area.Bottom}] => DIP Left={Left:F0} Top={Top:F0}");
        }

        /// <summary>
        /// Last line of defence against the window opening off-screen (e.g. mixed-DPI
        /// monitors): if its bounds fall outside every display, recentre on the primary.
        /// </summary>
        private void EnsureOnScreen()
        {
            double vsLeft = SystemParameters.VirtualScreenLeft;
            double vsTop = SystemParameters.VirtualScreenTop;
            double vsRight = vsLeft + SystemParameters.VirtualScreenWidth;
            double vsBottom = vsTop + SystemParameters.VirtualScreenHeight;

            bool onScreen = Left < vsRight && Left + Width > vsLeft && Top < vsBottom && Top + Height > vsTop;
            if (!onScreen)
            {
                var work = SystemParameters.WorkArea; // primary monitor, in DIPs
                Left = work.Left + ((work.Width - Width) / 2);
                Top = work.Top + ((work.Height - Height) / 2);
                Logger.Log($"EnsureOnScreen: off-screen, recentred to Left={Left:F0} Top={Top:F0}");
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // The Windows 10 picker is ready to search the moment it opens
            SearchBox.Focus();
            Keyboard.Focus(SearchBox);
        }

        private void MainWindow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // DragMove can activate the picker as well; choices during a transfer
            // must leave the recipient in control of keyboard focus.
            if (insertionRunning) { return; }
            // Allow dragging the window when clicking anywhere on it
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        // Popularity tier assigned to emoji absent from the frequency data
        // (rarely used, or newer than the dataset and not in its supplement)
        private const int UnrankedPopularity = 99;

        private void InitializeEmojis()
        {
            // Build the full emoji set from the Unicode database that ships
            // inside Emoji.Wpf, mapped onto the Windows 10 categories
            var keywords = LoadResourceMap<string>("keywords.json");
            var russian = LoadResourceMap<RussianAnnotation>("keywords.ru.json");
            var popularity = LoadResourceMap<int>("popularity.json");
            allEmojis = new List<Emoji>();
            foreach (var group in WpfEmojiData.AllGroups)
            {
                if (!GroupToCategory.TryGetValue(group.Name, out var categoryKey))
                {
                    continue;
                }

                foreach (var subGroup in group.SubGroups)
                {
                    // Windows 10 filed plants under "Food and plants", not with animals
                    var key = subGroup.Name.StartsWith("plant-", StringComparison.Ordinal) ? "Food" : categoryKey;

                    foreach (var emoji in subGroup.EmojiList)
                    {
                        if (emoji.Renderable)
                        {
                            var normalized = NormalizeEmoji(emoji.Text);
                            keywords.TryGetValue(normalized, out var tags);
                            russian.TryGetValue(normalized, out var russianAnnotation);
                            var rank = popularity.TryGetValue(normalized, out var tier) ? tier : UnrankedPopularity;
                            allEmojis.Add(new Emoji(emoji.Text, emoji.Name, key, tags ?? string.Empty, rank,
                                russianAnnotation?.Name ?? string.Empty, russianAnnotation?.Keywords));
                        }
                    }
                }
            }
        }

        // Emoji.Wpf and the keyword data can differ on the FE0F variation selector;
        // strip it so lookups line up
        private static string NormalizeEmoji(string text) => text.Replace("\uFE0F", string.Empty);

        private static Dictionary<string, T> LoadResourceMap<T>(string fileName)
        {
            try
            {
                var uri = new Uri($"pack://application:,,,/EmojiPicker;component/Resources/{fileName}");
                using var stream = Application.GetResourceStream(uri)?.Stream;
                if (stream != null)
                {
                    var map = JsonSerializer.Deserialize<Dictionary<string, T>>(stream);
                    if (map != null)
                    {
                        return map;
                    }
                }
            }
            catch (Exception)
            {
                // Search still works (on names, in category order) without the data
            }

            return new Dictionary<string, T>();
        }

        private void LoadCategory(string categoryKey)
        {
            if (EmojiGrid == null)
            {
                return; // UI not ready yet
            }

            List<Emoji> categoryEmojis = categoryKey == RecentCategoryKey
                ? recentEmojis.ToList()
                : allEmojis.Where(emoji => emoji.Category == categoryKey).ToList();

            Logger.Log($"LoadCategory '{categoryKey}' -> {categoryEmojis.Count} items");
            CategoryHeader.Text = Categories.FirstOrDefault(category => category.Key == categoryKey)?.DisplayName ?? categoryKey;
            ShowEmojis(categoryEmojis);
        }

        private void ShowEmojis(List<Emoji> emojis)
        {
            EmojiGrid.ItemsSource = emojis;
            EmojiGrid.SelectedIndex = emojis.Count > 0 ? 0 : -1;
            if (EmojiGrid.SelectedItem != null)
            {
                EmojiGrid.ScrollIntoView(EmojiGrid.SelectedItem);
            }

            if (Logger.Enabled)
            {
                // Confirm virtualization: realized containers should stay ~visible-only
                Dispatcher.BeginInvoke(
                    new Action(() =>
                    {
                        var realized = 0;
                        for (var i = 0; i < EmojiGrid.Items.Count; i++)
                        {
                            if (EmojiGrid.ItemContainerGenerator.ContainerFromIndex(i) != null)
                            {
                                realized++;
                            }
                        }

                        Logger.Log($"Grid realized {realized}/{EmojiGrid.Items.Count} containers");
                    }),
                    DispatcherPriority.Loaded);
            }
        }

        private void CategoryTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CategoryTabs.SelectedItem is EmojiCategory category)
            {
                Logger.Log($"TabSelectionChanged -> {category.Key}");
                currentCategory = category.Key;
                if (string.IsNullOrEmpty(SearchBox.Text))
                {
                    LoadCategory(category.Key);
                }
                else
                {
                    SearchBox.Clear(); // triggers TextChanged, which loads the category
                }

                // Typing must keep working after a tab is clicked
                SearchBox.Focus();
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (EmojiGrid == null)
            {
                return; // UI not ready yet
            }

            searchTimer.Stop();
            if (string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                // Clearing the box should restore the category instantly
                LoadCategory(currentCategory);
            }
            else
            {
                // Debounce: filter once typing pauses, not on every keystroke
                searchTimer.Start();
            }
        }

        private void RunSearch()
        {
            searchTimer.Stop();
            if (EmojiGrid == null || string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                return;
            }

            // Trim so an accidental trailing space doesn't zero out the results
            var searchText = SearchBox.Text.Trim();
            if (searchText.Length == 0)
            {
                return;
            }

            var filteredEmojis = EmojiSearch.Find(allEmojis, searchText);
            Logger.Log($"Search '{searchText}' -> {filteredEmojis.Count} results");
            CategoryHeader.Text = SearchHeader;
            ShowEmojis(filteredEmojis);
        }
        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // While an IME is composing (CJK etc.), Enter commits the candidate and
            // the arrows move the candidate window - let the IME have those keys
            // instead of hijacking them for grid nav / emoji commit.
            if (e.Key == Key.ImeProcessed || e.Key == Key.DeadCharProcessed)
            {
                return;
            }

            // Win10 picker behaviour: typing searches while the arrow keys move
            // the grid selection and Enter commits it, wherever focus happens to be
            switch (e.Key)
            {
                case Key.Enter:
                    // Apply any pending debounced search so the selection is current
                    if (searchTimer.IsEnabled)
                    {
                        RunSearch();
                    }

                    CommitSelectedEmoji();
                    e.Handled = true;
                    break;
                case Key.Tab:
                    // Cycle categories from the keyboard (the tab strip isn't
                    // reliably clickable when the picker isn't the active window)
                    SwitchCategory((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? -1 : 1);
                    e.Handled = true;
                    break;
                case Key.Left:
                case Key.Right:
                    // Plain arrows browse the emoji grid (focus stays in the
                    // search box). With Ctrl and/or Shift they are text editing
                    // - word jump and character/word selection - so leave them
                    // unhandled and let the search box do its normal thing.
                    if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0)
                    {
                        break;
                    }

                    MoveSelection(e.Key == Key.Left ? -1 : 1);
                    e.Handled = true;
                    break;
                case Key.Up:
                    MoveSelection(-ColumnsPerRow);
                    e.Handled = true;
                    break;
                case Key.Down:
                    MoveSelection(ColumnsPerRow);
                    e.Handled = true;
                    break;
            }
        }

        private void SwitchCategory(int direction)
        {
            var count = Categories.Count;
            if (count == 0)
            {
                return;
            }

            // Wrap around; SelectionChanged loads the category and refocuses search
            var next = (((CategoryTabs.SelectedIndex + direction) % count) + count) % count;
            CategoryTabs.SelectedIndex = next;
        }

        private void MoveSelection(int delta)
        {
            if (EmojiGrid.Items.Count == 0)
            {
                return;
            }

            var index = EmojiGrid.SelectedIndex < 0 ? 0 : EmojiGrid.SelectedIndex + delta;
            EmojiGrid.SelectedIndex = Math.Clamp(index, 0, EmojiGrid.Items.Count - 1);
            EmojiGrid.ScrollIntoView(EmojiGrid.SelectedItem);
            Logger.Log($"MoveSelection delta={delta} (columns={ColumnsPerRow}) -> index {EmojiGrid.SelectedIndex}");
        }

        private void CommitSelectedEmoji()
        {
            if (searchTimer.IsEnabled) { RunSearch(); }
            if (EmojiGrid.SelectedItem is Emoji emoji)
            {
                CommitEmoji(emoji);
            }
        }

        private void EmojiItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBoxItem { DataContext: Emoji emoji })
            {
                e.Handled = true;
                CommitEmoji(emoji);
            }
        }

        private void TabItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Select the category on mouse-up (like emoji commit), which lands even
            // when the picker only has attached-input focus rather than full activation
            if (sender is ListBoxItem { DataContext: EmojiCategory category })
            {
                e.Handled = true;
                var index = Categories.FindIndex(item => item.Key == category.Key);
                if (index >= 0)
                {
                    CategoryTabs.SelectedIndex = index;
                }
            }
        }

        private void CommitEmoji(Emoji emoji)
        {
            if (!IsPickerOpen || insertionSession == null) { return; }
            if (!insertionQueue.TryEnqueue(new InsertionQueue.Request(emoji, insertionTarget, insertionSession.Token)))
            {
                if (!queueFullReported)
                {
                    queueFullReported = true;
                    ((App)Application.Current).ReportInsertionQueueFull();
                }
                return;
            }
            TraceLifecycle("choice-enqueued");
            if (!insertionRunning)
            {
                insertionRunning = true;
                // Finish the current mouse/key handler before injecting input.
                Dispatcher.BeginInvoke(new Action(async () => await ProcessInsertionsAsync()), DispatcherPriority.Background);
            }
        }

        private async Task ProcessInsertionsAsync()
        {
            if (insertionQueue.IsDraining) { return; }
            TraceLifecycle("drain-begin");
            try
            {
                await insertionQueue.DrainAsync(async request =>
                {
                    var originSession = LifecycleTrace.Session;
                    TraceLifecycle($"insert-begin requestTarget={request.Target.Window}");
                    var inserted = false;
                    try
                    {
                        var handle = new WindowInteropHelper(this).Handle;
                        var foreground = NativeMethods.GetForegroundWindow();
                        var state = ForegroundPolicy.Classify(foreground, handle, request.Target.Window);
                        TraceLifecycle($"insert-preflight state={state} observedForeground={foreground}");
                        // A queued choice cannot take input away from an unrelated app.
                        // Unknown permits only a focus-restoration attempt; the
                        // injector still requires a confirmed recipient before input.
                        if (ForegroundPolicy.MayRestoreTarget(state))
                        {
                            inserted = await TextInjector.TryInsertAsync(request.Target, request.Emoji.Character, request.Token,
                                metadata => TraceLifecycle($"insert-origin={originSession} requestTarget={request.Target.Window} {metadata}"));
                        }
                        else { TraceLifecycle("insert-preflight-foreign-foreground"); }
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex) { Logger.LogAlways($"Insert threw: {ex}"); }

                    if (inserted) { AddToRecentEmojis(request.Emoji); }
                    TraceLifecycle($"insert-end accepted={inserted} requestCancelled={request.Token.IsCancellationRequested}");
                    if (request.Token.IsCancellationRequested || !IsPickerOpen) { return; }

                    var pickerHandle = new WindowInteropHelper(this).Handle;
                    var active = NativeMethods.GetForegroundWindow();
                    var postState = ForegroundPolicy.Classify(active, pickerHandle, request.Target.Window);
                    TraceLifecycle($"insert-postcheck state={postState} observedForeground={active}");
                    if (ForegroundPolicy.ShouldCloseAfterInsert(postState))
                    {
                        DismissPicker("insert-postcheck-foreign-foreground");
                        return;
                    }
                    restoringPickerFocus = true;
                    try
                    {
                        TraceLifecycle("programmatic-picker-focus-begin");
                        ForceForeground(pickerHandle);
                        SearchBox.Focus();
                        Keyboard.Focus(SearchBox);
                        TraceLifecycle("programmatic-picker-focus-end");
                    }
                    finally { restoringPickerFocus = false; }
                    if (!inserted)
                    {
                        insertionQueue.Clear();
                        ((App)Application.Current).ReportInsertionFailure();
                    }
                });
            }
            finally
            {
                insertionRunning = false;
                queueFullReported = false;
                TraceLifecycle("drain-finally");
                var finalForeground = NativeMethods.GetForegroundWindow();
                var finalState = ForegroundPolicy.Classify(finalForeground,
                    new WindowInteropHelper(this).Handle, insertionTarget.Window);
                TraceLifecycle($"drain-finally-check state={finalState} observedForeground={finalForeground}");
                if (IsPickerOpen && ForegroundPolicy.ShouldCloseAfterInsert(finalState))
                {
                    DismissPicker("drain-finally-not-foreground");
                }
                if (!IsVisible && recentsDirty)
                {
                    SaveRecentEmojis();
                    recentsDirty = false;
                }
            }
        }

        private void RequestDismiss(string reason)
        {
            // Hook callbacks do only cancellation and dispatch, never file I/O or UI.
            TraceLifecycle($"dismiss-request reason={reason}");
            var originSession = LifecycleTrace.Session;
            insertionSession?.Cancel();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                TraceLifecycle($"dismiss-dispatch origin={originSession}");
                DismissPicker(reason);
            }), DispatcherPriority.Send);
        }

        private void TraceLifecycle(string eventCode)
        {
            if (!LifecycleTrace.Active) { return; }
            LifecycleTrace.Record($"{eventCode} pid={Environment.ProcessId} " +
                $"picker={new WindowInteropHelper(this).Handle} target={insertionTarget.Window} " +
                $"foreground={NativeMethods.GetForegroundWindow()} visible={IsVisible} active={IsActive} " +
                $"showing={isShowing} running={insertionRunning} draining={insertionQueue.IsDraining} " +
                $"restoring={restoringPickerFocus} pending={insertionQueue.Count} " +
                $"cancelled={insertionSession?.IsCancellationRequested}");
        }

        private IntPtr PickerWindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WmMouseActivate = 0x0021;
            const int MaNoActivate = 3; // deliver the click, but don't steal target focus
            if (message == WmMouseActivate && insertionRunning && !restoringPickerFocus)
            {
                TraceLifecycle("wm-mouseactivate-noactivate");
                handled = true;
                return new IntPtr(MaNoActivate);
            }
            if (message == WmMouseActivate) { TraceLifecycle("wm-mouseactivate-normal"); }
            return IntPtr.Zero;
        }
        /// <summary>
        /// Hides the resident picker (it is reused on the next hotkey press)
        /// and persists the recents list. Public so the hotkey can toggle the
        /// picker closed when pressed while it is already open.
        /// </summary>
        public void DismissPicker(string reason = "explicit-dismiss")
        {
            TraceLifecycle($"dismiss reason={reason}");
            insertionSession?.Cancel();
            insertionQueue.Clear();
            dismissMonitor?.Dispose();
            dismissMonitor = null;
            searchTimer.Stop(); // no point filtering a hidden grid
            if (recentsDirty)
            {
                SaveRecentEmojis();
                recentsDirty = false;
            }

            TraceLifecycle($"hide-request reason={reason}");
            Hide();

            // Give the memory back while we idle in the tray; ContextIdle runs
            // after the hide (and any pending insertion) has fully settled
            Dispatcher.BeginInvoke(new Action(MemoryTrimmer.Trim), DispatcherPriority.ContextIdle);
        }

        private void MainWindow_Deactivated(object sender, EventArgs e)
        {
            TraceLifecycle("wpf-deactivated");
            // Ignore the transient deactivation that can occur while we are still
            // bringing the window to the foreground, or it would hide immediately
            if (isShowing)
            {
                Logger.Log("Deactivated ignored (still showing)");
                return;
            }

            if (insertionRunning && IsPickerOpen)
            {
                // WM_ACTIVATE can arrive before the foreground transition has
                // finished. Check after the activation messages, not inside them.
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    TraceLifecycle("deactivated-deferred-check");
                    var foreground = NativeMethods.GetForegroundWindow();
                    var state = ForegroundPolicy.Classify(foreground,
                        new WindowInteropHelper(this).Handle, insertionTarget.Window);
                    TraceLifecycle($"deactivated-deferred-state state={state} observedForeground={foreground}");
                    if (IsPickerOpen && ForegroundPolicy.ShouldCloseAfterInsert(state))
                    {
                        DismissPicker("deactivated-deferred-foreign-foreground");
                    }
                }), DispatcherPriority.Input);
                return;
            }

            var idleForeground = NativeMethods.GetForegroundWindow();
            var idleState = ForegroundPolicy.Classify(idleForeground,
                new WindowInteropHelper(this).Handle, insertionTarget.Window);
            TraceLifecycle($"deactivated-idle-state state={idleState} observedForeground={idleForeground}");
            if (ForegroundPolicy.ShouldCloseWhenIdle(idleState))
            {
                Logger.Log("Deactivated -> dismiss");
                DismissPicker("deactivated-idle");
            }
        }

        private void AddToRecentEmojis(Emoji emoji)
        {
            // Remove if already exists
            recentEmojis.RemoveAll(item => item.Character == emoji.Character);

            // Add to beginning
            recentEmojis.Insert(0, emoji);

            // Keep only the most recent MaxRecentEmojis
            if (recentEmojis.Count > MaxRecentEmojis)
            {
                recentEmojis.RemoveAt(recentEmojis.Count - 1);
            }

            recentsDirty = true;
            Logger.Log($"AddToRecent '{emoji.Character}' -> recents now {recentEmojis.Count}");
        }

        private void LoadRecentEmojis()
        {
            try
            {
                if (!File.Exists(RecentEmojisFile))
                {
                    return;
                }

                var characters = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(RecentEmojisFile));
                if (characters == null)
                {
                    return;
                }

                recentEmojis = characters
                    .Distinct() // a corrupt/legacy file could contain duplicates
                    .Select(character => allEmojis.FirstOrDefault(item => item.Character == character))
                    .OfType<Emoji>()
                    .Take(MaxRecentEmojis)
                    .ToList();
            }
            catch (Exception)
            {
                // A corrupt or unreadable recents file should never stop the app from starting
                recentEmojis = new List<Emoji>();
            }
        }

        private void SaveRecentEmojis()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(RecentEmojisFile)!);
                var json = JsonSerializer.Serialize(recentEmojis.Select(item => item.Character));

                // Write to a temp file then atomically swap it in, so a crash or
                // power loss mid-write can't leave a truncated recent.json
                var tmp = RecentEmojisFile + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(RecentEmojisFile))
                {
                    File.Replace(tmp, RecentEmojisFile, null);
                }
                else
                {
                    File.Move(tmp, RecentEmojisFile);
                }
            }
            catch (Exception)
            {
                // Losing the recents list is not worth interrupting the user for
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DismissPicker("close-button");
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                DismissPicker("escape-wpf");

                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

    }

    public class Emoji
    {
        public string Character { get; }
        public string Name { get; }
        public string Category { get; }

        /// <summary>Extra search terms (emojibase tags), e.g. "splash" for 💦.</summary>
        public string Keywords { get; }
        public string RussianName { get; }
        public string[] RussianKeywords { get; }

        /// <summary>Usage-popularity tier from Unicode's frequency data
        /// (0 = most used); unranked emoji get a large sentinel value.</summary>
        public int Popularity { get; }

        public Emoji(string character, string name, string category, string keywords, int popularity,
            string russianName = "", string[]? russianKeywords = null)
        {
            Character = character;
            Name = name;
            Category = category;
            Keywords = keywords;
            Popularity = popularity;
            RussianName = russianName;
            RussianKeywords = russianKeywords ?? Array.Empty<string>();
        }

        // Shown by UI Automation / screen readers for the grid items
        public override string ToString() => Name;
    }

    public class EmojiCategory
    {
        public string Key { get; }
        public string Icon { get; }
        public string DisplayName { get; }

        public EmojiCategory(string key, string icon, string displayName)
        {
            Key = key;
            Icon = icon;
            DisplayName = displayName;
        }
    }
}
