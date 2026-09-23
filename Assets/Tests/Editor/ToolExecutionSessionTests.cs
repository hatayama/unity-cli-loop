using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    public sealed class ToolExecutionSessionTests
    {
        [Test]
        public void Begin_WhenToolIsUnknown_ShouldThrowArgumentExceptionWithoutEnteringSession()
        {
            // Tests that unknown tool rejection preserves the exception message and leaves the session slot clean.
            ToolExecutionSession session = new ToolExecutionSession();
            UnityCliLoopToolRegistry registry = CreateRegistry(new InMemoryToolSettingsPort(), new IUnityCliLoopTool[0]);

            ArgumentException exception = Assert.Throws<ArgumentException>(() => session.Begin(registry, "missing-tool", CancellationToken.None));
            ToolExecutionSessionEnterResult enterResult = session.TryEnter("other-tool");

            Assert.That(exception.Message, Is.EqualTo("Unknown tool: missing-tool"));
            Assert.That(enterResult.IsEntered, Is.True);

            enterResult.Lease.Dispose();
        }

        [Test]
        public void Begin_WhenToolIsDisabled_ShouldThrowToolDisabledExceptionWithoutEnteringSession()
        {
            // Tests that disabled tool rejection happens before session entry and keeps the slot available.
            ToolExecutionSession session = new ToolExecutionSession();
            InMemoryToolSettingsPort settingsPort = new InMemoryToolSettingsPort();
            SessionTestTool disabledTool = new SessionTestTool("disabled-tool");
            settingsPort.SetToolEnabled(disabledTool.ToolName, false);
            UnityCliLoopToolRegistry registry = CreateRegistry(settingsPort, new IUnityCliLoopTool[] { disabledTool });

            ToolDisabledException exception = Assert.Throws<ToolDisabledException>(() => session.Begin(registry, disabledTool.ToolName, CancellationToken.None));
            ToolExecutionSessionEnterResult enterResult = session.TryEnter("other-tool");

            Assert.That(exception.Message, Is.EqualTo("Tool 'disabled-tool' is disabled"));
            Assert.That(exception.ToolName, Is.EqualTo(disabledTool.ToolName));
            Assert.That(enterResult.IsEntered, Is.True);

            enterResult.Lease.Dispose();
        }

        [Test]
        public void Begin_WhenToolIsBlockedBySecurity_ShouldThrowSecurityExceptionWithoutEnteringSession()
        {
            // Tests that security rejection preserves the reason string and leaves the session slot clean.
            ToolExecutionSession session = new ToolExecutionSession();
            SecurityBlockedSessionTestTool blockedTool = new SecurityBlockedSessionTestTool();
            UnityCliLoopToolRegistry registry = CreateRegistry(new InMemoryToolSettingsPort(), new IUnityCliLoopTool[] { blockedTool });

            UnityCliLoopSecurityException exception = Assert.Throws<UnityCliLoopSecurityException>(() => session.Begin(registry, blockedTool.ToolName, CancellationToken.None));
            ToolExecutionSessionEnterResult enterResult = session.TryEnter("other-tool");

            Assert.That(exception.SecurityReason, Is.EqualTo("Tool is blocked by security settings"));
            Assert.That(exception.ToolName, Is.EqualTo(blockedTool.ToolName));
            Assert.That(enterResult.IsEntered, Is.True);

            enterResult.Lease.Dispose();
        }

        [Test]
        public void Begin_WhenToolIsAllowed_ShouldReturnEnteredResultWithTool()
        {
            // Tests that a registered, enabled, allowed tool enters the session and carries the selected tool.
            ToolExecutionSession session = new ToolExecutionSession();
            SessionTestTool tool = new SessionTestTool("allowed-tool");
            UnityCliLoopToolRegistry registry = CreateRegistry(new InMemoryToolSettingsPort(), new IUnityCliLoopTool[] { tool });

            ToolExecutionSessionBeginResult result = session.Begin(registry, tool.ToolName, CancellationToken.None);

            Assert.That(result.IsEntered, Is.True);
            Assert.That(result.Tool, Is.SameAs(tool));
            Assert.That(result.RunningToolName, Is.Empty);

            result.Lease.Dispose();
        }

        [Test]
        public void Begin_WhenDifferentToolIsRunning_ShouldReturnBusyWithRunningToolName()
        {
            // Tests that admission returns busy after policy gates pass when another tool owns the slot.
            ToolExecutionSession session = new ToolExecutionSession();
            SessionTestTool requestedTool = new SessionTestTool("requested-tool");
            UnityCliLoopToolRegistry registry = CreateRegistry(new InMemoryToolSettingsPort(), new IUnityCliLoopTool[] { requestedTool });
            ToolExecutionLease runningLease = session.TryEnter("running-tool").Lease;

            ToolExecutionSessionBeginResult result = session.Begin(registry, requestedTool.ToolName, CancellationToken.None);

            Assert.That(result.IsEntered, Is.False);
            Assert.That(result.Tool, Is.Null);
            Assert.That(result.RunningToolName, Is.EqualTo("running-tool"));

            runningLease.Dispose();
        }

        [Test]
        public void TryEnter_WhenNoToolIsRunning_ShouldEnterAndAllowExitThenNextTool()
        {
            // Tests that an empty session admits a tool and releases the slot after exit.
            ToolExecutionSession session = new ToolExecutionSession();

            ToolExecutionSessionEnterResult firstResult = session.TryEnter("first-tool");
            firstResult.Lease.Dispose();
            ToolExecutionSessionEnterResult secondResult = session.TryEnter("second-tool");

            Assert.That(firstResult.IsEntered, Is.True);
            Assert.That(firstResult.RunningToolName, Is.Empty);
            Assert.That(secondResult.IsEntered, Is.True);

            secondResult.Lease.Dispose();
        }

        [Test]
        public void TryEnter_WhenDifferentToolIsRunning_ShouldReturnBusyWithRunningToolName()
        {
            // Tests that the single-flight gate reports the already running tool for rejected requests.
            ToolExecutionSession session = new ToolExecutionSession();

            ToolExecutionSessionEnterResult firstResult = session.TryEnter("running-tool");
            ToolExecutionSessionEnterResult busyResult = session.TryEnter("requested-tool");

            Assert.That(firstResult.IsEntered, Is.True);
            Assert.That(busyResult.IsEntered, Is.False);
            Assert.That(busyResult.RunningToolName, Is.EqualTo("running-tool"));

            firstResult.Lease.Dispose();
        }

        [Test]
        public void TryEnter_WhenExecuteDynamicCodeIsAlreadyRunning_ShouldAllowSecondExecuteDynamicCode()
        {
            // Tests that execute-dynamic-code keeps its existing shared execution slot behavior.
            ToolExecutionSession session = new ToolExecutionSession();

            ToolExecutionSessionEnterResult firstResult =
                session.TryEnter(UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE);
            ToolExecutionSessionEnterResult secondResult =
                session.TryEnter(UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE);

            Assert.That(firstResult.IsEntered, Is.True);
            Assert.That(secondResult.IsEntered, Is.True);

            firstResult.Lease.Dispose();
            secondResult.Lease.Dispose();
        }

        [Test]
        public void Exit_WhenTwoSharedDynamicCodeExecutionsEntered_ShouldKeepSlotBusyUntilBothExit()
        {
            // Tests that shared execute-dynamic-code entries keep the session busy until every entry exits.
            ToolExecutionSession session = new ToolExecutionSession();

            ToolExecutionLease firstLease = session.TryEnter(UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE).Lease;
            ToolExecutionLease secondLease = session.TryEnter(UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE).Lease;

            ToolExecutionSessionEnterResult busyBeforeExit = session.TryEnter("other-tool");
            firstLease.Dispose();
            ToolExecutionSessionEnterResult busyAfterOneExit = session.TryEnter("other-tool");
            secondLease.Dispose();
            ToolExecutionSessionEnterResult enteredAfterBothExit = session.TryEnter("other-tool");

            Assert.That(busyBeforeExit.IsEntered, Is.False);
            Assert.That(busyBeforeExit.RunningToolName, Is.EqualTo(UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE));
            Assert.That(busyAfterOneExit.IsEntered, Is.False);
            Assert.That(busyAfterOneExit.RunningToolName, Is.EqualTo(UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE));
            Assert.That(enteredAfterBothExit.IsEntered, Is.True);

            enteredAfterBothExit.Lease.Dispose();
        }

        /// <summary>
        /// Verifies a shared-slot second enter does not overwrite the first start timestamp.
        /// </summary>
        [Test]
        public void TryEnter_WhenSharedSlotSecondEnter_ShouldKeepFirstStartTimestamp()
        {
            long timestamp = 0;
            ToolExecutionSession session = new ToolExecutionSession(() => timestamp);

            ToolExecutionLease firstLease = session.TryEnter(UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE).Lease;
            timestamp += 3 * Stopwatch.Frequency;
            ToolExecutionLease secondLease = session.TryEnter(UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE).Lease;
            timestamp += 4 * Stopwatch.Frequency;
            ToolExecutionSessionEnterResult busyResult = session.TryEnter("other-tool");

            Assert.That(busyResult.IsEntered, Is.False);
            Assert.That(busyResult.RunningToolName, Is.EqualTo(UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE));
            Assert.That(busyResult.RunningToolElapsedSeconds, Is.EqualTo(7));

            firstLease.Dispose();
            secondLease.Dispose();
        }

        /// <summary>
        /// Verifies one shared-slot exit keeps the original start timestamp for the remaining execution.
        /// </summary>
        [Test]
        public void Exit_WhenOneSharedExecutionRemains_ShouldKeepOriginalStartTimestamp()
        {
            long timestamp = 0;
            ToolExecutionSession session = new ToolExecutionSession(() => timestamp);

            ToolExecutionLease firstLease = session.TryEnter(UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE).Lease;
            ToolExecutionLease secondLease = session.TryEnter(UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE).Lease;
            timestamp += 5 * Stopwatch.Frequency;
            firstLease.Dispose();
            ToolExecutionSessionEnterResult busyResult = session.TryEnter("other-tool");

            Assert.That(busyResult.IsEntered, Is.False);
            Assert.That(busyResult.RunningToolElapsedSeconds, Is.EqualTo(5));

            secondLease.Dispose();
        }

        /// <summary>
        /// Verifies the start timestamp is cleared when the last execution exits.
        /// </summary>
        [Test]
        public void Exit_WhenLastExecutionExits_ShouldClearStartTimestamp()
        {
            long timestamp = 0;
            ToolExecutionSession session = new ToolExecutionSession(() => timestamp);

            ToolExecutionLease firstLease = session.TryEnter("first-tool").Lease;
            timestamp += 10 * Stopwatch.Frequency;
            firstLease.Dispose();
            ToolExecutionLease secondLease = session.TryEnter("second-tool").Lease;
            timestamp += 2 * Stopwatch.Frequency;
            ToolExecutionSessionEnterResult busyResult = session.TryEnter("other-tool");

            Assert.That(busyResult.IsEntered, Is.False);
            Assert.That(busyResult.RunningToolName, Is.EqualTo("second-tool"));
            Assert.That(busyResult.RunningToolElapsedSeconds, Is.EqualTo(2));

            secondLease.Dispose();
        }

        /// <summary>
        /// Verifies a busy decision returns the running tool name and elapsed seconds from one snapshot.
        /// </summary>
        [Test]
        public void TryEnter_WhenBusy_ShouldReturnNameAndElapsedFromSameSnapshot()
        {
            long timestamp = 0;
            ToolExecutionSession session = new ToolExecutionSession(() => timestamp);

            ToolExecutionLease runningLease = session.TryEnter("running-tool").Lease;
            timestamp += 4 * Stopwatch.Frequency + (Stopwatch.Frequency - 1);
            ToolExecutionSessionEnterResult busyResult = session.TryEnter("requested-tool");

            Assert.That(busyResult.IsEntered, Is.False);
            Assert.That(busyResult.RunningToolName, Is.EqualTo("running-tool"));
            Assert.That(busyResult.RunningToolElapsedSeconds, Is.EqualTo(4));

            runningLease.Dispose();
        }

        /// <summary>
        /// Verifies Begin busy results carry the same elapsed snapshot as TryEnter.
        /// </summary>
        [Test]
        public void Begin_WhenDifferentToolIsRunning_ShouldReturnBusyWithElapsedSeconds()
        {
            long timestamp = 0;
            ToolExecutionSession session = new ToolExecutionSession(() => timestamp);
            SessionTestTool requestedTool = new SessionTestTool("requested-tool");
            UnityCliLoopToolRegistry registry = CreateRegistry(new InMemoryToolSettingsPort(), new IUnityCliLoopTool[] { requestedTool });
            ToolExecutionLease runningLease = session.TryEnter("running-tool").Lease;
            timestamp += 6 * Stopwatch.Frequency;

            ToolExecutionSessionBeginResult result = session.Begin(registry, requestedTool.ToolName, CancellationToken.None);

            Assert.That(result.IsEntered, Is.False);
            Assert.That(result.RunningToolName, Is.EqualTo("running-tool"));
            Assert.That(result.RunningToolElapsedSeconds, Is.EqualTo(6));

            runningLease.Dispose();
        }

        /// <summary>
        /// Verifies a second Dispose of a returned lease does not release the slot a later tool holds.
        /// </summary>
        [Test]
        public void Dispose_WhenLeaseIsDisposedTwice_ShouldKeepLaterHolderSlot()
        {
            ToolExecutionSession session = new ToolExecutionSession();

            ToolExecutionLease firstLease = session.TryEnter("first-tool").Lease;
            firstLease.Dispose();
            ToolExecutionLease secondLease = session.TryEnter("second-tool").Lease;
            firstLease.Dispose();
            ToolExecutionSessionEnterResult busyResult = session.TryEnter("other-tool");

            Assert.That(busyResult.IsEntered, Is.False);
            Assert.That(busyResult.RunningToolName, Is.EqualTo("second-tool"));

            secondLease.Dispose();
        }

        /// <summary>
        /// Verifies disposing one shared execute-dynamic-code lease twice keeps the other shared holder's slot.
        /// </summary>
        [Test]
        public void Dispose_WhenOneSharedLeaseIsDisposedTwice_ShouldKeepSlotBusyForOtherSharedHolder()
        {
            ToolExecutionSession session = new ToolExecutionSession();

            ToolExecutionLease firstLease = session.TryEnter(UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE).Lease;
            ToolExecutionLease secondLease = session.TryEnter(UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE).Lease;
            firstLease.Dispose();
            firstLease.Dispose();
            ToolExecutionSessionEnterResult busyResult = session.TryEnter("other-tool");

            Assert.That(busyResult.IsEntered, Is.False);
            Assert.That(busyResult.RunningToolName, Is.EqualTo(UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE));

            secondLease.Dispose();
        }

        /// <summary>
        /// Verifies a cancelled execute-dynamic-code holder still keeps other tools busy while its grace period runs.
        /// </summary>
        [Test]
        public void TryEnter_WhenDynamicCodeHolderIsCancelledWithinGrace_ShouldStayBusy()
        {
            long timestamp = 0;
            ToolExecutionSession session = new ToolExecutionSession(() => timestamp);
            using CancellationTokenSource holderCancellation = new CancellationTokenSource();
            ToolExecutionLease holderLease = session.TryEnter(
                UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE,
                holderCancellation.Token).Lease;
            holderCancellation.Cancel();

            ToolExecutionSessionEnterResult firstAttempt = session.TryEnter("other-tool");
            timestamp += GraceTicks - 1;
            ToolExecutionSessionEnterResult secondAttempt = session.TryEnter("other-tool");

            Assert.That(firstAttempt.IsEntered, Is.False);
            Assert.That(secondAttempt.IsEntered, Is.False);
            Assert.That(secondAttempt.RunningToolName, Is.EqualTo(UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE));

            holderLease.Dispose();
        }

        /// <summary>
        /// Verifies another tool enters once a cancelled execute-dynamic-code holder has outlived its grace period.
        /// </summary>
        [Test]
        public void TryEnter_WhenDynamicCodeHolderIsCancelledPastGrace_ShouldLetOtherToolEnter()
        {
            long timestamp = 0;
            ToolExecutionSession session = new ToolExecutionSession(() => timestamp);
            using CancellationTokenSource holderCancellation = new CancellationTokenSource();
            ToolExecutionLease holderLease = session.TryEnter(
                UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE,
                holderCancellation.Token).Lease;
            holderCancellation.Cancel();

            ToolExecutionSessionEnterResult firstAttempt = session.TryEnter("other-tool");
            timestamp += GraceTicks;
            ToolExecutionSessionEnterResult secondAttempt = session.TryEnter("other-tool");

            Assert.That(firstAttempt.IsEntered, Is.False);
            Assert.That(secondAttempt.IsEntered, Is.True);

            secondAttempt.Lease.Dispose();
            holderLease.Dispose();
        }

        /// <summary>
        /// Verifies repeated busy retries do not restart the grace period, so the holder is revoked a grace period after the first retry saw the cancellation.
        /// </summary>
        [Test]
        public void TryEnter_WhenBusyRetriesRepeatDuringGrace_ShouldMeasureGraceFromFirstObservation()
        {
            long timestamp = 0;
            ToolExecutionSession session = new ToolExecutionSession(() => timestamp);
            using CancellationTokenSource holderCancellation = new CancellationTokenSource();
            ToolExecutionLease holderLease = session.TryEnter(
                UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE,
                holderCancellation.Token).Lease;
            holderCancellation.Cancel();

            for (int second = 0; second < ToolExecutionSession.CancelledLeaseGraceSeconds; second++)
            {
                ToolExecutionSessionEnterResult retry = session.TryEnter("other-tool");
                Assert.That(retry.IsEntered, Is.False, $"retry at {second}s must stay busy");
                timestamp += Stopwatch.Frequency;
            }

            ToolExecutionSessionEnterResult lateAttempt = session.TryEnter("other-tool");

            Assert.That(timestamp, Is.EqualTo(GraceTicks));
            Assert.That(lateAttempt.IsEntered, Is.True);

            lateAttempt.Lease.Dispose();
            holderLease.Dispose();
        }

        /// <summary>
        /// Verifies the grace period starts when a retry first sees the cancellation, not when the lease was issued or when an earlier retry saw it still live.
        /// </summary>
        [Test]
        public void TryEnter_WhenHolderIsCancelledLongAfterIssue_ShouldStartGraceAtFirstObservedCancellation()
        {
            long timestamp = 0;
            ToolExecutionSession session = new ToolExecutionSession(() => timestamp);
            using CancellationTokenSource holderCancellation = new CancellationTokenSource();
            ToolExecutionLease holderLease = session.TryEnter(
                UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE,
                holderCancellation.Token).Lease;

            ToolExecutionSessionEnterResult liveAttempt = session.TryEnter("other-tool");
            timestamp += 10 * GraceTicks;
            holderCancellation.Cancel();
            ToolExecutionSessionEnterResult firstCancelledAttempt = session.TryEnter("other-tool");
            timestamp += GraceTicks - 1;
            ToolExecutionSessionEnterResult withinGraceAttempt = session.TryEnter("other-tool");
            timestamp += 1;
            ToolExecutionSessionEnterResult lateAttempt = session.TryEnter("other-tool");

            Assert.That(liveAttempt.IsEntered, Is.False);
            Assert.That(firstCancelledAttempt.IsEntered, Is.False);
            Assert.That(withinGraceAttempt.IsEntered, Is.False);
            Assert.That(lateAttempt.IsEntered, Is.True);

            lateAttempt.Lease.Dispose();
            holderLease.Dispose();
        }

        /// <summary>
        /// Verifies an execute-dynamic-code holder that was never cancelled keeps the slot however long it runs.
        /// </summary>
        [Test]
        public void TryEnter_WhenDynamicCodeHolderIsNotCancelled_ShouldStayBusyRegardlessOfElapsedTime()
        {
            long timestamp = 0;
            ToolExecutionSession session = new ToolExecutionSession(() => timestamp);
            using CancellationTokenSource holderCancellation = new CancellationTokenSource();
            ToolExecutionLease holderLease = session.TryEnter(
                UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE,
                holderCancellation.Token).Lease;

            session.TryEnter("other-tool");
            timestamp += 100 * GraceTicks;
            ToolExecutionSessionEnterResult lateAttempt = session.TryEnter("other-tool");

            Assert.That(lateAttempt.IsEntered, Is.False);

            holderLease.Dispose();
        }

        /// <summary>
        /// Verifies disposing a revoked lease late does not release the slot of the holder that entered after it.
        /// </summary>
        [Test]
        public void Dispose_WhenRevokedLeaseIsDisposedLate_ShouldKeepNewHolderSlot()
        {
            long timestamp = 0;
            ToolExecutionSession session = new ToolExecutionSession(() => timestamp);
            using CancellationTokenSource holderCancellation = new CancellationTokenSource();
            ToolExecutionLease revokedLease = session.TryEnter(
                UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE,
                holderCancellation.Token).Lease;
            holderCancellation.Cancel();
            session.TryEnter("new-tool");
            timestamp += GraceTicks;
            ToolExecutionLease newLease = session.TryEnter("new-tool").Lease;

            revokedLease.Dispose();
            ToolExecutionSessionEnterResult busyResult = session.TryEnter("other-tool");

            Assert.That(busyResult.IsEntered, Is.False);
            Assert.That(busyResult.RunningToolName, Is.EqualTo("new-tool"));

            newLease.Dispose();
        }

        /// <summary>
        /// Verifies revoking a stale cancelled shared lease keeps the slot busy while another shared lease is live.
        /// </summary>
        [Test]
        public void TryEnter_WhenOnlyOneSharedDynamicCodeLeaseIsCancelledPastGrace_ShouldStayBusy()
        {
            long timestamp = 0;
            ToolExecutionSession session = new ToolExecutionSession(() => timestamp);
            using CancellationTokenSource staleCancellation = new CancellationTokenSource();
            ToolExecutionLease staleLease = session.TryEnter(
                UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE,
                staleCancellation.Token).Lease;
            ToolExecutionLease liveLease = session.TryEnter(
                UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE,
                CancellationToken.None).Lease;
            staleCancellation.Cancel();

            session.TryEnter("other-tool");
            timestamp += GraceTicks;
            ToolExecutionSessionEnterResult lateAttempt = session.TryEnter("other-tool");

            Assert.That(lateAttempt.IsEntered, Is.False);
            Assert.That(lateAttempt.RunningToolName, Is.EqualTo(UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE));

            staleLease.Dispose();
            liveLease.Dispose();
        }

        /// <summary>
        /// Verifies a cancelled holder of a tool other than execute-dynamic-code is never revoked, since run-tests keeps cleaning up after a cancel.
        /// </summary>
        [Test]
        public void TryEnter_WhenNonDynamicCodeHolderIsCancelledPastGrace_ShouldStayBusy()
        {
            long timestamp = 0;
            ToolExecutionSession session = new ToolExecutionSession(() => timestamp);
            using CancellationTokenSource holderCancellation = new CancellationTokenSource();
            ToolExecutionLease holderLease = session.TryEnter("run-tests", holderCancellation.Token).Lease;
            holderCancellation.Cancel();

            session.TryEnter("other-tool");
            timestamp += 100 * GraceTicks;
            ToolExecutionSessionEnterResult lateAttempt = session.TryEnter("other-tool");

            Assert.That(lateAttempt.IsEntered, Is.False);
            Assert.That(lateAttempt.RunningToolName, Is.EqualTo("run-tests"));

            holderLease.Dispose();
        }

        private static long GraceTicks => ToolExecutionSession.CancelledLeaseGraceSeconds * Stopwatch.Frequency;

        private static UnityCliLoopToolRegistry CreateRegistry(InMemoryToolSettingsPort settingsPort, IUnityCliLoopTool[] tools)
        {
            UnityCliLoopToolRegistry registry = new UnityCliLoopToolRegistry(
                settingsPort,
                internalToolNameProvider: null,
                toolDiscovery: null);

            foreach (IUnityCliLoopTool tool in tools)
            {
                registry.RegisterTool(tool);
            }

            return registry;
        }

        private sealed class SessionTestTool : IUnityCliLoopTool
        {
            public SessionTestTool(string toolName)
            {
                ToolName = toolName;
            }

            public string ToolName { get; }

            public ToolParameterSchema ParameterSchema => new();

            public Task<UnityCliLoopToolResponse> ExecuteAsync(JToken paramsToken, CancellationToken ct)
            {
                return Task.FromResult<UnityCliLoopToolResponse>(new SessionTestResponse());
            }
        }

        // The enum currently has only None, so an undefined value is the only way to exercise security-blocked handling.
        [UnityCliLoopTool(RequiredSecuritySetting = (UnityCliLoopSecuritySetting)999)]
        private sealed class SecurityBlockedSessionTestTool : IUnityCliLoopTool
        {
            public string ToolName => "security-blocked-session-test";

            public ToolParameterSchema ParameterSchema => new();

            public Task<UnityCliLoopToolResponse> ExecuteAsync(JToken paramsToken, CancellationToken ct)
            {
                return Task.FromResult<UnityCliLoopToolResponse>(new SessionTestResponse());
            }
        }

        private sealed class InMemoryToolSettingsPort : IToolSettingsPort
        {
            private readonly HashSet<string> _disabledTools = new();

            public bool IsToolEnabled(string toolName)
            {
                return !_disabledTools.Contains(toolName);
            }

            public void SetToolEnabled(string toolName, bool enabled)
            {
                if (enabled)
                {
                    _disabledTools.Remove(toolName);
                    return;
                }

                _disabledTools.Add(toolName);
            }

            public string[] GetDisabledTools()
            {
                string[] disabledTools = new string[_disabledTools.Count];
                _disabledTools.CopyTo(disabledTools);
                return disabledTools;
            }

            public void InvalidateCache()
            {
            }
        }

        private sealed class SessionTestResponse : UnityCliLoopToolResponse
        {
        }
    }
}
