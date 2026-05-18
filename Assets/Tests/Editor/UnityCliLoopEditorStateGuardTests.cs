using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    public sealed class UnityCliLoopEditorStateGuardTests
    {
        [Test]
        public void ValidateForState_WhenDynamicCodeRunsDuringCompilation_ShouldThrow()
        {
            UnityCliLoopToolBusyException exception = Assert.Throws<UnityCliLoopToolBusyException>(
                () => UnityCliLoopEditorStateGuard.ValidateForState(
                    UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE,
                    true,
                    false));

            Assert.That(exception.RunningToolName, Is.Not.Empty);
            Assert.That(exception.RequestedToolName, Is.EqualTo(UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE));
        }

        [Test]
        public void ValidateForState_WhenPlayModeControlRunsDuringEditorUpdate_ShouldThrow()
        {
            UnityCliLoopToolBusyException exception = Assert.Throws<UnityCliLoopToolBusyException>(
                () => UnityCliLoopEditorStateGuard.ValidateForState(
                    UnityCliLoopConstants.TOOL_NAME_CONTROL_PLAY_MODE,
                    false,
                    true));

            Assert.That(exception.RunningToolName, Is.Not.Empty);
            Assert.That(exception.RequestedToolName, Is.EqualTo(UnityCliLoopConstants.TOOL_NAME_CONTROL_PLAY_MODE));
        }

        [Test]
        public void ValidateForState_WhenReadOnlyToolRunsDuringBusyEditorState_ShouldAllow()
        {
            Assert.DoesNotThrow(
                () => UnityCliLoopEditorStateGuard.ValidateForState(
                    "get-logs",
                    true,
                    true));
        }
    }
}
