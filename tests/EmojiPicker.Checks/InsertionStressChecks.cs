using EmojiPicker;

internal static class InsertionStressChecks
{
    // Deliberately no Windows input: the gates model an asynchronous focus/paste
    // operation, with the same bounded drain used by MainWindow in production.
    internal static async Task RunAsync(IReadOnlyList<EmojiPicker.Emoji> emojis, Action<bool, string> check)
    {
        await BurstAsync(Enumerable.Repeat(emojis[0], 50).ToArray(), check, "50 identical");
        await BurstAsync(emojis.Take(50).ToArray(), check, "50 different");
        await CancelAsync(emojis[0], check, "Escape");
        await CancelAsync(emojis[0], check, "outside click");
        await TargetChangeAsync(emojis[0], check);

        var bounded = new InsertionQueue();
        var request = new InsertionQueue.Request(emojis[0], default, CancellationToken.None);
        for (var i = 0; i < InsertionQueue.Capacity; i++)
        {
            if (!bounded.TryEnqueue(request)) { throw new Exception("Queue rejected an in-capacity request"); }
        }
        check(!bounded.TryEnqueue(request) && bounded.Count == InsertionQueue.Capacity, "queue capacity rejects excess without dropping accepted choices");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        bounded.Clear();
        check(!bounded.TryEnqueue(request with { Token = cancelled.Token }), "cancelled session cannot enqueue");
        // A cancelled queued session is skipped even if Clear has not yet run.
        using var stale = new CancellationTokenSource();
        bounded.TryEnqueue(request with { Token = stale.Token });
        stale.Cancel();
        var staleCalls = 0;
        await bounded.DrainAsync(_ => { staleCalls++; return Task.CompletedTask; });
        check(staleCalls == 0 && bounded.Count == 0, "cancelled queued work never reaches inserter");
    }

    private static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task BurstAsync(EmojiPicker.Emoji[] choices, Action<bool, string> check, string name)
    {
        var queue = new InsertionQueue();
        var gate = Gate();
        var received = new List<EmojiPicker.Emoji>();
        var active = 0;
        var maximumActive = 0;
        // Half the burst arrives while the first insertion is awaiting completion.
        foreach (var emoji in choices.Take(25))
        {
            check(queue.TryEnqueue(new(emoji, default, CancellationToken.None)), $"{name}: accepted before drain");
        }
        var drain = queue.DrainAsync(async request =>
        {
            active++;
            maximumActive = Math.Max(maximumActive, active);
            await gate.Task;
            received.Add(request.Emoji);
            active--;
        });
        foreach (var emoji in choices.Skip(25))
        {
            check(queue.TryEnqueue(new(emoji, default, CancellationToken.None)), $"{name}: accepted during insertion");
        }
        var competingCalls = 0;
        await queue.DrainAsync(_ => { competingCalls++; return Task.CompletedTask; });
        check(queue.IsDraining && !drain.IsCompleted && received.Count == 0, $"{name}: waits for operation, not a timer");
        check(competingCalls == 0 && active == 1, $"{name}: competing drain cannot start a parallel inserter");
        gate.SetResult();
        await drain;
        check(received.SequenceEqual(choices), $"{name}: all 50 exactly once and in order");
        check(maximumActive == 1 && active == 0 && queue.Count == 0 && !queue.IsDraining, $"{name}: single consumer released cleanly");
        await queue.DrainAsync(_ => { competingCalls++; return Task.CompletedTask; });
        check(competingCalls == 0, $"{name}: completed choices are not replayed");
    }

    private static async Task CancelAsync(EmojiPicker.Emoji emoji, Action<bool, string> check, string name)
    {
        var queue = new InsertionQueue();
        using var session = new CancellationTokenSource();
        for (var i = 0; i < 50; i++) { queue.TryEnqueue(new(emoji, default, session.Token)); }
        var gate = Gate();
        var calls = 0;
        var sent = 0;
        var cleanedUp = false;
        var drain = queue.DrainAsync(async request =>
        {
            calls++;
            try
            {
                await gate.Task;
                if (!request.Token.IsCancellationRequested) { sent++; }
            }
            finally { cleanedUp = true; }
        });
        session.Cancel();
        queue.Clear(); // same cancellation + clear used by DismissPicker
        check(queue.Count == 0 && queue.IsDraining && !drain.IsCompleted, $"{name}: cancellation does not release unfinished cleanup");
        gate.SetResult();
        await drain;
        check(calls == 1 && sent == 0 && cleanedUp && !queue.IsDraining, $"{name}: pending 49 discarded and active operation cleans up");
        var nextCalls = 0;
        queue.TryEnqueue(new(emoji, default, CancellationToken.None));
        await queue.DrainAsync(_ => { nextCalls++; return Task.CompletedTask; });
        check(nextCalls == 1, $"{name}: next session starts once after cleanup");
    }

    private static async Task TargetChangeAsync(EmojiPicker.Emoji emoji, Action<bool, string> check)
    {
        var queue = new InsertionQueue();
        var target = new TextInjector.Target(new IntPtr(100), new IntPtr(101), 1, 1);
        for (var i = 0; i < 50; i++) { queue.TryEnqueue(new(emoji, target, CancellationToken.None)); }
        var gate = Gate();
        var current = target;
        var sent = 0;
        var rejected = 0;
        var drain = queue.DrainAsync(async request =>
        {
            await gate.Task;
            // Model the production validation boundary immediately before input.
            if (current != request.Target)
            {
                rejected++;
                queue.Clear();
                return;
            }
            sent++;
        });
        current = target with { Window = new IntPtr(200), Process = 2 };
        gate.SetResult();
        await drain;
        check(sent == 0 && rejected == 1 && queue.Count == 0, "changed target: no input or automatic replay into new recipient");
    }
}
