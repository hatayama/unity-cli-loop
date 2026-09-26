using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Tests which applied-source record one processed file leaves behind, for every outcome kind
    /// and the combinations whose precedence matters.
    /// </summary>
    public sealed class HotReloadAppliedSourceRecordDecisionTests
    {
        private const string Hash = "hash-of-compiled-bytes";
        private const string FilePath = "Assets/A.cs";

        /// <summary>
        /// What: a result with no worker hash keeps the earlier record, because the worker never
        /// read the file and touched no patch.
        /// </summary>
        [Test]
        public void Decide_EmptyHash_Keeps()
        {
            HotReloadAppliedSourceRecordDecision decision = HotReloadAppliedSourceRecordDecision.Decide(
                string.Empty,
                new[] { HotReloadMethodOutcome.Failed("(file)", "unreadable", FilePath) },
                appliedAddedFieldsOrConsts: false);

            AssertDecision(decision, HotReloadAppliedSourceRecordKind.Keep, null);
        }

        /// <summary>
        /// What: a file with no rows and no added fields or consts forgets its record, because it
        /// converged to compiled IL and no hash describes a live change.
        /// </summary>
        [Test]
        public void Decide_NoRowsAndNoAddedFields_Forgets()
        {
            HotReloadAppliedSourceRecordDecision decision = HotReloadAppliedSourceRecordDecision.Decide(
                Hash,
                new List<HotReloadMethodOutcome>(),
                appliedAddedFieldsOrConsts: false);

            AssertDecision(decision, HotReloadAppliedSourceRecordKind.Forget, null);
        }

        /// <summary>
        /// What: a file with no rows but applied added fields or consts is recorded as fully
        /// applied, so a later sibling reload can tell its bytes are the ones those members came from.
        /// </summary>
        [Test]
        public void Decide_NoRowsWithAddedFields_RecordsFullyApplied()
        {
            HotReloadAppliedSourceRecordDecision decision = HotReloadAppliedSourceRecordDecision.Decide(
                Hash,
                new List<HotReloadMethodOutcome>(),
                appliedAddedFieldsOrConsts: true);

            AssertDecision(decision, HotReloadAppliedSourceRecordKind.FullyApplied, Hash);
        }

        /// <summary>
        /// What: Patched and Added rows alone record the hash as fully applied.
        /// </summary>
        [Test]
        public void Decide_PatchedAndAdded_RecordsFullyApplied()
        {
            HotReloadAppliedSourceRecordDecision decision = HotReloadAppliedSourceRecordDecision.Decide(
                Hash,
                new[]
                {
                    HotReloadMethodOutcome.Patched("Type.Patched()", FilePath),
                    HotReloadMethodOutcome.Added("Type.Added()", FilePath)
                },
                appliedAddedFieldsOrConsts: false);

            AssertDecision(decision, HotReloadAppliedSourceRecordKind.FullyApplied, Hash);
        }

        /// <summary>
        /// What: a Skipped row next to a Patched row records the hash as partially applied.
        /// </summary>
        [Test]
        public void Decide_SkippedNextToPatched_RecordsPartiallyApplied()
        {
            HotReloadAppliedSourceRecordDecision decision = HotReloadAppliedSourceRecordDecision.Decide(
                Hash,
                new[]
                {
                    HotReloadMethodOutcome.Patched("Type.Patched()", FilePath),
                    HotReloadMethodOutcome.Skipped("Type.Skipped()", "unsupported", FilePath)
                },
                appliedAddedFieldsOrConsts: false);

            AssertDecision(decision, HotReloadAppliedSourceRecordKind.PartiallyApplied, Hash);
        }

        /// <summary>
        /// What: a Failed row records the hash as partially applied even when a Stale row is next
        /// to it, because the file then holds an edit that is not loaded.
        /// </summary>
        [Test]
        public void Decide_FailedNextToStale_RecordsPartiallyApplied()
        {
            HotReloadAppliedSourceRecordDecision decision = HotReloadAppliedSourceRecordDecision.Decide(
                Hash,
                new[]
                {
                    HotReloadMethodOutcome.Stale("Type.Removed()", FilePath),
                    HotReloadMethodOutcome.Failed("Type.Failed()", "shim compile failed", FilePath)
                },
                appliedAddedFieldsOrConsts: false);

            AssertDecision(decision, HotReloadAppliedSourceRecordKind.PartiallyApplied, Hash);
        }

        /// <summary>
        /// What: AlreadyActive rows keep the record, because they come only from the unchanged-source
        /// short-circuit that matched that record.
        /// </summary>
        [Test]
        public void Decide_AlreadyActive_Keeps()
        {
            HotReloadAppliedSourceRecordDecision decision = HotReloadAppliedSourceRecordDecision.Decide(
                Hash,
                new[] { HotReloadMethodOutcome.AlreadyActive("Type.Patched()", FilePath) },
                appliedAddedFieldsOrConsts: false);

            AssertDecision(decision, HotReloadAppliedSourceRecordKind.Keep, null);
        }

        /// <summary>
        /// What: a Stale row alone records the hash as fully applied, because the row only keeps
        /// the patch of a method the current bytes no longer declare.
        /// </summary>
        [Test]
        public void Decide_StaleOnly_RecordsFullyApplied()
        {
            HotReloadAppliedSourceRecordDecision decision = HotReloadAppliedSourceRecordDecision.Decide(
                Hash,
                new[] { HotReloadMethodOutcome.Stale("Type.Removed()", FilePath) },
                appliedAddedFieldsOrConsts: false);

            AssertDecision(decision, HotReloadAppliedSourceRecordKind.FullyApplied, Hash);
        }

        /// <summary>
        /// What: a Stale row next to Patched and Added rows records the hash as fully applied, so
        /// a later reload can bring the file back for the members it added.
        /// </summary>
        [Test]
        public void Decide_StaleNextToPatchedAndAdded_RecordsFullyApplied()
        {
            HotReloadAppliedSourceRecordDecision decision = HotReloadAppliedSourceRecordDecision.Decide(
                Hash,
                new[]
                {
                    HotReloadMethodOutcome.Stale("Type.Removed()", FilePath),
                    HotReloadMethodOutcome.Patched("Type.Patched()", FilePath),
                    HotReloadMethodOutcome.Added("Type.Added()", FilePath)
                },
                appliedAddedFieldsOrConsts: false);

            AssertDecision(decision, HotReloadAppliedSourceRecordKind.FullyApplied, Hash);
        }

        private static void AssertDecision(
            HotReloadAppliedSourceRecordDecision decision,
            HotReloadAppliedSourceRecordKind expectedKind,
            string expectedHash)
        {
            Assert.That(decision.Kind, Is.EqualTo(expectedKind));
            Assert.That(decision.Hash, Is.EqualTo(expectedHash));
        }
    }
}
