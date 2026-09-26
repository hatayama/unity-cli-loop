using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers RecommendedNextAction wording for hot-reload apply responses.
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
                addedCount: 0,
                introducedTypeCount: 0,
                allRequestedSkipped: false);

            Assert.That(
                action,
                Is.EqualTo(
                    "Partially applied. Fix the failed declarations or methods and rerun, run 'uloop compile' to apply every edit, or run 'uloop hot-reload --revert-all' to discard the applied patches."));
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
                addedCount: 1,
                introducedTypeCount: 0,
                allRequestedSkipped: false);

            Assert.That(
                action,
                Is.EqualTo(
                    "Partially applied. Fix the failed declarations or methods and rerun, run 'uloop compile' to apply every edit, or run 'uloop hot-reload --revert-all' to discard the applied patches."));
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
                addedCount: 0,
                introducedTypeCount: 0,
                allRequestedSkipped: false);

            Assert.That(
                action,
                Is.EqualTo("Fix the failed declarations or methods and rerun, or run 'uloop compile'."));
        }

        /// <summary>
        /// What: a Failed run whose only applied change is an active introduced type is still
        /// treated as a partial apply, because that type stays loaded until a Domain Reload.
        /// </summary>
        [Test]
        public void Resolve_WhenFailureWithIntroducedTypesOnly_ReturnsPartialApplyAction()
        {
            string action = HotReloadRecommendedNextAction.Resolve(
                hasFailure: true,
                patchedTotal: 0,
                addedCount: 0,
                introducedTypeCount: 1,
                allRequestedSkipped: false);

            Assert.That(
                action,
                Is.EqualTo(
                    "Partially applied. Fix the failed declarations or methods and rerun, run 'uloop compile' to apply every edit, or run 'uloop hot-reload --revert-all' to discard the applied patches."));
        }

        /// <summary>
        /// What: a successful apply that applied something does not recommend a next action.
        /// </summary>
        [Test]
        public void Resolve_NoFailureAndNotAllSkipped_ReturnsEmpty()
        {
            string action = HotReloadRecommendedNextAction.Resolve(
                hasFailure: false,
                patchedTotal: 1,
                addedCount: 1,
                introducedTypeCount: 0,
                allRequestedSkipped: false);

            Assert.That(action, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// What: a run without a failure whose requested files were all Skipped first points at
        /// the fix each Skipped row's Reason names, and offers a compile only as the alternative,
        /// because a Reason can name a fix that needs no compile.
        /// </summary>
        [Test]
        public void Resolve_NoFailureButEveryRequestedMethodSkipped_RecommendsTheReasonFixBeforeACompile()
        {
            string action = HotReloadRecommendedNextAction.Resolve(
                hasFailure: false,
                patchedTotal: 0,
                addedCount: 1,
                introducedTypeCount: 0,
                allRequestedSkipped: true);

            Assert.That(
                action,
                Is.EqualTo(
                    "Each Skipped row's Methods[].Reason names what to change (a file to pass with --files, an initializer to drop, a shape hot reload can patch; see Warnings); do that and rerun. Run 'uloop compile' to apply the Skipped edits as they are instead."));
        }
    }
}
