using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests the watchdog that fails a request still waiting for a blocked Editor main thread.
    /// </summary>
    public sealed class MainThreadEntryWatchdogTests
    {
        /// <summary>
        /// Verifies a stall below the threshold leaves the waiting request alone.
        /// </summary>
        [Test]
        public void Observe_WhenStallBelowThreshold_LeavesLeaseAlone()
        {
            ToolExecutionLease lease = new ToolExecutionSession().TryEnter("tool").Lease;
            ManualPolls polls = new ManualPolls();
            MainThreadEntryWatchdog watchdog = new MainThreadEntryWatchdog(new ScriptedLiveness(29.9), polls.WaitOnePoll);

            bool cancelled = watchdog.Observe(lease);

            Assert.That(cancelled, Is.False);
            Assert.That(lease.ExecutionToken.IsCancellationRequested, Is.False);
            lease.Dispose();
        }

        /// <summary>
        /// Verifies a stall at the threshold cancels the waiting request and records the stall it saw.
        /// </summary>
        [Test]
        public void Observe_WhenStallAtThreshold_CancelsLeaseWithStallSeconds()
        {
            ToolExecutionLease lease = new ToolExecutionSession().TryEnter("tool").Lease;
            ManualPolls polls = new ManualPolls();
            MainThreadEntryWatchdog watchdog = new MainThreadEntryWatchdog(new ScriptedLiveness(30.0), polls.WaitOnePoll);

            bool cancelled = watchdog.Observe(lease);

            Assert.That(cancelled, Is.True);
            Assert.That(lease.WasCancelledForBlockedMainThread, Is.True);
            Assert.That(lease.BlockedMainThreadStallSeconds, Is.EqualTo(30));
            lease.Dispose();
        }

        /// <summary>
        /// Verifies the first stall check waits for one poll, so an idle Editor that only slept is never judged.
        /// </summary>
        [Test]
        public void GuardAsync_WhenStoppedBeforeFirstPoll_NeverReadsLiveness()
        {
            ToolExecutionLease lease = new ToolExecutionSession().TryEnter("tool").Lease;
            ManualPolls polls = new ManualPolls();
            ScriptedLiveness liveness = new ScriptedLiveness(45);
            MainThreadEntryWatchdog watchdog = new MainThreadEntryWatchdog(liveness, polls.WaitOnePoll);
            CancellationTokenSource stop = new CancellationTokenSource();
            SynchronizationContext originalContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);

            try
            {
                Task guardTask = watchdog.GuardAsync(lease, stop.Token);

                Assert.That(polls.Count, Is.EqualTo(1));
                Assert.That(liveness.Reads, Is.EqualTo(0));

                stop.Cancel();

                Assert.That(guardTask.Status, Is.EqualTo(TaskStatus.RanToCompletion));
                Assert.That(liveness.Reads, Is.EqualTo(0));
                Assert.That(lease.ExecutionToken.IsCancellationRequested, Is.False);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(originalContext);
                stop.Dispose();
                lease.Dispose();
            }
        }

        /// <summary>
        /// Verifies the watchdog keeps polling while the stall grows and cancels the request once it reaches the threshold.
        /// </summary>
        [Test]
        public void GuardAsync_PollsUntilTheStallReachesTheThreshold()
        {
            ToolExecutionLease lease = new ToolExecutionSession().TryEnter("tool").Lease;
            ManualPolls polls = new ManualPolls();
            ScriptedLiveness liveness = new ScriptedLiveness(10, 20, 45);
            MainThreadEntryWatchdog watchdog = new MainThreadEntryWatchdog(liveness, polls.WaitOnePoll);
            CancellationTokenSource stop = new CancellationTokenSource();
            SynchronizationContext originalContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);

            try
            {
                Task guardTask = watchdog.GuardAsync(lease, stop.Token);

                polls.CompleteLatest();
                Assert.That(lease.ExecutionToken.IsCancellationRequested, Is.False);
                Assert.That(polls.Count, Is.EqualTo(2));

                polls.CompleteLatest();
                Assert.That(lease.ExecutionToken.IsCancellationRequested, Is.False);
                Assert.That(polls.Count, Is.EqualTo(3));

                polls.CompleteLatest();
                Assert.That(lease.WasCancelledForBlockedMainThread, Is.True);
                Assert.That(guardTask.Status, Is.EqualTo(TaskStatus.RanToCompletion));
                Assert.That(polls.Count, Is.EqualTo(3));
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(originalContext);
                stop.Cancel();
                stop.Dispose();
                lease.Dispose();
            }
        }
    }

    // Stands in for the one-second poll: each poll is a pending task the test completes by hand,
    // and stopping the watchdog cancels the pending one so no background work is left behind.
    internal sealed class ManualPolls
    {
        private readonly List<TaskCompletionSource<bool>> _polls = new List<TaskCompletionSource<bool>>();

        internal int Count => _polls.Count;

        internal Task Latest => _polls[_polls.Count - 1].Task;

        internal Task WaitOnePoll(CancellationToken ct)
        {
            TaskCompletionSource<bool> poll = new TaskCompletionSource<bool>();
            ct.Register(() => poll.TrySetCanceled());
            _polls.Add(poll);
            return poll.Task;
        }

        internal void CompleteLatest()
        {
            _polls[_polls.Count - 1].SetResult(true);
        }
    }

    // Returns the scripted stall values in order (repeating the last one) and runs an optional hook
    // at each read, so a test can let the main thread resume while the watchdog is deciding.
    internal sealed class ScriptedLiveness : IMainThreadLivenessPort
    {
        private readonly double[] _values;

        internal ScriptedLiveness(params double[] values)
        {
            _values = values;
        }

        internal int Reads { get; private set; }

        internal Action OnRead { get; set; }

        public double SecondsSinceLastMainThreadTick()
        {
            OnRead?.Invoke();
            double value = _values[Math.Min(Reads, _values.Length - 1)];
            Reads++;
            return value;
        }
    }
}
