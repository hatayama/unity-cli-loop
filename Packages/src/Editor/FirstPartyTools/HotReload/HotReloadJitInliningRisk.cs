using System.Collections.Generic;

using UnityEditor.Compilation;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Pure heuristic for whether a patched method is likely already JIT-inlined into callers.
    /// Callers inject CompilationPipeline.codeOptimization so EditMode tests can cover both modes.
    /// </summary>
    internal static class HotReloadJitInliningRisk
    {
        /// <summary>
        /// Returns true when the method should contribute to the aggregated inline-risk warning.
        /// [AggressiveInlining] always qualifies; the IL-size heuristic applies only in Release.
        /// </summary>
        public static bool Evaluate(
            bool hasAggressiveInlining,
            int? ilByteLength,
            CodeOptimization codeOptimization)
        {
            if (hasAggressiveInlining)
            {
                return true;
            }

            if (codeOptimization != CodeOptimization.Release)
            {
                return false;
            }

            return ilByteLength.HasValue
                && ilByteLength.Value <= HotReloadConstants.SmallMethodInliningRiskThresholdBytes;
        }

        /// <summary>
        /// Builds the single aggregated Warning line for methods that Evaluate flagged.
        /// </summary>
        public static string FormatAggregatedWarning(
            int atRiskCount,
            int patchedTotal,
            IReadOnlyList<string> methodLabels)
        {
            Debug.Assert(atRiskCount > 0, "atRiskCount must be positive.");
            Debug.Assert(methodLabels != null, "methodLabels must not be null.");
            Debug.Assert(methodLabels.Count == atRiskCount, "methodLabels count must match atRiskCount.");

            string methods = string.Join(", ", methodLabels);
            return $"{atRiskCount} of {patchedTotal} patched methods had pre-patch bodies small enough (or marked [AggressiveInlining]) that the Mono JIT may already have inlined them into callers compiled before the patch; those call sites keep the old behavior until a real compile: {methods}"
                + " If 'uloop hot-reload --status' shows the method's InvocationCount increasing afterwards, its call sites are reaching the patched body and this warning did not apply.";
        }
    }
}
