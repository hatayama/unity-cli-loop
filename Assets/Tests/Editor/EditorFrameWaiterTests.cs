using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies Editor frame and wall-clock wait primitives.
    /// </summary>
    [TestFixture]
    public sealed class EditorFrameWaiterTests
    {
        [SetUp]
        public void SetUp()
        {
            EditorFrameWaiter.ClearAllForTests();
            EditorFrameWaiter.ResetFrameCountForTests();
            EditorFrameWaiter.InitializeForEditorStartup();
        }

        [TearDown]
        public void TearDown()
        {
            EditorFrameWaiter.ClearAllForTests();
        }

        [Test]
        public async Task WaitFramesAsync_WithZeroFrames_CompletesImmediately()
        {
            // Verifies that zero-frame waits do not subscribe to Editor update.
            int startFrame = EditorFrameWaiter.CurrentFrameCount;

            await EditorFrameWaiter.WaitFramesAsync(0, CancellationToken.None);

            Assert.AreEqual(startFrame, EditorFrameWaiter.CurrentFrameCount);
            Assert.AreEqual(0, EditorFrameWaiter.PendingWaitCount);
        }

        [UnityTest]
        public IEnumerator WaitFramesAsync_WithSingleFrame_CompletesAfterOneEditorUpdate()
        {
            // Verifies that a one-frame wait resumes after the next Editor update.
            bool completed = false;
            int completionFrame = -1;
            int startFrame = EditorFrameWaiter.CurrentFrameCount;

            WaitAsync().Forget();

            Assert.IsFalse(completed);
            yield return null;

            Assert.IsTrue(completed);
            Assert.AreEqual(startFrame + 1, completionFrame);

            async Task WaitAsync()
            {
                await EditorFrameWaiter.WaitFramesAsync(1, CancellationToken.None);
                completed = true;
                completionFrame = EditorFrameWaiter.CurrentFrameCount;
            }
        }

        [UnityTest]
        public IEnumerator WaitFramesAsync_WithConcurrentWaits_CompletesByTargetFrame()
        {
            // Verifies that concurrent frame waits complete in target-frame order.
            List<string> executionLog = new List<string>();
            int startFrame = EditorFrameWaiter.CurrentFrameCount;

            WaitAsync("one", 1, executionLog).Forget();
            WaitAsync("three", 3, executionLog).Forget();
            WaitAsync("two", 2, executionLog).Forget();

            yield return null;
            CollectionAssert.AreEqual(new[] { "one" }, executionLog);
            Assert.AreEqual(startFrame + 1, EditorFrameWaiter.CurrentFrameCount);

            yield return null;
            CollectionAssert.AreEqual(new[] { "one", "two" }, executionLog);

            yield return null;
            CollectionAssert.AreEqual(new[] { "one", "two", "three" }, executionLog);

            async Task WaitAsync(string label, int frameCount, List<string> log)
            {
                await EditorFrameWaiter.WaitFramesAsync(frameCount, CancellationToken.None);
                log.Add(label);
            }
        }

        [UnityTest]
        public IEnumerator WaitFramesAsync_WhenCancelledBeforeTarget_CancelsWithoutWaitingForTargetFrame()
        {
            // Verifies that cancellation releases the pending wait before the target frame arrives.
            CancellationTokenSource cts = new CancellationTokenSource();
            Task waitTask = EditorFrameWaiter.WaitFramesAsync(5, cts.Token);

            yield return null;
            Assert.AreEqual(1, EditorFrameWaiter.PendingWaitCount);

            cts.Cancel();
            yield return new WaitUntil(() => waitTask.IsCompleted);

            Assert.IsTrue(waitTask.IsCanceled);
            Assert.AreEqual(0, EditorFrameWaiter.PendingWaitCount);
            cts.Dispose();
        }

        [Test]
        public async Task TimerDelay_Wait_CompletesWithoutEditorFrameDependency()
        {
            // Verifies that wall-clock waits do not register Editor frame wait requests.
            Assert.AreEqual(0, EditorFrameWaiter.PendingWaitCount);

            await TimerDelay.Wait(10, CancellationToken.None);

            Assert.AreEqual(0, EditorFrameWaiter.PendingWaitCount);
        }

        [UnityTest]
        public IEnumerator WaitThenExecuteOnMainThread_WhenActionThrows_FaultsReturnedTask()
        {
            // Verifies that delayed main-thread action failures are observable by the awaiting caller.
            Task waitTask = TimerDelay.WaitThenExecuteOnMainThread(
                1,
                () => throw new InvalidOperationException("Delayed action failed."),
                CancellationToken.None);
            float startTime = Time.realtimeSinceStartup;

            while (!waitTask.IsCompleted && Time.realtimeSinceStartup - startTime < 2f)
            {
                yield return null;
            }

            Assert.IsTrue(waitTask.IsFaulted);
            Exception exception = waitTask.Exception?.GetBaseException();
            Assert.IsInstanceOf<InvalidOperationException>(exception);
            Assert.AreEqual("Delayed action failed.", exception.Message);
        }
    }
}
