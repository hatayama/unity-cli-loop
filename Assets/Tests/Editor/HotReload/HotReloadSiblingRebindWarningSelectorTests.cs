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

        private static HotReloadFileProcessResult CreateResult(params HotReloadMethodOutcome[] outcomes)
        {
            return new HotReloadFileProcessResult(
                new List<HotReloadMethodOutcome>(outcomes),
                new List<string>(),
                patchedCount: 0);
        }
    }
}
