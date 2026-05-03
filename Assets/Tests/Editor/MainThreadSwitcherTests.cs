using NUnit.Framework;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.TestTools;

namespace io.github.hatayama.UnityCliLoop
{
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
        /// Verifies that when called from a background thread, it can switch back to main thread
        /// </summary>
        [UnityTest]
        public IEnumerator SwitchToMainThread_WhenCalledFromBackgroundThread_ShouldSwitchBackToMainThread()
        {
            // Arrange
            bool executedImmediately = false;
            int executionThreadId = -1;
            bool completed = false;

            // Act
            Task.Run(async () =>
            {
                try
                {
                    await MainThreadSwitcher.SwitchToMainThread();
                    executedImmediately = true;
                    executionThreadId = Thread.CurrentThread.ManagedThreadId;
                    completed = true;
                }
                catch (System.Exception ex)
                {
                    UnityEngine.Debug.LogError($"Test failed: {ex.Message}");
                    completed = true;
                }
            });

            // Wait for completion
            yield return new UnityEngine.WaitUntil(() => completed);

            // Assert
            Assert.That(executedImmediately, Is.True, "Should execute when called from background thread");
            Assert.That(executionThreadId, Is.EqualTo(mainThreadId), "Should switch to main thread");
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
        /// Verifies that PlayerLoopTiming can be specified
        /// </summary>
        [UnityTest]
        public IEnumerator SwitchToMainThread_WithPlayerLoopTiming_ShouldAcceptTiming()
        {
            // Arrange
            PlayerLoopTiming timing = PlayerLoopTiming.FixedUpdate;
            bool executed = false;
            bool completed = false;

            // Act
            Task.Run(async () =>
            {
                try
                {
                    await MainThreadSwitcher.SwitchToMainThread(timing);
                    executed = true;
                    completed = true;
                }
                catch (System.Exception ex)
                {
                    UnityEngine.Debug.LogError($"Test failed: {ex.Message}");
                    completed = true;
                }
            });

            // Wait for completion
            yield return new UnityEngine.WaitUntil(() => completed);

            // Assert
            Assert.That(executed, Is.True, "Should execute with specified timing");
        }

    }
}
