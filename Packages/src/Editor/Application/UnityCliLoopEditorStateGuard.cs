using System.Diagnostics;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Application
{
    /// <summary>
    /// Adapts editor-state readiness decisions to the existing tool busy exception contract.
    /// </summary>
    internal static class UnityCliLoopEditorStateGuard
    {
        public static void Validate(string toolName, IEditorRuntimeStatePort editorRuntimeStatePort)
        {
            Debug.Assert(editorRuntimeStatePort != null, "editorRuntimeStatePort must not be null");

            ValidateForState(
                toolName: toolName,
                isCompiling: editorRuntimeStatePort.IsCompiling,
                isUpdating: editorRuntimeStatePort.IsUpdating,
                isPlaying: editorRuntimeStatePort.IsPlaying,
                isPaused: editorRuntimeStatePort.IsPaused);
        }

        internal static void ValidateForState(
            string toolName,
            bool isCompiling,
            bool isUpdating,
            bool isPlaying,
            bool isPaused)
        {
            ToolExecutionEditorState editorState = new ToolExecutionEditorState(
                isCompiling,
                isUpdating,
                isPlaying,
                isPaused);
            ToolExecutionEditorReadyDecision decision =
                ToolExecutionEditorReadyPolicy.Evaluate(toolName, editorState);
            if (decision.IsReady)
            {
                return;
            }

            throw new UnityCliLoopToolBusyException(
                decision.RunningOperationName,
                decision.RequestedToolName,
                decision.IsPlaying,
                decision.IsPaused);
        }
    }
}
