using System;
using System.Collections.Generic;
using System.Threading;
using UnityEditor;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Delay processing management class for Unity Editor
    /// Driven by EditorApplication.update to manage frame-based waiting processes
    /// </summary>
    [InitializeOnLoad]
    public static class EditorDelayManager
    {
        private static readonly List<DelayTask> delayTasks = new();
        private static readonly object lockObject = new();
        private static int currentFrameCount = 0;

        /// <summary>
        /// Class representing a waiting task
        /// </summary>
        private class DelayTask
        {
            public Action Continuation { get; }
            public int RemainingFrames { get; set; }
            public CancellationToken CancellationToken { get; }

            public DelayTask(Action continuation, int frames, CancellationToken cancellationToken)
            {
                Continuation = continuation ?? throw new ArgumentNullException(nameof(continuation));
                RemainingFrames = frames;
                CancellationToken = cancellationToken;
            }
        }

        /// <summary>
        /// Static constructor
        /// Register frame processing to EditorApplication.update
        /// </summary>
        static EditorDelayManager()
        {
            EditorApplication.update += UpdateDelayTasks;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        /// <summary>
        /// Register a new waiting task
        /// </summary>
        /// <param name="continuation">Process to execute after waiting completion</param>
        /// <param name="frames">Number of frames to wait</param>
        /// <param name="cancellationToken">Cancellation token</param>
        public static void RegisterDelay(Action continuation, int frames, CancellationToken cancellationToken)
        {
            if (continuation == null)
            {
                return;
            }

            if (frames <= 0)
            {
                // Execute immediately if 0 frames or less
                continuation.Invoke();
                return;
            }

            lock (lockObject)
            {
                delayTasks.Add(new DelayTask(continuation, frames, cancellationToken));
            }
        }

        /// <summary>
        /// Update processing for waiting tasks called every frame
        /// </summary>
        private static void UpdateDelayTasks()
        {
            // Update frame counter
            currentFrameCount++;

            if (delayTasks.Count == 0) return;

            lock (lockObject)
            {
                for (int i = delayTasks.Count - 1; i >= 0; i--)
                {
                    DelayTask task = delayTasks[i];

                    // Remove cancelled tasks by throwing exceptions
                    if (task.CancellationToken.IsCancellationRequested)
                    {
                        delayTasks.RemoveAt(i);
                        task.Continuation.Invoke();
                        continue;
                    }

                    // Decrease frame count
                    task.RemainingFrames--;

                    // Execute and remove completed waiting tasks
                    if (task.RemainingFrames <= 0)
                    {
                        delayTasks.RemoveAt(i);
                        task.Continuation.Invoke();
                    }
                }
            }
        }

        /// <summary>
        /// Get current number of waiting tasks (for debugging)
        /// </summary>
        public static int PendingTaskCount
        {
            get
            {
                lock (lockObject)
                {
                    return delayTasks.Count;
                }
            }
        }

        /// <summary>
        /// Get current frame count (for testing)
        /// </summary>
        public static int CurrentFrameCount
        {
            get
            {
                lock (lockObject)
                {
                    return currentFrameCount;
                }
            }
        }

        /// <summary>
        /// Event handler for PlayMode state changes
        /// </summary>
        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.ExitingPlayMode)
            {
                ResetFrameCount();
            }
        }

        /// <summary>
        /// Reset frame counter (for testing and internal use)
        /// </summary>
        public static void ResetFrameCount()
        {
            lock (lockObject)
            {
                currentFrameCount = 0;
            }
        }

        /// <summary>
        /// Clear all waiting tasks (for testing)
        /// </summary>
        public static void ClearAllTasks()
        {
            lock (lockObject)
            {
                delayTasks.Clear();
            }
        }
    }
}