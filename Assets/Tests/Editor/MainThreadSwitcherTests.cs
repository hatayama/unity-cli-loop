using NUnit.Framework;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.TestTools;

using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies Main Thread Switcher behavior.
    /// </summary>
    public class MainThreadSwitcherTests
    {
        private int mainThreadId;

        [SetUp]
        public void Setup()
        {
            // Record the main thread ID
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        /// <summary>
        /// Verifies that when called from a background thread, it switches to the main thread
        /// </summary>
        [UnityTest]
        public IEnumerator SwitchToMainThread_WhenCalledFromBackgroundThread_ShouldSwitchToMainThread()
        {
            // Arrange
            int backgroundThreadId = -1;
            int afterSwitchThreadId = -1;
            bool taskCompleted = false;

            // Act
            Task.Run(async () =>
            {
                backgroundThreadId = Thread.CurrentThread.ManagedThreadId;

                await MainThreadSwitcher.SwitchToMainThread();

                afterSwitchThreadId = Thread.CurrentThread.ManagedThreadId;
                taskCompleted = true;
            });

            // Wait until the task is completed (maximum 5 seconds)
            float timeoutTime = Time.realtimeSinceStartup + 5f;
            while (!taskCompleted && Time.realtimeSinceStartup < timeoutTime)
            {
                yield return null;
            }

            // Assert
            Assert.That(taskCompleted, Is.True, "Background task should complete within timeout");
            Assert.That(backgroundThreadId, Is.Not.EqualTo(mainThreadId), "Should start on background thread");
            Assert.That(afterSwitchThreadId, Is.EqualTo(mainThreadId), "Should switch to main thread");
        }

        /// <summary>
        /// Verifies that a cancellation arriving between IsCompleted and OnCompleted still resumes the
        /// continuation, and that the cancellation surfaces from GetResult instead of from OnCompleted.
        /// </summary>
        [Test]
        public void Awaiter_WhenCancelledAfterIsCompleted_OnCompletedResumesAndGetResultThrows()
        {
            CancellationTokenSource cancellation = new CancellationTokenSource();
            CancelOnFirstThreadCheckDispatcher dispatcher = new CancelOnFirstThreadCheckDispatcher(cancellation);
            MainThreadSwitcher.RegisterService(dispatcher);

            try
            {
                SwitchToMainThreadAwaitable.Awaiter awaiter =
                    MainThreadSwitcher.SwitchToMainThread(cancellation.Token).GetAwaiter();
                bool isCompleted = awaiter.IsCompleted;
                bool resumed = false;

                Assert.That(isCompleted, Is.False);
                Assert.That(cancellation.IsCancellationRequested, Is.True);
                Assert.DoesNotThrow(() => awaiter.OnCompleted(() => resumed = true));
                Assert.That(resumed, Is.True);
                Assert.Throws<OperationCanceledException>(() => awaiter.GetResult());
            }
            finally
            {
                cancellation.Dispose();
                RestoreEditorMainThreadDispatcher();
            }
        }

        /// <summary>
        /// Verifies that an async method awaiting the switch completes as cancelled, instead of never
        /// completing, when the cancellation lands between IsCompleted and OnCompleted.
        /// </summary>
        [Test]
        public void SwitchToMainThread_WhenCancelledAfterIsCompleted_CompletesTheAwaitingMethodAsCancelled()
        {
            CancellationTokenSource cancellation = new CancellationTokenSource();
            CancelOnFirstThreadCheckDispatcher dispatcher = new CancelOnFirstThreadCheckDispatcher(cancellation);
            MainThreadSwitcher.RegisterService(dispatcher);

            try
            {
                Task switching = AwaitSwitchAsync(cancellation.Token);
                bool completed = ((IAsyncResult)switching).AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(2));

                Assert.That(completed, Is.True, "The awaiting method never completed after the cancellation.");
                Assert.That(switching.IsCanceled, Is.True);
            }
            finally
            {
                cancellation.Dispose();
                RestoreEditorMainThreadDispatcher();
            }
        }

        /// <summary>
        /// Verifies that a switch queued for the Editor main thread reports a wait to the current
        /// observer, and reports its end when the Editor queue resumes the continuation.
        /// </summary>
        [Test]
        public void Awaiter_WhenContinuationIsQueued_ReportsWaitUntilEditorQueueResumes()
        {
            QueueingDispatcher dispatcher = new QueueingDispatcher();
            CountingWaitObserver observer = new CountingWaitObserver();
            MainThreadSwitcher.RegisterService(dispatcher);
            MainThreadWaitObservation.SetCurrent(observer);

            try
            {
                SwitchToMainThreadAwaitable.Awaiter awaiter =
                    MainThreadSwitcher.SwitchToMainThread(CancellationToken.None).GetAwaiter();
                bool resumed = false;

                awaiter.OnCompleted(() => resumed = true);

                Assert.That(observer.Started, Is.EqualTo(1));
                Assert.That(observer.Ended, Is.EqualTo(0));

                dispatcher.RunQueued();

                Assert.That(resumed, Is.True);
                Assert.That(observer.Started, Is.EqualTo(1));
                Assert.That(observer.Ended, Is.EqualTo(1));
            }
            finally
            {
                MainThreadWaitObservation.SetCurrent(null);
                RestoreEditorMainThreadDispatcher();
            }
        }

        /// <summary>
        /// Verifies that a queued switch resumed by cancellation reports the end of its wait exactly
        /// once, even when the stalled Editor queue later runs the same continuation.
        /// </summary>
        [Test]
        public void Awaiter_WhenQueuedSwitchIsCancelled_ReportsWaitEndedOnce()
        {
            QueueingDispatcher dispatcher = new QueueingDispatcher();
            CountingWaitObserver observer = new CountingWaitObserver();
            MainThreadSwitcher.RegisterService(dispatcher);
            MainThreadWaitObservation.SetCurrent(observer);
            CancellationTokenSource cancellation = new CancellationTokenSource();

            try
            {
                SwitchToMainThreadAwaitable.Awaiter awaiter =
                    MainThreadSwitcher.SwitchToMainThread(cancellation.Token).GetAwaiter();
                int resumeCount = 0;

                awaiter.OnCompleted(() => resumeCount++);
                cancellation.Cancel();

                Assert.That(resumeCount, Is.EqualTo(1));
                Assert.That(observer.Ended, Is.EqualTo(1));

                dispatcher.RunQueued();

                Assert.That(resumeCount, Is.EqualTo(1));
                Assert.That(observer.Started, Is.EqualTo(1));
                Assert.That(observer.Ended, Is.EqualTo(1));
            }
            finally
            {
                cancellation.Dispose();
                MainThreadWaitObservation.SetCurrent(null);
                RestoreEditorMainThreadDispatcher();
            }
        }

        /// <summary>
        /// Verifies that a switch that needs no queueing, because the caller is already on the main
        /// thread, reports no wait to the observer.
        /// </summary>
        [Test]
        public void Awaiter_WhenAlreadyOnMainThread_ReportsNoWait()
        {
            CountingWaitObserver observer = new CountingWaitObserver();
            MainThreadSwitcher.RegisterService(new MainThreadReportingDispatcher());
            MainThreadWaitObservation.SetCurrent(observer);

            try
            {
                SwitchToMainThreadAwaitable.Awaiter awaiter =
                    MainThreadSwitcher.SwitchToMainThread(CancellationToken.None).GetAwaiter();
                bool resumed = false;

                Assert.That(awaiter.IsCompleted, Is.True);
                awaiter.OnCompleted(() => resumed = true);

                Assert.That(resumed, Is.True);
                Assert.That(observer.Started, Is.EqualTo(0));
                Assert.That(observer.Ended, Is.EqualTo(0));
            }
            finally
            {
                MainThreadWaitObservation.SetCurrent(null);
                RestoreEditorMainThreadDispatcher();
            }
        }

        private static async Task AwaitSwitchAsync(CancellationToken ct)
        {
            await MainThreadSwitcher.SwitchToMainThread(ct);
        }

        private static void RestoreEditorMainThreadDispatcher()
        {
            EditorMainThreadDispatcher dispatcher = new EditorMainThreadDispatcher();
            MainThreadSwitcher.RegisterService(dispatcher);
            dispatcher.Initialize();
        }

        // Reports a background thread and keeps queued continuations until the test runs them, as a
        // stalled Editor main thread would.
        private sealed class QueueingDispatcher : IMainThreadDispatcher
        {
            private readonly List<Action> _queued = new();

            public bool IsMainThread => false;

            public void Initialize()
            {
            }

            public void AddContinuation(Action continuation)
            {
                _queued.Add(continuation);
            }

            public void RunQueued()
            {
                foreach (Action continuation in _queued)
                {
                    continuation();
                }

                _queued.Clear();
            }
        }

        private sealed class MainThreadReportingDispatcher : IMainThreadDispatcher
        {
            public bool IsMainThread => true;

            public void Initialize()
            {
            }

            public void AddContinuation(Action continuation)
            {
                Assert.Fail("A caller already on the main thread must not queue a continuation.");
            }
        }

        private sealed class CountingWaitObserver : IMainThreadWaitObserver
        {
            public int Started { get; private set; }

            public int Ended { get; private set; }

            public void OnWaitStarted()
            {
                Started++;
            }

            public void OnWaitEnded()
            {
                Ended++;
            }
        }

        // Reports a background thread and cancels on the first check, which reproduces a cancellation
        // landing right after IsCompleted returned false. Queued continuations are dropped, as they are
        // while Unity's main thread is stalled.
        private sealed class CancelOnFirstThreadCheckDispatcher : IMainThreadDispatcher
        {
            private readonly CancellationTokenSource _cancellation;
            private int _threadChecks;

            public CancelOnFirstThreadCheckDispatcher(CancellationTokenSource cancellation)
            {
                _cancellation = cancellation;
            }

            public bool IsMainThread
            {
                get
                {
                    _threadChecks++;
                    if (_threadChecks == 1)
                    {
                        _cancellation.Cancel();
                    }

                    return false;
                }
            }

            public void Initialize()
            {
            }

            public void AddContinuation(Action continuation)
            {
                Assert.That(continuation, Is.Not.Null);
            }
        }
    }
}
