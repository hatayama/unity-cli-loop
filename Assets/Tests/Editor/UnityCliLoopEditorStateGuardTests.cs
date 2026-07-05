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
            // Tests that compile-time editor state is reported as retryable tool busy.
            UnityCliLoopToolBusyException exception = Assert.Throws<UnityCliLoopToolBusyException>(
                () => UnityCliLoopEditorStateGuard.ValidateForState(
                    toolName: UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE,
                    isCompiling: true,
                    isUpdating: false,
                    isPlaying: false,
                    isPaused: false));

            Assert.That(exception.RunningToolName, Is.Not.Empty);
            Assert.That(exception.RequestedToolName, Is.EqualTo(UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE));
        }

        [Test]
        public void ValidateForState_WhenPlayModeControlRunsDuringEditorUpdate_ShouldThrow()
        {
            // Tests that asset-update editor state is reported as retryable tool busy.
            UnityCliLoopToolBusyException exception = Assert.Throws<UnityCliLoopToolBusyException>(
                () => UnityCliLoopEditorStateGuard.ValidateForState(
                    toolName: UnityCliLoopConstants.TOOL_NAME_CONTROL_PLAY_MODE,
                    isCompiling: false,
                    isUpdating: true,
                    isPlaying: false,
                    isPaused: false));

            Assert.That(exception.RunningToolName, Is.Not.Empty);
            Assert.That(exception.RequestedToolName, Is.EqualTo(UnityCliLoopConstants.TOOL_NAME_CONTROL_PLAY_MODE));
        }

        [Test]
        public void ValidateForState_WhenReadOnlyToolRunsDuringBusyEditorState_ShouldAllow()
        {
            // Tests that read-only tools bypass state guards that only protect mutating commands.
            Assert.DoesNotThrow(
                () => UnityCliLoopEditorStateGuard.ValidateForState(
                    toolName: "get-logs",
                    isCompiling: true,
                    isUpdating: true,
                    isPlaying: false,
                    isPaused: false));
        }
    }
}
