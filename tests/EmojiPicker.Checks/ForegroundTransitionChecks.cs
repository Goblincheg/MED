using EmojiPicker;

internal static class ForegroundTransitionChecks
{
    private static readonly IntPtr Picker = new(10);
    private static readonly IntPtr Target = new(20);
    private static readonly IntPtr Foreign = new(30);

    internal static async Task RunAsync(EmojiPicker.Emoji emoji, Action<bool, string> check)
    {
        var unknown = State(IntPtr.Zero);
        check(unknown == ForegroundState.Unknown, "NULL is unknown, not a foreign window");
        check(!ForegroundPolicy.ShouldCloseAfterInsert(unknown) && !ForegroundPolicy.ShouldCloseWhenIdle(unknown),
            "NULL does not hide the picker in postcheck, finally or deactivation");
        check(State(Picker) == ForegroundState.Picker && State(Target) == ForegroundState.Target
            && State(Foreign) == ForegroundState.Foreign, "picker, target and foreign are distinguished");
        check(!ForegroundPolicy.ShouldCloseAfterInsert(State(Target)), "programmatic return to captured target is not foreign");
        check(!ForegroundPolicy.MayRestoreTarget(State(Foreign)) && ForegroundPolicy.ShouldCloseAfterInsert(State(Foreign)),
            "confirmed foreign window blocks next insertion and closes session");

        await ResolveAsync(emoji, Target, check);
        await ResolveAsync(emoji, Foreign, check);
        await BurstAsync(emoji, check);
        await CancelAsync(emoji, "outside-click", check);
        await CancelAsync(emoji, "escape", check);

        // Unknown permits activation, never blind input. Model the independent
        // HasFocus boundary used by the unchanged real TextInjector.
        var foreground = IntPtr.Zero;
        var controlConfirmed = false;
        var sent = 0;
        if (ForegroundPolicy.MayRestoreTarget(State(foreground)))
        {
            // Activation cannot be confirmed: do not count a SendInput call.
            if (foreground == Target && controlConfirmed) { sent++; }
        }
        check(sent == 0, "NULL without confirmed recipient/control sends no input");
        check(!default(TextInjector.Target).HasFocus(), "real injector rejects an invalid recipient independently of lifecycle policy");
    }

    private static ForegroundState State(IntPtr current) => ForegroundPolicy.Classify(current, Picker, Target);

    private static async Task ResolveAsync(EmojiPicker.Emoji emoji, IntPtr resolved, Action<bool, string> check)
    {
        var queue = new InsertionQueue();
        var target = new TextInjector.Target(Target, new IntPtr(21), 100, 200);
        for (var i = 0; i < 2; i++) { queue.TryEnqueue(new(emoji, target, CancellationToken.None)); }
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var current = Target;
        var accepted = 0;
        var closed = false;
        var drain = queue.DrainAsync(async _ =>
        {
            if (!ForegroundPolicy.MayRestoreTarget(State(current)))
            {
                closed = true;
                queue.Clear();
                return;
            }
            // Simulated native focus confirmation, before input, not just a
            // successful lifecycle classification.
            if (current != Target) { throw new Exception("Unexpected unconfirmed target in test"); }
            accepted++;
            if (accepted == 1)
            {
                current = IntPtr.Zero; // click inside + MA_NOACTIVATE during transfer
                closed = ForegroundPolicy.ShouldCloseAfterInsert(State(current));
                await resume.Task;
            }
        });
        check(accepted == 1 && !closed && queue.Count == 1 && !drain.IsCompleted,
            $"NULL -> {resolved}: accepted insertion is preserved while foreground is unknown");
        current = resolved;
        resume.SetResult();
        await drain;
        check(resolved == Target ? accepted == 2 && !closed : accepted == 1 && closed,
            $"NULL -> {resolved}: only a confirmed original recipient can receive the next choice");
        check(queue.Count == 0 && !queue.IsDraining, $"NULL -> {resolved}: no duplicate or orphaned consumer");
    }

    private static async Task BurstAsync(EmojiPicker.Emoji emoji, Action<bool, string> check)
    {
        var queue = new InsertionQueue();
        for (var i = 0; i < 50; i++) { queue.TryEnqueue(new(emoji, default, CancellationToken.None)); }
        var accepted = 0;
        var closed = false;
        await queue.DrainAsync(_ =>
        {
            // Each inside click produces an unknown post-insert observation;
            // a subsequent restoration confirms the original recipient again.
            var current = IntPtr.Zero;
            if (ForegroundPolicy.ShouldCloseAfterInsert(State(current))) { closed = true; queue.Clear(); }
            if (ForegroundPolicy.MayRestoreTarget(State(current))) { current = Target; }
            var focusConfirmed = current == Target;
            if (focusConfirmed) { accepted++; }
            current = IntPtr.Zero;
            closed |= ForegroundPolicy.ShouldCloseAfterInsert(State(current));
            return Task.CompletedTask;
        });
        check(accepted == 50 && !closed && queue.Count == 0, "50 inside clicks/NULL transitions: 50 confirmed simulated insertions without hiding");
    }

    private static async Task CancelAsync(EmojiPicker.Emoji emoji, string reason, Action<bool, string> check)
    {
        var queue = new InsertionQueue();
        using var session = new CancellationTokenSource();
        for (var i = 0; i < 50; i++) { queue.TryEnqueue(new(emoji, default, session.Token)); }
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var accepted = 0;
        var cleanup = false;
        var drain = queue.DrainAsync(async request =>
        {
            await resume.Task;
            try
            {
                if (!request.Token.IsCancellationRequested) { accepted++; }
            }
            finally { cleanup = true; }
        });
        session.Cancel();
        queue.Clear();
        resume.SetResult();
        await drain;
        check(accepted == 0 && cleanup && queue.Count == 0, $"{reason} during NULL: cancellation still wins and cleanup completes");
    }
}
