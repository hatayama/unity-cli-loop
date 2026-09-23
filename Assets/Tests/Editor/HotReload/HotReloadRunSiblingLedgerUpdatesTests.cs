using System;
using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers where a file stands once a run has written its sibling records, which decides
    /// whether a later reload brings the file back.
    /// </summary>
    public class HotReloadRunSiblingLedgerUpdatesTests
    {
        private const string EnumPath = "Assets/Scripts/Kinds.cs";
        private const string OtherPath = "Assets/Scripts/World.cs";
        private const string CurrentHash = "hash-current";
        private const string OlderHash = "hash-older";

        /// <summary>
        /// What: a file the run was neither given nor brought back reads as not in the run.
        /// </summary>
        [Test]
        public void DescribeAfterApply_FileOutsideTheRun_IsNotInRun()
        {
            using (HotReloadDomain domain = HotReloadCompositionRoot.CreateProductionDomain())
            {
                HotReloadRunSiblingLedgerUpdates updates = new HotReloadRunSiblingLedgerUpdates(domain);
                updates.Observe(OtherPath, AppliedResult(OtherPath));
                updates.ApplyTo(domain);

                Assert.That(
                    updates.DescribeAfterApply(new HotReloadDomainCarriedInLookup(domain), EnumPath),
                    Is.EqualTo(HotReloadCarriedInState.NotInRun));
            }
        }

        /// <summary>
        /// What: a file the run applied a change from reads as applied in this run.
        /// </summary>
        [Test]
        public void DescribeAfterApply_FileWithAppliedChange_IsAppliedInThisRun()
        {
            using (HotReloadDomain domain = HotReloadCompositionRoot.CreateProductionDomain())
            {
                HotReloadRunSiblingLedgerUpdates updates = new HotReloadRunSiblingLedgerUpdates(domain);
                updates.Observe(EnumPath, AppliedResult(EnumPath));
                updates.ApplyTo(domain);

                Assert.That(
                    updates.DescribeAfterApply(new HotReloadDomainCarriedInLookup(domain), EnumPath),
                    Is.EqualTo(HotReloadCarriedInState.AppliedInThisRun));
            }
        }

        /// <summary>
        /// What: a companion whose source changed since it was recorded is dropped from the
        /// ledger, so the next reload does not warn about it again.
        /// </summary>
        [Test]
        public void ApplyTo_ChangedCompanion_IsRemovedFromTheLedger()
        {
            using (HotReloadDomain domain = HotReloadCompositionRoot.CreateProductionDomain())
            {
                domain.CompanionSources.Record(EnumPath, OlderHash);
                HotReloadRunSiblingLedgerUpdates updates = new HotReloadRunSiblingLedgerUpdates(domain);
                updates.NoteChangedCompanion(EnumPath);
                updates.ApplyTo(domain);

                Assert.That(domain.CompanionSources.TryGetHash(EnumPath), Is.Null);
            }
        }

        /// <summary>
        /// What: a file that applied nothing now but holds changes of an earlier reload reads as
        /// active from an earlier run.
        /// </summary>
        [Test]
        public void DescribeAfterApply_FileActiveBeforeTheRun_IsActiveFromEarlierRun()
        {
            using (HotReloadDomain domain = HotReloadCompositionRoot.CreateProductionDomain())
            {
                HotReloadRunSiblingLedgerUpdates updates = new HotReloadRunSiblingLedgerUpdates(domain);
                updates.Observe(EnumPath, UnchangedResult());
                updates.ApplyTo(domain);

                Assert.That(
                    updates.DescribeAfterApply(new StubLookup { ActivePath = EnumPath }, EnumPath),
                    Is.EqualTo(HotReloadCarriedInState.ActiveFromEarlierRun));
            }
        }

        /// <summary>
        /// What: a passed file with no row of its own is recorded as a companion when another
        /// file of the same run applied a change, so it reads as recorded at its current source.
        /// </summary>
        [Test]
        public void DescribeAfterApply_RowlessFileBesideAnAppliedFile_IsRecordedAtCurrentSource()
        {
            using (HotReloadDomain domain = HotReloadCompositionRoot.CreateProductionDomain())
            {
                HotReloadRunSiblingLedgerUpdates updates = new HotReloadRunSiblingLedgerUpdates(domain);
                updates.Observe(EnumPath, UnchangedResult());
                updates.Observe(OtherPath, AppliedResult(OtherPath));
                updates.ApplyTo(domain);

                Assert.That(
                    updates.DescribeAfterApply(new HotReloadDomainCarriedInLookup(domain), EnumPath),
                    Is.EqualTo(HotReloadCarriedInState.RecordedAtCurrentSource));
            }
        }

        /// <summary>
        /// What: a file whose applied-source record holds its current hash reads as recorded at
        /// its current source even when it has rows of its own.
        /// </summary>
        [Test]
        public void DescribeAfterApply_AppliedSourceRecordAtCurrentHash_IsRecordedAtCurrentSource()
        {
            using (HotReloadDomain domain = HotReloadCompositionRoot.CreateProductionDomain())
            {
                domain.RecordAppliedSource(EnumPath, CurrentHash, false);
                HotReloadRunSiblingLedgerUpdates updates = new HotReloadRunSiblingLedgerUpdates(domain);
                updates.Observe(EnumPath, SkippedResult(EnumPath));
                updates.ApplyTo(domain);

                Assert.That(
                    updates.DescribeAfterApply(new HotReloadDomainCarriedInLookup(domain), EnumPath),
                    Is.EqualTo(HotReloadCarriedInState.RecordedAtCurrentSource));
            }
        }

        /// <summary>
        /// What: a run that applied nothing records no companion, so a rowless passed file reads
        /// as not recorded.
        /// </summary>
        [Test]
        public void DescribeAfterApply_RunThatAppliedNothing_IsNotRecorded()
        {
            using (HotReloadDomain domain = HotReloadCompositionRoot.CreateProductionDomain())
            {
                HotReloadRunSiblingLedgerUpdates updates = new HotReloadRunSiblingLedgerUpdates(domain);
                updates.Observe(EnumPath, UnchangedResult());
                updates.Observe(OtherPath, SkippedResult(OtherPath));
                updates.ApplyTo(domain);

                Assert.That(
                    updates.DescribeAfterApply(new HotReloadDomainCarriedInLookup(domain), EnumPath),
                    Is.EqualTo(HotReloadCarriedInState.NotRecorded));
            }
        }

        /// <summary>
        /// What: a companion record of older bytes survives a run that applied nothing, and the
        /// file still reads as not recorded, because only a return to those bytes brings it back.
        /// </summary>
        [Test]
        public void DescribeAfterApply_CompanionRecordOfOlderBytes_IsNotRecorded()
        {
            using (HotReloadDomain domain = HotReloadCompositionRoot.CreateProductionDomain())
            {
                domain.CompanionSources.Record(EnumPath, OlderHash);
                HotReloadRunSiblingLedgerUpdates updates = new HotReloadRunSiblingLedgerUpdates(domain);
                updates.Observe(EnumPath, UnchangedResult());
                updates.Observe(OtherPath, SkippedResult(OtherPath));
                updates.ApplyTo(domain);

                Assert.That(domain.CompanionSources.TryGetHash(EnumPath), Is.EqualTo(OlderHash));
                Assert.That(
                    updates.DescribeAfterApply(new HotReloadDomainCarriedInLookup(domain), EnumPath),
                    Is.EqualTo(HotReloadCarriedInState.NotRecorded));
            }
        }

        /// <summary>
        /// What: reading the state before the run's records are written is refused.
        /// </summary>
        [Test]
        public void DescribeAfterApply_BeforeApplyTo_Throws()
        {
            using (HotReloadDomain domain = HotReloadCompositionRoot.CreateProductionDomain())
            {
                HotReloadRunSiblingLedgerUpdates updates = new HotReloadRunSiblingLedgerUpdates(domain);
                updates.Observe(EnumPath, UnchangedResult());

                Assert.Throws<InvalidOperationException>(
                    () => updates.DescribeAfterApply(new HotReloadDomainCarriedInLookup(domain), EnumPath));
            }
        }

        private static HotReloadFileProcessResult UnchangedResult()
        {
            return new HotReloadFileProcessResult(
                new List<HotReloadMethodOutcome>(),
                new List<string>(),
                0,
                sourceContentSha256: CurrentHash);
        }

        private static HotReloadFileProcessResult AppliedResult(string path)
        {
            return new HotReloadFileProcessResult(
                new List<HotReloadMethodOutcome> { HotReloadMethodOutcome.Patched("World.Tick()", path) },
                new List<string>(),
                1,
                sourceContentSha256: "hash-applied");
        }

        private static HotReloadFileProcessResult SkippedResult(string path)
        {
            return new HotReloadFileProcessResult(
                new List<HotReloadMethodOutcome> { HotReloadMethodOutcome.Skipped("World.Step()", "skipped", path) },
                new List<string>(),
                0,
                sourceContentSha256: CurrentHash);
        }

        private sealed class StubLookup : IHotReloadCarriedInLookup
        {
            public string ActivePath { get; set; }

            public string TryGetCompanionHash(string projectRelativePath)
            {
                return null;
            }

            public string TryGetAppliedHash(string projectRelativePath)
            {
                return null;
            }

            public bool IsActive(string projectRelativePath)
            {
                return string.Equals(projectRelativePath, ActivePath, StringComparison.Ordinal);
            }
        }
    }
}
