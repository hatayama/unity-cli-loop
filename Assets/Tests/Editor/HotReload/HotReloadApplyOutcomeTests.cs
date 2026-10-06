using System;
using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers the one-word answer to "are the requested edits live now?" for an apply run, judged
    /// on the requested files with the rows of re-applied sibling files left out.
    /// </summary>
    public sealed class HotReloadApplyOutcomeTests
    {
        private const string RequestedPath = "Assets/Requested.cs";
        private const string SiblingPath = "Assets/Sibling.cs";

        /// <summary>
        /// What: a run with a Failed row answers Failed even though another requested method was
        /// patched, the same condition that turns Success false.
        /// </summary>
        [Test]
        public void Decide_HasFailure_ReturnsFailed()
        {
            HotReloadApplyOutcomeKind outcome = HotReloadApplyOutcome.Decide(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Patched("Requested.M", RequestedPath),
                    HotReloadMethodOutcome.Failed("Requested.N", "reason", RequestedPath)
                },
                Array.Empty<HotReloadIntroducedTypeOutcome>(),
                NoSiblings(),
                hasFailure: true);

            Assert.That(outcome, Is.EqualTo(HotReloadApplyOutcomeKind.Failed));
        }

        /// <summary>
        /// What: a requested method that is Patched, Added, or AlreadyActive each counts as live,
        /// so a run of only that row answers Applied.
        /// </summary>
        [Test]
        public void Decide_RequestedRowPatchedAddedOrAlreadyActive_ReturnsApplied()
        {
            Assert.That(
                DecideMethods(NoSiblings(), HotReloadMethodOutcome.Patched("Requested.M", RequestedPath)),
                Is.EqualTo(HotReloadApplyOutcomeKind.Applied),
                "Patched");
            Assert.That(
                DecideMethods(NoSiblings(), HotReloadMethodOutcome.Added("Requested.M", RequestedPath)),
                Is.EqualTo(HotReloadApplyOutcomeKind.Applied),
                "Added");
            Assert.That(
                DecideMethods(NoSiblings(), HotReloadMethodOutcome.AlreadyActive("Requested.M", RequestedPath)),
                Is.EqualTo(HotReloadApplyOutcomeKind.Applied),
                "AlreadyActive");
        }

        /// <summary>
        /// What: a Skipped row of a re-applied sibling does not count against the requested file,
        /// so a requested Patched row still answers Applied.
        /// </summary>
        [Test]
        public void Decide_RequestedPatchedAndSiblingSkipped_ReturnsApplied()
        {
            HotReloadApplyOutcomeKind outcome = DecideMethods(
                WithSibling(),
                HotReloadMethodOutcome.Patched("Requested.M", RequestedPath),
                HotReloadMethodOutcome.Skipped("Sibling.N", "reason", SiblingPath));

            Assert.That(outcome, Is.EqualTo(HotReloadApplyOutcomeKind.Applied));
        }

        /// <summary>
        /// What: requested methods of which some are live and some Skipped answer PartiallyApplied.
        /// </summary>
        [Test]
        public void Decide_RequestedPatchedAndSkipped_ReturnsPartiallyApplied()
        {
            HotReloadApplyOutcomeKind outcome = DecideMethods(
                NoSiblings(),
                HotReloadMethodOutcome.Patched("Requested.M", RequestedPath),
                HotReloadMethodOutcome.Skipped("Requested.N", "reason", RequestedPath));

            Assert.That(outcome, Is.EqualTo(HotReloadApplyOutcomeKind.PartiallyApplied));
        }

        /// <summary>
        /// What: requested methods that were all Skipped answer NothingApplied.
        /// </summary>
        [Test]
        public void Decide_RequestedAllSkipped_ReturnsNothingApplied()
        {
            HotReloadApplyOutcomeKind outcome = DecideMethods(
                NoSiblings(),
                HotReloadMethodOutcome.Skipped("Requested.M", "reason", RequestedPath),
                HotReloadMethodOutcome.Skipped("Requested.N", "reason", RequestedPath));

            Assert.That(outcome, Is.EqualTo(HotReloadApplyOutcomeKind.NothingApplied));
        }

        /// <summary>
        /// What: an Added row of a re-applied sibling does not make the requested file live, so a
        /// requested file whose only row was Skipped answers NothingApplied, not PartiallyApplied.
        /// </summary>
        [Test]
        public void Decide_RequestedSkippedAndSiblingAdded_ReturnsNothingApplied()
        {
            HotReloadApplyOutcomeKind outcome = DecideMethods(
                WithSibling(),
                HotReloadMethodOutcome.Skipped("Requested.M", "reason", RequestedPath),
                HotReloadMethodOutcome.Added("Sibling.N", SiblingPath));

            Assert.That(outcome, Is.EqualTo(HotReloadApplyOutcomeKind.NothingApplied));
        }

        /// <summary>
        /// What: a run with no rows at all, as when every method is unchanged or the files hold
        /// no method bodies, answers NothingToApply.
        /// </summary>
        [Test]
        public void Decide_NoRows_ReturnsNothingToApply()
        {
            HotReloadApplyOutcomeKind outcome = DecideMethods(NoSiblings());

            Assert.That(outcome, Is.EqualTo(HotReloadApplyOutcomeKind.NothingToApply));
        }

        /// <summary>
        /// What: Stale rows are neither live nor Skipped, so a run of only Stale rows answers
        /// NothingToApply.
        /// </summary>
        [Test]
        public void Decide_OnlyStaleRows_ReturnsNothingToApply()
        {
            HotReloadApplyOutcomeKind outcome = DecideMethods(
                NoSiblings(),
                HotReloadMethodOutcome.Stale("Requested.M", RequestedPath));

            Assert.That(outcome, Is.EqualTo(HotReloadApplyOutcomeKind.NothingToApply));
        }

        /// <summary>
        /// What: a Skipped row without a FilePath, such as a file-level row, counts as a requested
        /// row even when the run re-applied a sibling, so it answers NothingApplied.
        /// </summary>
        [Test]
        public void Decide_SkippedRowWithoutFilePath_ReturnsNothingApplied()
        {
            HotReloadApplyOutcomeKind outcome = DecideMethods(
                WithSibling(),
                HotReloadMethodOutcome.Skipped("Requested.M", "reason", string.Empty));

            Assert.That(outcome, Is.EqualTo(HotReloadApplyOutcomeKind.NothingApplied));
        }

        /// <summary>
        /// What: a requested type row that is Introduced or AlreadyActive each counts as live, so a
        /// run of only that row and no method rows answers Applied.
        /// </summary>
        [Test]
        public void Decide_OnlyRequestedIntroducedTypes_ReturnsApplied()
        {
            Assert.That(
                DecideTypes(
                    NoSiblings(),
                    HotReloadIntroducedTypeOutcome.Introduced("Example.NewType", "Assembly-CSharp", RequestedPath)),
                Is.EqualTo(HotReloadApplyOutcomeKind.Applied),
                "Introduced");
            Assert.That(
                DecideTypes(
                    NoSiblings(),
                    HotReloadIntroducedTypeOutcome.AlreadyActive(
                        "Example.OtherType",
                        "Assembly-CSharp",
                        RequestedPath,
                        bodyEdited: false)),
                Is.EqualTo(HotReloadApplyOutcomeKind.Applied),
                "AlreadyActive");
        }

        /// <summary>
        /// What: an Introduced type row whose owner is a re-applied sibling does not make the
        /// requested files live, so a run of only that row answers NothingToApply.
        /// </summary>
        [Test]
        public void Decide_OnlySiblingOwnedIntroducedType_ReturnsNothingToApply()
        {
            HotReloadApplyOutcomeKind outcome = DecideTypes(
                WithSibling(),
                HotReloadIntroducedTypeOutcome.Introduced("Example.NewType", "Assembly-CSharp", SiblingPath));

            Assert.That(outcome, Is.EqualTo(HotReloadApplyOutcomeKind.NothingToApply));
        }

        private static HotReloadApplyOutcomeKind DecideMethods(
            HotReloadReappliedSiblingFiles siblingFiles,
            params HotReloadMethodOutcome[] methods)
        {
            return HotReloadApplyOutcome.Decide(
                methods,
                Array.Empty<HotReloadIntroducedTypeOutcome>(),
                siblingFiles,
                hasFailure: false);
        }

        private static HotReloadApplyOutcomeKind DecideTypes(
            HotReloadReappliedSiblingFiles siblingFiles,
            params HotReloadIntroducedTypeOutcome[] introducedTypes)
        {
            return HotReloadApplyOutcome.Decide(
                Array.Empty<HotReloadMethodOutcome>(),
                introducedTypes,
                siblingFiles,
                hasFailure: false);
        }

        private static HotReloadReappliedSiblingFiles NoSiblings()
        {
            return new HotReloadReappliedSiblingFiles(Array.Empty<string>(), path => path);
        }

        private static HotReloadReappliedSiblingFiles WithSibling()
        {
            return new HotReloadReappliedSiblingFiles(new[] { SiblingPath }, path => path);
        }
    }
}
