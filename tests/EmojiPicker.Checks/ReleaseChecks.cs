using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using EmojiPicker;
using System.IO;
using System.Reflection;

internal static class ReleaseChecks
{
    private sealed class Store : IStartupStore
    {
        internal string? Value;
        internal int Writes;
        public string? Read() => Value;
        public void Write(string value) { Value = value; Writes++; }
        public void Delete() { Value = null; Writes++; }
    }

    internal static void Run(Action<bool, string> check)
    {
        var assembly = typeof(App).Assembly;
        check(assembly.GetName().Name == "MED" && assembly.GetName().Version == new Version(1, 0, 0, 0), "MED assembly identity/version");
        check(assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product == "Mikhalych Emoji Dock", "full product name is present in assembly metadata");
        check(assembly.GetCustomAttribute<AssemblyTitleAttribute>()?.Title == "MED", "display title is MED");
        check(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion == "1.0.0", "release version excludes unrelated baseline commit hash");
        var root = Path.Combine(AppContext.BaseDirectory, "release-check-data", Guid.NewGuid().ToString("N"));
        var roaming = Path.Combine(root, "Roaming");
        var legacy = Path.Combine(roaming, "ClassicEmojiPicker");
        Directory.CreateDirectory(legacy);
        const string original = "{\"emojiInsertMode\":\"paste\",\"pasteRestoreDelayMs\":300}";
        File.WriteAllText(Path.Combine(legacy, "settings.json"), original);
        File.WriteAllText(Path.Combine(legacy, "recent.json"), "[\"🦆\",\"👩‍💻\"]");
        var installed = Path.Combine(root, "installed");
        AppDataPaths.Initialize(installed, roaming);
        check(!AppDataPaths.IsPortable && AppDataPaths.DirectoryPath == Path.Combine(roaming, "MED"), "installed data is under MED roaming directory");
        Settings.Load();
        check(Settings.Current.InsertMode == EmojiInsertMode.Paste && Settings.Current.PasteRestoreDelayMs == 300, "legacy settings migrated");
        check(!Settings.Current.ShowTrayIcon, "tray icon defaults to hidden including migrated settings");
        check(File.ReadAllText(Path.Combine(legacy, "settings.json")) == original, "original legacy data remains untouched");
        check(File.ReadAllText(AppDataPaths.FilePath("recent.json")) == "[\"🦆\",\"👩‍💻\"]", "history Unicode copied intact");
        Settings.Current.ShowTrayIcon = true;
        check(Settings.Save(), "settings saved atomically");
        Settings.Load();
        check(Settings.Current.ShowTrayIcon && File.Exists(AppDataPaths.FilePath("settings.json.bak")), "tray preference persisted and previous settings backed up");
        AppDataPaths.Initialize(installed, roaming);
        check(File.ReadAllText(AppDataPaths.FilePath("settings.json")) != original, "migration does not overwrite existing MED settings");

        var portable = Path.Combine(root, "portable");
        Directory.CreateDirectory(portable);
        File.WriteAllText(Path.Combine(portable, "portable.txt"), "MED portable");
        AppDataPaths.Initialize(portable, roaming);
        check(AppDataPaths.IsPortable && AppDataPaths.DirectoryPath == Path.Combine(portable, "Data"), "portable data stays beside executable");
        Settings.Load();
        check(Settings.Current.InsertMode == EmojiInsertMode.Paste, "portable migrates old settings without deleting source");
        // A file in place of Data deterministically simulates an unwritable directory,
        // without changing ACLs or requiring administrator privileges.
        var blocked = Path.Combine(root, "blocked");
        Directory.CreateDirectory(blocked);
        File.WriteAllText(Path.Combine(blocked, "portable.txt"), "MED portable");
        File.WriteAllText(Path.Combine(blocked, "Data"), "not a directory");
        AppDataPaths.Initialize(blocked, roaming);
        var fallback = AppDataPaths.DirectoryPath;
        check(AppDataPaths.Warning != null && fallback.StartsWith(Path.Combine(roaming, "MED", "Portable")), "unwritable portable uses explicit per-copy fallback with warning");
        Settings.Load(); Settings.Current.ShowTrayIcon = true; Settings.Save();
        File.Delete(Path.Combine(blocked, "Data"));
        AppDataPaths.Initialize(blocked, roaming);
        Settings.Load();
        check(AppDataPaths.DirectoryPath == fallback && Settings.Current.ShowTrayIcon, "fallback remains authoritative after write access returns");

        File.WriteAllText(AppDataPaths.FilePath("settings.json"), "invalid-settings");
        Settings.Load();
        check(Settings.LoadWarning != null && !Settings.Save(), "invalid settings are preserved and never replaced with defaults");
        check(File.ReadAllText(AppDataPaths.FilePath("settings.json")) == "invalid-settings", "corrupt user settings remain recoverable");

        var store = new Store();
        var startup = new StartupRegistration(store, Path.Combine(portable, "MED.exe"));
        check(!startup.Enabled && store.Writes == 0, "portable/installed app startup does not write autostart registry");
        startup.Enable();
        check(startup.Enabled && store.Value == $"\"{Path.Combine(portable, "MED.exe")}\" --background", "autostart quotes full path and requests silent startup");
        var moved = new StartupRegistration(store, Path.Combine(blocked, "MED.exe"));
        check(!moved.Enabled && moved.Status.Contains("перемещённую"), "moved portable explains old registration");
        moved.DisableOwned();
        check(store.Value != null, "unrelated copy cannot implicitly disable another copy's autostart");
        moved.Enable();
        check(moved.Enabled, "enabling moved portable repairs path");
        moved.RemoveRegistration();
        check(store.Value == null, "explicit cleanup removes stale registration");
        check(App.IsBackgroundLaunch(["--background"]) && App.IsBackgroundLaunch(["--BACKGROUND"]) && !App.IsBackgroundLaunch([]), "background relaunch never requests panel");
        check(App.ExitEventName(Path.Combine(portable, "MED.exe")) != App.ExitEventName(Path.Combine(blocked, "MED.exe")), "uninstaller shutdown is scoped to exact executable path");

        AppDataPaths.Initialize(Path.Combine(root, "final-host"), Path.Combine(root, "FinalRoaming"));
        Settings.Load();
        Logger.Initialize();
        var changed = false;
        var exited = false;
        var settings = new SettingsWindow(startup, visible => changed = visible, () => exited = true)
        {
            ShowActivated = false, ShowInTaskbar = false, Left = -20000, Top = -20000,
            WindowStartupLocation = WindowStartupLocation.Manual,
        };
        settings.Show();
        settings.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        check(settings.IsVisible && settings.Title == "Настройки MED", "compiled settings BAML starts successfully");
        var tray = (CheckBox)settings.FindName("TrayToggle");
        check(tray.IsChecked == false, "settings exposes hidden tray preference");
        tray.IsChecked = true;
        tray.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Settings.Load();
        check(changed && Settings.Current.ShowTrayIcon, "panel settings can restore persistent tray icon");
        ((Button)settings.FindName("ExitButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        check(exited, "settings exposes exit command without tray icon");
        settings.Close();
        Settings.Current.ShowTrayIcon = false;
        Settings.Save();
    }

    internal static void History(Action<bool, string> check)
    {
        var file = AppDataPaths.FilePath("recent.json");
        File.WriteAllText(file, "[\"🦆\",\"👩‍💻\"]");
        var window = new MainWindow();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var history = (List<EmojiPicker.Emoji>)typeof(MainWindow).GetField("recentEmojis", flags)!.GetValue(window)!;
        check(string.Concat(history.Select(item => item.Character)) == "🦆👩‍💻", "history restores exact Unicode and order");
        typeof(MainWindow).GetMethod("SaveRecentEmojis", flags)!.Invoke(window, null);
        check(File.Exists(file + ".bak"), "history save retains previous file as backup");
        window.Close();
        const string invalid = "{bad-history";
        File.WriteAllText(file, invalid);
        var corrupt = new MainWindow();
        typeof(MainWindow).GetMethod("SaveRecentEmojis", flags)!.Invoke(corrupt, null);
        check(corrupt.DataWarning != null && File.ReadAllText(file) == invalid, "corrupt history warned and never overwritten by empty defaults");
        corrupt.Close();
        // Fixture-only restoration in the isolated data root.
        File.WriteAllText(file, "[]");
    }
}
