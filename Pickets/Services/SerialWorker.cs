using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Pickets.Services
{
    /// <summary>
    /// Runs work one item at a time on its own background thread, and gives up on it for good once
    /// an item takes longer than <see cref="Timeout"/>. For Windows calls that can block forever:
    /// performance counters load other programs' plug-ins into Pickets, and one waiting on a
    /// service that never answers can leave the call stuck in the kernel, where nothing can end
    /// it. The caller is never held up past the timeout, and nothing more is queued behind it.
    /// </summary>
    internal sealed class SerialWorker
    {
        private readonly BlockingCollection<Action> _queue = new();
        private int _stalled;

        public SerialWorker(string name, TimeSpan timeout)
        {
            Timeout = timeout;
            var thread = new Thread(() =>
            {
                foreach (var work in _queue.GetConsumingEnumerable()) work();
            })
            { IsBackground = true, Name = name };
            thread.Start();
        }

        /// <summary>How long one item (with any wait behind the one before it) may take.</summary>
        public TimeSpan Timeout { get; }

        /// <summary>An item took too long, so nothing runs here any more.</summary>
        public bool Stalled => Volatile.Read(ref _stalled) != 0;

        /// <summary>Raised once, on a thread-pool thread, when an item first takes too long.</summary>
        public event Action? HasStalled;

        /// <summary>Runs <paramref name="work"/> on the worker's thread. Done is false (and the work
        /// may never run) when the worker is stalled, or stalls now. Exceptions come back here.</summary>
        public async Task<(bool Done, T Value)> TryRun<T>(Func<T> work)
        {
            if (Stalled) return (false, default!);
            var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Add(() =>
            {
                try { result.SetResult(work()); }
                catch (Exception ex) { result.SetException(ex); }
            });
            if (await Task.WhenAny(result.Task, Task.Delay(Timeout)).ConfigureAwait(false) == result.Task)
                return (true, await result.Task.ConfigureAwait(false));
            if (Interlocked.Exchange(ref _stalled, 1) == 0) HasStalled?.Invoke();
            return (false, default!);
        }
    }
}
