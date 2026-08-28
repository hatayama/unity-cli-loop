using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Reports the session and startup Code Optimization values around a persistent Debug switch.
    /// </summary>
    public class SetCodeOptimizationDebugStartupResponse : UnityCliLoopToolResponse
    {
        public string Previous { get; set; } = string.Empty;

        public string Current { get; set; } = string.Empty;

        public bool StartupPrevious { get; set; }

        public bool StartupCurrent { get; set; }

        public bool StartupVerified { get; set; }
    }

    /// <summary>
    /// Switches the current session and Unity's machine-wide startup preference to Debug.
    /// </summary>
    internal static class SetCodeOptimizationDebugStartupBridgeCommand
    {
        private const string ScriptDebugInfoEnabledEditorPrefsKey = "ScriptDebugInfoEnabled";

        public static SetCodeOptimizationDebugStartupResponse Execute()
        {
            CodeOptimization previous = CompilationPipeline.codeOptimization;
            bool startupPrevious = EditorPrefs.GetBool(ScriptDebugInfoEnabledEditorPrefsKey, false);

            CompilationPipeline.codeOptimization = CodeOptimization.Debug;
            Debug.Assert(
                CompilationPipeline.codeOptimization == CodeOptimization.Debug,
                "Code Optimization must be Debug after the switch.");

            EditorPrefs.SetBool(ScriptDebugInfoEnabledEditorPrefsKey, true);
            bool startupCurrent = EditorPrefs.GetBool(ScriptDebugInfoEnabledEditorPrefsKey, false);
            bool startupVerified = startupCurrent;
            Debug.Assert(startupVerified, "The startup Code Optimization preference must read back as Debug.");

            return new SetCodeOptimizationDebugStartupResponse
            {
                Success = startupVerified,
                Previous = previous.ToString(),
                Current = CompilationPipeline.codeOptimization.ToString(),
                StartupPrevious = startupPrevious,
                StartupCurrent = startupCurrent,
                StartupVerified = startupVerified
            };
        }
    }
}
