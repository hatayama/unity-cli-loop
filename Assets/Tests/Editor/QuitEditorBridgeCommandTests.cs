using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests the quit-editor bridge command: its routing, and when it exits the Editor. The handler itself is
    /// not run here because it exits the Editor that runs the tests.
    /// </summary>
    [TestFixture]
    public sealed class QuitEditorBridgeCommandTests
    {
        private const double ExitAt = 10.0;

        /// <summary>
        /// Verifies quit-editor is routed as an internal bridge command, which uloop launch --restart and
        /// --quit rely on before they fall back to force-killing the Editor.
        /// </summary>
        [Test]
        public void InternalBridgeCommandRouter_QuitEditorCommand_IsInternal()
        {
            bool isInternal = InternalBridgeCommandRouter.IsInternalCommand(
                UnityCliLoopConstants.COMMAND_NAME_QUIT_EDITOR);

            Assert.That(isInternal, Is.True);
        }

        /// <summary>
        /// Verifies the command name matches the one the CLI sends.
        /// </summary>
        [Test]
        public void QuitEditorCommandName_MatchesTheCliRequest()
        {
            Assert.That(UnityCliLoopConstants.COMMAND_NAME_QUIT_EDITOR, Is.EqualTo("quit-editor"));
        }

        /// <summary>
        /// Verifies the Editor is not exited before the deadline, which leaves quit-editor time to answer.
        /// </summary>
        [Test]
        public void DelayedEditorExit_BeforeTheDeadline_DoesNotExit()
        {
            List<int> exitCodes = new();
            DelayedEditorExit delayedExit = new(ExitAt, exitCodes.Add);

            bool exitRequested = delayedExit.ExitIfDue(ExitAt - 0.1);

            Assert.That(exitRequested, Is.False);
            Assert.That(exitCodes, Is.Empty);
        }

        /// <summary>
        /// Verifies the Editor exits with code 0 once the deadline is reached.
        /// </summary>
        [Test]
        public void DelayedEditorExit_AtTheDeadline_ExitsWithCodeZero()
        {
            List<int> exitCodes = new();
            DelayedEditorExit delayedExit = new(ExitAt, exitCodes.Add);

            bool exitRequested = delayedExit.ExitIfDue(ExitAt);

            Assert.That(exitRequested, Is.True);
            Assert.That(exitCodes, Is.EqualTo(new[] { 0 }));
        }

        /// <summary>
        /// Verifies a check after the exit does not exit again, since the update callback can run once more
        /// before it is removed.
        /// </summary>
        [Test]
        public void DelayedEditorExit_AfterExiting_DoesNotExitAgain()
        {
            List<int> exitCodes = new();
            DelayedEditorExit delayedExit = new(ExitAt, exitCodes.Add);
            delayedExit.ExitIfDue(ExitAt);

            bool exitRequested = delayedExit.ExitIfDue(ExitAt + 1.0);

            Assert.That(exitRequested, Is.True);
            Assert.That(exitCodes, Is.EqualTo(new[] { 0 }));
        }
    }
}
