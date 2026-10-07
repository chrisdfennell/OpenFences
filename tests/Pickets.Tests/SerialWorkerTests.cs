using System;
using System.Threading;
using System.Threading.Tasks;
using Pickets.Services;
using Xunit;

namespace Pickets.Tests
{
    public class SerialWorkerTests
    {
        [Fact]
        public async Task Work_runs_in_order_on_one_thread_of_its_own()
        {
            var worker = new SerialWorker("test", TimeSpan.FromSeconds(10));
            var first = await worker.TryRun(() => Environment.CurrentManagedThreadId);
            var second = await worker.TryRun(() => Environment.CurrentManagedThreadId);

            Assert.True(first.Done && second.Done);
            Assert.Equal(first.Value, second.Value);
            Assert.NotEqual(Environment.CurrentManagedThreadId, first.Value);
            Assert.False(worker.Stalled);
        }

        [Fact]
        public async Task An_exception_comes_back_to_the_caller_and_the_worker_carries_on()
        {
            var worker = new SerialWorker("test", TimeSpan.FromSeconds(10));
            await Assert.ThrowsAsync<InvalidOperationException>(() => worker.TryRun<int>(() => throw new InvalidOperationException()));
            Assert.Equal((true, 7), await worker.TryRun(() => 7));
        }

        [Fact]
        public async Task A_stuck_item_stalls_the_worker_for_good()
        {
            var worker = new SerialWorker("test", TimeSpan.FromMilliseconds(200));
            int stalls = 0;
            worker.HasStalled += () => Interlocked.Increment(ref stalls);
            using var gate = new ManualResetEventSlim();
            try
            {
                var stuck = await worker.TryRun(() => { gate.Wait(); return 1; });
                Assert.False(stuck.Done);
                Assert.True(worker.Stalled);

                // Nothing more is queued behind the stuck item: the answer comes straight back.
                bool ran = false;
                var started = DateTime.UtcNow;
                var next = await worker.TryRun(() => ran = true);
                Assert.False(next.Done);
                Assert.True(DateTime.UtcNow - started < TimeSpan.FromMilliseconds(150));

                gate.Set();
                await Task.Delay(100);
                Assert.False(ran);
                Assert.Equal(1, stalls);
            }
            finally { gate.Set(); }
        }
    }
}
