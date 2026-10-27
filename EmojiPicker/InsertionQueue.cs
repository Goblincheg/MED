using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace EmojiPicker
{
    // Owned by the UI thread. Only the active drain may dequeue; awaiting a
    // recipient never starts another consumer or overlaps clipboard transactions.
    internal sealed class InsertionQueue : IReadOnlyCollection<InsertionQueue.Request>
    {
        internal readonly record struct Request(Emoji Emoji, TextInjector.Target Target, CancellationToken Token);
        internal const int Capacity = 128;
        private readonly Queue<Request> pending = new();
        internal bool IsDraining { get; private set; }
        public int Count => pending.Count;

        internal bool TryEnqueue(Request request)
        {
            if (request.Token.IsCancellationRequested || pending.Count >= Capacity) { return false; }
            pending.Enqueue(request);
            return true;
        }

        internal void Clear() => pending.Clear();

        internal async Task DrainAsync(Func<Request, Task> process)
        {
            if (IsDraining) { return; }
            IsDraining = true;
            try
            {
                while (pending.TryDequeue(out var request))
                {
                    if (!request.Token.IsCancellationRequested) { await process(request); }
                }
            }
            finally { IsDraining = false; }
        }

        public IEnumerator<Request> GetEnumerator() => pending.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
