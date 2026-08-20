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

            ArgumentException exception = Assert.Throws<ArgumentException>(() => session.Begin(registry, "missing-tool"));
            ToolExecutionSessionEnterResult enterResult = session.TryEnter("other-tool");

            Assert.That(exception.Message, Is.EqualTo("Unknown tool: missing-tool"));
            Assert.That(enterResult.IsEntered, Is.True);

            session.Exit();
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

            ToolDisabledException exception = Assert.Throws<ToolDisabledException>(() => session.Begin(registry, disabledTool.ToolName));
            ToolExecutionSessionEnterResult enterResult = session.TryEnter("other-tool");

            Assert.That(exception.Message, Is.EqualTo("Tool 'disabled-tool' is disabled"));
            Assert.That(exception.ToolName, Is.EqualTo(disabledTool.ToolName));
            Assert.That(enterResult.IsEntered, Is.True);

            session.Exit();
        }

        [Test]
        public void Begin_WhenToolIsBlockedBySecurity_ShouldThrowSecurityExceptionWithoutEnteringSession()
        {
            // Tests that security rejection preserves the reason string and leaves the session slot clean.
            ToolExecutionSession session = new ToolExecutionSession();
            SecurityBlockedSessionTestTool blockedTool = new SecurityBlockedSessionTestTool();
            UnityCliLoopToolRegistry registry = CreateRegistry(new InMemoryToolSettingsPort(), new IUnityCliLoopTool[] { blockedTool });

            UnityCliLoopSecurityException exception = Assert.Throws<UnityCliLoopSecurityException>(() => session.Begin(registry, blockedTool.ToolName));
            ToolExecutionSessionEnterResult enterResult = session.TryEnter("other-tool");

            Assert.That(exception.SecurityReason, Is.EqualTo("Tool is blocked by security settings"));
            Assert.That(exception.ToolName, Is.EqualTo(blockedTool.ToolName));
            Assert.That(enterResult.IsEntered, Is.True);

            session.Exit();
        }

        [Test]
        public void Begin_WhenToolIsAllowed_ShouldReturnEnteredResultWithTool()
        {
            // Tests that a registered, enabled, allowed tool enters the session and carries the selected tool.
            ToolExecutionSession session = new ToolExecutionSession();
            SessionTestTool tool = new SessionTestTool("allowed-tool");
            UnityCliLoopToolRegistry registry = CreateRegistry(new InMemoryToolSettingsPort(), new IUnityCliLoopTool[] { tool });

            ToolExecutionSessionBeginResult result = session.Begin(registry, tool.ToolName);

            Assert.That(result.IsEntered, Is.True);
            Assert.That(result.Tool, Is.SameAs(tool));
            Assert.That(result.RunningToolName, Is.Empty);

            session.Exit();
        }

        [Test]
        public void Begin_WhenDifferentToolIsRunning_ShouldReturnBusyWithRunningToolName()
        {
            // Tests that admission returns busy after policy gates pass when another tool owns the slot.
            ToolExecutionSession session = new ToolExecutionSession();
            SessionTestTool requestedTool = new SessionTestTool("requested-tool");
            UnityCliLoopToolRegistry registry = CreateRegistry(new InMemoryToolSettingsPort(), new IUnityCliLoopTool[] { requestedTool });
            session.TryEnter("running-tool");

            ToolExecutionSessionBeginResult result = session.Begin(registry, requestedTool.ToolName);

            Assert.That(result.IsEntered, Is.False);
            Assert.That(result.Tool, Is.Null);
            Assert.That(result.RunningToolName, Is.EqualTo("running-tool"));

            session.Exit();
        }

        [Test]
        public void TryEnter_WhenNoToolIsRunning_ShouldEnterAndAllowExitThenNextTool()
        {
            // Tests that an empty session admits a tool and releases the slot after exit.
            ToolExecutionSession session = new ToolExecutionSession();

            ToolExecutionSessionEnterResult firstResult = session.TryEnter("first-tool");
            session.Exit();
            ToolExecutionSessionEnterResult secondResult = session.TryEnter("second-tool");

            Assert.That(firstResult.IsEntered, Is.True);
            Assert.That(firstResult.RunningToolName, Is.Empty);
            Assert.That(secondResult.IsEntered, Is.True);

            session.Exit();
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

            session.Exit();
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

            session.Exit();
            session.Exit();
        }

        [Test]
        public void Exit_WhenTwoSharedDynamicCodeExecutionsEntered_ShouldKeepSlotBusyUntilBothExit()
        {
            // Tests that shared execute-dynamic-code entries keep the session busy until every entry exits.
            ToolExecutionSession session = new ToolExecutionSession();

            session.TryEnter(UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE);
            session.TryEnter(UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE);

            ToolExecutionSessionEnterResult busyBeforeExit = session.TryEnter("other-tool");
            session.Exit();
            ToolExecutionSessionEnterResult busyAfterOneExit = session.TryEnter("other-tool");
            session.Exit();
            ToolExecutionSessionEnterResult enteredAfterBothExit = session.TryEnter("other-tool");

            Assert.That(busyBeforeExit.IsEntered, Is.False);
            Assert.That(busyBeforeExit.RunningToolName, Is.EqualTo(UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE));
            Assert.That(busyAfterOneExit.IsEntered, Is.False);
            Assert.That(busyAfterOneExit.RunningToolName, Is.EqualTo(UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE));
            Assert.That(enteredAfterBothExit.IsEntered, Is.True);

            session.Exit();
        }

        /// <summary>
        /// Verifies a shared-slot second enter does not overwrite the first start timestamp.
        /// </summary>
        [Test]
        public void TryEnter_WhenSharedSlotSecondEnter_ShouldKeepFirstStartTimestamp()
        {
            long timestamp = 0;
            ToolExecutionSession session = new ToolExecutionSession(() => timestamp);

            session.TryEnter(UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE);
            timestamp += 3 * Stopwatch.Frequency;
            session.TryEnter(UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE);
            timestamp += 4 * Stopwatch.Frequency;
            ToolExecutionSessionEnterResult busyResult = session.TryEnter("other-tool");

            Assert.That(busyResult.IsEntered, Is.False);
            Assert.That(busyResult.RunningToolName, Is.EqualTo(UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE));
            Assert.That(busyResult.RunningToolElapsedSeconds, Is.EqualTo(7));

            session.Exit();
            session.Exit();
        }

        /// <summary>
        /// Verifies one shared-slot exit keeps the original start timestamp for the remaining execution.
        /// </summary>
        [Test]
        public void Exit_WhenOneSharedExecutionRemains_ShouldKeepOriginalStartTimestamp()
        {
            long timestamp = 0;
            ToolExecutionSession session = new ToolExecutionSession(() => timestamp);

            session.TryEnter(UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE);
            session.TryEnter(UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE);
            timestamp += 5 * Stopwatch.Frequency;
            session.Exit();
            ToolExecutionSessionEnterResult busyResult = session.TryEnter("other-tool");

            Assert.That(busyResult.IsEntered, Is.False);
            Assert.That(busyResult.RunningToolElapsedSeconds, Is.EqualTo(5));

            session.Exit();
        }

        /// <summary>
        /// Verifies the start timestamp is cleared when the last execution exits.
        /// </summary>
        [Test]
        public void Exit_WhenLastExecutionExits_ShouldClearStartTimestamp()
        {
            long timestamp = 0;
            ToolExecutionSession session = new ToolExecutionSession(() => timestamp);

            session.TryEnter("first-tool");
            timestamp += 10 * Stopwatch.Frequency;
            session.Exit();
            session.TryEnter("second-tool");
            timestamp += 2 * Stopwatch.Frequency;
            ToolExecutionSessionEnterResult busyResult = session.TryEnter("other-tool");

            Assert.That(busyResult.IsEntered, Is.False);
            Assert.That(busyResult.RunningToolName, Is.EqualTo("second-tool"));
            Assert.That(busyResult.RunningToolElapsedSeconds, Is.EqualTo(2));

            session.Exit();
        }

        /// <summary>
        /// Verifies a busy decision returns the running tool name and elapsed seconds from one snapshot.
        /// </summary>
        [Test]
        public void TryEnter_WhenBusy_ShouldReturnNameAndElapsedFromSameSnapshot()
        {
            long timestamp = 0;
            ToolExecutionSession session = new ToolExecutionSession(() => timestamp);

            session.TryEnter("running-tool");
            timestamp += 4 * Stopwatch.Frequency + (Stopwatch.Frequency - 1);
            ToolExecutionSessionEnterResult busyResult = session.TryEnter("requested-tool");

            Assert.That(busyResult.IsEntered, Is.False);
            Assert.That(busyResult.RunningToolName, Is.EqualTo("running-tool"));
            Assert.That(busyResult.RunningToolElapsedSeconds, Is.EqualTo(4));

            session.Exit();
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
            session.TryEnter("running-tool");
            timestamp += 6 * Stopwatch.Frequency;

            ToolExecutionSessionBeginResult result = session.Begin(registry, requestedTool.ToolName);

            Assert.That(result.IsEntered, Is.False);
            Assert.That(result.RunningToolName, Is.EqualTo("running-tool"));
            Assert.That(result.RunningToolElapsedSeconds, Is.EqualTo(6));

            session.Exit();
        }

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
