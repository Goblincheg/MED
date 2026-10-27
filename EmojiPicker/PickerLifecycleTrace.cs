using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace EmojiPicker
{
    // UI-thread ring buffer: no file I/O in low-level input callbacks. The
    // caller records only event codes, flags, counts and window handles.
    internal sealed class PickerLifecycleTrace
    {
        internal const int Capacity = 256;
        private readonly Queue<string> events = new();
        private long started;
        private long sequence;
        internal long Session { get; private set; }
        internal bool Active { get; private set; }
        internal Action<IReadOnlyList<string>> Writer { get; set; } = Logger.WritePickerLifecycle;

        internal void Start()
        {
            events.Clear();
            Session++;
            sequence = 0;
            started = Stopwatch.GetTimestamp();
            Active = true;
        }

        internal void Record(string metadata)
        {
            if (!Active) { return; }
            if (events.Count == Capacity) { events.Dequeue(); }
            events.Enqueue($"MED-4.3-v1 {DateTime.UtcNow:O} session={Session} seq={++sequence} " +
                $"elapsedMs={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1} {metadata}");
        }

        internal void Finish()
        {
            if (!Active) { return; }
            Active = false;
            var snapshot = events.ToArray();
            events.Clear();
            Writer(snapshot);
        }
    }
}
