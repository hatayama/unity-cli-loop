using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;

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
        public void ShouldResolveOnPlayModeStateChange_WhenDeferralRestoredAfterReload_ResolvesOnceAndClears()
        {
            // Verifies a deferral restored from SessionState after a Play Mode domain reload runs exactly once
            // when Edit Mode returns, even though this instance never saw the Editor leave Edit Mode.
            ExternalSceneFocusReturnDeferral deferral = new ExternalSceneFocusReturnDeferral(isDeferred: true);

            bool firstResolve = deferral.ShouldResolveOnPlayModeStateChange(
                PlayModeStateChange.EnteredEditMode,
                isFocused: true);
            bool secondResolve = deferral.ShouldResolveOnPlayModeStateChange(
                PlayModeStateChange.EnteredEditMode,
                isFocused: true);

            Assert.That(firstResolve, Is.True);
            Assert.That(secondResolve, Is.False);
            Assert.That(deferral.IsDeferred, Is.False);
        }

        [Test]
        public void ShouldResolveOnPlayModeStateChange_AfterAPlaySession_ResolvesOnceEvenWhenUnfocused()
        {
            // Verifies every Play session runs one preflight when Edit Mode returns, without a focus return and
            // while unfocused, because Unity's own post-Play import otherwise raises the reload dialog after Stop.
            ExternalSceneFocusReturnDeferral deferral = new ExternalSceneFocusReturnDeferral(isDeferred: false);

            bool resolvesOnExitingEditMode = deferral.ShouldResolveOnPlayModeStateChange(
                PlayModeStateChange.ExitingEditMode,
                isFocused: false);
            bool resolvesOnEnteredPlayMode = deferral.ShouldResolveOnPlayModeStateChange(
                PlayModeStateChange.EnteredPlayMode,
                isFocused: false);
            bool resolvesOnEnteredEditMode = deferral.ShouldResolveOnPlayModeStateChange(
                PlayModeStateChange.EnteredEditMode,
                isFocused: false);
            bool resolvesOnSecondEnteredEditMode = deferral.ShouldResolveOnPlayModeStateChange(
                PlayModeStateChange.EnteredEditMode,
                isFocused: false);

            Assert.That(resolvesOnExitingEditMode, Is.False);
            Assert.That(resolvesOnEnteredPlayMode, Is.False);
            Assert.That(resolvesOnEnteredEditMode, Is.True);
            Assert.That(resolvesOnSecondEnteredEditMode, Is.False);
        }

        [Test]
        public void ShouldResolveOnPlayModeStateChange_WhenLeavingEditMode_KeepsFingerprintsAcrossPlayReloads()
        {
            // Verifies a domain reload while entering or leaving Play Mode keeps the pre-Play fingerprints,
            // so an external change made before or during Play is still detected when Edit Mode returns.
            ExternalSceneFocusReturnDeferral deferral = new ExternalSceneFocusReturnDeferral(isDeferred: false);

            deferral.ShouldResolveOnPlayModeStateChange(PlayModeStateChange.ExitingEditMode, isFocused: true);
            bool shouldRecord = deferral.ShouldRecordBaselineOnInitialize(
                isFocused: true,
                restoredSceneSnapshots: true);

            Assert.That(shouldRecord, Is.False);
            Assert.That(deferral.IsDeferred, Is.True);
        }

        [Test]
        public void ShouldResolveOnPlayModeStateChange_WhenEditModeReturnsWithoutAPlaySession_DoesNotResolve()
        {
            // Verifies Edit Mode return resolves only for a scheduled preflight, not on every state change.
            ExternalSceneFocusReturnDeferral deferral = new ExternalSceneFocusReturnDeferral(isDeferred: false);

            bool shouldResolve = deferral.ShouldResolveOnPlayModeStateChange(
                PlayModeStateChange.EnteredEditMode,
                isFocused: true);

            Assert.That(shouldResolve, Is.False);
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
