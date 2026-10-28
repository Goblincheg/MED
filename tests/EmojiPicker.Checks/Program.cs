using System.Reflection;
using System.Text.Json;
using System.Windows;
using EmojiPicker;

internal static class Program
{
    private static int checks;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length >= 3 && args[0] == "--startup-check") { return ReleaseStartupChecks.Run(args); }
        try
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; // never invoke MED's startup or global hooks
            ReleaseChecks.Run(Check); // isolated user-data roots; fake autostart store only
            Emoji.Wpf.EmojiData.Load();
            var russian = Resource<RussianAnnotation>("keywords.ru.json");
            var keywords = Resource<string>("keywords.json");
            var popularity = Resource<int>("popularity.json");
            Check(russian.Count == 4348, "CLDR and six original cat names embedded");
            var groups = new HashSet<string> { "Smileys & Emotion", "Animals & Nature", "People & Body", "Activities", "Objects", "Food & Drink", "Travel & Places", "Symbols" };
            var catalog = Emoji.Wpf.EmojiData.AllGroups.Where(group => groups.Contains(group.Name))
                .SelectMany(group => group.SubGroups).SelectMany(group => group.EmojiList).ToList();
            var missing = catalog.Where(item => !russian.ContainsKey(Key(item.Text))).ToList();
            Console.WriteLine($"Catalog: {catalog.Count}; Russian coverage: {catalog.Count - missing.Count}/{catalog.Count}");
            foreach (var item in missing) { Console.WriteLine($"MISSING: {item.Text} {item.Name}"); }
            Check(missing.Count == 0, "full catalog has Russian annotations");
            var emojis = catalog.Where(item => FluentEmojiImages.HasImage(item.Text) || item.Renderable).Select(item =>
            {
                var key = Key(item.Text);
                russian.TryGetValue(key, out var ru);
                return new EmojiPicker.Emoji(item.Text, item.Name, "", keywords.GetValueOrDefault(key, ""),
                    popularity.GetValueOrDefault(key, 99), ru?.Name ?? "", ru?.Keywords);
            }).ToList();
            Console.WriteLine($"System renderable: {catalog.Count(item => item.Renderable)}; displayable: {emojis.Count}");
            InsertionStressChecks.RunAsync(emojis, Check).GetAwaiter().GetResult();
            ForegroundTransitionChecks.RunAsync(emojis[0], Check).GetAwaiter().GetResult();

            var queries = new Dictionary<string, string[]>
            {
                ["утка"] = ["🦆"],
                ["уточка"] = ["🦆"],
                ["сердце"] = ["❤"],
                ["любовь"] = ["❤"],
                ["смех"] = ["🤣", "😂"],
                ["ржу"] = ["🤣", "😂"],
                ["кот"] = ["🐈", "🐱"],
                ["кошка"] = ["🐈", "🐱"],
                ["огонь"] = ["🔥"],
                ["праздник"] = ["🎉"],
                ["палец вверх"] = ["👍"],
                ["smile"] = ["😀"],
                ["heart"] = ["❤"],
                ["splash"] = ["💦"],
                ["кофе"] = ["☕"],
                ["ракета"] = ["🚀"],
                ["телефон"] = ["📱"],
                ["яблоко"] = ["🍎"],
                ["собака"] = ["🐕"],
                ["солнце"] = ["☀"]
            };
            foreach (var (query, expected) in queries)
            {
                var results = EmojiSearch.Find(emojis, query);
                foreach (var character in expected)
                {
                    Check(results.Any(item => Key(item.Character) == character), $"{query} -> {character}");
                }
                Console.WriteLine($"{query}: {results.Count}; top: {string.Join(" ", results.Take(3).Select(item => item.Character))}");
            }
            Check(EmojiSearch.Find(emojis, "   ").Count == 0, "blank query");
            Check(EmojiSearch.Find(emojis, "неттакогоэмодзитест").Count == 0, "unknown query");
            Check(SameResults(emojis, "УТОЧКА", "уточка"), "case-insensitive Russian");
            Check(SameResults(emojis, "SMILE", "smile"), "case-insensitive English");
            Check(SameResults(emojis, "ёж", "еж"), "yo/ye normalisation");
            Check(SameResults(emojis, " ПАЛЕЦ   ВВЕРХ ", "палец вверх"), "phrase whitespace");
            Check(EmojiSearch.Find(emojis, "уточка").First().Character == "🦆", "exact alias rank");

            var rankFixture = new List<EmojiPicker.Emoji>
            {
                new("a", "midwordsmile", "", "", 0),
                new("b", "smiley", "", "", 1),
                new("c", "smile", "", "", 99),
                new("d", "other", "", "smiling", 0),
                new("e", "smiley too", "", "", 1)
            };
            Check(string.Concat(EmojiSearch.Find(rankFixture, "smile").Select(item => item.Character)) == "cbea", "exact/word-start/substring priority and stable ties");
            Check(string.Concat(EmojiSearch.Find(rankFixture, "smil").Select(item => item.Character)) == "bedca", "original name/keyword penalty and popularity");

            // Instantiate actual compiled XAML, without showing or taking focus.
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/MED;component/Theme/LightTheme.xaml")
            });
            ReleaseChecks.History(Check);
            var window = new MainWindow();
            var loaded = (List<EmojiPicker.Emoji>)typeof(MainWindow).GetField("allEmojis", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
            Check(loaded.Count == emojis.Count, "compiled window catalog");
            Check(EmojiSearch.Find(loaded, "уточка").Any(item => item.Character == "🦆"), "compiled window uses Russian resource");
            Check(!window.IsPickerOpen, "no insertion session before open");
            FluentGraphicsChecks.Run(window, loaded, Check);
            typeof(MainWindow).GetMethod("CommitEmoji", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [loaded[0]]);
            Check(!window.IsVisible, "hidden picker ignores stale commits");

            Check(!default(TextInjector.Target).IsValid(), "missing target rejected");
            Check(!TextInjector.TryInsertAsync(default, "😀", CancellationToken.None).GetAwaiter().GetResult(), "missing target sends no input");
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            try
            {
                TextInjector.TryInsertAsync(default, "😀", cancelled.Token).GetAwaiter().GetResult();
                throw new Exception("Cancelled insertion was not rejected");
            }
            catch (OperationCanceledException) { checks++; }
            var hwnd = new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
            Check(!TextInjector.Target.Capture(hwnd, hwnd).IsValid(), "picker/own process rejected as recipient");

            // Off-screen, non-activating window: exercise real handlers and queue
            // cancellation without sending keystrokes or modifying the clipboard.
            window.ShowActivated = false;
            window.Left = -32000;
            window.Top = -32000;
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var sessionField = typeof(MainWindow).GetField("insertionSession", flags)!;
            var queue = (InsertionQueue)
                typeof(MainWindow).GetField("insertionQueue", flags)!.GetValue(window)!;
            var commit = typeof(MainWindow).GetMethod("CommitEmoji", flags)!;
            window.Show();
            using var firstSession = new CancellationTokenSource();
            sessionField.SetValue(window, firstSession);
            var laughing = loaded.First(item => item.Character == "🤣");
            for (var i = 0; i < 50; i++) { commit.Invoke(window, [laughing]); }
            Check(window.IsVisible && window.IsPickerOpen, "50 choices keep panel open");
            Check(string.Concat(queue.Select(item => item.Emoji.Character)) == string.Concat(Enumerable.Repeat("🤣", 50)), "50 repeated choices queued in order by real commit handler");
            var windowProc = typeof(MainWindow).GetMethod("PickerWindowProc", flags)!;
            object[] activateArgs = [hwnd, 0x0021, IntPtr.Zero, IntPtr.Zero, false];
            Check((IntPtr)windowProc.Invoke(window, activateArgs)! == new IntPtr(3) && (bool)activateArgs[4],
                "clicks during insertion use MA_NOACTIVATE without discarding the click");
            var searchFocus = (System.Windows.Controls.TextBox)window.FindName("SearchBox");
            var mouseFocusAttempt = new System.Windows.Input.KeyboardFocusChangedEventArgs(
                System.Windows.Input.Keyboard.PrimaryDevice, Environment.TickCount, null, searchFocus)
            {
                RoutedEvent = System.Windows.Input.Keyboard.PreviewGotKeyboardFocusEvent
            };
            window.RaiseEvent(mouseFocusAttempt);
            Check(mouseFocusAttempt.Handled, "WPF focus attempts cannot steal recipient focus during insertion");
            typeof(MainWindow).GetField("restoringPickerFocus", flags)!.SetValue(window, true);
            object[] restoreArgs = [hwnd, 0x0021, IntPtr.Zero, IntPtr.Zero, false];
            Check((IntPtr)windowProc.Invoke(window, restoreArgs)! == IntPtr.Zero && !(bool)restoreArgs[4],
                "intentional focus return is not suppressed");
            typeof(MainWindow).GetField("restoringPickerFocus", flags)!.SetValue(window, false);
            window.ShowPicker();
            Check(queue.Count == 50 && ReferenceEquals(sessionField.GetValue(window), firstSession), "opening an already-open picker preserves its single session");
            var foreground = NativeMethods.GetForegroundWindow();
            typeof(MainWindow).GetField("insertionTarget", flags)!.SetValue(window, new TextInjector.Target(foreground, IntPtr.Zero, 0, 0));
            typeof(MainWindow).GetMethod("MainWindow_Deactivated", flags)!.Invoke(window, [window, EventArgs.Empty]);
            Check(window.IsVisible && !firstSession.IsCancellationRequested, "programmatic deactivation to target does not close picker");
            typeof(MainWindow).GetMethod("RequestDismiss", flags)!.Invoke(window, ["outside-click"]);
            Check(firstSession.IsCancellationRequested, "outside click cancels pending input immediately");
            window.DismissPicker();
            Check(queue.Count == 0 && !window.IsVisible, "dismiss clears queue and hides panel");
            ((Task)typeof(MainWindow).GetMethod("ProcessInsertionsAsync", flags)!.Invoke(window, null)!).GetAwaiter().GetResult();
            object[] idleArgs = [hwnd, 0x0021, IntPtr.Zero, IntPtr.Zero, false];
            Check((IntPtr)windowProc.Invoke(window, idleArgs)! == IntPtr.Zero && !(bool)idleArgs[4],
                "idle picker retains normal mouse activation");

            window.Show();
            using var secondSession = new CancellationTokenSource();
            sessionField.SetValue(window, secondSession);
            foreach (var character in new[] { "😀", "😊", "❤️", "🦆", "👩‍💻" })
            {
                var item = loaded.First(emoji => Key(emoji.Character) == Key(character));
                commit.Invoke(window, [item]);
            }
            Check(string.Concat(queue.Select(item => item.Emoji.Character)) == "😀😊❤️🦆👩‍💻", "different choices and ZWJ remain intact in queue");
            var searchBox = (System.Windows.Controls.TextBox)window.FindName("SearchBox");
            searchBox.Text = "утка";
            var escape = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                PresentationSource.FromVisual(window), Environment.TickCount, System.Windows.Input.Key.Escape)
            {
                RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent
            };
            typeof(MainWindow).GetMethod("OnKeyDown", flags)!.Invoke(window, [escape]);
            Check(!window.IsVisible && secondSession.IsCancellationRequested && queue.Count == 0, "Escape closes with active search and cancels queued choices");
            ((Task)typeof(MainWindow).GetMethod("ProcessInsertionsAsync", flags)!.Invoke(window, null)!).GetAwaiter().GetResult();
            LifecycleTraceChecks.Run(window, loaded, Check);
            window.Close();
            Console.WriteLine($"PASS: {checks} checks");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static string Key(string value) => value.Replace("\uFE0F", "");
    private static bool SameResults(List<EmojiPicker.Emoji> emojis, string a, string b) =>
        EmojiSearch.Find(emojis, a).Select(item => item.Character).SequenceEqual(EmojiSearch.Find(emojis, b).Select(item => item.Character));
    private static void Check(bool condition, string name)
    {
        if (!condition) { throw new Exception($"FAIL: {name}"); }
        checks++;
    }
    private static Dictionary<string, T> Resource<T>(string file)
    {
        using var stream = Application.GetResourceStream(new Uri($"pack://application:,,,/MED;component/Resources/{file}"))!.Stream;
        return JsonSerializer.Deserialize<Dictionary<string, T>>(stream)!;
    }
}
