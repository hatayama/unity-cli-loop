using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers which warning a sibling pulled in to re-bind its active patches gets, from the rows
    /// the reload wrote for it.
    /// </summary>
    public sealed class HotReloadSiblingRebindWarningSelectorTests
    {
        private const string SiblingPath = "Assets/Sibling.cs";

        /// <summary>
        /// What: a sibling with a Patched row and no Failed row was re-applied, so no warning
        /// format is selected for it.
        /// </summary>
        [Test]
        public void SelectUnappliedWarningFormat_PatchedWithoutFailure_ReturnsNull()
        {
            string format = HotReloadSiblingRebindWarningSelector.SelectUnappliedWarningFormat(
                CreateResult(
                    HotReloadMethodOutcome.Patched("Sibling.Earlier", SiblingPath),
                    HotReloadMethodOutcome.Skipped("Sibling.Skip", "reason", SiblingPath)));

            Assert.That(format, Is.Null);
        }

        /// <summary>
        /// What: a sibling with a Failed row gets the failed-rebind warning even when another of
        /// its rows was Patched.
        /// </summary>
        [Test]
        public void SelectUnappliedWarningFormat_FailedRow_ReturnsTheFailedWarning()
        {
            string format = HotReloadSiblingRebindWarningSelector.SelectUnappliedWarningFormat(
                CreateResult(
                    HotReloadMethodOutcome.Patched("Sibling.Earlier", SiblingPath),
                    HotReloadMethodOutcome.Failed("Sibling.Broken", "reason", SiblingPath)));

            Assert.That(format, Is.EqualTo(HotReloadConstants.ActiveSiblingRebindFailedWarningFormat));
        }

        /// <summary>
        /// What: a sibling whose every row was Skipped did not fail, so it gets the Skipped-only
        /// warning instead of being told the reload failed for it.
        /// </summary>
        [Test]
        public void SelectUnappliedWarningFormat_EveryRowSkipped_ReturnsTheSkippedOnlyWarning()
        {
            string format = HotReloadSiblingRebindWarningSelector.SelectUnappliedWarningFormat(
                CreateResult(
                    HotReloadMethodOutcome.Skipped("Sibling.First", "reason", SiblingPath),
                    HotReloadMethodOutcome.Skipped("Sibling.Second", "reason", SiblingPath)));

            Assert.That(
                format,
                Is.EqualTo(HotReloadConstants.ActiveSiblingRebindSkippedOnlyWarningFormat));
        }

        /// <summary>
        /// What: a sibling the reload wrote no row for gets the stopped-before-re-applying
        /// warning, which does not point at rows that were never written.
        /// </summary>
        [Test]
        public void SelectUnappliedWarningFormat_NoRows_ReturnsTheStoppedWarning()
        {
            string format = HotReloadSiblingRebindWarningSelector.SelectUnappliedWarningFormat(CreateResult());

            Assert.That(format, Is.EqualTo(HotReloadConstants.ActiveSiblingRebindSkippedWarningFormat));
        }

        /// <summary>
        /// What: a sibling whose only rows are the file-level Failed rows of a refused reload, with
        /// nothing reverted, gets the run-refused warning because none of its patches changed.
        /// </summary>
        [Test]
        public void SelectUnappliedWarningFormat_OnlyFileLevelFailedRowsAndNothingReverted_ReturnsTheRunRefusedWarning()
        {
            string format = HotReloadSiblingRebindWarningSelector.SelectUnappliedWarningFormat(
                CreateResultWithReverts(
                    0,
                    HotReloadMethodOutcome.Failed("(file)", "reason", SiblingPath),
                    HotReloadMethodOutcome.Failed("(file)", "second reason", SiblingPath)));

            Assert.That(format, Is.EqualTo(HotReloadConstants.ActiveSiblingRebindRunRefusedWarningFormat));
        }

        /// <summary>
        /// What: file-level Failed rows alone do not prove the patches are unchanged once unchanged
        /// patches were reverted, so that sibling keeps the failed-rebind warning.
        /// </summary>
        [Test]
        public void SelectUnappliedWarningFormat_OnlyFileLevelFailedRowsAfterARevert_ReturnsTheFailedWarning()
        {
            string format = HotReloadSiblingRebindWarningSelector.SelectUnappliedWarningFormat(
                CreateResultWithReverts(1, HotReloadMethodOutcome.Failed("(file)", "reason", SiblingPath)));

            Assert.That(format, Is.EqualTo(HotReloadConstants.ActiveSiblingRebindFailedWarningFormat));
        }

        /// <summary>
        /// What: a file-level Failed row next to a method-level Failed row keeps the failed-rebind
        /// warning, because a method of the sibling failed on its own.
        /// </summary>
        [Test]
        public void SelectUnappliedWarningFormat_FileLevelAndMethodFailedRows_ReturnsTheFailedWarning()
        {
            string format = HotReloadSiblingRebindWarningSelector.SelectUnappliedWarningFormat(
                CreateResultWithReverts(
                    0,
                    HotReloadMethodOutcome.Failed("(file)", "reason", SiblingPath),
                    HotReloadMethodOutcome.Failed("Sibling.Broken", "reason", SiblingPath)));

            Assert.That(format, Is.EqualTo(HotReloadConstants.ActiveSiblingRebindFailedWarningFormat));
        }

        /// <summary>
        /// What: a file-level Failed row next to a Skipped row is not a refused run alone, so the
        /// sibling keeps the failed-rebind warning.
        /// </summary>
        [Test]
        public void SelectUnappliedWarningFormat_FileLevelFailedAndSkippedRows_ReturnsTheFailedWarning()
        {
            string format = HotReloadSiblingRebindWarningSelector.SelectUnappliedWarningFormat(
                CreateResultWithReverts(
                    0,
                    HotReloadMethodOutcome.Failed("(file)", "reason", SiblingPath),
                    HotReloadMethodOutcome.Skipped("Sibling.Skip", "reason", SiblingPath)));

            Assert.That(format, Is.EqualTo(HotReloadConstants.ActiveSiblingRebindFailedWarningFormat));
        }

        /// <summary>
        /// What: a sibling that wrote no row but applied an added field was re-applied, so no
        /// warning format is selected and the reload counts it as a change.
        /// </summary>
        [Test]
        public void SelectUnappliedWarningFormat_NoRowsButAnAddedField_ReturnsNullAndCountsAsApplied()
        {
            HotReloadFileProcessResult result = new HotReloadFileProcessResult(
                new List<HotReloadMethodOutcome>(),
                new List<string>(),
                patchedCount: 0,
                addedFieldNames: new[] { "Sibling.AddedTable" });

            Assert.That(HotReloadSiblingRebindWarningSelector.SelectUnappliedWarningFormat(result), Is.Null);
            Assert.That(HotReloadSiblingRebindWarningSelector.AppliedAnyChange(result), Is.True);
        }

        /// <summary>
        /// What: a sibling that wrote no row but applied an added const was re-applied, so no
        /// warning format is selected and the reload counts it as a change.
        /// </summary>
        [Test]
        public void SelectUnappliedWarningFormat_NoRowsButAnAddedConst_ReturnsNullAndCountsAsApplied()
        {
            HotReloadFileProcessResult result = new HotReloadFileProcessResult(
                new List<HotReloadMethodOutcome>(),
                new List<string>(),
                patchedCount: 0,
                addedConstNames: new[] { "Sibling.AddedLimit" });

            Assert.That(HotReloadSiblingRebindWarningSelector.SelectUnappliedWarningFormat(result), Is.Null);
            Assert.That(HotReloadSiblingRebindWarningSelector.AppliedAnyChange(result), Is.True);
        }

        /// <summary>
        /// What: an applied added field does not hide a Failed row, so the sibling still gets the
        /// failed-rebind warning.
        /// </summary>
        [Test]
        public void SelectUnappliedWarningFormat_AddedFieldWithAFailedRow_ReturnsTheFailedWarning()
        {
            HotReloadFileProcessResult result = new HotReloadFileProcessResult(
                new List<HotReloadMethodOutcome> { HotReloadMethodOutcome.Failed("Sibling.Broken", "reason", SiblingPath) },
                new List<string>(),
                patchedCount: 0,
                addedFieldNames: new[] { "Sibling.AddedTable" });

            Assert.That(
                HotReloadSiblingRebindWarningSelector.SelectUnappliedWarningFormat(result),
                Is.EqualTo(HotReloadConstants.ActiveSiblingRebindFailedWarningFormat));
        }

        private static HotReloadFileProcessResult CreateResult(params HotReloadMethodOutcome[] outcomes)
        {
            return CreateResultWithReverts(0, outcomes);
        }

        private static HotReloadFileProcessResult CreateResultWithReverts(
            int revertedUnchangedCount,
            params HotReloadMethodOutcome[] outcomes)
        {
            return new HotReloadFileProcessResult(
                new List<HotReloadMethodOutcome>(outcomes),
                new List<string>(),
                patchedCount: 0,
                revertedUnchangedCount: revertedUnchangedCount);
        }
    }
}
