using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Carries startup Debug preference values before and after persistence.
    /// </summary>
    internal readonly struct StartupDebugPreferenceResult
    {
        public bool Previous { get; }

        public bool Current { get; }

        public bool Verified { get; }

        public StartupDebugPreferenceResult(bool previous, bool current, bool verified)
        {
            Previous = previous;
            Current = current;
            Verified = verified;
        }
    }

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

            CompilationPipeline.codeOptimization = CodeOptimization.Debug;
            Debug.Assert(
                CompilationPipeline.codeOptimization == CodeOptimization.Debug,
                "Code Optimization must be Debug after the switch.");

            StartupDebugPreferenceResult startupPreference = PersistStartupDebugPreference();

            return new SetCodeOptimizationDebugStartupResponse
            {
                Success = startupPreference.Verified,
                Previous = previous.ToString(),
                Current = CompilationPipeline.codeOptimization.ToString(),
                StartupPrevious = startupPreference.Previous,
                StartupCurrent = startupPreference.Current,
                StartupVerified = startupPreference.Verified
            };
        }

        /// <summary>
        /// Persists Debug as the machine-wide startup preference and verifies its readback.
        /// </summary>
        internal static StartupDebugPreferenceResult PersistStartupDebugPreference()
        {
            bool startupPrevious = EditorPrefs.GetBool(ScriptDebugInfoEnabledEditorPrefsKey, false);
            EditorPrefs.SetBool(ScriptDebugInfoEnabledEditorPrefsKey, true);
            bool startupCurrent = EditorPrefs.GetBool(ScriptDebugInfoEnabledEditorPrefsKey, false);
            bool startupVerified = startupCurrent;
            Debug.Assert(startupVerified, "The startup Code Optimization preference must read back as Debug.");

            return new StartupDebugPreferenceResult(
                startupPrevious,
                startupCurrent,
                startupVerified);
        }
    }
}
