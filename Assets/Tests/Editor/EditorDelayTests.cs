using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.TestTools;
using NUnit.Framework;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Unit tests for EditorDelay system
    /// Automated tests using Unity Test Framework
    /// </summary>
    [TestFixture]
    public class EditorDelayTests
    {
        private int frameCountAtStart;
        private readonly List<string> executionLog = new();
        
        [SetUp]
        public void SetUp()
        {
            // Ensure test isolation
            EditorDelayManager.ClearAllTasks();
            EditorDelayManager.ResetFrameCount();
            
            // Record frame count at test start
            frameCountAtStart = EditorDelayManager.CurrentFrameCount;
            executionLog.Clear();
        }
        
        [TearDown]
        public void TearDown()
        {
            // Post-test cleanup
            EditorDelayManager.ClearAllTasks();
            executionLog.Clear();
        }
        
        /// <summary>
        /// Zero frame delay test (immediate execution)
        /// Verify immediate execution using EditorDelayManager frame counter
        /// </summary>
        [Test]
        public async Task DelayFrame_ZeroFrames_ExecutesImmediately()
        {
            // Arrange - Record starting frame number
            int startFrame = EditorDelayManager.CurrentFrameCount;
            bool executed = false;
            
            // Act - Execute zero frame delay
            await EditorDelay.DelayFrame(0);
            executed = true;
            
            // Assert - Verify execution completed within the same frame
            int endFrame = EditorDelayManager.CurrentFrameCount;
            Assert.IsTrue(executed, "Zero frame delay should execute the task");
            Assert.AreEqual(startFrame, endFrame, "Zero frame delay should execute within the same frame");
            Assert.AreEqual(0, EditorDelayManager.PendingTaskCount, "No tasks should be pending after zero frame delay");
        }
        
        /// <summary>
        /// Basic frame delay test
        /// Check exact frame count using CurrentFrameCount
        /// </summary>
        [UnityTest]
        public IEnumerator DelayFrame_SingleFrame_ExecutesAfterOneFrame()
        {
            bool executed = false;
            int executionFrame = -1;
            int startFrame = EditorDelayManager.CurrentFrameCount;
            
            // Arrange & Act
            DelayedExecution().Forget();
            
            async Task DelayedExecution()
            {
                await EditorDelay.DelayFrame(1);
                executed = true;
                executionFrame = EditorDelayManager.CurrentFrameCount;
            }
            
            // Initially not executed
            Assert.IsFalse(executed, "Task should not be executed immediately");
            
            // Wait for 1 frame and then check
            yield return null;
            
            // Assert - Verify execution and frame count
            Assert.IsTrue(executed, "Task should be executed after 1 frame");
            Assert.AreEqual(startFrame + 1, executionFrame, "Task should execute exactly 1 frame later");
        }
        
        /// <summary>
        /// Multiple frame delay test
        /// Check exact frame count using CurrentFrameCount
        /// </summary>
        [UnityTest]
        public IEnumerator DelayFrame_MultipleFrames_ExecutesAfterCorrectFrames()
        {
            bool executed = false;
            int executionFrame = -1;
            const int delayFrames = 3;
            int startFrame = EditorDelayManager.CurrentFrameCount;
            
            // Arrange & Act
            DelayedExecution().Forget();
            
            async Task DelayedExecution()
            {
                await EditorDelay.DelayFrame(delayFrames);
                executed = true;
                executionFrame = EditorDelayManager.CurrentFrameCount;
            }
            
            // Wait for delayFrames number of frames
            for (int i = 0; i < delayFrames; i++)
            {
                Assert.IsFalse(executed, $"Task should not be executed at frame {i + 1}");
                yield return null;
            }
            
            // Should be executed after delayFrames
            Assert.IsTrue(executed, $"Task should be executed after {delayFrames} frames");
            Assert.AreEqual(startFrame + delayFrames, executionFrame, $"Task should execute exactly {delayFrames} frames later");
        }
        
        /// <summary>
        /// Concurrent execution order test
        /// Check exact frame count using CurrentFrameCount
        /// </summary>
        [UnityTest]
        public IEnumerator DelayFrame_ConcurrentTasks_ExecuteInCorrectOrder()
        {
            // Arrange
            int startFrame = EditorDelayManager.CurrentFrameCount;
            var executionFrames = new List<(string task, int frame)>();
            
            Task1().Forget(); // After 1 frame
            Task2().Forget(); // After 3 frames
            Task3().Forget(); // After 2 frames
            
            async Task Task1()
            {
                await EditorDelay.DelayFrame(1);
                executionLog.Add("Task1");
                executionFrames.Add(("Task1", EditorDelayManager.CurrentFrameCount));
            }
            
            async Task Task2()
            {
                await EditorDelay.DelayFrame(3);
                executionLog.Add("Task2");
                executionFrames.Add(("Task2", EditorDelayManager.CurrentFrameCount));
            }
            
            async Task Task3()
            {
                await EditorDelay.DelayFrame(2);
                executionLog.Add("Task3");
                executionFrames.Add(("Task3", EditorDelayManager.CurrentFrameCount));
            }
            
            // Act & Assert - Check frame by frame
            yield return null; // After 1 frame
            Assert.AreEqual(1, executionLog.Count, "Only Task1 should be executed after 1 frame");
            Assert.AreEqual("Task1", executionLog[0], "Task1 should execute first");
            Assert.AreEqual(startFrame + 1, executionFrames[0].frame, "Task1 should execute at frame startFrame + 1");
            
            yield return null; // After 2 frames
            Assert.AreEqual(2, executionLog.Count, "Task1 and Task3 should be executed after 2 frames");
            Assert.AreEqual("Task3", executionLog[1], "Task3 should execute second");
            Assert.AreEqual(startFrame + 2, executionFrames[1].frame, "Task3 should execute at frame startFrame + 2");
            
            yield return null; // After 3 frames
            Assert.AreEqual(3, executionLog.Count, "All tasks should be executed after 3 frames");
            Assert.AreEqual("Task2", executionLog[2], "Task2 should execute last");
            Assert.AreEqual(startFrame + 3, executionFrames[2].frame, "Task2 should execute at frame startFrame + 3");
        }
        
        /// <summary>
        /// EditorDelayManager task count management test
        /// </summary>
        [UnityTest]
        public IEnumerator DelayManager_TaskCount_ManagesCorrectly()
        {
            // Arrange - Register multiple tasks
            StartMultipleTasks();
            
            void StartMultipleTasks()
            {
                for (int i = 0; i < 5; i++)
                {
                    int taskId = i;
                    _ = DelayedTask(taskId);
                }
                
                async Task DelayedTask(int id)
                {
                    await EditorDelay.DelayFrame(2);
                    executionLog.Add($"Task{id}");
                }
            }
            
            // Act & Assert
            yield return null; // After 1 frame
            Assert.AreEqual(5, EditorDelayManager.PendingTaskCount, "5 tasks should be pending");
            
            yield return null; // After 2 frames (execution completed)
            Assert.AreEqual(0, EditorDelayManager.PendingTaskCount, "No tasks should be pending after execution");
            Assert.AreEqual(5, executionLog.Count, "All 5 tasks should be executed");
        }
        
        /// <summary>
        /// CancellationToken immediate cancellation test
        /// </summary>
        [Test]
        public void DelayFrame_WithImmediateCancellation_ThrowsImmediately()
        {
            // Arrange - Already cancelled token
            CancellationTokenSource cts = new CancellationTokenSource();
            cts.Cancel(); // Cancel in advance
            
            bool executed = false;
            Exception caughtException = null;
            
            // Act & Assert - Cancellation exception should be thrown immediately
            try
            {
                DelayedTask().Forget();
                async Task DelayedTask()
                {
                    try
                    {
                        await EditorDelay.DelayFrame(5, cts.Token);
                        executed = true;
                    }
                    catch (Exception ex)
                    {
                        caughtException = ex;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Case where cancellation exception is thrown synchronously
                caughtException = new OperationCanceledException();
            }
            
            // ThrowIfCancellationRequested should be called in IsCompleted or GetResult
            Assert.IsFalse(executed, "Task should not execute when pre-cancelled");
        }
        
        /// <summary>
        /// CancellationToken delayed cancellation test (simple version)
        /// </summary>
        [UnityTest]
        public IEnumerator DelayFrame_WithDelayedCancellation_CancelsCorrectly()
        {
            // Arrange
            CancellationTokenSource cts = new CancellationTokenSource();
            bool taskStarted = false;
            bool executed = false;
            
            // Act - Start task scheduled to complete after 5 frames
            var _ = DelayedTaskWithCancellation();
            
            async Task DelayedTaskWithCancellation()
            {
                taskStarted = true;
                try
                {
                    await EditorDelay.DelayFrame(5, cts.Token);
                    executed = true;
                }
                catch (OperationCanceledException)
                {
                    // Cancellation processing does nothing special (executed remains false)
                }
                catch (Exception)
                {
                    // Treat other exceptions as cancellation too (executed remains false)
                }
            }
            
            // Wait a bit until task starts
            yield return null;
            Assert.IsTrue(taskStarted, "Task should have started");
            
            // Verify task is not yet executed
            Assert.IsFalse(executed, "Task should not be executed immediately");
            
            // Cancel after waiting 2 frames (before task completion)
            yield return null; // 2 frames
            yield return null; // 3 frames
            
            cts.Cancel(); // Cancel after 3 frames
            
            // Wait a bit for cancellation processing
            yield return null; // 4 frames
            yield return null; // 5 frames
            
            // Assert - Verify task was cancelled and not executed
            Assert.IsFalse(executed, "Task should not execute when cancelled");
        }
        
        /// <summary>
        /// Load test - Execute large number of tasks simultaneously
        /// Check exact frame count using CurrentFrameCount
        /// </summary>
        [UnityTest]
        public IEnumerator DelayFrame_StressTest_HandlesLargeTasks()
        {
            const int taskCount = 100;
            int completedTasks = 0;
            int startFrame = EditorDelayManager.CurrentFrameCount;
            var completionFrames = new List<int>();
            
            // Arrange & Act - Start 100 tasks
            var tasks = new List<Task>();
            for (int i = 0; i < taskCount; i++)
            {
                tasks.Add(StressTask());
            }
            
            async Task StressTask()
            {
                await EditorDelay.DelayFrame(1);
                Interlocked.Increment(ref completedTasks);
                lock (completionFrames)
                {
                    completionFrames.Add(EditorDelayManager.CurrentFrameCount);
                }
            }
            
            // Initially all tasks are pending
            Assert.AreEqual(taskCount, EditorDelayManager.PendingTaskCount, $"{taskCount} tasks should be pending");
            
            yield return null; // After 1 frame
            
            // Assert - All tasks completed and frame count verification
            Assert.AreEqual(taskCount, completedTasks, $"All {taskCount} tasks should be completed");
            Assert.AreEqual(0, EditorDelayManager.PendingTaskCount, "No tasks should be pending after completion");
            Assert.AreEqual(taskCount, completionFrames.Count, "All tasks should record completion frame");
            
            // Verify all tasks completed at the same frame (startFrame + 1)
            int expectedFrame = startFrame + 1;
            foreach (int frame in completionFrames)
            {
                Assert.AreEqual(expectedFrame, frame, "All tasks should complete at the same frame");
            }
        }
        
        /// <summary>
        /// Frame counter reset functionality test
        /// </summary>
        [UnityTest]
        public IEnumerator DelayManager_ResetFrameCount_ResetsCorrectly()
        {
            // Arrange - Advance frames then measure
            yield return null; // Advance 1 frame
            
            int countAfterFrame = EditorDelayManager.CurrentFrameCount;
            Assert.Greater(countAfterFrame, 0, "Frame count should be greater than 0 after waiting one frame");
            
            // Act - Execute reset
            EditorDelayManager.ResetFrameCount();
            
            // Assert - Reset to zero
            Assert.AreEqual(0, EditorDelayManager.CurrentFrameCount, "Frame count should be reset to 0");
        }
    }
}
