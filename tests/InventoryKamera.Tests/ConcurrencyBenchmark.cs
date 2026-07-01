using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace InventoryKamera.Tests
{
    /// <summary>
    /// Measures the latency characteristics that changed in the §1.2/1.3 concurrency rework
    /// (Channels + BlockingCollection replacing a hand-rolled polling Queue and a ConcurrentBag
    /// spin-wait). This does NOT benchmark the end-to-end game scan — that needs recorded
    /// screenshots or a live game session, which this environment doesn't have. What it proves
    /// instead: "time from an item becoming available to the consumer noticing it" — the specific
    /// thing a fixed-interval poll bounds from below — is far lower with the new primitives.
    ///
    /// Results are written to test output (run with `dotnet test -l "console;verbosity=detailed"`
    /// to see them) rather than asserted as a hard performance gate, since timing is inherently
    /// noisy on shared CI hardware. The assertions kept are generous sanity bounds well under the
    /// old poll intervals (250ms / 10ms, matching the actual production values before this rework),
    /// not tight thresholds.
    /// </summary>
    public class ConcurrencyBenchmark
    {
        private readonly ITestOutputHelper output;

        public ConcurrencyBenchmark(ITestOutputHelper output) => this.output = output;

        private const int Trials = 20;
        private const int OldQueuePollIntervalMs = 250; // matches the removed ImageProcessorWorker's Thread.Sleep(250)
        private const int OldEnginePollIntervalMs = 10;  // matches the removed AnalyzeText's Thread.Sleep(10)

        [Fact]
        public async Task ChannelNoticeLatency_IsFarBelow_OldPollingQueueInterval()
        {
            var channelLatency = await MeasureChannelNoticeLatencyAsync(Trials);
            var pollingLatency = MeasurePollingQueueNoticeLatency(Trials, OldQueuePollIntervalMs);

            output.WriteLine($"Channel.ReadAsync notice latency:        avg {channelLatency.TotalMilliseconds:F2}ms (n={Trials})");
            output.WriteLine($"Old lock-queue + Thread.Sleep({OldQueuePollIntervalMs}) notice latency: avg {pollingLatency.TotalMilliseconds:F2}ms (n={Trials})");
            output.WriteLine($"Improvement: {pollingLatency.TotalMilliseconds / Math.Max(channelLatency.TotalMilliseconds, 0.001):F1}x lower latency");

            // Sanity bound: comfortably under the old poll interval, not a tight perf gate.
            Assert.True(channelLatency < TimeSpan.FromMilliseconds(OldQueuePollIntervalMs / 5.0),
                $"Expected Channel notice latency well under the old {OldQueuePollIntervalMs}ms poll interval, was {channelLatency.TotalMilliseconds}ms");
        }

        [Fact]
        public void BlockingCollectionNoticeLatency_IsFarBelow_OldSpinWaitInterval()
        {
            var blockingLatency = MeasureBlockingCollectionNoticeLatency(Trials);
            var spinWaitLatency = MeasureSpinWaitNoticeLatency(Trials, OldEnginePollIntervalMs);

            output.WriteLine($"BlockingCollection.Take() notice latency:   avg {blockingLatency.TotalMilliseconds:F2}ms (n={Trials})");
            output.WriteLine($"Old ConcurrentBag + Thread.Sleep({OldEnginePollIntervalMs}) notice latency: avg {spinWaitLatency.TotalMilliseconds:F2}ms (n={Trials})");
            output.WriteLine($"Improvement: {spinWaitLatency.TotalMilliseconds / Math.Max(blockingLatency.TotalMilliseconds, 0.001):F1}x lower latency");

            Assert.True(blockingLatency < TimeSpan.FromMilliseconds(OldEnginePollIntervalMs / 2.0),
                $"Expected BlockingCollection notice latency well under the old {OldEnginePollIntervalMs}ms spin-wait interval, was {blockingLatency.TotalMilliseconds}ms");
        }

        /// <summary>Time from writing an item to a Channel until a waiting reader observes it.</summary>
        private static async Task<TimeSpan> MeasureChannelNoticeLatencyAsync(int trials)
        {
            var latencies = new double[trials];
            for (int t = 0; t < trials; t++)
            {
                var channel = Channel.CreateUnbounded<int>();
                var readTask = channel.Reader.ReadAsync().AsTask();
                await Task.Delay(2); // let the reader start actually waiting on an empty channel

                var sw = Stopwatch.StartNew();
                channel.Writer.TryWrite(1);
                await readTask;
                sw.Stop();

                latencies[t] = sw.Elapsed.TotalMilliseconds;
            }
            return TimeSpan.FromMilliseconds(latencies.Average());
        }

        /// <summary>Reproduces the removed design's shape: a lock-guarded queue plus a consumer polling with Thread.Sleep.</summary>
        private static TimeSpan MeasurePollingQueueNoticeLatency(int trials, int pollIntervalMs)
        {
            var latencies = new double[trials];
            for (int t = 0; t < trials; t++)
            {
                var locker = new object();
                var queue = new System.Collections.Generic.Queue<int>();
                var sw = new Stopwatch();

                var consumer = Task.Run(() =>
                {
                    while (true)
                    {
                        bool gotItem;
                        lock (locker) gotItem = queue.Count > 0;
                        if (gotItem) return;
                        Thread.Sleep(pollIntervalMs);
                    }
                });
                Thread.Sleep(2); // ensure the consumer is already in its wait loop on an empty queue

                sw.Start();
                lock (locker) queue.Enqueue(1);
                consumer.Wait();
                sw.Stop();

                latencies[t] = sw.Elapsed.TotalMilliseconds;
            }
            return TimeSpan.FromMilliseconds(latencies.Average());
        }

        /// <summary>Time from adding an item to a BlockingCollection until a waiting Take() observes it.</summary>
        private static TimeSpan MeasureBlockingCollectionNoticeLatency(int trials)
        {
            var latencies = new double[trials];
            for (int t = 0; t < trials; t++)
            {
                var pool = new BlockingCollection<int>();
                var sw = new Stopwatch();

                var consumer = Task.Run(() => pool.Take());
                Thread.Sleep(2); // ensure the consumer is already blocked on an empty collection

                sw.Start();
                pool.Add(1);
                consumer.Wait();
                sw.Stop();

                latencies[t] = sw.Elapsed.TotalMilliseconds;
            }
            return TimeSpan.FromMilliseconds(latencies.Average());
        }

        /// <summary>Reproduces the removed design's shape: a ConcurrentBag plus a spin-wait loop.</summary>
        private static TimeSpan MeasureSpinWaitNoticeLatency(int trials, int pollIntervalMs)
        {
            var latencies = new double[trials];
            for (int t = 0; t < trials; t++)
            {
                var pool = new ConcurrentBag<int>();
                var sw = new Stopwatch();

                var consumer = Task.Run(() =>
                {
                    while (!pool.TryTake(out _)) Thread.Sleep(pollIntervalMs);
                });
                Thread.Sleep(2); // ensure the consumer is already spinning on an empty bag

                sw.Start();
                pool.Add(1);
                consumer.Wait();
                sw.Stop();

                latencies[t] = sw.Elapsed.TotalMilliseconds;
            }
            return TimeSpan.FromMilliseconds(latencies.Average());
        }
    }
}
