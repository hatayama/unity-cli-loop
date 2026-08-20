using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests tool execution coordination helpers.
    /// </summary>
    public sealed class UnityCliLoopToolExecutionServiceTests
    {
        [Test]
        public void CreateBusyException_WhenDispatcherReportsBackgroundThread_UsesEditorStateSnapshot()
        {
            // Verifies busy responses include the last editor state even before the request reaches Unity's main thread.
            CapturingMainThreadDispatcher dispatcher = new CapturingMainThreadDispatcher();
            MainThreadSwitcher.RegisterService(dispatcher);
            UnityCliLoopEditorStateSnapshot.SetPlayStateForTesting(
                isPlaying: true,
                isPaused: true);

            try
            {
                UnityCliLoopToolBusyException exception =
                    UnityCliLoopToolExecutionService.CreateBusyException(
                        "running-tool",
                        "requested-tool",
                        new NoOpEditorRuntimeStatePort());

                Assert.That(exception.RunningToolName, Is.EqualTo("running-tool"));
                Assert.That(exception.RequestedToolName, Is.EqualTo("requested-tool"));
                Assert.That(exception.IsPlaying, Is.True);
                Assert.That(exception.IsPaused, Is.True);
            }
            finally
            {
                UnityCliLoopEditorStateSnapshot.ClearForTesting();
                RestoreEditorMainThreadDispatcher();
            }
        }

        [Test]
        public async Task ExecuteToolAsync_WhenTypedToolCompletesUnderBlockedSynchronizationContext_ReleasesExecutionSlot()
        {
            // Verifies timeout-style tool completions are not trapped behind a captured Editor SynchronizationContext.
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();
            PendingTypedTool pendingTool = new PendingTypedTool();
            registry.RegisterTool(pendingTool);
            UnityCliLoopToolExecutionService executionService =
                new UnityCliLoopToolExecutionService(new NoOpEditorRuntimeStatePort());
            ImmediateMainThreadDispatcher dispatcher = new ImmediateMainThreadDispatcher();
            MainThreadSwitcher.RegisterService(dispatcher);
            BlockingSynchronizationContext blockedContext = new BlockingSynchronizationContext();
            System.Threading.SynchronizationContext previousContext =
                System.Threading.SynchronizationContext.Current;

            try
            {
                System.Threading.SynchronizationContext.SetSynchronizationContext(blockedContext);
                Task<UnityCliLoopToolResponse> firstTask = executionService.ExecuteToolAsync(
                    registry,
                    PendingTypedTool.Name,
                    null,
                    CancellationToken.None);
                System.Threading.SynchronizationContext.SetSynchronizationContext(previousContext);

                Assert.That(firstTask.IsCompleted, Is.False);

                pendingTool.Complete();
                UnityCliLoopToolResponse firstResponse = await AwaitResponseWithTimeout(
                    firstTask,
                    CancellationToken.None);

                Assert.That(firstResponse, Is.InstanceOf<PendingTypedResponse>());
                Assert.That(blockedContext.PostCount, Is.EqualTo(0));

                UnityCliLoopToolResponse secondResponse = await executionService.ExecuteToolAsync(
                    registry,
                    PendingTypedTool.Name,
                    null,
                    CancellationToken.None);

                Assert.That(secondResponse, Is.InstanceOf<PendingTypedResponse>());
            }
            finally
            {
                System.Threading.SynchronizationContext.SetSynchronizationContext(previousContext);
                RestoreEditorMainThreadDispatcher();
            }
        }

        /// <summary>
        /// Verifies ExecuteToolAsync copies the session elapsed-seconds snapshot onto the busy exception.
        /// </summary>
        [Test]
        public void ExecuteToolAsync_WhenAnotherToolIsRunning_ShouldThrowBusyWithElapsedSeconds()
        {
            long timestamp = 0;
            ToolExecutionSession session = new ToolExecutionSession(() => timestamp);
            session.TryEnter("running-tool");
            timestamp += 5 * Stopwatch.Frequency;

            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();
            BusyRouteRequestedTool requestedTool = new BusyRouteRequestedTool();
            registry.RegisterTool(requestedTool);
            UnityCliLoopToolExecutionService executionService =
                new UnityCliLoopToolExecutionService(new NoOpEditorRuntimeStatePort(), session);

            UnityCliLoopToolBusyException exception = Assert.ThrowsAsync<UnityCliLoopToolBusyException>(
                () => executionService.ExecuteToolAsync(
                    registry,
                    requestedTool.ToolName,
                    null,
                    CancellationToken.None));

            Assert.That(exception.RunningToolName, Is.EqualTo("running-tool"));
            Assert.That(exception.RequestedToolName, Is.EqualTo(requestedTool.ToolName));
            Assert.That(exception.RunningToolElapsedSeconds, Is.EqualTo(5));

            session.Exit();
        }

        [Test]
        public void ScreenshotUseCase_CreateTimedOutResult_PreservesTimeoutDetails()
        {
            // Verifies screenshot timeout results keep the timeout marker, message, and captured screenshot count.
            List<ScreenshotInfo> screenshots = new List<ScreenshotInfo>
            {
                new ScreenshotInfo
                {
                    ImagePath = "capture.png",
                },
            };
            ScreenshotResponse response = ScreenshotCaptureResults.CreateTimedOutResult(
                "EditorWindow capture",
                "test-correlation-id",
                screenshots);

            Assert.That(response.TimedOut, Is.True);
            Assert.That(
                response.Message,
                Is.EqualTo($"Timed out after {UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS}ms while waiting for EditorWindow capture frames."));
            Assert.That(response.ScreenshotCount, Is.EqualTo(1));
        }

        private static void RestoreEditorMainThreadDispatcher()
        {
            EditorMainThreadDispatcher dispatcher = new EditorMainThreadDispatcher();
            MainThreadSwitcher.RegisterService(dispatcher);
            dispatcher.Initialize();
        }

        private static async Task<UnityCliLoopToolResponse> AwaitResponseWithTimeout(
            Task<UnityCliLoopToolResponse> task,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(1), ct);
            Task completedTask = await Task.WhenAny(task, timeoutTask);
            Assert.That(completedTask, Is.SameAs(task), "Tool execution did not complete before the test timeout.");
            return await task;
        }

        private sealed class CapturingMainThreadDispatcher : IMainThreadDispatcher
        {
            public bool IsMainThread => false;

            public void Initialize()
            {
            }

            public void AddContinuation(System.Action continuation)
            {
                Assert.That(continuation, Is.Not.Null);
            }
        }

        private sealed class ImmediateMainThreadDispatcher : IMainThreadDispatcher
        {
            public bool IsMainThread => true;

            public void Initialize()
            {
            }

            public void AddContinuation(Action continuation)
            {
                Assert.That(continuation, Is.Not.Null);
                continuation();
            }
        }

        private sealed class BlockingSynchronizationContext : System.Threading.SynchronizationContext
        {
            public int PostCount { get; private set; }

            public override void Post(SendOrPostCallback callback, object state)
            {
                Assert.That(callback, Is.Not.Null);
                PostCount++;
            }
        }

        private sealed class PendingTypedTool : UnityCliLoopTool<PendingTypedSchema, PendingTypedResponse>
        {
            public const string Name = "pending-typed-tool";

            private readonly TaskCompletionSource<PendingTypedResponse> _completionSource =
                new TaskCompletionSource<PendingTypedResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

            public override string ToolName => Name;

            protected override Task<PendingTypedResponse> ExecuteAsync(PendingTypedSchema parameters, CancellationToken ct)
            {
                return _completionSource.Task;
            }

            public void Complete()
            {
                _completionSource.TrySetResult(new PendingTypedResponse());
            }
        }

        private sealed class BusyRouteRequestedTool : IUnityCliLoopTool
        {
            public string ToolName => "requested-tool";

            public ToolParameterSchema ParameterSchema => new();

            public Task<UnityCliLoopToolResponse> ExecuteAsync(JToken paramsToken, CancellationToken ct)
            {
                return Task.FromResult<UnityCliLoopToolResponse>(new PendingTypedResponse());
            }
        }

        private sealed class PendingTypedSchema : UnityCliLoopToolSchema
        {
        }

        private sealed class PendingTypedResponse : UnityCliLoopToolResponse
        {
        }
    }
}
