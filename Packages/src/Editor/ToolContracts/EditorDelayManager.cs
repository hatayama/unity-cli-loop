using System;
using System.Collections.Generic;
using System.Threading;
using UnityEditor;

namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// Delay processing management class for Unity Editor
    /// Driven by EditorApplication.update to manage frame-based waiting processes
    /// </summary>
    public sealed class EditorDelayManagerService
    {
        private readonly List<DelayTask> _delayTasks = new();
        private readonly object _lockObject = new();
        private int _currentFrameCount = 0;

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

        public void Initialize()
        {
            EditorApplication.update -= UpdateDelayTasks;
            EditorApplication.update += UpdateDelayTasks;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        /// <summary>
        /// Register a new waiting task
        /// </summary>
        /// <param name="continuation">Process to execute after waiting completion</param>
        /// <param name="frames">Number of frames to wait</param>
        /// <param name="cancellationToken">Cancellation token</param>
        public void RegisterDelay(Action continuation, int frames, CancellationToken cancellationToken)
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

            lock (_lockObject)
            {
                _delayTasks.Add(new DelayTask(continuation, frames, cancellationToken));
            }
        }

        /// <summary>
        /// Update processing for waiting tasks called every frame
        /// </summary>
        private void UpdateDelayTasks()
        {
            // Update frame counter
            _currentFrameCount++;

            if (_delayTasks.Count == 0) return;

            lock (_lockObject)
            {
                for (int i = _delayTasks.Count - 1; i >= 0; i--)
                {
                    DelayTask task = _delayTasks[i];

                    // Remove cancelled tasks by throwing exceptions
                    if (task.CancellationToken.IsCancellationRequested)
                    {
                        _delayTasks.RemoveAt(i);
                        task.Continuation.Invoke();
                        continue;
                    }

                    // Decrease frame count
                    task.RemainingFrames--;

                    // Execute and remove completed waiting tasks
                    if (task.RemainingFrames <= 0)
                    {
                        _delayTasks.RemoveAt(i);
                        task.Continuation.Invoke();
                    }
                }
            }
        }

        /// <summary>
        /// Get current number of waiting tasks (for debugging)
        /// </summary>
        public int PendingTaskCount
        {
            get
            {
                lock (_lockObject)
                {
                    return _delayTasks.Count;
                }
            }
        }

        /// <summary>
        /// Get current frame count (for testing)
        /// </summary>
        public int CurrentFrameCount
        {
            get
            {
                lock (_lockObject)
                {
                    return _currentFrameCount;
                }
            }
        }

        /// <summary>
        /// Event handler for PlayMode state changes
        /// </summary>
        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.ExitingPlayMode)
            {
                ResetFrameCount();
            }
        }

        /// <summary>
        /// Reset frame counter (for testing and internal use)
        /// </summary>
        public void ResetFrameCount()
        {
            lock (_lockObject)
            {
                _currentFrameCount = 0;
            }
        }

        /// <summary>
        /// Clear all waiting tasks (for testing)
        /// </summary>
        public void ClearAllTasks()
        {
            lock (_lockObject)
            {
                _delayTasks.Clear();
            }
        }
    }

    /// <summary>
    /// Manages Editor Delay state and operations for the owning workflow.
    /// </summary>
    public static class EditorDelayManager
    {
        private static readonly EditorDelayManagerService ServiceValue = new EditorDelayManagerService();

        public static void InitializeForEditorStartup()
        {
            ServiceValue.Initialize();
        }

        public static void RegisterDelay(Action continuation, int frames, CancellationToken cancellationToken)
        {
            ServiceValue.RegisterDelay(continuation, frames, cancellationToken);
        }

        public static int PendingTaskCount
        {
            get { return ServiceValue.PendingTaskCount; }
        }

        public static int CurrentFrameCount
        {
            get { return ServiceValue.CurrentFrameCount; }
        }

        public static void ResetFrameCount()
        {
            ServiceValue.ResetFrameCount();
        }

        public static void ClearAllTasks()
        {
            ServiceValue.ClearAllTasks();
        }
    }
}
