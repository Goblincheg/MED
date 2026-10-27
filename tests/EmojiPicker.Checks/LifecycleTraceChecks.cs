using System.Reflection;
using System.Windows;
using System.Windows.Input;
using EmojiPicker;

internal static class LifecycleTraceChecks
{
    internal static void Run(MainWindow window, List<EmojiPicker.Emoji> emojis, Action<bool, string> check)
    {
        var batches = new List<IReadOnlyList<string>>();
        var trace = new PickerLifecycleTrace { Writer = lines => batches.Add(lines) };
        trace.Start();
        for (var i = 0; i < 1000; i++) { trace.Record($"event-{i}"); }
        trace.Finish();
        check(batches.Count == 1 && batches[0].Count == PickerLifecycleTrace.Capacity, "lifecycle trace is bounded to 256 events");
        check(batches[0][^1].Contains("seq=1000") && batches[0][^1].Contains("event-999"), "trace retains latest causal events");
        trace.Finish();
        check(batches.Count == 1, "already-hidden trace does not flush twice");
        trace.Start();
        trace.Record("reopened");
        trace.Finish();
        check(batches[1].Count == 1 && batches[1][0].Contains("session=2") && !batches[1][0].Contains("event-999"), "reopening starts an independent diagnostic session");

        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var sessionField = typeof(MainWindow).GetField("insertionSession", flags)!;
        var runningField = typeof(MainWindow).GetField("insertionRunning", flags)!;
        var targetField = typeof(MainWindow).GetField("insertionTarget", flags)!;
        var queue = (InsertionQueue)typeof(MainWindow).GetField("insertionQueue", flags)!.GetValue(window)!;
        var commit = typeof(MainWindow).GetMethod("CommitEmoji", flags)!;
        var deactivate = typeof(MainWindow).GetMethod("MainWindow_Deactivated", flags)!;
        var requestDismiss = typeof(MainWindow).GetMethod("RequestDismiss", flags)!;
        // All output stays in memory. Do not write test sessions to user logs.
        var windows = new List<IReadOnlyList<string>>();
        window.LifecycleTrace.Writer = lines => windows.Add(lines);
        var searchBox = (System.Windows.Controls.TextBox)window.FindName("SearchBox");
        const string fieldSentinel = "PRIVATE-FIELD-MUST-NOT-BE-LOGGED";

        window.LifecycleTrace.Start();
        window.Show(); // off-screen and ShowActivated=false from the existing fixture
        using var first = new CancellationTokenSource();
        sessionField.SetValue(window, first);
        targetField.SetValue(window, new TextInjector.Target(NativeMethods.GetForegroundWindow(), IntPtr.Zero, 0, 0));
        runningField.SetValue(window, true);
        searchBox.Text = fieldSentinel;
        for (var i = 0; i < 50; i++)
        {
            commit.Invoke(window, [emojis[0]]);
            deactivate.Invoke(window, [window, EventArgs.Empty]);
        }
        check(window.IsVisible && !first.IsCancellationRequested && queue.Count == 50, "50 choices plus programmatic deactivation do not immediately hide/cancel");
        window.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent));
        window.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, Environment.TickCount, searchBox, null)
        {
            RoutedEvent = Keyboard.LostKeyboardFocusEvent
        });
        check(window.IsVisible && !first.IsCancellationRequested, "LostFocus/LostKeyboardFocus diagnostics do not hide the picker");
        var escape = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), Environment.TickCount, Key.Escape)
        {
            RoutedEvent = Keyboard.KeyDownEvent
        };
        typeof(MainWindow).GetMethod("OnKeyDown", flags)!.Invoke(window, [escape]);
        check(first.IsCancellationRequested && !window.IsVisible && queue.Count == 0, "Escape during insertion cancels and hides");
        check(windows.Count == 1 && windows[0].Any(line => line.Contains("dismiss reason=escape-wpf"))
            && windows[0].Any(line => line.Contains("visibility-hidden")), "Escape trace records cause and actual hide");
        check(windows[0].Any(line => line.Contains("wpf-deactivated") && line.Contains("running=True")), "trace distinguishes deactivation during insertion");
        check(windows[0].Any(line => line.Contains("wpf-lost-focus")) && windows[0].Any(line => line.Contains("wpf-lost-keyboard-focus")), "both WPF focus-loss events are traced");
        check(!windows[0].Any(line => line.Contains(fieldSentinel) || line.Contains(emojis[0].Character)), "lifecycle diagnostics contain neither field text nor selected emoji");
        runningField.SetValue(window, false);

        window.LifecycleTrace.Start();
        window.Show();
        using var second = new CancellationTokenSource();
        sessionField.SetValue(window, second);
        runningField.SetValue(window, true);
        commit.Invoke(window, [emojis[1]]);
        requestDismiss.Invoke(window, ["outside-click"]);
        check(second.IsCancellationRequested, "outside click cancels synchronously during insertion");
        window.DismissPicker("outside-click"); // dispatched counterpart, without pumping native input
        check(!window.IsVisible && queue.Count == 0 && windows.Count == 2, "outside click hides and clears its session");
        check(windows[1].Any(line => line.Contains("dismiss-request reason=outside-click"))
            && windows[1].Any(line => line.Contains("hide-request reason=outside-click")), "outside-click trace stays distinct from Escape");
        runningField.SetValue(window, false);

        window.LifecycleTrace.Start();
        window.Show();
        using var third = new CancellationTokenSource();
        sessionField.SetValue(window, third);
        check(window.IsVisible && window.IsPickerOpen && !third.IsCancellationRequested, "newly reopened session is independent of earlier cancellation");
        // Deliberately invoke the existing idle-deactivation path: diagnose its
        // actual hide instead of claiming that it caused the user's native input.
        deactivate.Invoke(window, [window, EventArgs.Empty]);
        check(!window.IsVisible && third.IsCancellationRequested, "idle deactivation still follows original closing policy");
        check(windows.Count == 3 && windows[2].Any(line => line.Contains("dismiss reason=deactivated-idle")), "active-window-change closure has a distinct diagnostic reason");
    }
}
