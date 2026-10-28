using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace EmojiPicker
{
    public partial class App : Application
    {
        private const string MutexName = "ClassicEmojiPicker.SingleInstance";
        private const string ShowEventName = "ClassicEmojiPicker.Show";
        // Retain the old mutex identity to avoid two competing Win+. hooks.
        private const string SettingsEventName = "MED.Settings";
        private const string ReadyEventName = "MED.Ready";
        internal static string ExitEventName(string path) => "MED.Exit." + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant())))[..24];
        internal static bool IsBackgroundLaunch(string[] args) => args.Contains("--background", StringComparer.OrdinalIgnoreCase);
        private readonly StartupRegistration startup = new(new RegistryStartupStore(), Environment.ProcessPath!);
        private EventWaitHandle? exitEvent;
        private EventWaitHandle? settingsEvent;
        private EventWaitHandle? readyEvent;
        private SettingsWindow? settingsWindow;
        private System.Windows.Threading.DispatcherTimer? notificationTimer;
        // Internal test seam: isolated data/IPC and no global hooks. Never set by the app entry point.
        internal string? ReleaseCheckRoot { get; set; }
        internal StartupRegistration? ReleaseCheckStartup { get; set; }
        private string instanceMutexName = MutexName;
        private string instanceShowEventName = ShowEventName;
        private string instanceSettingsEventName = SettingsEventName;
        private string instanceReadyEventName = ReadyEventName;
        private string instanceExitEventName = string.Empty;

        /// <summary>
        /// The foreground window when the hotkey fired; selected emoji are inserted
        /// into it. Set by the hotkey hook before the picker is shown.
        /// </summary>
        public static IntPtr PreviousForegroundWindow { get; set; }

        /// <summary>
        /// The child control that had keyboard focus when the hotkey fired (e.g.
        /// Explorer's Search or address edit); focus is restored to it on insert.
        /// </summary>
        public static IntPtr PreviousFocusWindow { get; set; }

        /// <summary>
        /// Screen rectangle of the target app's text caret at hotkey time, when it
        /// exposed one; the picker anchors to it (like the Windows 10 panel) instead
        /// of the mouse pointer. Null when unknown - the picker falls back to the mouse.
        /// </summary>
        public static System.Drawing.Rectangle? PreviousCaretRect { get; set; }

        private Mutex? instanceMutex;
        private EventWaitHandle? showEvent;
        private MainWindow? picker;
        private HotkeyListener? hotkey;
        private NotifyIcon? trayIcon;
        private Thread? showThread;
        private System.Windows.Threading.DispatcherTimer? hookRearmTimer;
        private volatile bool shuttingDown;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var executable = Environment.ProcessPath!;
            var suffix = ReleaseCheckRoot == null ? "" : ".Check." + ExitEventName(ReleaseCheckRoot);
            instanceMutexName = MutexName + suffix;
            instanceShowEventName = ShowEventName + suffix;
            instanceSettingsEventName = SettingsEventName + suffix;
            instanceReadyEventName = ReadyEventName + suffix;
            instanceExitEventName = ExitEventName(executable) + suffix;
            if (e.Args.Contains("--shutdown", StringComparer.OrdinalIgnoreCase))
            {
                var result = 0;
                try
                {
                    using var signal = EventWaitHandle.OpenExisting(instanceExitEventName);
                    signal.Set();
                    using var gate = Mutex.OpenExisting(instanceMutexName);
                    var acquired = false;
                    try { acquired = gate.WaitOne(TimeSpan.FromSeconds(10)); }
                    catch (AbandonedMutexException) { acquired = true; }
                    if (acquired) { gate.ReleaseMutex(); } else { result = 1; }
                }
                catch (WaitHandleCannotBeOpenedException) { }
                Shutdown(result);
                return;
            }
            instanceMutex = new Mutex(true, instanceMutexName, out var isNew);
            if (!isNew)
            {
                if (!IsBackgroundLaunch(e.Args))
                {
                    try
                    {
                        using var ready = EventWaitHandle.OpenExisting(instanceReadyEventName);
                        using var signal = EventWaitHandle.OpenExisting(e.Args.Contains("--settings") ? instanceSettingsEventName : instanceShowEventName);
                        signal.Set();
                    }
                    catch (WaitHandleCannotBeOpenedException)
                    {
                        MessageBox.Show("Уже запущена предыдущая копия Classic Emoji Picker или MED запускается. Закрой старую копию через её меню и повтори запуск.", "MED");
                    }
                }
                Shutdown();
                return;
            }
            try
            {
                AppDataPaths.Initialize(ReleaseCheckRoot == null ? AppContext.BaseDirectory : Path.Combine(ReleaseCheckRoot, "App"),
                    ReleaseCheckRoot == null ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) : Path.Combine(ReleaseCheckRoot, "Roaming"));
            }
            catch (Exception error)
            {
                MessageBox.Show("MED не запущен: " + error.Message, "MED", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
                return;
            }
            Logger.Initialize();
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            Logger.Log($"=== Startup v{version} ===");
            Settings.Load();

            // Create the run-again event now, before the heavy warm-up below, so a
            // relaunch during startup can signal it instead of failing to open a
            // not-yet-created event. The auto-reset event latches the signal until
            // the show-event loop starts (after the picker exists).
            showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, instanceShowEventName);

            settingsEvent = new EventWaitHandle(false, EventResetMode.AutoReset, instanceSettingsEventName);
            exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, instanceExitEventName);
            readyEvent = new EventWaitHandle(true, EventResetMode.ManualReset, instanceReadyEventName);

            // A resident utility should survive a bad frame: log the exception
            // and keep running rather than take Win+. down until relaunch
            DispatcherUnhandledException += (_, args) =>
            {
                Logger.LogAlways($"UNHANDLED (UI, continuing): {args.Exception}");
                args.Handled = true;
                Notify($"Произошла ошибка. Подробности: {Logger.LogPath}", ToolTipIcon.Warning);
            };
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
                Logger.LogAlways($"FATAL: {args.ExceptionObject}");

            // Stay alive with no visible window until the hotkey shows the picker
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            global::Emoji.Wpf.EmojiData.Load();
            ThemeManager.Initialize();

            picker = new MainWindow();
            picker.PreWarm(); // warm the render path so the first hotkey open is fast

            if (ReleaseCheckRoot == null)
            {
                hotkey = new HotkeyListener();
                hotkey.HotkeyPressed += OnHotkeyPressed;
                hotkey.Start();
                Logger.Log("Keyboard hook installed");
            }

            // Windows can silently drop a low-level hook (callback timeout, secure
            // desktop, session switch). Re-arm on session change and on a periodic
            // backstop so Win+. recovers without a restart.
            Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;
            hookRearmTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(60),
            };
            hookRearmTimer.Tick += (_, _) => hotkey?.Rearm();
            hookRearmTimer.Start();

            // Now that the picker exists, start handling run-again signals (a signal
            // latched during startup is processed here). The event was created above.
            showThread = new Thread(ShowEventLoop) { IsBackground = true };
            showThread.Start();

            CreateTrayIcon();
            if (e.Args.Contains("--settings", StringComparer.OrdinalIgnoreCase)) { ShowSettings(); }
            if (AppDataPaths.Warning != null || Settings.LoadWarning != null || picker.DataWarning != null)
            {
                MessageBox.Show(string.Join("\n\n", new[] { AppDataPaths.Warning, Settings.LoadWarning, picker.DataWarning }.Where(text => text != null)), "MED — данные", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OnHotkeyPressed(IntPtr targetWindow)
        {
            if (picker?.IsPickerOpen == true)
            {
                picker.DismissPicker("hotkey-toggle");
                return;
            }
            // Resolve the focused control and caret here (UI thread), off the hook
            // thread. The target app still has focus (our window isn't shown yet),
            // so this is the same state the hook would have captured - but a hung
            // target can no longer stall the low-level hook and get it removed.
            var focusWindow = TextInjector.GetFocusedControl(targetWindow);
            System.Drawing.Rectangle? caretRect =
                TextInjector.TryGetCaretRect(targetWindow, out var rect) ? rect : null;

            Logger.Log($"Hotkey pressed; target={targetWindow} focus={focusWindow} caret={(caretRect.HasValue ? caretRect.Value.ToString() : "none")}");

            // Win+. while the picker is open dismisses it (like the Windows 10
            // panel). Without this, the hook would capture the picker itself as
            // the insertion target and the chosen emoji would go nowhere.
            if (picker != null && targetWindow == new System.Windows.Interop.WindowInteropHelper(picker).Handle)
            {
                if (picker.IsVisible)
                {
                    Logger.Log("Hotkey while open -> toggle dismiss");
                    picker.DismissPicker("hotkey-picker");
                }

                return; // hidden-but-captured is a stale race; keep the old target
            }

            PreviousForegroundWindow = targetWindow;
            PreviousFocusWindow = focusWindow;
            PreviousCaretRect = caretRect;
            picker?.ShowPicker();
        }

        private void OnSessionSwitch(object sender, Microsoft.Win32.SessionSwitchEventArgs e)
        {
            // SystemEvents raises this on a background thread; re-arm on the UI
            // thread, which owns the hook's message loop
            Dispatcher.BeginInvoke(new Action(() =>
            {
                hotkey?.Rearm();
                Logger.Log($"Session switch ({e.Reason}) -> hook re-armed");
            }));
        }

        private void ShowEventLoop()
        {
            while (showEvent != null && exitEvent != null && settingsEvent != null)
            {
                var command = WaitHandle.WaitAny([showEvent, exitEvent, settingsEvent]);
                if (shuttingDown)
                {
                    return;
                }

                if (command == 1) { Dispatcher.BeginInvoke(new Action(() => Shutdown())); continue; }
                if (command == 2) { Dispatcher.BeginInvoke(new Action(ShowSettings)); continue; }
                // Snapshot the foreground state on this thread, at signal time
                var target = NativeMethods.GetForegroundWindow();
                var focus = TextInjector.GetFocusedControl(target);
                System.Drawing.Rectangle? caret =
                    TextInjector.TryGetCaretRect(target, out var caretRect) ? caretRect : null;

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    Logger.Log("Show requested (run-again)");

                    // Don't let the picker become its own insertion target when
                    // it is already open; keep whatever target it had
                    if (picker == null || target != new System.Windows.Interop.WindowInteropHelper(picker).Handle)
                    {
                        PreviousForegroundWindow = target;
                        PreviousFocusWindow = focus;
                        PreviousCaretRect = caret;
                    }

                    picker?.ShowPicker();
                }));
            }
        }

        private void CreateTrayIcon()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add("Открыть MED", null, (_, _) => ShowPickerFromTray());
            menu.Items.Add("Настройки MED", null, (_, _) => ShowSettings());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Выход из MED", null, (_, _) => Shutdown());
            trayIcon = new NotifyIcon
            {
                Icon = LoadTrayIcon(),
                Text = "MED — Mikhalych Emoji Dock",
                Visible = Settings.Current.ShowTrayIcon,
                ContextMenuStrip = menu,
            };
            trayIcon.DoubleClick += (_, _) => ShowPickerFromTray();
            trayIcon.BalloonTipClosed += (_, _) => Dispatcher.BeginInvoke(new Action(RestoreTrayVisibility));
        }

        internal void ShowSettings()
        {
            picker?.DismissPicker("settings-open");
            if (settingsWindow != null) { settingsWindow.Activate(); return; }
            settingsWindow = new SettingsWindow(ReleaseCheckStartup ?? startup, _ => RestoreTrayVisibility(), () => Shutdown());
            if (ReleaseCheckRoot != null)
            {
                settingsWindow.ShowActivated = false;
                settingsWindow.ShowInTaskbar = false;
                settingsWindow.WindowStartupLocation = WindowStartupLocation.Manual;
                settingsWindow.Left = -20000; settingsWindow.Top = -20000;
            }
            settingsWindow.Closed += (_, _) => settingsWindow = null;
            settingsWindow.Show();
        }

        private void RestoreTrayVisibility()
        {
            notificationTimer?.Stop();
            if (trayIcon != null) { trayIcon.Visible = Settings.Current.ShowTrayIcon; }
        }

        private void Notify(string message, ToolTipIcon kind)
        {
            if (trayIcon == null) { return; }
            // Shell balloons require a registered icon. It is temporary when the
            // user disabled the persistent icon; this does not change the setting.
            trayIcon.Visible = true;
            trayIcon.ShowBalloonTip(4000, "MED", message, kind);
            notificationTimer ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            notificationTimer.Stop();
            notificationTimer.Tick -= NotificationExpired;
            notificationTimer.Tick += NotificationExpired;
            notificationTimer.Start();
        }
        private void NotificationExpired(object? sender, EventArgs e) => RestoreTrayVisibility();

        private void ShowPickerFromTray()
        {
            if (picker?.IsPickerOpen == true) { return; }
            // Opened by mouse from the tray: there is no caret to anchor to, and
            // the target/caret left over from an earlier hotkey press would send
            // the emoji to a window the user isn't looking at any more. With no
            // target, a pick reports failure rather than guessing a recipient.
            PreviousForegroundWindow = IntPtr.Zero;
            PreviousFocusWindow = IntPtr.Zero;
            PreviousCaretRect = null;
            picker?.ShowPicker();
        }

        internal void ReportInsertionFailure()
        {
            Notify("Не удалось безопасно вставить эмодзи. Верни курсор в нужное поле и открой панель через Win + . ещё раз.", ToolTipIcon.Warning);
        }

        internal void ReportDataFailure() => Notify("Историю не удалось сохранить. Проверь права записи в каталог данных MED (путь указан в настройках).", ToolTipIcon.Warning);

        internal void ReportInsertionQueueFull()
        {
            Notify("Очередь вставки заполнена. Новые выборы не приняты; дождись завершения и повтори их.", ToolTipIcon.Warning);
        }

        private static Icon LoadTrayIcon()
        {
            try
            {
                var uri = new Uri("pack://application:,,,/MED;component/Resources/app.ico");
                using var stream = GetResourceStream(uri)?.Stream;
                if (stream != null)
                {
                    return new Icon(stream);
                }
            }
            catch (Exception)
            {
                // Fall through to the shipped application icon
            }

            var exePath = Environment.ProcessPath;
            return (exePath != null ? Icon.ExtractAssociatedIcon(exePath) : null) ?? SystemIcons.Application;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            notificationTimer?.Stop();
            settingsWindow?.Close();
            picker?.DismissPicker("application-exit");
            TextInjector.FinishPendingClipboardRestore();
            hookRearmTimer?.Stop();
            try
            {
                Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch;
            }
            catch (Exception)
            {
                // SystemEvents teardown is best-effort
            }

            hotkey?.Dispose();
            ThemeManager.Shutdown();

            // Wake the show-event thread so it observes the flag and exits before
            // the handle is disposed (disposing mid-WaitOne throws on that thread)
            shuttingDown = true;
            showEvent?.Set();
            showThread?.Join(1000);
            showEvent?.Dispose();
            exitEvent?.Dispose();
            settingsEvent?.Dispose();
            readyEvent?.Dispose();

            if (trayIcon != null)
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
            }

            instanceMutex?.Dispose();
            base.OnExit(e);
        }
    }
}
