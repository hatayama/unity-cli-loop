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
            ToolExecutionLease runningLease = session.TryEnter("running-tool").Lease;
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
            Assert.That(exception.RunningToolPhase, Is.EqualTo("Executing"));

            runningLease.Dispose();
        }

        /// <summary>
        /// Verifies a request that holds the slot while its switch to the Editor main thread is still
        /// queued is reported as waiting for the main thread, not as running, and that the slot is
        /// released once the main thread resumes it.
        /// </summary>
        [Test]
        public void ExecuteToolAsync_WhenHolderWaitsForMainThread_ShouldThrowBusyWithWaitingPhase()
        {
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();
            registry.RegisterTool(new ImmediateTool(ImmediateTool.HolderName));
            registry.RegisterTool(new ImmediateTool(ImmediateTool.RequestedName));
            UnityCliLoopToolExecutionService executionService =
                new UnityCliLoopToolExecutionService(new NoOpEditorRuntimeStatePort(), new ToolExecutionSession());
            QueueingMainThreadDispatcher dispatcher = new QueueingMainThreadDispatcher();
            MainThreadSwitcher.RegisterService(dispatcher);

            try
            {
                Task<UnityCliLoopToolResponse> holderTask = executionService.ExecuteToolAsync(
                    registry,
                    ImmediateTool.HolderName,
                    null,
                    CancellationToken.None);

                UnityCliLoopToolBusyException exception = Assert.ThrowsAsync<UnityCliLoopToolBusyException>(
                    () => executionService.ExecuteToolAsync(
                        registry,
                        ImmediateTool.RequestedName,
                        null,
                        CancellationToken.None));

                Assert.That(exception.RunningToolName, Is.EqualTo(ImmediateTool.HolderName));
                Assert.That(exception.RunningToolPhase, Is.EqualTo("WaitingForMainThread"));

                // Why synchronous: every switch in this test, including the retry below, is queued on
                // the fake dispatcher, so the test resumes them itself instead of spanning frames.
                dispatcher.RunQueued();

                Assert.That(holderTask.Status, Is.EqualTo(TaskStatus.RanToCompletion));

                Task<UnityCliLoopToolResponse> requestedTask = executionService.ExecuteToolAsync(
                    registry,
                    ImmediateTool.RequestedName,
                    null,
                    CancellationToken.None);
                dispatcher.RunQueued();

                Assert.That(requestedTask.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            }
            finally
            {
                RestoreEditorMainThreadDispatcher();
            }
        }

        /// <summary>
        /// Verifies a switch to the Editor main thread awaited inside the tool's own code is also
        /// reported as waiting, so a holder that finished its snippet but still waits to return
        /// through the main thread is not reported as running.
        /// </summary>
        [Test]
        public void ExecuteToolAsync_WhenToolCodeAwaitsQueuedMainThreadSwitch_ShouldThrowBusyWithWaitingPhase()
        {
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();
            registry.RegisterTool(new MainThreadSwitchingTool());
            registry.RegisterTool(new ImmediateTool(ImmediateTool.RequestedName));
            UnityCliLoopToolExecutionService executionService =
                new UnityCliLoopToolExecutionService(new NoOpEditorRuntimeStatePort(), new ToolExecutionSession());
            QueueingMainThreadDispatcher dispatcher = new QueueingMainThreadDispatcher();
            MainThreadSwitcher.RegisterService(dispatcher);
            // Why clear the context: under Unity's context the service's ConfigureAwait(false) resume
            // after the tool is moved to the thread pool, which would leave background work behind and
            // make the holder's completion untestable without waiting.
            SynchronizationContext originalContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);

            try
            {
                Task<UnityCliLoopToolResponse> holderTask = executionService.ExecuteToolAsync(
                    registry,
                    MainThreadSwitchingTool.ToolNameValue,
                    null,
                    CancellationToken.None);
                // Resumes the switch before the tool, so the tool code runs and queues its own switch.
                dispatcher.RunQueued();

                Assert.That(holderTask.IsCompleted, Is.False);

                UnityCliLoopToolBusyException exception = Assert.ThrowsAsync<UnityCliLoopToolBusyException>(
                    () => executionService.ExecuteToolAsync(
                        registry,
                        ImmediateTool.RequestedName,
                        null,
                        CancellationToken.None));

                Assert.That(exception.RunningToolName, Is.EqualTo(MainThreadSwitchingTool.ToolNameValue));
                Assert.That(exception.RunningToolPhase, Is.EqualTo("WaitingForMainThread"));

                dispatcher.RunQueued();

                Assert.That(holderTask.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(originalContext);
                RestoreEditorMainThreadDispatcher();
            }
        }

        /// <summary>
        /// Verifies ExecuteToolAsync hands the request token to the session, so a cancelled execute-dynamic-code run that never returns stops blocking other tools after the grace period.
        /// </summary>
        [Test]
        public async Task ExecuteToolAsync_WhenDynamicCodeRequestIsCancelledAndNeverReturns_ShouldLetOtherToolRunAfterGrace()
        {
            long timestamp = 0;
            ToolExecutionSession session = new ToolExecutionSession(() => timestamp);
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();
            registry.RegisterTool(new NeverReturningDynamicCodeTool());
            BusyRouteRequestedTool requestedTool = new BusyRouteRequestedTool();
            registry.RegisterTool(requestedTool);
            UnityCliLoopToolExecutionService executionService =
                new UnityCliLoopToolExecutionService(new NoOpEditorRuntimeStatePort(), session);
            MainThreadSwitcher.RegisterService(new ImmediateMainThreadDispatcher());
            using CancellationTokenSource requestCancellation = new CancellationTokenSource();

            try
            {
                Task<UnityCliLoopToolResponse> stuckTask = executionService.ExecuteToolAsync(
                    registry,
                    UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE,
                    null,
                    requestCancellation.Token);
                requestCancellation.Cancel();

                Assert.ThrowsAsync<UnityCliLoopToolBusyException>(
                    () => executionService.ExecuteToolAsync(
                        registry,
                        requestedTool.ToolName,
                        null,
                        CancellationToken.None));
                timestamp += ToolExecutionSession.CancelledLeaseGraceSeconds * Stopwatch.Frequency;
                UnityCliLoopToolResponse response = await executionService.ExecuteToolAsync(
                    registry,
                    requestedTool.ToolName,
                    null,
                    CancellationToken.None);

                Assert.That(stuckTask.IsCompleted, Is.False);
                Assert.That(response, Is.InstanceOf<PendingTypedResponse>());
            }
            finally
            {
                RestoreEditorMainThreadDispatcher();
            }
        }

        /// <summary>
        /// Verifies a tool that honours its token ends with UnityCliLoopToolCancelledException when
        /// uloop cancel fires, and that the slot is free again afterwards.
        /// </summary>
        [Test]
        public async Task ExecuteToolAsync_WhenCancelledByUser_ThrowsToolCancelledAndReleasesSlot()
        {
            ToolExecutionSession session = new ToolExecutionSession();
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();
            CooperativeTool tool = new CooperativeTool();
            registry.RegisterTool(tool);
            UnityCliLoopToolExecutionService executionService =
                new UnityCliLoopToolExecutionService(new NoOpEditorRuntimeStatePort(), session);
            MainThreadSwitcher.RegisterService(new ImmediateMainThreadDispatcher());

            try
            {
                Task<UnityCliLoopToolResponse> runningTask = executionService.ExecuteToolAsync(
                    registry,
                    CooperativeTool.ToolNameValue,
                    null,
                    CancellationToken.None);

                UnityCliLoopCancelRequestResult cancelResult = executionService.CancelRunningTool();
                await AwaitCompletionWithTimeout(runningTask);

                Assert.That(cancelResult.Outcome, Is.EqualTo("Cancelled"));
                Assert.That(cancelResult.RunningToolName, Is.EqualTo(CooperativeTool.ToolNameValue));
                UnityCliLoopToolCancelledException exception =
                    Assert.ThrowsAsync<UnityCliLoopToolCancelledException>(() => runningTask);
                Assert.That(exception.ToolName, Is.EqualTo(CooperativeTool.ToolNameValue));
                ToolExecutionSessionEnterResult nextEnter = session.TryEnter("other-tool");
                Assert.That(nextEnter.IsEntered, Is.True);
                nextEnter.Lease.Dispose();
            }
            finally
            {
                RestoreEditorMainThreadDispatcher();
            }
        }

        /// <summary>
        /// Verifies a request still waiting for the stalled Editor main thread is resumed by uloop
        /// cancel, ends with UnityCliLoopToolCancelledException, and frees the slot without the main
        /// thread ever running it.
        /// </summary>
        [Test]
        public async Task ExecuteToolAsync_WhenCancelledByUserWhileWaitingForMainThread_ThrowsToolCancelledAndReleasesSlot()
        {
            ToolExecutionSession session = new ToolExecutionSession();
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();
            registry.RegisterTool(new ImmediateTool(ImmediateTool.HolderName));
            UnityCliLoopToolExecutionService executionService =
                new UnityCliLoopToolExecutionService(new NoOpEditorRuntimeStatePort(), session);
            QueueingMainThreadDispatcher dispatcher = new QueueingMainThreadDispatcher();
            MainThreadSwitcher.RegisterService(dispatcher);

            try
            {
                Task<UnityCliLoopToolResponse> waitingTask = executionService.ExecuteToolAsync(
                    registry,
                    ImmediateTool.HolderName,
                    null,
                    CancellationToken.None);
                Assert.That(waitingTask.IsCompleted, Is.False);

                executionService.CancelRunningTool();
                await AwaitCompletionWithTimeout(waitingTask);

                Assert.ThrowsAsync<UnityCliLoopToolCancelledException>(() => waitingTask);
                ToolExecutionSessionEnterResult nextEnter = session.TryEnter("other-tool");
                Assert.That(nextEnter.IsEntered, Is.True);
                nextEnter.Lease.Dispose();
            }
            finally
            {
                RestoreEditorMainThreadDispatcher();
            }
        }

        /// <summary>
        /// Verifies a request whose client also disconnected after uloop cancel keeps ending with the
        /// plain OperationCanceledException, because nobody is left to read a cancel error.
        /// </summary>
        [Test]
        public async Task ExecuteToolAsync_WhenRequestTokenAlsoCancelledAfterUserCancel_ThrowsOperationCanceled()
        {
            ToolExecutionSession session = new ToolExecutionSession();
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();
            ManuallyCancelledTool tool = new ManuallyCancelledTool();
            registry.RegisterTool(tool);
            UnityCliLoopToolExecutionService executionService =
                new UnityCliLoopToolExecutionService(new NoOpEditorRuntimeStatePort(), session);
            MainThreadSwitcher.RegisterService(new ImmediateMainThreadDispatcher());
            using CancellationTokenSource requestCancellation = new CancellationTokenSource();

            try
            {
                Task<UnityCliLoopToolResponse> runningTask = executionService.ExecuteToolAsync(
                    registry,
                    ManuallyCancelledTool.ToolNameValue,
                    null,
                    requestCancellation.Token);

                executionService.CancelRunningTool();
                requestCancellation.Cancel();
                tool.EndWithCancellation();
                await AwaitCompletionWithTimeout(runningTask);

                Assert.CatchAsync<OperationCanceledException>(() => runningTask);
            }
            finally
            {
                RestoreEditorMainThreadDispatcher();
            }
        }

        /// <summary>
        /// Verifies a client disconnect alone still ends the request with OperationCanceledException,
        /// not with the uloop cancel error.
        /// </summary>
        [Test]
        public async Task ExecuteToolAsync_WhenOnlyRequestTokenIsCancelled_ThrowsOperationCanceled()
        {
            ToolExecutionSession session = new ToolExecutionSession();
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();
            registry.RegisterTool(new CooperativeTool());
            UnityCliLoopToolExecutionService executionService =
                new UnityCliLoopToolExecutionService(new NoOpEditorRuntimeStatePort(), session);
            MainThreadSwitcher.RegisterService(new ImmediateMainThreadDispatcher());
            using CancellationTokenSource requestCancellation = new CancellationTokenSource();

            try
            {
                Task<UnityCliLoopToolResponse> runningTask = executionService.ExecuteToolAsync(
                    registry,
                    CooperativeTool.ToolNameValue,
                    null,
                    requestCancellation.Token);

                requestCancellation.Cancel();
                await AwaitCompletionWithTimeout(runningTask);

                Exception exception = Assert.CatchAsync<OperationCanceledException>(() => runningTask);
                Assert.That(exception, Is.Not.InstanceOf<UnityCliLoopToolCancelledException>());
            }
            finally
            {
                RestoreEditorMainThreadDispatcher();
            }
        }

        /// <summary>
        /// Verifies a tool started through the public registrar entry point, which Editor code uses
        /// with its own token, is reported NotCancellable and keeps running when uloop cancel arrives.
        /// </summary>
        [Test]
        public async Task CancelRunningTool_WhenToolRunsThroughPublicRegistrarEntryPoint_ReportsNotCancellable()
        {
            PendingTypedTool tool = new PendingTypedTool();
            UnityCliLoopToolRegistrarService registrarService = new UnityCliLoopToolRegistrarService(
                new EmptyInternalToolNameProvider(),
                new AlwaysEnabledToolSettingsPort(),
                new UnityCliLoopToolExecutionService(new NoOpEditorRuntimeStatePort(), new ToolExecutionSession()),
                () => new IUnityCliLoopTool[] { tool });
            MainThreadSwitcher.RegisterService(new ImmediateMainThreadDispatcher());

            try
            {
                Task<UnityCliLoopToolResponse> runningTask = registrarService.ExecuteToolAsync(
                    PendingTypedTool.Name,
                    null,
                    CancellationToken.None);

                UnityCliLoopCancelRequestResult cancelResult = registrarService.CancelRunningTool();

                Assert.That(cancelResult.Outcome, Is.EqualTo("NotCancellable"));
                Assert.That(cancelResult.RunningToolName, Is.EqualTo(PendingTypedTool.Name));
                Assert.That(runningTask.IsCompleted, Is.False);

                tool.Complete();
                UnityCliLoopToolResponse response = await AwaitResponseWithTimeout(runningTask, CancellationToken.None);
                Assert.That(response, Is.InstanceOf<PendingTypedResponse>());
            }
            finally
            {
                RestoreEditorMainThreadDispatcher();
            }
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

        /// <summary>
        /// Verifies a request still waiting for a main thread that stays blocked fails with the blocked-main-thread error, frees the slot, and never runs its tool.
        /// </summary>
        [Test]
        public void ExecuteToolAsync_WhenMainThreadStaysBlockedBeforeToolEntry_ThrowsMainThreadBlockedAndReleasesSlot()
        {
            AssertBlockedBeforeToolEntryThrowsMainThreadBlocked(cancellableByUser: true);
        }

        /// <summary>
        /// Verifies the blocked-main-thread failure also applies to requests uloop cancel may not stop, because no tool code has run yet.
        /// </summary>
        [Test]
        public void ExecuteToolAsync_WhenNotCancellableByUserAndMainThreadBlocked_StillThrowsMainThreadBlocked()
        {
            AssertBlockedBeforeToolEntryThrowsMainThreadBlocked(cancellableByUser: false);
        }

        /// <summary>
        /// Verifies a main thread that resumes before the stall reaches the threshold runs the tool and stops the watchdog.
        /// </summary>
        [Test]
        public void ExecuteToolAsync_WhenMainThreadResumesBeforeThreshold_RunsTool()
        {
            ToolExecutionSession session = new ToolExecutionSession();
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();
            ImmediateTool tool = new ImmediateTool(ImmediateTool.HolderName);
            registry.RegisterTool(tool);
            ManualPolls polls = new ManualPolls();
            UnityCliLoopToolExecutionService executionService = CreateServiceWithWatchdog(session, new ScriptedLiveness(10), polls);

            RunWithQueueingDispatcherAndNoContext(dispatcher =>
            {
                Task<UnityCliLoopToolResponse> task = executionService.ExecuteToolAsync(
                    registry,
                    ImmediateTool.HolderName,
                    null,
                    CancellationToken.None);
                polls.CompleteLatest();

                Assert.That(task.IsCompleted, Is.False);

                dispatcher.RunQueued();

                Assert.That(task.Status, Is.EqualTo(TaskStatus.RanToCompletion));
                Assert.That(tool.ExecutionCount, Is.EqualTo(1));
                Assert.That(polls.Latest.IsCanceled, Is.True);
            });
        }

        /// <summary>
        /// Verifies the watchdog stops once the tool code runs, so a tool that waits for the main thread itself is never failed as blocked.
        /// </summary>
        [Test]
        public void ExecuteToolAsync_WhenToolCodeWaitsForMainThreadAfterEntry_WatchdogIsStopped()
        {
            ToolExecutionSession session = new ToolExecutionSession();
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();
            registry.RegisterTool(new MainThreadSwitchingTool());
            ManualPolls polls = new ManualPolls();
            UnityCliLoopToolExecutionService executionService = CreateServiceWithWatchdog(session, new ScriptedLiveness(45), polls);

            RunWithQueueingDispatcherAndNoContext(dispatcher =>
            {
                Task<UnityCliLoopToolResponse> holderTask = executionService.ExecuteToolAsync(
                    registry,
                    MainThreadSwitchingTool.ToolNameValue,
                    null,
                    CancellationToken.None);
                // Resumes the switch before the tool, so the tool code runs and queues its own switch.
                dispatcher.RunQueued();

                Assert.That(polls.Latest.IsCanceled, Is.True);
                Assert.That(holderTask.IsCompleted, Is.False);

                dispatcher.RunQueued();

                Assert.That(holderTask.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            });
        }

        /// <summary>
        /// Verifies a lease claimed for a blocked main thread refuses the tool even when the main thread resumes before the claimer cancels the token.
        /// </summary>
        [Test]
        public void ExecuteToolAsync_WhenWatchdogClaimsTheLeaseBeforeTheMainThreadResumes_ThrowsMainThreadBlockedWithoutRunningTool()
        {
            ToolExecutionSession session = new ToolExecutionSession();
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();
            ImmediateTool tool = new ImmediateTool(ImmediateTool.HolderName);
            registry.RegisterTool(tool);
            CapturingWatchdog capturing = new CapturingWatchdog(null);
            UnityCliLoopToolExecutionService executionService =
                new UnityCliLoopToolExecutionService(new NoOpEditorRuntimeStatePort(), session, capturing);

            RunWithQueueingDispatcherAndNoContext(dispatcher =>
            {
                Task<UnityCliLoopToolResponse> task = executionService.ExecuteToolAsync(
                    registry,
                    ImmediateTool.HolderName,
                    null,
                    CancellationToken.None);

                Assert.That(capturing.Lease.TryClaimBlockedMainThread(45), Is.True);
                Assert.That(capturing.Lease.ExecutionToken.IsCancellationRequested, Is.False);

                dispatcher.RunQueued();

                // Checked first: ThrowsAsync waits on a pending task and would freeze the Editor.
                Assert.That(task.IsFaulted, Is.True);
                UnityCliLoopMainThreadBlockedException exception =
                    Assert.ThrowsAsync<UnityCliLoopMainThreadBlockedException>(() => task);
                Assert.That(exception.StallSeconds, Is.EqualTo(45));
                Assert.That(tool.ExecutionCount, Is.EqualTo(0));
                ToolExecutionSessionEnterResult nextEnter = session.TryEnter("other-tool");
                Assert.That(nextEnter.IsEntered, Is.True);
                nextEnter.Lease.Dispose();
            });
        }

        /// <summary>
        /// Verifies a main thread that resumes while the watchdog is deciding wins, so the tool runs and is not failed as blocked.
        /// </summary>
        [Test]
        public void ExecuteToolAsync_WhenMainThreadResumesWhileTheWatchdogIsDeciding_RunsToolAndIsNotCancelled()
        {
            ToolExecutionSession session = new ToolExecutionSession();
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();
            ImmediateTool tool = new ImmediateTool(ImmediateTool.HolderName);
            registry.RegisterTool(tool);
            ManualPolls polls = new ManualPolls();
            ScriptedLiveness liveness = new ScriptedLiveness(45);
            CapturingWatchdog capturing = new CapturingWatchdog(new MainThreadEntryWatchdog(liveness, polls.WaitOnePoll));
            UnityCliLoopToolExecutionService executionService =
                new UnityCliLoopToolExecutionService(new NoOpEditorRuntimeStatePort(), session, capturing);

            RunWithQueueingDispatcherAndNoContext(dispatcher =>
            {
                liveness.OnRead = dispatcher.RunQueued;
                Task<UnityCliLoopToolResponse> task = executionService.ExecuteToolAsync(
                    registry,
                    ImmediateTool.HolderName,
                    null,
                    CancellationToken.None);

                polls.CompleteLatest();

                Assert.That(task.Status, Is.EqualTo(TaskStatus.RanToCompletion));
                Assert.That(tool.ExecutionCount, Is.EqualTo(1));
                Assert.That(liveness.Reads, Is.EqualTo(1));
                Assert.That(capturing.Lease.WasCancelledForBlockedMainThread, Is.False);
            });
        }

        /// <summary>
        /// Verifies a uloop cancel that reaches a waiting request before the watchdog keeps its cancelled outcome and stops the watchdog.
        /// </summary>
        [Test]
        public void ExecuteToolAsync_WhenUserCancelsBeforeTheWatchdog_ThrowsToolCancelled()
        {
            ToolExecutionSession session = new ToolExecutionSession();
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();
            registry.RegisterTool(new ImmediateTool(ImmediateTool.HolderName));
            ManualPolls polls = new ManualPolls();
            UnityCliLoopToolExecutionService executionService = CreateServiceWithWatchdog(session, new ScriptedLiveness(45), polls);

            RunWithQueueingDispatcherAndNoContext(dispatcher =>
            {
                Task<UnityCliLoopToolResponse> task = executionService.ExecuteToolAsync(
                    registry,
                    ImmediateTool.HolderName,
                    null,
                    CancellationToken.None);

                executionService.CancelRunningTool();

                // Checked first: ThrowsAsync waits on a pending task and would freeze the Editor.
                Assert.That(task.IsFaulted, Is.True);
                Assert.ThrowsAsync<UnityCliLoopToolCancelledException>(() => task);
                Assert.That(polls.Latest.IsCanceled, Is.True);
            });
        }

        /// <summary>
        /// Verifies that when an execute-dynamic-code run blocks the main thread from inside its code, only the request sharing its slot and still waiting fails as blocked.
        /// </summary>
        [Test]
        public void ExecuteToolAsync_WhenASharedSlotHolderBlocksTheMainThread_FailsTheWaitingSharerAndKeepsTheHolder()
        {
            ToolExecutionSession session = new ToolExecutionSession();
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();
            registry.RegisterTool(new DynamicCodeMainThreadSwitchingTool());
            ManualPolls polls = new ManualPolls();
            UnityCliLoopToolExecutionService executionService = CreateServiceWithWatchdog(session, new ScriptedLiveness(45), polls);

            RunWithQueueingDispatcherAndNoContext(dispatcher =>
            {
                Task<UnityCliLoopToolResponse> holderTask = executionService.ExecuteToolAsync(
                    registry,
                    UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE,
                    null,
                    CancellationToken.None);
                dispatcher.RunQueued();
                Assert.That(polls.Latest.IsCanceled, Is.True);

                Task<UnityCliLoopToolResponse> sharerTask = executionService.ExecuteToolAsync(
                    registry,
                    UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE,
                    null,
                    CancellationToken.None);
                polls.CompleteLatest();

                // Checked first: ThrowsAsync waits on a pending task and would freeze the Editor.
                Assert.That(sharerTask.IsFaulted, Is.True);
                Assert.ThrowsAsync<UnityCliLoopMainThreadBlockedException>(() => sharerTask);
                Assert.That(holderTask.IsCompleted, Is.False);

                dispatcher.RunQueued();

                Assert.That(holderTask.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            });
        }

        private static void AssertBlockedBeforeToolEntryThrowsMainThreadBlocked(bool cancellableByUser)
        {
            ToolExecutionSession session = new ToolExecutionSession();
            UnityCliLoopToolRegistry registry = ToolRegistryTestFactory.Create();
            ImmediateTool tool = new ImmediateTool(ImmediateTool.HolderName);
            registry.RegisterTool(tool);
            ManualPolls polls = new ManualPolls();
            UnityCliLoopToolExecutionService executionService = CreateServiceWithWatchdog(session, new ScriptedLiveness(45), polls);

            RunWithQueueingDispatcherAndNoContext(dispatcher =>
            {
                Task<UnityCliLoopToolResponse> task = executionService.ExecuteToolAsync(
                    registry,
                    ImmediateTool.HolderName,
                    null,
                    CancellationToken.None,
                    cancellableByUser);

                Assert.That(task.IsCompleted, Is.False);

                polls.CompleteLatest();

                // Checked first: ThrowsAsync waits on a pending task and would freeze the Editor.
                Assert.That(task.IsFaulted, Is.True);
                UnityCliLoopMainThreadBlockedException exception =
                    Assert.ThrowsAsync<UnityCliLoopMainThreadBlockedException>(() => task);
                Assert.That(exception.ToolName, Is.EqualTo(ImmediateTool.HolderName));
                Assert.That(exception.StallSeconds, Is.EqualTo(45));
                ToolExecutionSessionEnterResult nextEnter = session.TryEnter("other-tool");
                Assert.That(nextEnter.IsEntered, Is.True);
                nextEnter.Lease.Dispose();
                dispatcher.RunQueued();
                Assert.That(tool.ExecutionCount, Is.EqualTo(0));
            });
        }

        private static UnityCliLoopToolExecutionService CreateServiceWithWatchdog(
            ToolExecutionSession session,
            ScriptedLiveness liveness,
            ManualPolls polls)
        {
            return new UnityCliLoopToolExecutionService(
                new NoOpEditorRuntimeStatePort(),
                session,
                new MainThreadEntryWatchdog(liveness, polls.WaitOnePoll));
        }

        // Why clear the context: with no context every continuation in the service and the watchdog
        // runs synchronously on the thread that completes the poll or runs the queue, so each step's
        // outcome is visible right after it without waiting or leaving pool work behind.
        private static void RunWithQueueingDispatcherAndNoContext(Action<QueueingMainThreadDispatcher> body)
        {
            QueueingMainThreadDispatcher dispatcher = new QueueingMainThreadDispatcher();
            MainThreadSwitcher.RegisterService(dispatcher);
            SynchronizationContext originalContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);

            try
            {
                body(dispatcher);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(originalContext);
                RestoreEditorMainThreadDispatcher();
            }
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

        // Waits at most one second so a tool that ignores the cancellation fails the test instead of
        // hanging the Editor. The task's own outcome is asserted by the caller.
        private static async Task AwaitCompletionWithTimeout(Task<UnityCliLoopToolResponse> task)
        {
            Task timeoutTask = Task.Delay(TimeSpan.FromSeconds(1));
            Task completedTask = await Task.WhenAny(task, timeoutTask);
            Assert.That(completedTask, Is.SameAs(task), "Tool execution did not complete before the test timeout.");
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

        // Keeps queued continuations until the test runs them, as a stalled Editor main thread would.
        private sealed class QueueingMainThreadDispatcher : IMainThreadDispatcher
        {
            private readonly List<Action> _queued = new List<Action>();

            public bool IsMainThread => false;

            public void Initialize()
            {
            }

            public void AddContinuation(Action continuation)
            {
                Assert.That(continuation, Is.Not.Null);
                _queued.Add(continuation);
            }

            public void RunQueued()
            {
                List<Action> queued = new List<Action>(_queued);
                _queued.Clear();
                foreach (Action continuation in queued)
                {
                    continuation();
                }
            }
        }

        private sealed class ImmediateTool : IUnityCliLoopTool
        {
            public const string HolderName = "holder-tool";
            public const string RequestedName = "requested-tool";

            public ImmediateTool(string toolName)
            {
                ToolName = toolName;
            }

            public string ToolName { get; }

            public ToolParameterSchema ParameterSchema => new();

            public int ExecutionCount { get; private set; }

            public Task<UnityCliLoopToolResponse> ExecuteAsync(JToken paramsToken, CancellationToken ct)
            {
                ExecutionCount++;
                return Task.FromResult<UnityCliLoopToolResponse>(new PendingTypedResponse());
            }
        }

        // Awaits the Editor main thread inside its own code, as execute-dynamic-code does after the snippet.
        private sealed class MainThreadSwitchingTool : IUnityCliLoopTool
        {
            public const string ToolNameValue = "main-thread-switching-tool";

            public string ToolName => ToolNameValue;

            public ToolParameterSchema ParameterSchema => new();

            public async Task<UnityCliLoopToolResponse> ExecuteAsync(JToken paramsToken, CancellationToken ct)
            {
                await MainThreadSwitcher.SwitchToMainThread(ct);
                return new PendingTypedResponse();
            }
        }

        // Shares the execute-dynamic-code slot and awaits the main thread inside its code, as a
        // snippet that keeps the main thread busy would.
        private sealed class DynamicCodeMainThreadSwitchingTool : IUnityCliLoopTool
        {
            public string ToolName => UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE;

            public ToolParameterSchema ParameterSchema => new();

            public async Task<UnityCliLoopToolResponse> ExecuteAsync(JToken paramsToken, CancellationToken ct)
            {
                await MainThreadSwitcher.SwitchToMainThread(ct);
                return new PendingTypedResponse();
            }
        }

        // Hands the test the lease the service guards, so the test can claim it at a chosen moment or
        // inspect it after the real watchdog it wraps has decided.
        private sealed class CapturingWatchdog : IMainThreadEntryWatchdog
        {
            private readonly IMainThreadEntryWatchdog _inner;

            public CapturingWatchdog(IMainThreadEntryWatchdog inner)
            {
                _inner = inner;
            }

            public ToolExecutionLease Lease { get; private set; }

            public Task GuardAsync(ToolExecutionLease lease, CancellationToken stop)
            {
                Lease = lease;
                return _inner == null ? Task.CompletedTask : _inner.GuardAsync(lease, stop);
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

        // Ends with a cancellation as soon as the token it receives fires, as a tool that honours ct does.
        private sealed class CooperativeTool : IUnityCliLoopTool
        {
            public const string ToolNameValue = "cooperative-tool";

            public string ToolName => ToolNameValue;

            public ToolParameterSchema ParameterSchema => new();

            public Task<UnityCliLoopToolResponse> ExecuteAsync(JToken paramsToken, CancellationToken ct)
            {
                TaskCompletionSource<UnityCliLoopToolResponse> completion =
                    new TaskCompletionSource<UnityCliLoopToolResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
                ct.Register(() => completion.TrySetCanceled(ct));
                return completion.Task;
            }
        }

        // Ends with a cancellation only when the test says so, so the test can change the request
        // token between the cancel and the moment the tool gives up.
        private sealed class ManuallyCancelledTool : IUnityCliLoopTool
        {
            public const string ToolNameValue = "manually-cancelled-tool";

            private readonly TaskCompletionSource<UnityCliLoopToolResponse> _completion =
                new TaskCompletionSource<UnityCliLoopToolResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            private CancellationToken _receivedToken;

            public string ToolName => ToolNameValue;

            public ToolParameterSchema ParameterSchema => new();

            public Task<UnityCliLoopToolResponse> ExecuteAsync(JToken paramsToken, CancellationToken ct)
            {
                _receivedToken = ct;
                return _completion.Task;
            }

            public void EndWithCancellation()
            {
                _completion.TrySetCanceled(_receivedToken);
            }
        }

        // Stands in for an execute-dynamic-code run stuck on an await that ignores cancellation.
        private sealed class NeverReturningDynamicCodeTool : IUnityCliLoopTool
        {
            private readonly TaskCompletionSource<UnityCliLoopToolResponse> _neverCompleted = new();

            public string ToolName => UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE;

            public ToolParameterSchema ParameterSchema => new();

            public Task<UnityCliLoopToolResponse> ExecuteAsync(JToken paramsToken, CancellationToken ct)
            {
                return _neverCompleted.Task;
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
