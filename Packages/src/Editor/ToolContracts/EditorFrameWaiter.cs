using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// Waits for Unity Editor update frames when code must observe rendered or runtime frame progress.
    /// </summary>
    public sealed class EditorFrameWaiterService
    {
        private readonly object _lockObject = new object();
        private readonly List<EditorFrameWaitRequest> _requests = new List<EditorFrameWaitRequest>();
        private int _currentFrameCount;

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

        public int PendingWaitCount
        {
            get
            {
                lock (_lockObject)
                {
                    return _requests.Count;
                }
            }
        }

        public void InitializeForEditorStartup()
        {
            EditorApplication.update -= UpdateRequests;
            EditorApplication.update += UpdateRequests;
        }

        public Task WaitFramesAsync(int frameCount, CancellationToken ct)
        {
            Debug.Assert(frameCount >= 0, "frameCount must be non-negative");
            ct.ThrowIfCancellationRequested();

            if (frameCount <= 0)
            {
                return Task.CompletedTask;
            }

            TaskCompletionSource<bool> completionSource = new TaskCompletionSource<bool>();
            EditorFrameWaitRequest request;
            lock (_lockObject)
            {
                request = new EditorFrameWaitRequest(
                    completionSource,
                    _currentFrameCount + frameCount,
                    RemoveRequestFromCancellation);
                _requests.Add(request);
            }

            request.RegisterCancellation(ct);
            EditorApplication.QueuePlayerLoopUpdate();
            return completionSource.Task;
        }

        public void ClearAllForTests()
        {
            List<EditorFrameWaitRequest> requestsToCancel;
            lock (_lockObject)
            {
                requestsToCancel = new List<EditorFrameWaitRequest>(_requests);
                _requests.Clear();
            }

            for (int i = 0; i < requestsToCancel.Count; i++)
            {
                requestsToCancel[i].CancelFromTest();
            }
        }

        public void ResetFrameCountForTests()
        {
            lock (_lockObject)
            {
                _currentFrameCount = 0;
            }
        }

        private void UpdateRequests()
        {
            List<EditorFrameWaitRequest> completedRequests = new List<EditorFrameWaitRequest>();
            lock (_lockObject)
            {
                _currentFrameCount++;
                for (int i = _requests.Count - 1; i >= 0; i--)
                {
                    EditorFrameWaitRequest request = _requests[i];
                    if (!request.IsReady(_currentFrameCount))
                    {
                        continue;
                    }

                    _requests.RemoveAt(i);
                    completedRequests.Add(request);
                }
            }

            for (int i = 0; i < completedRequests.Count; i++)
            {
                completedRequests[i].Complete();
            }
        }

        private void RemoveRequestFromCancellation(EditorFrameWaitRequest request)
        {
            bool removed = false;
            lock (_lockObject)
            {
                removed = _requests.Remove(request);
            }

            if (removed)
            {
                request.CancelFromCancellation();
            }
        }
    }

    /// <summary>
    /// Tracks a single Editor frame wait request and its cancellation lifetime.
    /// </summary>
    internal sealed class EditorFrameWaitRequest
    {
        private readonly TaskCompletionSource<bool> _completionSource;
        private readonly int _targetFrameCount;
        private readonly Action<EditorFrameWaitRequest> _removeRequest;
        private CancellationTokenRegistration _cancellationRegistration;
        private CancellationToken _ct;
        private int _isCompleted;

        public EditorFrameWaitRequest(
            TaskCompletionSource<bool> completionSource,
            int targetFrameCount,
            Action<EditorFrameWaitRequest> removeRequest)
        {
            Debug.Assert(completionSource != null, "completionSource must not be null");
            Debug.Assert(removeRequest != null, "removeRequest must not be null");

            _completionSource = completionSource ?? throw new ArgumentNullException(nameof(completionSource));
            _targetFrameCount = targetFrameCount;
            _removeRequest = removeRequest ?? throw new ArgumentNullException(nameof(removeRequest));
        }

        public bool IsReady(int currentFrameCount)
        {
            return currentFrameCount >= _targetFrameCount;
        }

        public void RegisterCancellation(CancellationToken ct)
        {
            _ct = ct;
            if (!ct.CanBeCanceled)
            {
                return;
            }

            CancellationTokenRegistration registration = ct.Register(RemoveFromCancellation);
            _cancellationRegistration = registration;
            if (Interlocked.CompareExchange(ref _isCompleted, 0, 0) != 0)
            {
                registration.Dispose();
            }
        }

        public void Complete()
        {
            if (Interlocked.Exchange(ref _isCompleted, 1) != 0)
            {
                return;
            }

            _cancellationRegistration.Dispose();
            _completionSource.TrySetResult(true);
        }

        public void CancelFromCancellation()
        {
            if (Interlocked.Exchange(ref _isCompleted, 1) != 0)
            {
                return;
            }

            _cancellationRegistration.Dispose();
            _completionSource.TrySetCanceled(_ct);
        }

        public void CancelFromTest()
        {
            if (Interlocked.Exchange(ref _isCompleted, 1) != 0)
            {
                return;
            }

            _cancellationRegistration.Dispose();
            _completionSource.TrySetCanceled();
        }

        private void RemoveFromCancellation()
        {
            _removeRequest(this);
        }
    }

    /// <summary>
    /// Provides Editor update frame waits without representing them as wall-clock timeouts.
    /// </summary>
    public static class EditorFrameWaiter
    {
        private static readonly EditorFrameWaiterService ServiceValue = new EditorFrameWaiterService();

        public static int CurrentFrameCount => ServiceValue.CurrentFrameCount;
        public static int PendingWaitCount => ServiceValue.PendingWaitCount;

        public static void InitializeForEditorStartup()
        {
            ServiceValue.InitializeForEditorStartup();
        }

        public static Task WaitFramesAsync(int frameCount, CancellationToken ct = default)
        {
            return ServiceValue.WaitFramesAsync(frameCount, ct);
        }

        public static void ClearAllForTests()
        {
            ServiceValue.ClearAllForTests();
        }

        public static void ResetFrameCountForTests()
        {
            ServiceValue.ResetFrameCountForTests();
        }
    }
}
