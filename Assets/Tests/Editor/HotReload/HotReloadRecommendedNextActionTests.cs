using System;
using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers RecommendedNextAction wording for hot-reload apply responses.
    /// </summary>
    public sealed class HotReloadRecommendedNextActionTests
    {
        private const int NothingApplied = 0;
        private const int OnePatched = 1;

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
                allRequestedSkipped: false,
                failureKinds: HotReloadFailureKinds.Declaration);

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
                allRequestedSkipped: false,
                failureKinds: HotReloadFailureKinds.Declaration);

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
                allRequestedSkipped: false,
                failureKinds: HotReloadFailureKinds.Declaration);

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
                allRequestedSkipped: false,
                failureKinds: HotReloadFailureKinds.Declaration);

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
                allRequestedSkipped: false,
                failureKinds: HotReloadFailureKinds.None);

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
                allRequestedSkipped: true,
                failureKinds: HotReloadFailureKinds.None);

            Assert.That(
                action,
                Is.EqualTo(
                    "Each Skipped row's Methods[].Reason names what to change (a file to pass with --files, an initializer to drop, a shape hot reload can patch; see Warnings); do that and rerun. Run 'uloop compile' to apply the Skipped edits as they are instead."));
        }

        /// <summary>
        /// What: a run that applied nothing and failed only because the Editor compiled or imported
        /// during the reload says there is nothing to fix, and to wait and rerun.
        /// </summary>
        [Test]
        public void Resolve_WhenOnlyEditorNotReadyFailed_RecommendsWaitingForTheEditor()
        {
            string action = ResolveFailure(HotReloadFailureKinds.EditorNotReady, NothingApplied);

            Assert.That(action, Is.EqualTo(HotReloadConstants.EditorNotReadyRecommendedNextAction));
        }

        /// <summary>
        /// What: a run that patched part of the request before the Editor became busy says the rest
        /// only needs a rerun, and still offers to revert what was applied.
        /// </summary>
        [Test]
        public void Resolve_WhenEditorNotReadyFailedAfterAPartialApply_RecommendsWaitingOrRevert()
        {
            string action = ResolveFailure(HotReloadFailureKinds.EditorNotReady, OnePatched);

            Assert.That(
                action,
                Is.EqualTo(HotReloadConstants.EditorNotReadyAfterPartialApplyRecommendedNextAction));
        }

        /// <summary>
        /// What: a Virtual Player that has no compiled assembly is told to compile the main
        /// Editor's project, the only compile that reaches it.
        /// </summary>
        [Test]
        public void Resolve_WhenOnlyVirtualPlayerFailed_RecommendsCompilingTheMainProject()
        {
            string action = ResolveFailure(
                HotReloadFailureKinds.CompiledAssemblyMissing | HotReloadFailureKinds.VirtualPlayer,
                NothingApplied);

            Assert.That(action, Is.EqualTo(HotReloadConstants.VirtualPlayerRecommendedNextAction));
        }

        /// <summary>
        /// What: a missing compiled assembly in an ordinary project is told to compile, with
        /// nothing to fix in the source.
        /// </summary>
        [Test]
        public void Resolve_WhenOnlyCompiledAssemblyMissing_RecommendsACompile()
        {
            string action = ResolveFailure(HotReloadFailureKinds.CompiledAssemblyMissing, NothingApplied);

            Assert.That(action, Is.EqualTo(HotReloadConstants.CompiledAssemblyMissingRecommendedNextAction));
        }

        /// <summary>
        /// What: a run whose failures all need a fix keeps the advice it had before the failure
        /// kinds existed, with nothing appended, whether or not it applied anything.
        /// </summary>
        [Test]
        public void Resolve_WhenDeclarationFailedOnly_IsUnchanged()
        {
            string nothingApplied = ResolveFailure(HotReloadFailureKinds.Declaration, NothingApplied);
            string partiallyApplied = ResolveFailure(HotReloadFailureKinds.Declaration, OnePatched);

            Assert.That(nothingApplied, Is.EqualTo(HotReloadConstants.FailedWithNoApplyRecommendedNextAction));
            Assert.That(partiallyApplied, Is.EqualTo(HotReloadConstants.PartialApplyRecommendedNextAction));
        }

        /// <summary>
        /// What: a run with more than one kind of failure leads with the advice for the failure the
        /// reader has to fix, or for the busy Editor when nothing needs a fix, and adds a sentence
        /// for each other kind.
        /// </summary>
        [TestCaseSource(nameof(ComposedCases))]
        public void Resolve_ComposesTheAdviceFromTheFailureKinds(string kindsName, int patchedTotal, string expected)
        {
            HotReloadFailureKinds kinds = (HotReloadFailureKinds)Enum.Parse(typeof(HotReloadFailureKinds), kindsName);

            string action = ResolveFailure(kinds, patchedTotal);

            Assert.That(action, Is.EqualTo(expected));
        }

        private static IEnumerable<TestCaseData> ComposedCases()
        {
            HotReloadFailureKinds declaration = HotReloadFailureKinds.Declaration;
            HotReloadFailureKinds editorNotReady = HotReloadFailureKinds.EditorNotReady;
            HotReloadFailureKinds missing = HotReloadFailureKinds.CompiledAssemblyMissing;
            HotReloadFailureKinds virtualPlayer =
                HotReloadFailureKinds.CompiledAssemblyMissing | HotReloadFailureKinds.VirtualPlayer;

            yield return ComposedCase(
                missing,
                OnePatched,
                HotReloadConstants.PartialApplyRecommendedNextAction,
                HotReloadConstants.CompiledAssemblyMissingRecommendedNextAction);
            yield return ComposedCase(
                virtualPlayer,
                OnePatched,
                HotReloadConstants.PartialApplyRecommendedNextAction,
                HotReloadConstants.VirtualPlayerRecommendedNextAction);
            yield return ComposedCase(
                declaration | editorNotReady,
                NothingApplied,
                HotReloadConstants.FailedWithNoApplyRecommendedNextAction,
                HotReloadConstants.EditorNotReadyAppendedRecommendedNextAction);
            yield return ComposedCase(
                declaration | editorNotReady,
                OnePatched,
                HotReloadConstants.PartialApplyRecommendedNextAction,
                HotReloadConstants.EditorNotReadyAppendedRecommendedNextAction);
            yield return ComposedCase(
                declaration | missing,
                NothingApplied,
                HotReloadConstants.FailedWithNoApplyRecommendedNextAction,
                HotReloadConstants.CompiledAssemblyMissingRecommendedNextAction);
            yield return ComposedCase(
                declaration | missing,
                OnePatched,
                HotReloadConstants.PartialApplyRecommendedNextAction,
                HotReloadConstants.CompiledAssemblyMissingRecommendedNextAction);
            yield return ComposedCase(
                declaration | virtualPlayer,
                NothingApplied,
                HotReloadConstants.FailedWithNoApplyRecommendedNextAction,
                HotReloadConstants.VirtualPlayerRecommendedNextAction);
            yield return ComposedCase(
                declaration | virtualPlayer,
                OnePatched,
                HotReloadConstants.PartialApplyRecommendedNextAction,
                HotReloadConstants.VirtualPlayerRecommendedNextAction);
            yield return ComposedCase(
                editorNotReady | missing,
                NothingApplied,
                HotReloadConstants.EditorNotReadyRecommendedNextAction,
                HotReloadConstants.CompiledAssemblyMissingRecommendedNextAction);
            yield return ComposedCase(
                editorNotReady | virtualPlayer,
                NothingApplied,
                HotReloadConstants.EditorNotReadyRecommendedNextAction,
                HotReloadConstants.VirtualPlayerRecommendedNextAction);
            yield return ComposedCase(
                editorNotReady | virtualPlayer,
                OnePatched,
                HotReloadConstants.EditorNotReadyAfterPartialApplyRecommendedNextAction,
                HotReloadConstants.VirtualPlayerRecommendedNextAction);
            yield return ComposedCase(
                declaration | editorNotReady | virtualPlayer,
                NothingApplied,
                HotReloadConstants.FailedWithNoApplyRecommendedNextAction,
                HotReloadConstants.EditorNotReadyAppendedRecommendedNextAction,
                HotReloadConstants.VirtualPlayerRecommendedNextAction);
        }

        // Why the kinds travel as their name: the test method is public, and a public method cannot
        // take the internal enum as a parameter.
        private static TestCaseData ComposedCase(
            HotReloadFailureKinds kinds,
            int patchedTotal,
            params string[] expectedSentences)
        {
            string applied = patchedTotal > 0 ? "AfterAPartialApply" : "WithNothingApplied";
            return new TestCaseData(kinds.ToString(), patchedTotal, string.Join(" ", expectedSentences))
                .SetName("Resolve_Composes_" + kinds.ToString().Replace(", ", "And") + "_" + applied);
        }

        private static string ResolveFailure(HotReloadFailureKinds kinds, int patchedTotal)
        {
            return HotReloadRecommendedNextAction.Resolve(
                hasFailure: true,
                patchedTotal: patchedTotal,
                addedCount: 0,
                introducedTypeCount: 0,
                allRequestedSkipped: false,
                failureKinds: kinds);
        }
    }
}
