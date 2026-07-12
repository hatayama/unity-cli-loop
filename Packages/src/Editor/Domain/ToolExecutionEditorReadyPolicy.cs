using System;
using System.Diagnostics;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Evaluates editor-state preconditions for tools whose execution can destabilize Unity state transitions.
    /// </summary>
    internal static class ToolExecutionEditorReadyPolicy
    {
        private const string UnityCompileOperationName = "unity-compile";
        private const string UnityAssetDatabaseUpdateOperationName = "unity-asset-database-update";

        [Flags]
        private enum GuardCondition
        {
            None = 0,
            NotCompiling = 1,
            NotUpdating = 2,
        }

        internal static ToolExecutionEditorReadyDecision Evaluate(string toolName, ToolExecutionEditorState editorState)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(toolName), "toolName must not be null or whitespace");

            GuardCondition condition = GetCondition(toolName);
            if (condition == GuardCondition.None)
            {
                return ToolExecutionEditorReadyDecision.Ready(toolName);
            }

            if ((condition & GuardCondition.NotCompiling) != 0 && editorState.IsCompiling)
            {
                return ToolExecutionEditorReadyDecision.Busy(
                    UnityCompileOperationName,
                    toolName,
                    editorState.IsPlaying,
                    editorState.IsPaused,
                    true,
                    false);
            }

            if ((condition & GuardCondition.NotUpdating) != 0 && editorState.IsUpdating)
            {
                return ToolExecutionEditorReadyDecision.Busy(
                    UnityAssetDatabaseUpdateOperationName,
                    toolName,
                    editorState.IsPlaying,
                    editorState.IsPaused,
                    false,
                    true);
            }

            return ToolExecutionEditorReadyDecision.Ready(toolName);
        }

        private static GuardCondition GetCondition(string toolName)
        {
            switch (toolName)
            {
                case UnityCliLoopConstants.TOOL_NAME_CONTROL_PLAY_MODE:
                case UnityCliLoopConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE:
                    return GuardCondition.NotCompiling | GuardCondition.NotUpdating;
                default:
                    return GuardCondition.None;
            }
        }
    }

    /// <summary>
    /// Snapshot of editor state used by the tool execution readiness policy.
    /// </summary>
    internal readonly struct ToolExecutionEditorState
    {
        public readonly bool IsCompiling;
        public readonly bool IsUpdating;
        public readonly bool IsPlaying;
        public readonly bool IsPaused;

        public ToolExecutionEditorState(
            bool isCompiling,
            bool isUpdating,
            bool isPlaying,
            bool isPaused)
        {
            IsCompiling = isCompiling;
            IsUpdating = isUpdating;
            IsPlaying = isPlaying;
            IsPaused = isPaused;
        }
    }

    /// <summary>
    /// Result of evaluating whether a tool can execute against the current editor state.
    /// </summary>
    internal readonly struct ToolExecutionEditorReadyDecision
    {
        public readonly bool IsReady;
        public readonly string RunningOperationName;
        public readonly string RequestedToolName;
        public readonly bool IsPlaying;
        public readonly bool IsPaused;
        public readonly bool IsCompiling;
        public readonly bool IsUpdating;

        private ToolExecutionEditorReadyDecision(
            bool isReady,
            string runningOperationName,
            string requestedToolName,
            bool isPlaying,
            bool isPaused,
            bool isCompiling,
            bool isUpdating)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestedToolName), "requestedToolName must not be null or whitespace");
            Debug.Assert(isReady || !string.IsNullOrWhiteSpace(runningOperationName), "runningOperationName must not be null or whitespace for busy decisions");

            IsReady = isReady;
            RunningOperationName = runningOperationName;
            RequestedToolName = requestedToolName;
            IsPlaying = isPlaying;
            IsPaused = isPaused;
            IsCompiling = isCompiling;
            IsUpdating = isUpdating;
        }

        public static ToolExecutionEditorReadyDecision Ready(string requestedToolName)
        {
            return new ToolExecutionEditorReadyDecision(
                true,
                string.Empty,
                requestedToolName,
                false,
                false,
                false,
                false);
        }

        public static ToolExecutionEditorReadyDecision Busy(
            string runningOperationName,
            string requestedToolName,
            bool isPlaying,
            bool isPaused,
            bool isCompiling,
            bool isUpdating)
        {
            return new ToolExecutionEditorReadyDecision(
                false,
                runningOperationName,
                requestedToolName,
                isPlaying,
                isPaused,
                isCompiling,
                isUpdating);
        }
    }
}
