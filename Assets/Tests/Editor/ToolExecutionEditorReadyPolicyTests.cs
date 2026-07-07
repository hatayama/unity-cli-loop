using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    public sealed class ToolExecutionEditorReadyPolicyTests
    {
        [Test]
        public void Evaluate_WhenDynamicCodeRunsDuringCompilation_ShouldReturnCompileBusyDecision()
        {
            // Tests that compile-time editor state is reported as a retryable tool busy decision.
            ToolExecutionEditorState editorState = new ToolExecutionEditorState(
                isCompiling: true,
                isUpdating: false,
                isPlaying: false,
                isPaused: false);

            ToolExecutionEditorReadyDecision decision = ToolExecutionEditorReadyPolicy.Evaluate(
                UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE,
                editorState);

            Assert.That(decision.IsReady, Is.False);
            Assert.That(decision.RunningOperationName, Is.EqualTo("unity-compile"));
            Assert.That(decision.RequestedToolName, Is.EqualTo(UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE));
        }

        [Test]
        public void Evaluate_WhenPlayModeControlRunsDuringEditorUpdate_ShouldReturnAssetUpdateBusyDecision()
        {
            // Tests that asset-update editor state is reported as a retryable tool busy decision.
            ToolExecutionEditorState editorState = new ToolExecutionEditorState(
                isCompiling: false,
                isUpdating: true,
                isPlaying: true,
                isPaused: true);

            ToolExecutionEditorReadyDecision decision = ToolExecutionEditorReadyPolicy.Evaluate(
                UnityCliLoopConstants.TOOL_NAME_CONTROL_PLAY_MODE,
                editorState);

            Assert.That(decision.IsReady, Is.False);
            Assert.That(decision.RunningOperationName, Is.EqualTo("unity-asset-database-update"));
            Assert.That(decision.RequestedToolName, Is.EqualTo(UnityCliLoopConstants.TOOL_NAME_CONTROL_PLAY_MODE));
            Assert.That(decision.IsPlaying, Is.True);
            Assert.That(decision.IsPaused, Is.True);
        }

        [Test]
        public void Evaluate_WhenReadOnlyToolRunsDuringBusyEditorState_ShouldReturnReadyDecision()
        {
            // Tests that read-only tools bypass state guards that only protect mutating commands.
            ToolExecutionEditorState editorState = new ToolExecutionEditorState(
                isCompiling: true,
                isUpdating: true,
                isPlaying: false,
                isPaused: false);

            ToolExecutionEditorReadyDecision decision = ToolExecutionEditorReadyPolicy.Evaluate(
                "get-logs",
                editorState);

            Assert.That(decision.IsReady, Is.True);
            Assert.That(decision.RequestedToolName, Is.EqualTo("get-logs"));
        }
    }
}
