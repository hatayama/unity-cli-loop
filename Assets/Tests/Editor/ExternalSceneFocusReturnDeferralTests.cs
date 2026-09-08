using System;
using System.Collections.Generic;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests the Play Mode deferral of the focus-return Scene preflight without Unity's Play Mode APIs.
    /// </summary>
    public sealed class ExternalSceneFocusReturnDeferralTests
    {
        [Test]
        public void ShouldResolveOnFocusReturn_WhenEditMode_ReturnsTrueWithoutDeferring()
        {
            // Verifies the Edit Mode focus return still resolves immediately and leaves nothing deferred.
            ExternalSceneFocusReturnDeferral deferral = new ExternalSceneFocusReturnDeferral(isDeferred: false);

            bool shouldResolve = deferral.ShouldResolveOnFocusReturn(isPlayingOrWillChangePlaymode: false);

            Assert.That(shouldResolve, Is.True);
            Assert.That(deferral.IsDeferred, Is.False);
        }

        [Test]
        public void ShouldResolveOnFocusReturn_WhenPlaying_ReturnsFalseAndDefers()
        {
            // Verifies the focus return is skipped during Play Mode and remembered for Edit Mode.
            ExternalSceneFocusReturnDeferral deferral = new ExternalSceneFocusReturnDeferral(isDeferred: false);

            bool shouldResolve = deferral.ShouldResolveOnFocusReturn(isPlayingOrWillChangePlaymode: true);

            Assert.That(shouldResolve, Is.False);
            Assert.That(deferral.IsDeferred, Is.True);
        }

        [Test]
        public void ConsumeOnEnteredEditMode_WhenDeferred_ReturnsTrueOnceAndClears()
        {
            // Verifies a deferred focus return runs exactly once after Play Mode ends.
            ExternalSceneFocusReturnDeferral deferral = new ExternalSceneFocusReturnDeferral(isDeferred: true);

            bool firstConsume = deferral.ConsumeOnEnteredEditMode();
            bool secondConsume = deferral.ConsumeOnEnteredEditMode();

            Assert.That(firstConsume, Is.True);
            Assert.That(secondConsume, Is.False);
            Assert.That(deferral.IsDeferred, Is.False);
        }

        [Test]
        public void ConsumeOnEnteredEditMode_WhenNothingDeferred_ReturnsFalse()
        {
            // Verifies leaving Play Mode without a skipped focus return does not trigger a resolve.
            ExternalSceneFocusReturnDeferral deferral = new ExternalSceneFocusReturnDeferral(isDeferred: false);

            Assert.That(deferral.ConsumeOnEnteredEditMode(), Is.False);
        }

        [Test]
        public void RemoveSnapshotsForScenesNotOpen_RemovesRuntimeLoadedScenesAndKeepsOpenOnes()
        {
            // Verifies fingerprints recorded for Scenes loaded only at runtime do not survive into Edit Mode.
            Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)> snapshots =
                new Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)>(StringComparer.Ordinal)
                {
                    ["Assets/Scenes/Bootstrap.unity"] = (true, DateTime.MinValue, 10),
                    ["Assets/Scenes/SubScene.unity"] = (true, DateTime.MinValue, 20)
                };

            string[] removed = ExternalSceneSnapshotPruner.RemoveSnapshotsForScenesNotOpen(
                snapshots,
                new[] { "Assets/Scenes/Bootstrap.unity" });

            Assert.That(removed, Is.EqualTo(new[] { "Assets/Scenes/SubScene.unity" }));
            Assert.That(snapshots.Keys, Is.EqualTo(new[] { "Assets/Scenes/Bootstrap.unity" }));
        }

        [Test]
        public void RemoveSnapshotsForScenesNotOpen_WhenAllOpen_RemovesNothing()
        {
            // Verifies the baseline for Scenes still open in the Editor is kept so external changes remain detectable.
            Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)> snapshots =
                new Dictionary<string, (bool Exists, DateTime LastWriteTimeUtc, long Length)>(StringComparer.Ordinal)
                {
                    ["Assets/Scenes/Bootstrap.unity"] = (true, DateTime.MinValue, 10)
                };

            string[] removed = ExternalSceneSnapshotPruner.RemoveSnapshotsForScenesNotOpen(
                snapshots,
                new[] { "Assets/Scenes/Bootstrap.unity" });

            Assert.That(removed, Is.Empty);
            Assert.That(snapshots.Count, Is.EqualTo(1));
        }

        [Test]
        public void ShouldRecordBaselineOnInitialize_WhenDeferredAndFocused_KeepsTheRestoredFingerprints()
        {
            // Verifies a deferred focus return keeps the pre-reload fingerprints it still has to compare against.
            ExternalSceneFocusReturnDeferral deferral = new ExternalSceneFocusReturnDeferral(isDeferred: true);

            bool shouldRecord = deferral.ShouldRecordBaselineOnInitialize(
                isFocused: true,
                restoredSceneSnapshots: true);

            Assert.That(shouldRecord, Is.False);
        }

        [Test]
        public void ShouldRecordBaselineOnInitialize_WhenNotDeferredAndFocused_RecordsTheCurrentState()
        {
            // Verifies a focused reload with nothing deferred still refreshes the baseline as before.
            ExternalSceneFocusReturnDeferral deferral = new ExternalSceneFocusReturnDeferral(isDeferred: false);

            bool shouldRecord = deferral.ShouldRecordBaselineOnInitialize(
                isFocused: true,
                restoredSceneSnapshots: true);

            Assert.That(shouldRecord, Is.True);
        }

        [Test]
        public void ShouldRecordBaselineOnInitialize_WhenNothingWasRestored_RecordsEvenWhileDeferred()
        {
            // Verifies first launch always records a baseline, because there is nothing to preserve.
            ExternalSceneFocusReturnDeferral deferral = new ExternalSceneFocusReturnDeferral(isDeferred: true);

            bool shouldRecord = deferral.ShouldRecordBaselineOnInitialize(
                isFocused: true,
                restoredSceneSnapshots: false);

            Assert.That(shouldRecord, Is.True);
        }

        [Test]
        public void ShouldRecordBaselineOnInitialize_WhenUnfocused_KeepsTheRestoredFingerprints()
        {
            // Verifies the existing unfocused-reload behavior is unchanged when nothing is deferred.
            ExternalSceneFocusReturnDeferral deferral = new ExternalSceneFocusReturnDeferral(isDeferred: false);

            bool shouldRecord = deferral.ShouldRecordBaselineOnInitialize(
                isFocused: false,
                restoredSceneSnapshots: true);

            Assert.That(shouldRecord, Is.False);
        }
    }
}
