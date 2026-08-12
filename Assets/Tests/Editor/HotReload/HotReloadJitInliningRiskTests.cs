using NUnit.Framework;

using UnityEditor.Compilation;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Pure-function coverage for the hot-reload JIT-inlining risk heuristic (PR-4 branch a).
    /// </summary>
    public class HotReloadJitInliningRiskTests
    {
        /// <summary>
        /// What: [AggressiveInlining] is always treated as at-risk, including Debug mode.
        /// </summary>
        [Test]
        public void Evaluate_AggressiveInlining_IsAtRiskInDebugAndRelease()
        {
            Assert.That(
                HotReloadJitInliningRisk.Evaluate(
                    hasAggressiveInlining: true,
                    ilByteLength: HotReloadConstants.SmallMethodInliningRiskThresholdBytes + 100,
                    codeOptimization: CodeOptimization.Debug),
                Is.True);
            Assert.That(
                HotReloadJitInliningRisk.Evaluate(
                    hasAggressiveInlining: true,
                    ilByteLength: HotReloadConstants.SmallMethodInliningRiskThresholdBytes + 100,
                    codeOptimization: CodeOptimization.Release),
                Is.True);
        }

        /// <summary>
        /// What: the IL-size heuristic fires only under Release code optimization.
        /// </summary>
        [Test]
        public void Evaluate_SmallIlBody_IsAtRiskOnlyInRelease()
        {
            Assert.That(
                HotReloadJitInliningRisk.Evaluate(
                    hasAggressiveInlining: false,
                    ilByteLength: HotReloadConstants.SmallMethodInliningRiskThresholdBytes,
                    codeOptimization: CodeOptimization.Debug),
                Is.False);
            Assert.That(
                HotReloadJitInliningRisk.Evaluate(
                    hasAggressiveInlining: false,
                    ilByteLength: HotReloadConstants.SmallMethodInliningRiskThresholdBytes,
                    codeOptimization: CodeOptimization.Release),
                Is.True);
        }

        /// <summary>
        /// What: bodies larger than the threshold are not flagged by the IL-size heuristic.
        /// </summary>
        [Test]
        public void Evaluate_LargeIlBody_IsNotAtRiskFromSizeAlone()
        {
            Assert.That(
                HotReloadJitInliningRisk.Evaluate(
                    hasAggressiveInlining: false,
                    ilByteLength: HotReloadConstants.SmallMethodInliningRiskThresholdBytes + 1,
                    codeOptimization: CodeOptimization.Release),
                Is.False);
            Assert.That(
                HotReloadJitInliningRisk.Evaluate(
                    hasAggressiveInlining: false,
                    ilByteLength: null,
                    codeOptimization: CodeOptimization.Release),
                Is.False);
        }

        /// <summary>
        /// What: FormatAggregatedWarning ends with the status-based self-check sentence.
        /// </summary>
        [Test]
        public void FormatAggregatedWarning_EndsWithStatusSelfCheckSentence()
        {
            string warning = HotReloadJitInliningRisk.FormatAggregatedWarning(
                atRiskCount: 1,
                patchedTotal: 3,
                methodLabels: new[] { "Demo.get_Probe()" });

            Assert.That(warning, Does.Contain("1 of 3 patched methods had pre-patch bodies"));
            Assert.That(warning, Does.Contain("Demo.get_Probe()"));
            Assert.That(
                warning,
                Does.EndWith(
                    " If 'uloop hot-reload --status' shows the method's InvocationCount increasing afterwards, its call sites are reaching the patched body and this warning did not apply."));
        }
    }
}
