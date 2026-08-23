using System.Collections.Generic;
using NUnit.Framework;

using UnityEditor;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Verifies Play-entry drop record/clear decisions without driving Editor events.
    /// </summary>
    [TestFixture]
    public sealed class HotReloadPlayModeEntryDropRecorderTests
    {
        private HotReloadPlayModeEntryDropLedgerSessionScope _ledgerSessionScope;

        [SetUp]
        public void SetUp()
        {
            _ledgerSessionScope = new HotReloadPlayModeEntryDropLedgerSessionScope();
        }

        [TearDown]
        public void TearDown()
        {
            _ledgerSessionScope.Restore();
        }

        /// <summary>
        /// What: only ExitingEditMode with domain reload and at least one identity records.
        /// </summary>
        [Test]
        public void ShouldRecord_RequiresExitingEditModeEnabledReloadAndIdentities()
        {
            Assert.That(
                HotReloadPlayModeEntryDropRecorder.ShouldRecord(
                    PlayModeStateChange.ExitingEditMode,
                    isDomainReloadDisabledOnEnterPlayMode: false,
                    activeIdentityCount: 2),
                Is.True);
            Assert.That(
                HotReloadPlayModeEntryDropRecorder.ShouldRecord(
                    PlayModeStateChange.EnteredPlayMode,
                    isDomainReloadDisabledOnEnterPlayMode: false,
                    activeIdentityCount: 2),
                Is.False);
            Assert.That(
                HotReloadPlayModeEntryDropRecorder.ShouldRecord(
                    PlayModeStateChange.ExitingEditMode,
                    isDomainReloadDisabledOnEnterPlayMode: true,
                    activeIdentityCount: 2),
                Is.False);
            Assert.That(
                HotReloadPlayModeEntryDropRecorder.ShouldRecord(
                    PlayModeStateChange.ExitingEditMode,
                    isDomainReloadDisabledOnEnterPlayMode: false,
                    activeIdentityCount: 0),
                Is.False);
        }

        /// <summary>
        /// What: a failed compile keeps the leftover identities.
        /// </summary>
        [Test]
        public void NotifyCompilationFinished_WhenErrorCountIsPositive_KeepsIdentities()
        {
            HotReloadPlayModeEntryDropLedger.Record(new[] { "Type.A()", "Type.B()" });

            HotReloadPlayModeEntryDropRecorder.NotifyCompilationFinished(1);

            Assert.That(
                HotReloadPlayModeEntryDropLedger.GetIdentities(),
                Is.EqualTo(new[] { "Type.A()", "Type.B()" }));
        }

        /// <summary>
        /// What: a successful compile clears every leftover identity.
        /// </summary>
        [Test]
        public void NotifyCompilationFinished_WhenErrorCountIsZero_ClearsIdentities()
        {
            HotReloadPlayModeEntryDropLedger.Record(new[] { "Type.A()" });

            HotReloadPlayModeEntryDropRecorder.NotifyCompilationFinished(0);

            Assert.That(HotReloadPlayModeEntryDropLedger.Count, Is.EqualTo(0));
        }

        /// <summary>
        /// What: apply removes only Patched and Added identities from the leftover set.
        /// </summary>
        [Test]
        public void NotifyApplyRecovered_RemovesOnlyPatchedAndAddedIdentities()
        {
            HotReloadPlayModeEntryDropLedger.Record(
                new[] { "Type.Patched()", "Type.Added()", "Type.Failed()", "Type.Skipped()" });

            HotReloadPlayModeEntryDropRecorder.NotifyApplyRecovered(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Patched("Type.Patched()", "Assets/A.cs"),
                    HotReloadMethodOutcome.Added("Type.Added()", "Assets/A.cs"),
                    HotReloadMethodOutcome.Failed("Type.Failed()", "reason", "Assets/A.cs"),
                    HotReloadMethodOutcome.Skipped("Type.Skipped()", "reason", "Assets/A.cs")
                });

            Assert.That(
                HotReloadPlayModeEntryDropLedger.GetIdentities(),
                Is.EqualTo(new[] { "Type.Failed()", "Type.Skipped()" }));
        }

        /// <summary>
        /// What: revert-all clears every leftover identity.
        /// </summary>
        [Test]
        public void NotifyRevertAll_ClearsIdentities()
        {
            HotReloadPlayModeEntryDropLedger.Record(new[] { "Type.A()" });

            HotReloadPlayModeEntryDropRecorder.NotifyRevertAll();

            Assert.That(HotReloadPlayModeEntryDropLedger.Count, Is.EqualTo(0));
        }

        /// <summary>
        /// What: a same-domain EnteredEditMode after ExitingEditMode means Play entry
        /// was cancelled, so the just-recorded identities leave the ledger and older
        /// leftovers stay.
        /// </summary>
        [Test]
        public void NotifyPlayModeStateChanged_WhenEnteredEditModeFollowsExitingEditModeInSameDomain_RemovesOnlyPendingIdentities()
        {
            HotReloadPlayModeEntryDropLedger.Record(new[] { "Type.Old()" });

            HotReloadPlayModeEntryDropRecorder.NotifyPlayModeStateChanged(
                PlayModeStateChange.ExitingEditMode,
                new[] { "Type.Active()" },
                isDomainReloadDisabledOnEnterPlayMode: false);

            Assert.That(
                HotReloadPlayModeEntryDropLedger.GetIdentities(),
                Is.EqualTo(new[] { "Type.Active()", "Type.Old()" }));

            HotReloadPlayModeEntryDropRecorder.NotifyPlayModeStateChanged(
                PlayModeStateChange.EnteredEditMode,
                new[] { "Type.Active()" },
                isDomainReloadDisabledOnEnterPlayMode: false);

            Assert.That(
                HotReloadPlayModeEntryDropLedger.GetIdentities(),
                Is.EqualTo(new[] { "Type.Old()" }));
        }
    }
}
