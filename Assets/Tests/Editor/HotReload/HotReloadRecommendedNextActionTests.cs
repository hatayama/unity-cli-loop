using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers RecommendedNextAction wording for failed hot-reload apply responses.
    /// </summary>
    public sealed class HotReloadRecommendedNextActionTests
    {
        /// <summary>
        /// What: a Failed run that still patched methods recommends fix-and-rerun, compile, or
        /// revert-all.
        /// </summary>
        [Test]
        public void Resolve_WhenFailureWithPatchedMethods_ReturnsPartialApplyAction()
        {
            string action = HotReloadRecommendedNextAction.Resolve(
                hasFailure: true,
                patchedTotal: 1,
                addedCount: 0);

            Assert.That(
                action,
                Is.EqualTo(
                    "Partially applied. Fix the failed methods and rerun, run 'uloop compile' to apply every edit, or run 'uloop hot-reload --revert-all' to discard the applied patches."));
        }

        /// <summary>
        /// What: a Failed run that applied only added members is still treated as a partial apply.
        /// </summary>
        [Test]
        public void Resolve_WhenFailureWithAddedMembers_ReturnsPartialApplyAction()
        {
            string action = HotReloadRecommendedNextAction.Resolve(
                hasFailure: true,
                patchedTotal: 0,
                addedCount: 1);

            Assert.That(
                action,
                Is.EqualTo(
                    "Partially applied. Fix the failed methods and rerun, run 'uloop compile' to apply every edit, or run 'uloop hot-reload --revert-all' to discard the applied patches."));
        }

        /// <summary>
        /// What: a Failed run that applied nothing recommends fix-and-rerun or compile.
        /// </summary>
        [Test]
        public void Resolve_WhenFailureWithNothingApplied_ReturnsFixOrCompileAction()
        {
            string action = HotReloadRecommendedNextAction.Resolve(
                hasFailure: true,
                patchedTotal: 0,
                addedCount: 0);

            Assert.That(
                action,
                Is.EqualTo("Fix the failed methods and rerun, or run 'uloop compile'."));
        }

        /// <summary>
        /// What: a successful apply does not recommend a next action.
        /// </summary>
        [Test]
        public void Resolve_WhenNoFailure_ReturnsEmpty()
        {
            string action = HotReloadRecommendedNextAction.Resolve(
                hasFailure: false,
                patchedTotal: 1,
                addedCount: 1);

            Assert.That(action, Is.EqualTo(string.Empty));
        }
    }
}
