using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests the routing of the quit-editor bridge command. Its handler is not run here because it exits
    /// the Editor that runs the tests.
    /// </summary>
    [TestFixture]
    public sealed class QuitEditorBridgeCommandTests
    {
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
    }
}
