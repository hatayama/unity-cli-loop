using UnityEditor.Compilation;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Reports the Code Optimization value before and after switching to Debug.
    /// </summary>
    public class SetCodeOptimizationDebugResponse : UnityCliLoopToolResponse
    {
        public string Previous { get; set; } = string.Empty;

        public string Current { get; set; } = string.Empty;
    }

    /// <summary>
    /// Switches CompilationPipeline.codeOptimization to Debug without waiting for recompile.
    /// </summary>
    internal static class SetCodeOptimizationDebugBridgeCommand
    {
        public static SetCodeOptimizationDebugResponse Execute()
        {
            CodeOptimization previous = CompilationPipeline.codeOptimization;
            CompilationPipeline.codeOptimization = CodeOptimization.Debug;
            SetCodeOptimizationDebugResponse response = new SetCodeOptimizationDebugResponse
            {
                Previous = previous.ToString(),
                Current = CodeOptimization.Debug.ToString()
            };
            Debug.Assert(response.Current == nameof(CodeOptimization.Debug), "Code Optimization must be Debug after the switch.");
            return response;
        }
    }
}
