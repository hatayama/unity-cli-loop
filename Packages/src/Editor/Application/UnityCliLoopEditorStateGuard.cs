using System;
using System.Diagnostics;
using UnityEditor;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Application
{
    /// <summary>
    /// Centralizes editor-state preconditions for tools whose execution can destabilize Unity state transitions.
    /// </summary>
    internal static class UnityCliLoopEditorStateGuard
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

        public static void Validate(string toolName)
        {
            ValidateForState(
                toolName,
                EditorApplication.isCompiling,
                EditorApplication.isUpdating);
        }

        internal static void ValidateForState(string toolName, bool isCompiling, bool isUpdating)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(toolName), "toolName must not be null or whitespace");

            GuardCondition condition = GetCondition(toolName);
            if (condition == GuardCondition.None)
            {
                return;
            }

            if ((condition & GuardCondition.NotCompiling) != 0 && isCompiling)
            {
                throw new UnityCliLoopToolBusyException(
                    UnityCompileOperationName,
                    toolName,
                    EditorApplication.isPlaying,
                    EditorApplication.isPaused);
            }

            if ((condition & GuardCondition.NotUpdating) != 0 && isUpdating)
            {
                throw new UnityCliLoopToolBusyException(
                    UnityAssetDatabaseUpdateOperationName,
                    toolName,
                    EditorApplication.isPlaying,
                    EditorApplication.isPaused);
            }
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
}
