using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using EmojiPicker;

internal static class ReleaseStartupChecks
{
    private sealed class Store : IStartupStore
    {
        internal int Writes;
        public string? Read() => null;
        public void Write(string command) { Writes++; }
        public void Delete() { Writes++; }
    }

    internal static int Run(string[] args)
    {
        var mode = args[1];
        var root = args[2];
        if (mode == "portable")
        {
            Directory.CreateDirectory(Path.Combine(root, "App"));
            File.WriteAllText(Path.Combine(root, "App", "portable.txt"), "test");
        }
        var store = new Store();
        var app = new App
        {
            ReleaseCheckRoot = root,
            ReleaseCheckStartup = new StartupRegistration(store, Path.Combine(root, "App", "MED.exe")),
        };
        var checks = 0;
        var failed = false;
        void Check(bool condition, string message)
        {
            if (!condition) { throw new Exception(message); }
            checks++;
        }
        object? Field(string name) => typeof(App).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(app);
        if (mode != "secondary")
        {
            app.Startup += (_, _) => app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(async () =>
            {
                try
                {
                    var picker = (MainWindow)Field("picker")!;
                    var tray = (System.Windows.Forms.NotifyIcon)Field("trayIcon")!;
                    Check(picker != null && !picker.IsVisible && !picker.IsPickerOpen, "background startup leaves picker hidden");
                    Check(!tray.Visible && !Settings.Current.ShowTrayIcon, "background startup has no persistent tray icon");
                    Check(AppDataPaths.IsPortable == (mode == "portable"), "startup selects correct data mode");
                    Check(store.Writes == 0, "startup never changes real or fake autostart");
                    Check(Field("hotkey") == null, "isolated fixture never installs competing global hook");

                    var childInfo = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
                    foreach (var arg in new[] { "--startup-check", "secondary", root, "--background" }) { childInfo.ArgumentList.Add(arg); }
                    using var child = Process.Start(childInfo)!;
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    await child.WaitForExitAsync(timeout.Token);
                    await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    Check(child.ExitCode == 0 && !picker!.IsVisible, "second background process exits without opening panel");
                    Check(Field("settingsWindow") == null, "background duplicate does not open settings");

                    var settingsInfo = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
                    foreach (var arg in new[] { "--startup-check", "secondary", root, "--settings" }) { settingsInfo.ArgumentList.Add(arg); }
                    using var settingsChild = Process.Start(settingsInfo)!;
                    await settingsChild.WaitForExitAsync(timeout.Token);
                    var wait = Stopwatch.StartNew();
                    while (Field("settingsWindow") == null && wait.Elapsed < TimeSpan.FromSeconds(8))
                    {
                        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                    }
                    var settings = (SettingsWindow)Field("settingsWindow")!;
                    Check(settingsChild.ExitCode == 0 && settings != null && settings.IsVisible && !picker!.IsVisible, "IPC settings command opens settings while picker remains hidden");
                    if (mode == "installed")
                    {
                        var shutdownInfo = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
                        foreach (var arg in new[] { "--startup-check", "secondary", root, "--shutdown" }) { shutdownInfo.ArgumentList.Add(arg); }
                        using var shutdownChild = Process.Start(shutdownInfo)!;
                        File.WriteAllText(Path.Combine(root, "shutdown-child.pid"), shutdownChild.Id.ToString());
                    }
                    else { ((Button)settings!.FindName("ExitButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); }
                }
                catch (Exception error) { failed = true; Console.Error.WriteLine(error); app.Shutdown(1); }
            }));
            app.Exit += (_, _) =>
            {
                try
                {
                    Check(!((MainWindow)Field("picker")!).IsVisible, "exit hides picker");
                    Check(!((Thread)Field("showThread")!).IsAlive, "exit joins IPC thread");
                    Check(!((System.Windows.Forms.NotifyIcon)Field("trayIcon")!).Visible, "exit releases tray icon");
                    Check(store.Writes == 0, "lifecycle never changes autostart registry");
                }
                catch (Exception error) { failed = true; Console.Error.WriteLine(error); }
            };
        }
        var result = app.Run();
        if (mode == "secondary" && args.Contains("--shutdown")) { File.WriteAllText(Path.Combine(root, "shutdown-result.txt"), result.ToString()); }
        if (mode != "secondary") { Console.WriteLine($"Startup {mode}: {(failed ? "FAIL" : "PASS")}, {checks} checks (isolated data/IPC; global hook disabled)"); }
        return failed ? 1 : result;
    }
}
