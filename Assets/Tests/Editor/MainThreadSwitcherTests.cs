using NUnit.Framework;
using System;
using System.Collections;
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
