using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the run-tests hot-reload discard Warning uses the policy-form literal
    /// and stays empty when no patches were live at test-run start.
    /// </summary>
    public sealed class RunTestsHotReloadDiscardWarningBuilderTests
    {
        /// <summary>
        /// What: a positive live-change count formats the exact policy-form Warning.
        /// </summary>
        [Test]
        public void Build_WhenActiveChangeCountIsTwo_ReturnsExactPolicyFormWarning()
        {
            string warning = RunTestsHotReloadDiscardWarningBuilder.Build(2);

            Assert.That(
                warning,
                Is.EqualTo(
                    "2 active hot-reload change(s) were live during this test run. If script changes were imported during the run, the deferred domain reload that follows it discards active patches - check 'uloop hot-reload --status' and re-apply, or run 'uloop compile' to bake them in."));
        }

        /// <summary>
        /// What: a zero live-change count produces no Warning text.
        /// </summary>
        [Test]
        public void Build_WhenActiveChangeCountIsZero_ReturnsEmpty()
        {
            string warning = RunTestsHotReloadDiscardWarningBuilder.Build(0);

            Assert.That(warning, Is.EqualTo(string.Empty));
        }
    }
}
