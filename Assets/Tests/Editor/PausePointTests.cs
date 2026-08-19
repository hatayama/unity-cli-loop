using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.Tests.PausePointToolsFixtures;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies named pause point behavior without pausing the real Unity Editor during tests.
    /// </summary>
    [TestFixture]
    public sealed class PausePointTests
    {
        private DateTime _nowUtc;
        private bool _originalEnterPlayModeOptionsEnabled;
        private EnterPlayModeOptions _originalEnterPlayModeOptions;
        private FakePauseController _pauseController;

        [SetUp]
        public void SetUp()
        {
            _nowUtc = new DateTime(2026, 6, 3, 0, 0, 0, DateTimeKind.Utc);
            _originalEnterPlayModeOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            _originalEnterPlayModeOptions = EditorSettings.enterPlayModeOptions;
            _pauseController = new FakePauseController();
            UloopPausePointRegistry.ConfigureForTests(_pauseController, () => _nowUtc);
        }

        [TearDown]
        public void TearDown()
        {
            EditorSettings.enterPlayModeOptionsEnabled = _originalEnterPlayModeOptionsEnabled;
            EditorSettings.enterPlayModeOptions = _originalEnterPlayModeOptions;
            // Tests that enable pause points by File/Line leave a Harmony transpiler attached to
            // the fixture method; clear it so later tests re-patch cleanly instead of hitting the
            // Patcher's "already patched" no-op path against a previous test's ledger entry.
            SourcePausePointPatcher.UnpatchAll();
            UloopPausePointRegistry.ResetForTests();
        }

        [Test]
        public void Pause_WhenPausePointIsNotEnabled_DoesNotPause()
        {
            // Verifies marker calls are no-op until the CLI enables the same id.
            UloopPausePoint.Pause("jump");

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus("jump");

            Assert.That(_pauseController.PauseCount, Is.EqualTo(0));
            Assert.That(snapshot.Status, Is.EqualTo(UloopPausePointStatus.NotEnabled));
            Assert.That(snapshot.IsEnabled, Is.False);
        }

        [Test]
        public void Pause_WhenPausePointIsEnabled_RecordsHitAndRequestsPause()
        {
            // Verifies an enabled marker hit records state and requests a Unity pause.
            UloopPausePointRegistry.Enable("jump", 30);

            UloopPausePoint.Pause("jump");

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus("jump");
            Assert.That(_pauseController.PauseCount, Is.EqualTo(1));
            Assert.That(snapshot.Status, Is.EqualTo(UloopPausePointStatus.Hit));
            Assert.That(snapshot.IsHit, Is.True);
            Assert.That(snapshot.IsEnabled, Is.False);
            Assert.That(snapshot.EditorState.IsPaused, Is.True);
            Assert.That(snapshot.EditorState.CapturedAt, Is.EqualTo(UloopPausePointEditorStateCapturedAt.PausePointHit));
            Assert.That(snapshot.HitCount, Is.EqualTo(1));
        }

        [Test]
        public void Pause_WhenPausePointIsEnabled_RecordsHitEvidence()
        {
            // Verifies a hit snapshot includes stable timing and sequence evidence.
            UloopPausePointRegistry.Enable("jump", 30);
            _nowUtc = _nowUtc.AddMilliseconds(125);

            UloopPausePoint.Pause("jump");

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus("jump");
            Assert.That(snapshot.FirstHitAtUtc, Is.EqualTo("2026-06-03T00:00:00.1250000Z"));
            Assert.That(snapshot.LastHitAtUtc, Is.EqualTo("2026-06-03T00:00:00.1250000Z"));
            Assert.That(snapshot.FirstHitSequence, Is.EqualTo(1));
            Assert.That(snapshot.LastHitSequence, Is.EqualTo(1));
        }

        [Test]
        public void Pause_WhenPausePointIsEnabled_StoresLatestHitSnapshot()
        {
            // Verifies input interruption responses can read the latest marker hit.
            UloopPausePointRegistry.Enable("jump", 30);

            UloopPausePoint.Pause("jump");

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetLatestHitSnapshot();
            Assert.That(snapshot, Is.Not.Null);
            Assert.That(snapshot.Id, Is.EqualTo("jump"));
            Assert.That(snapshot.HitCount, Is.EqualTo(1));
        }

        [Test]
        public void Pause_WhenMultiplePausePointsHit_StoresAllHitSnapshotsInOrder()
        {
            // Verifies input interruption responses can list every marker hit, not just the latest.
            UloopPausePointRegistry.Enable("jump", 30);
            UloopPausePointRegistry.Enable("land", 30);

            UloopPausePoint.Pause("jump");
            UloopPausePoint.Pause("land");

            IReadOnlyList<UloopPausePointSnapshot> hits = UloopPausePointRegistry.GetHitSnapshots();
            Assert.That(hits.Count, Is.EqualTo(2));
            Assert.That(hits[0].Id, Is.EqualTo("jump"));
            Assert.That(hits[1].Id, Is.EqualTo("land"));
        }

        [Test]
        public void Enable_WhenSamePausePointWasHit_RemovesItFromHitSnapshots()
        {
            // Verifies re-enabling a hit marker drops its stale entry from the hit list.
            UloopPausePointRegistry.Enable("jump", 30);
            UloopPausePoint.Pause("jump");

            UloopPausePointRegistry.Enable("jump", 30);

            Assert.That(UloopPausePointRegistry.GetHitSnapshots(), Is.Empty);
        }

        [Test]
        public void Clear_WhenAlreadyClearedByRunTests_PreservesOriginalReason()
        {
            // Verifies a later explicit clear does not erase run-tests auto-clear evidence.
            UloopPausePointRegistry.Enable("jump", 30);
            UloopPausePointRegistry.ClearAll(UloopPausePointClearedReason.RunTestsAutoClear);

            UloopPausePointRegistry.Clear("jump");
            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus("jump");

            Assert.That(snapshot.Status, Is.EqualTo(UloopPausePointStatus.Cleared));
            Assert.That(snapshot.ClearedReason, Is.EqualTo(UloopPausePointClearedReason.RunTestsAutoClear));
            Assert.That(snapshot.StatusBeforeClear, Is.EqualTo(UloopPausePointStatus.Enabled));
        }

        [Test]
        public void ClearAll_WhenEnabled_SetsRunTestsAutoClearReason()
        {
            // Verifies run-tests-style ClearAll is visible on status after wiping an enabled marker.
            UloopPausePointRegistry.Enable("jump", 30);

            UloopPausePointRegistry.ClearAll(UloopPausePointClearedReason.RunTestsAutoClear);
            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus("jump");

            Assert.That(snapshot.Status, Is.EqualTo(UloopPausePointStatus.Cleared));
            Assert.That(snapshot.ClearedReason, Is.EqualTo(UloopPausePointClearedReason.RunTestsAutoClear));
            Assert.That(snapshot.StatusBeforeClear, Is.EqualTo(UloopPausePointStatus.Enabled));
        }

        [Test]
        public void ClearAll_WhenExpired_ReportsAfterExpiredReason()
        {
            // Verifies ClearAll after timeout keeps AfterExpired instead of erasing the timeout clue.
            UloopPausePointRegistry.Enable("jump", 1);
            _nowUtc = _nowUtc.AddSeconds(2);

            UloopPausePointRegistry.ClearAll(UloopPausePointClearedReason.ClearAll);
            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus("jump");

            Assert.That(snapshot.Status, Is.EqualTo(UloopPausePointStatus.Cleared));
            Assert.That(snapshot.ClearedReason, Is.EqualTo(UloopPausePointClearedReason.AfterExpired));
            Assert.That(snapshot.StatusBeforeClear, Is.EqualTo(UloopPausePointStatus.Expired));
        }

        [Test]
        public void Hit_WhenAlreadyCleared_LogsLateHitAndSetsDiscardFlag()
        {
            // Verifies a delayed hit after Clear is observable instead of a silent no-op.
            UloopPausePointRegistry.Enable("jump", 30);
            UloopPausePointRegistry.Clear("jump");

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("hit after it was cleared"));
            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.Hit("jump");

            Assert.That(snapshot.Status, Is.EqualTo(UloopPausePointStatus.Cleared));
            Assert.That(snapshot.LateHitDiscardedAfterClear, Is.True);
            Assert.That(snapshot.ClearedReason, Is.EqualTo(UloopPausePointClearedReason.ExplicitClear));
            Assert.That(_pauseController.PauseCount, Is.EqualTo(0));
        }

        [Test]
        public void GetStatus_WhenTimeoutPasses_ExpiresAndDisarms()
        {
            // Verifies timeout disables the marker before a late hit can pause Unity.
            UloopPausePointRegistry.Enable("jump", 1);
            _nowUtc = _nowUtc.AddSeconds(2);

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus("jump");
            UloopPausePoint.Pause("jump");

            Assert.That(snapshot.Status, Is.EqualTo(UloopPausePointStatus.Expired));
            Assert.That(snapshot.Expired, Is.True);
            Assert.That(snapshot.RemainingMilliseconds, Is.EqualTo(0));
            Assert.That(
                snapshot.RecommendedNextAction,
                Is.EqualTo("Re-enable the marker with a longer --timeout-seconds and trigger the code path again; clearing the expired marker first is not required."));
            Assert.That(snapshot.IsEnabled, Is.False);
            Assert.That(_pauseController.PauseCount, Is.EqualTo(0));
        }

        [Test]
        public void GetStatus_WhenExpiredIdContainsShellSyntax_ReturnsShellNeutralRecoveryAction()
        {
            // Verifies recovery guidance does not embed shell syntax that differs between user environments.
            UloopPausePointRegistry.Enable("jump && other-command", 1);
            _nowUtc = _nowUtc.AddSeconds(2);

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus("jump && other-command");

            Assert.That(
                snapshot.RecommendedNextAction,
                Is.EqualTo("Re-enable the marker with a longer --timeout-seconds and trigger the code path again; clearing the expired marker first is not required."));
        }

        [Test]
        public void GetStatus_WhenEnabled_ReportsTimingAndGenerationFields()
        {
            // Verifies status reports the marker lifetime and generation without making callers recompute it.
            UloopPausePointRegistry.Enable("jump", 30);
            _nowUtc = _nowUtc.AddMilliseconds(250);

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus("jump");

            Assert.That(snapshot.EnabledAtUtc, Is.EqualTo("2026-06-03T00:00:00.0000000Z"));
            Assert.That(snapshot.ElapsedSinceEnabledMilliseconds, Is.EqualTo(250));
            Assert.That(snapshot.RemainingMilliseconds, Is.EqualTo(29750));
            Assert.That(snapshot.Generation, Is.EqualTo(1));
            Assert.That(snapshot.Expired, Is.False);
            Assert.That(snapshot.EditorState.CapturedAt, Is.EqualTo(UloopPausePointEditorStateCapturedAt.Current));
        }

        [Test]
        public void ExtendExpiryForAwait_WhenRequestedMinimumExceedsRemaining_PushesExpiryForward()
        {
            // Verifies await-pause-point can extend a marker's countdown to at least its own
            // deadline, so a slow multi-step CLI round trip does not expire the marker mid-wait.
            UloopPausePointRegistry.Enable("jump", 10);
            _nowUtc = _nowUtc.AddSeconds(5);

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.ExtendExpiryForAwait("jump", 30);

            Assert.That(snapshot.RemainingMilliseconds, Is.EqualTo(30000));
        }

        [Test]
        public void ExtendExpiryForAwait_WhenRemainingAlreadyExceedsRequestedMinimum_DoesNotShrinkExpiry()
        {
            // Verifies extension only ever moves the deadline forward, never backward.
            UloopPausePointRegistry.Enable("jump", 60);
            _nowUtc = _nowUtc.AddSeconds(5);

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.ExtendExpiryForAwait("jump", 10);

            Assert.That(snapshot.RemainingMilliseconds, Is.EqualTo(55000));
        }

        [Test]
        public void ExtendExpiryForAwait_WhenMarkerIsNotEnabled_ReturnsNotEnabledWithoutThrowing()
        {
            // Verifies extending an unknown/not-yet-enabled id is a safe no-op status query.
            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.ExtendExpiryForAwait("jump", 30);

            Assert.That(snapshot.Status, Is.EqualTo(UloopPausePointStatus.NotEnabled));
        }

        [Test]
        public void ExtendExpiryForAwait_WhenMarkerAlreadyExpired_DoesNotResurrectIt()
        {
            // Verifies extension cannot bring an already-expired marker back to life.
            UloopPausePointRegistry.Enable("jump", 1);
            _nowUtc = _nowUtc.AddSeconds(2);

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.ExtendExpiryForAwait("jump", 30);

            Assert.That(snapshot.Status, Is.EqualTo(UloopPausePointStatus.Expired));
            Assert.That(snapshot.RemainingMilliseconds, Is.EqualTo(0));
        }

        [Test]
        public void Enable_WhenMarkerIsReenabled_IncrementsGeneration()
        {
            // Verifies callers can distinguish a fresh marker from stale status or log evidence with the same id.
            UloopPausePointSnapshot firstSnapshot = UloopPausePointRegistry.Enable("jump", 30);
            UloopPausePointSnapshot secondSnapshot = UloopPausePointRegistry.Enable("jump", 30);

            Assert.That(firstSnapshot.Generation, Is.EqualTo(1));
            Assert.That(secondSnapshot.Generation, Is.EqualTo(2));
        }

        [Test]
        public void Clear_WhenPausePointIsEnabled_DisablesWithoutPause()
        {
            // Verifies explicit clear prevents later marker hits from pausing Unity.
            UloopPausePointRegistry.Enable("jump", 30);

            (UloopPausePointSnapshot snapshot, _, _) = UloopPausePointRegistry.Clear("jump");
            UloopPausePoint.Pause("jump");

            Assert.That(snapshot.Status, Is.EqualTo(UloopPausePointStatus.Cleared));
            Assert.That(snapshot.IsEnabled, Is.False);
            Assert.That(_pauseController.PauseCount, Is.EqualTo(0));
        }

        [Test]
        public void Clear_WhenPausePointWasHit_ReportsAlreadyHitMessage()
        {
            // Verifies clearing an already-hit one-shot marker explains why nothing was armed anymore.
            UloopPausePointRegistry.Enable("jump", 30);
            UloopPausePoint.Pause("jump");

            (UloopPausePointSnapshot snapshot, _, _) = UloopPausePointRegistry.Clear("jump");

            Assert.That(snapshot.Message, Is.EqualTo("Pause point was already hit (auto-disarmed); nothing to clear."));
            Assert.That(_pauseController.IsPaused, Is.False);
        }

        [Test]
        public void Clear_AfterHit_ResumesPausePointOwnedPauseAndReportsResumed()
        {
            // Verifies Clear resumes and reports ResumedFromPause when a pause-point hit owns the pause.
            UloopPausePointRegistry.Enable("jump", 30);
            UloopPausePoint.Pause("jump");
            Assert.That(_pauseController.IsPaused, Is.True);

            (UloopPausePointSnapshot _, bool resumedFromPause, _) = UloopPausePointRegistry.Clear("jump");

            Assert.That(resumedFromPause, Is.True);
            Assert.That(_pauseController.IsPaused, Is.False);
        }

        [Test]
        public void Clear_WhenEditorManuallyPaused_LeavesPauseUntouchedAndReportsNotResumed()
        {
            // Verifies Clear preserves a manual pause (no open pause window) instead of resuming it.
            UloopPausePointRegistry.Enable("jump", 30);
            _pauseController.PauseExternally();

            (UloopPausePointSnapshot _, bool resumedFromPause, _) = UloopPausePointRegistry.Clear("jump");

            Assert.That(resumedFromPause, Is.False);
            Assert.That(_pauseController.IsPaused, Is.True);
            Assert.That(_pauseController.ResumeCount, Is.EqualTo(0));
        }

        [Test]
        public void ClearAll_AfterHit_ResumesPausePointOwnedPauseAndReportsResumed()
        {
            // Verifies ClearAll resumes and reports ResumedFromPause when a hit owns the pause.
            UloopPausePointRegistry.Enable("jump", 30);
            UloopPausePoint.Pause("jump");

            UloopPausePointClearAllResult result = UloopPausePointRegistry.ClearAll();

            Assert.That(result.ResumedFromPause, Is.True);
            Assert.That(_pauseController.IsPaused, Is.False);
        }

        [Test]
        public void ClearAll_WhenEditorManuallyPaused_LeavesPauseUntouchedAndReportsNotResumed()
        {
            // Verifies ClearAll preserves a manual pause (no open pause window) instead of resuming it.
            UloopPausePointRegistry.Enable("jump", 30);
            _pauseController.PauseExternally();

            UloopPausePointClearAllResult result = UloopPausePointRegistry.ClearAll();

            Assert.That(result.ResumedFromPause, Is.False);
            Assert.That(_pauseController.IsPaused, Is.True);
            Assert.That(_pauseController.ResumeCount, Is.EqualTo(0));
        }

        [Test]
        public void Clear_WhenWindowOpenButEditorAlreadyExternallyUnpaused_DoesNotReportResumeAndClosesStaleWindow()
        {
            // Verifies the still-open window is reconciled before deciding: a hit opened the
            // window, then the Editor was unpaused externally before the update tick observed it.
            // Clear must not claim it resumed Play Mode (Resume is a no-op on an unpaused Editor)
            // and must close the stale window so it stops freezing expiry. ClearAll shares the same
            // ResumeEditorPauseIfOwnedByPausePoint path, so this covers both entry points.
            UloopPausePointRegistry.Enable("jump", 30);
            UloopPausePoint.Pause("jump");
            _pauseController.ResumeExternally();

            (UloopPausePointSnapshot _, bool resumedFromPause, _) = UloopPausePointRegistry.Clear("jump");

            Assert.That(resumedFromPause, Is.False);
            Assert.That(_pauseController.ResumeCount, Is.EqualTo(0));
            // The stale window is closed: an unrelated marker's countdown is no longer frozen.
            UloopPausePointRegistry.Enable("dash", 1);
            _nowUtc = _nowUtc.AddSeconds(2);
            UloopPausePointRegistry.ApplyCaptureWindowExpirations();
            Assert.That(UloopPausePointRegistry.GetStatus("dash").Status, Is.EqualTo(UloopPausePointStatus.Expired));
        }

        [Test]
        public void ApplyCaptureWindowExpirations_WhenHitPastTimeoutWhilePaused_DoesNotExpireUntilResumed()
        {
            // Verifies a hit's own Editor pause freezes the capture window countdown, so an
            // abandoned SingleShot hit does not expire mid-inspection even past its original timeout.
            UloopPausePointRegistry.Enable("jump", 30);
            UloopPausePoint.Pause("jump");
            _nowUtc = _nowUtc.AddSeconds(31);

            UloopPausePointRegistry.ApplyCaptureWindowExpirations();

            UloopPausePointSnapshot status = UloopPausePointRegistry.GetStatus("jump");
            Assert.That(status.Status, Is.EqualTo(UloopPausePointStatus.Hit));
            Assert.That(_pauseController.IsPaused, Is.True);
        }

        [Test]
        public void ApplyCaptureWindowExpirations_AfterResumeFollowingFrozenPause_ExpiresOnlyAfterCreditedDeadline()
        {
            // Verifies the frozen duration is credited back to ExpiresAtUtc on resume: expiry
            // does not fire at the original deadline, only after the extended one elapses.
            UloopPausePointRegistry.Enable("jump", 30);
            UloopPausePoint.Pause("jump");
            _nowUtc = _nowUtc.AddSeconds(20);
            UloopPausePointRegistry.ResumeEditorPauseForClientDisconnect();
            UloopPausePointRegistry.ApplyPendingClientDisconnectResume();
            Assert.That(_pauseController.IsPaused, Is.False);

            _nowUtc = _nowUtc.AddSeconds(25);
            UloopPausePointRegistry.ApplyCaptureWindowExpirations();
            Assert.That(UloopPausePointRegistry.GetStatus("jump").Status, Is.EqualTo(UloopPausePointStatus.Hit));

            _nowUtc = _nowUtc.AddSeconds(6);
            UloopPausePointRegistry.ApplyCaptureWindowExpirations();
            Assert.That(UloopPausePointRegistry.GetStatus("jump").Status, Is.EqualTo(UloopPausePointStatus.Expired));
        }

        [Test]
        public void ApplyCaptureWindowExpirations_WhenAnotherMarkerHoldsEditorPaused_FreezesUnrelatedMarkerToo()
        {
            // Verifies the freeze is registry-wide: an unrelated marker's countdown is also
            // frozen for as long as any hit is holding the Editor paused for inspection.
            UloopPausePointRegistry.Enable("jump", 30);
            UloopPausePointRegistry.Enable("dash", 10);
            UloopPausePoint.Pause("jump");

            _nowUtc = _nowUtc.AddSeconds(15);
            UloopPausePointRegistry.ApplyCaptureWindowExpirations();

            Assert.That(UloopPausePointRegistry.GetStatus("dash").Status, Is.EqualTo(UloopPausePointStatus.Enabled));
        }

        [Test]
        public void ClosePauseWindowIfEditorResumedExternally_WhenEditorUnpausedOutsideRegistry_ClosesWindowAndCreditsTime()
        {
            // Verifies an external unpause (control-play-mode Play/Stop, or the Editor's own
            // pause button) that never calls back into Clear/ClearAll/ResumeEditorPause still
            // closes the open freeze window, so the countdown resumes instead of staying frozen
            // forever and expiry does not later over-credit game-running time.
            UloopPausePointRegistry.Enable("jump", 30);
            UloopPausePoint.Pause("jump");
            _nowUtc = _nowUtc.AddSeconds(20);
            _pauseController.ResumeExternally();

            UloopPausePointRegistry.ClosePauseWindowIfEditorResumedExternally();

            _nowUtc = _nowUtc.AddSeconds(25);
            UloopPausePointRegistry.ApplyCaptureWindowExpirations();
            Assert.That(UloopPausePointRegistry.GetStatus("jump").Status, Is.EqualTo(UloopPausePointStatus.Hit));

            _nowUtc = _nowUtc.AddSeconds(6);
            UloopPausePointRegistry.ApplyCaptureWindowExpirations();
            Assert.That(UloopPausePointRegistry.GetStatus("jump").Status, Is.EqualTo(UloopPausePointStatus.Expired));
        }

        [Test]
        public void ClosePauseWindowIfEditorResumedExternally_WhileStillPaused_KeepsWindowOpen()
        {
            // Verifies the close only triggers once the controller reports Unpaused; a stray call
            // while still genuinely paused must not clear the freeze early.
            UloopPausePointRegistry.Enable("jump", 30);
            UloopPausePoint.Pause("jump");
            _nowUtc = _nowUtc.AddSeconds(31);

            UloopPausePointRegistry.ClosePauseWindowIfEditorResumedExternally();
            UloopPausePointRegistry.ApplyCaptureWindowExpirations();

            Assert.That(UloopPausePointRegistry.GetStatus("jump").Status, Is.EqualTo(UloopPausePointStatus.Hit));
        }

        [Test]
        public void GetActivePausePointId_WhenNoMarkerHasPausedTheEditor_ReturnsEmpty()
        {
            // Verifies the read-only signal used by execute-dynamic-code stays empty before any hit.
            UloopPausePointRegistry.Enable("jump", 30);

            Assert.That(UloopPausePointRegistry.GetActivePausePointId(), Is.Empty);
        }

        [Test]
        public void GetActivePausePointId_WhileAMarkerHitHoldsTheEditorPaused_ReturnsThatMarkerId()
        {
            // Verifies execute-dynamic-code can attribute an in-progress pause to the hitting marker.
            UloopPausePointRegistry.Enable("jump", 30);
            UloopPausePoint.Pause("jump");

            Assert.That(UloopPausePointRegistry.GetActivePausePointId(), Is.EqualTo("jump"));
        }

        [Test]
        public void GetActivePausePointId_WhenATraceMarkerHitsWhileAnotherMarkerHoldsThePause_StaysOnTheOriginalMarker()
        {
            // Verifies a Trace-mode hit (which never pauses, e.g. fired via execute-dynamic-code
            // or Step while already paused) does not steal attribution from the marker actually
            // holding the Editor paused.
            UloopPausePointRegistry.Enable("jump", 30);
            UloopPausePointRegistry.Enable("trace-marker", 30, UloopPausePointCaptureMode.Trace);
            UloopPausePoint.Pause("jump");

            UloopPausePoint.Pause("trace-marker");

            Assert.That(UloopPausePointRegistry.GetActivePausePointId(), Is.EqualTo("jump"));
        }

        [Test]
        public void GetActivePausePointId_WhenASecondMarkerHitsWhileAlreadyPaused_UpdatesToTheNewMarker()
        {
            // Verifies a second non-Trace hit while already paused becomes the new (only) reason
            // the Editor stays paused, so attribution correctly moves to it.
            UloopPausePointRegistry.Enable("jump", 30);
            UloopPausePointRegistry.Enable("dash", 30);
            UloopPausePoint.Pause("jump");

            UloopPausePoint.Pause("dash");

            Assert.That(UloopPausePointRegistry.GetActivePausePointId(), Is.EqualTo("dash"));
        }

        [Test]
        public void GetActivePausePointId_AfterTheFreezeWindowIsClosed_ReturnsEmpty()
        {
            // Verifies the signal clears once the freeze window closes (Clear here), not just once
            // the Editor itself unpauses, so a resumed session never keeps reporting a stale id.
            UloopPausePointRegistry.Enable("jump", 30);
            UloopPausePoint.Pause("jump");

            UloopPausePointRegistry.Clear("jump");

            Assert.That(UloopPausePointRegistry.GetActivePausePointId(), Is.Empty);
        }

        [Test]
        public void ResumeEditorPauseForClientDisconnect_WhenPaused_ShouldResumeOnMainThreadApply()
        {
            // Verifies disconnect only arms a pending flag; main-thread apply resumes once (Option B).
            UloopPausePointRegistry.Enable("jump", 30);
            UloopPausePoint.Pause("jump");

            UloopPausePointRegistry.ResumeEditorPauseForClientDisconnect();
            Assert.That(_pauseController.IsPaused, Is.True);
            Assert.That(_pauseController.ResumeCount, Is.EqualTo(0));

            UloopPausePointRegistry.ApplyPendingClientDisconnectResume();

            Assert.That(_pauseController.IsPaused, Is.False);
            Assert.That(_pauseController.ResumeCount, Is.EqualTo(1));
        }

        [Test]
        public void ApplyPendingClientDisconnectResume_WhenNotPaused_ShouldDiscardPendingFlag()
        {
            // Verifies a disconnect request while already running does not call Resume.
            Assert.That(_pauseController.IsPaused, Is.False);

            UloopPausePointRegistry.ResumeEditorPauseForClientDisconnect();
            UloopPausePointRegistry.ApplyPendingClientDisconnectResume();

            Assert.That(_pauseController.ResumeCount, Is.EqualTo(0));
            Assert.That(_pauseController.IsPaused, Is.False);
        }

        [Test]
        public void Clear_WhenPausePointExpired_ReportsAlreadyExpiredMessage()
        {
            // Verifies clearing an expired marker explains it was never hit instead of claiming a clear.
            UloopPausePointRegistry.Enable("jump", 1);
            _nowUtc = _nowUtc.AddSeconds(2);

            (UloopPausePointSnapshot snapshot, _, _) = UloopPausePointRegistry.Clear("jump");

            Assert.That(snapshot.Message, Is.EqualTo("Pause point had already expired before being hit; nothing to clear."));
        }

        [Test]
        public void Clear_WhenPausePointAlreadyCleared_ReportsAlreadyClearedMessage()
        {
            // Verifies a repeated clear reports the marker was already cleared.
            UloopPausePointRegistry.Enable("jump", 30);
            UloopPausePointRegistry.Clear("jump");

            (UloopPausePointSnapshot snapshot, _, _) = UloopPausePointRegistry.Clear("jump");

            Assert.That(snapshot.Message, Is.EqualTo("Pause point was already cleared."));
        }

        /// <summary>
        /// Verifies Clear(id) reports 1 when an enabled marker that has been hit is actually cleared.
        /// </summary>
        [Test]
        public void Clear_WhenHitMarkerIsCleared_ReportsClearedCountOne()
        {
            UloopPausePointRegistry.Enable("jump", 30);
            UloopPausePoint.Pause("jump");

            (UloopPausePointSnapshot _, bool _, int clearedCount) = UloopPausePointRegistry.Clear("jump");

            Assert.That(clearedCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies Clear(id, AwaitTimeoutAutoClear) stores that reason on the cleared snapshot.
        /// </summary>
        [Test]
        public void Clear_WithAwaitTimeoutAutoClearReason_ReportsThatReasonOnSnapshot()
        {
            UloopPausePointRegistry.Enable("jump", 30);

            (UloopPausePointSnapshot snapshot, bool _, int clearedCount) =
                UloopPausePointRegistry.Clear("jump", UloopPausePointClearedReason.AwaitTimeoutAutoClear);

            Assert.That(clearedCount, Is.EqualTo(1));
            Assert.That(snapshot.Status, Is.EqualTo(UloopPausePointStatus.Cleared));
            Assert.That(snapshot.ClearedReason, Is.EqualTo(UloopPausePointClearedReason.AwaitTimeoutAutoClear));
            Assert.That(snapshot.StatusBeforeClear, Is.EqualTo(UloopPausePointStatus.Enabled));
        }

        /// <summary>
        /// Verifies the status-bridge Clear path stores AwaitTimeoutAutoClear when Reason is supplied.
        /// </summary>
        [Test]
        public void PausePointStatusBridgeCommand_Clear_WithAwaitTimeoutAutoClearReason_ReportsThatReason()
        {
            UloopPausePointRegistry.Enable("jump", 30);

            JObject parameters = new()
            {
                ["Id"] = "jump",
                ["Reason"] = UloopPausePointClearedReason.AwaitTimeoutAutoClear
            };
            PausePointStatusResponse response = PausePointStatusBridgeCommand.Clear(parameters);

            Assert.That(response.Status, Is.EqualTo(UloopPausePointStatus.Cleared));
            Assert.That(response.ClearedReason, Is.EqualTo(UloopPausePointClearedReason.AwaitTimeoutAutoClear));
        }

        /// <summary>
        /// Verifies the status-bridge Clear path still records ExplicitClear when Reason is omitted.
        /// </summary>
        [Test]
        public void PausePointStatusBridgeCommand_Clear_WithoutReason_ReportsExplicitClear()
        {
            UloopPausePointRegistry.Enable("jump", 30);

            JObject parameters = new() { ["Id"] = "jump" };
            PausePointStatusResponse response = PausePointStatusBridgeCommand.Clear(parameters);

            Assert.That(response.Status, Is.EqualTo(UloopPausePointStatus.Cleared));
            Assert.That(response.ClearedReason, Is.EqualTo(UloopPausePointClearedReason.ExplicitClear));
        }

        /// <summary>
        /// Verifies Clear(id) reports 0 for an id that was never enabled.
        /// </summary>
        [Test]
        public void Clear_WhenIdIsUnknown_ReportsClearedCountZero()
        {
            (UloopPausePointSnapshot _, bool _, int clearedCount) = UloopPausePointRegistry.Clear("missing");

            Assert.That(clearedCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies a second Clear(id) on the same marker reports 0 instead of recounting the first clear.
        /// </summary>
        [Test]
        public void Clear_WhenSameIdClearedTwice_ReportsZeroOnSecondClear()
        {
            UloopPausePointRegistry.Enable("jump", 30);
            (UloopPausePointSnapshot _, bool _, int firstClearedCount) = UloopPausePointRegistry.Clear("jump");

            (UloopPausePointSnapshot _, bool _, int secondClearedCount) = UloopPausePointRegistry.Clear("jump");

            Assert.That(firstClearedCount, Is.EqualTo(1));
            Assert.That(secondClearedCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies Clear(id) reports 1 when the first clear happens after the capture window expired.
        /// </summary>
        [Test]
        public void Clear_WhenExpiredMarkerIsCleared_ReportsClearedCountOne()
        {
            UloopPausePointRegistry.Enable("jump", 1);
            _nowUtc = _nowUtc.AddSeconds(2);

            (UloopPausePointSnapshot _, bool _, int clearedCount) = UloopPausePointRegistry.Clear("jump");

            Assert.That(clearedCount, Is.EqualTo(1));
        }

        [Test]
        public async Task ClearAll_WhenNothingActive_ReportsNoActiveMessage()
        {
            // Verifies bulk clear with no armed markers does not claim that markers were cleared.
            ClearPausePointTool tool = new();
            JObject parameters = new()
            {
                ["all"] = true
            };

            PausePointResponse response = (PausePointResponse)await tool.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.ClearedCount, Is.EqualTo(0));
            Assert.That(response.EditorState.IsPlaying, Is.True);
            Assert.That(response.EditorState.IsPaused, Is.False);
            Assert.That(response.EditorState.CapturedAt, Is.EqualTo(UloopPausePointEditorStateCapturedAt.ClearAll));
            Assert.That(response.Message, Is.EqualTo("No active pause points to clear."));
        }

        [Test]
        public async Task Clear_WhenResumingPausePointOwnedPause_SetsResumedPlayModeWarning()
        {
            // Verifies the clear-pause-point tool warns when the clear resumed a pause-point-owned pause.
            UloopPausePointRegistry.Enable("jump", 30);
            UloopPausePoint.Pause("jump");

            ClearPausePointTool tool = new();
            JObject parameters = new() { ["id"] = "jump" };
            PausePointResponse response = (PausePointResponse)await tool.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.Warning, Is.EqualTo(SourcePausePointConstants.ClearResumedPlayModeWarning));
        }

        [Test]
        public async Task Clear_WhenManualPausePreserved_SetsNoWarning()
        {
            // Verifies the clear-pause-point tool emits no resume warning when it preserves a manual pause.
            UloopPausePointRegistry.Enable("jump", 30);
            _pauseController.PauseExternally();

            ClearPausePointTool tool = new();
            JObject parameters = new() { ["id"] = "jump" };
            PausePointResponse response = (PausePointResponse)await tool.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.Warning, Is.Empty);
        }

        /// <summary>
        /// Verifies clear-pause-point --id reports ClearedCount 1 through the public tool response path.
        /// </summary>
        [Test]
        public async Task ClearPausePointTool_WhenClearingById_ReportsClearedCountOne()
        {
            UloopPausePointRegistry.Enable("jump", 30);

            ClearPausePointTool tool = new();
            JObject parameters = new() { ["id"] = "jump" };
            PausePointResponse response = (PausePointResponse)await tool.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.ClearedCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies clear-pause-point --id reports ClearedCount 0 on a no-op second clear through the public tool path.
        /// </summary>
        [Test]
        public async Task ClearPausePointTool_WhenClearingSameIdTwice_ReportsClearedCountZeroOnSecondCall()
        {
            UloopPausePointRegistry.Enable("jump", 30);
            ClearPausePointTool tool = new();
            JObject parameters = new() { ["id"] = "jump" };
            await tool.ExecuteAsync(parameters, CancellationToken.None);

            PausePointResponse response = (PausePointResponse)await tool.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.ClearedCount, Is.EqualTo(0));
        }

        [Test]
        public async Task ClearAll_WhenResumingPausePointOwnedPause_SetsResumedPlayModeWarning()
        {
            // Verifies clear-pause-point --all warns when the bulk clear resumed a pause-point-owned pause.
            UloopPausePointRegistry.Enable("jump", 30);
            UloopPausePoint.Pause("jump");

            ClearPausePointTool tool = new();
            JObject parameters = new() { ["all"] = true };
            PausePointResponse response = (PausePointResponse)await tool.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.Warning, Is.EqualTo(SourcePausePointConstants.ClearResumedPlayModeWarning));
        }

        [Test]
        public async Task Enable_WhenMarkerCreated_ReturnsStateManagementFields()
        {
            // Verifies the public enable-pause-point tool exposes timing and generation fields from the registry.
            PausePointResponse response = await EnablePausePointAsync("jump");

            Assert.That(response.EnabledAtUtc, Is.EqualTo("2026-06-03T00:00:00.0000000Z"));
            Assert.That(response.RemainingMilliseconds, Is.EqualTo(30000));
            Assert.That(response.Generation, Is.EqualTo(1));
            Assert.That(response.Expired, Is.False);
            // An id-only marker has no resolved source line, so no pre-line timing note applies.
            Assert.That(response.SnapshotTiming, Is.Empty);
            Assert.That(response.EditorState.CapturedAt, Is.EqualTo(UloopPausePointEditorStateCapturedAt.Current));
            Assert.That(response.RecommendedNextAction, Is.Empty);
        }

        [Test]
        public async Task Enable_WhenMarkerCreated_EmitsPausePointEnableVibeLog()
        {
            // Verifies enable-pause-point records a pause_point_enable observability event.
            VibeLogger.ClearMemoryLogs();

            await EnablePausePointAsync("jump");

            string logs = VibeLogger.GetLogsForAi("pause_point_enable");
            Assert.That(logs, Does.Contain("pause_point_enable"));
            Assert.That(logs, Does.Contain("\"Id\": \"jump\""));
        }

        [Test]
        public async Task Clear_WhenMarkerCleared_EmitsPausePointClearedVibeLog()
        {
            // Verifies clear-pause-point records a pause_point_cleared observability event.
            await EnablePausePointAsync("jump");
            VibeLogger.ClearMemoryLogs();

            ClearPausePointTool tool = new();
            JObject parameters = new() { ["id"] = "jump" };
            await tool.ExecuteAsync(parameters, CancellationToken.None);

            string logs = VibeLogger.GetLogsForAi("pause_point_cleared");
            Assert.That(logs, Does.Contain("pause_point_cleared"));
            Assert.That(logs, Does.Contain("\"Target\": \"jump\""));
        }

        [Test]
        public async Task Clear_WhenMarkerExpired_EmitsPausePointExpiredVibeLog()
        {
            // Verifies clear-pause-point on an already-expired marker records a pause_point_expired
            // observability event alongside pause_point_cleared.
            await EnablePausePointAsync("jump");
            _nowUtc = _nowUtc.AddSeconds(31);
            VibeLogger.ClearMemoryLogs();

            ClearPausePointTool tool = new();
            JObject parameters = new() { ["id"] = "jump" };
            await tool.ExecuteAsync(parameters, CancellationToken.None);

            string logs = VibeLogger.GetLogsForAi("pause_point_expired");
            Assert.That(logs, Does.Contain("pause_point_expired"));
            Assert.That(logs, Does.Contain("\"Id\": \"jump\""));
        }

        [Test]
        public async Task Clear_WhenPhysicsFlaggedMarkerClearedWhileEnabled_EmitsClearedWithoutHitPhysicsDiagnostics()
        {
            // Verifies a physics-flagged marker cleared with HitCount==0 while still Enabled
            // (the CLI await timeout expiring before the marker's own longer expiry, the actual
            // 2026-07-22 Block.cs:29 field incident) still emits the physics dispatch diagnostics,
            // not just the Expired case.
            PausePointResponse enableResponse = await EnablePausePointByFileLineAsync(PhysicsFixtureFilePath, PhysicsFixtureLine);
            Assert.That(enableResponse.Success, Is.True);
            VibeLogger.ClearMemoryLogs();

            ClearPausePointTool tool = new();
            JObject parameters = new() { ["id"] = enableResponse.Id };
            await tool.ExecuteAsync(parameters, CancellationToken.None);

            string logs = VibeLogger.GetLogsForAi("pause_point_cleared_without_hit_physics");
            Assert.That(logs, Does.Contain("pause_point_cleared_without_hit_physics"));
            Assert.That(logs, Does.Contain($"\"Id\": \"{enableResponse.Id}\""));
            Assert.That(logs, Does.Contain("\"StatusBeforeClear\": \"Enabled\""));
        }

        [Test]
        public async Task Clear_WhenPhysicsFlaggedMarkerExpiredWithoutHit_EmitsClearedWithoutHitPhysicsDiagnostics()
        {
            // Verifies the pre-existing expired-without-hit case still fires diagnostics under the
            // unified operation name, with StatusBeforeClear reporting Expired.
            EnablePausePointTool enableTool = new();
            JObject enableParameters = new()
            {
                ["file"] = PhysicsFixtureFilePath,
                ["line"] = PhysicsFixtureLine,
                ["timeoutSeconds"] = 1
            };
            PausePointResponse enableResponse = (PausePointResponse)await enableTool.ExecuteAsync(enableParameters, CancellationToken.None);
            Assert.That(enableResponse.Success, Is.True);
            _nowUtc = _nowUtc.AddSeconds(2);
            VibeLogger.ClearMemoryLogs();

            ClearPausePointTool clearTool = new();
            JObject clearParameters = new() { ["id"] = enableResponse.Id };
            await clearTool.ExecuteAsync(clearParameters, CancellationToken.None);

            string logs = VibeLogger.GetLogsForAi("pause_point_cleared_without_hit_physics");
            Assert.That(logs, Does.Contain("pause_point_cleared_without_hit_physics"));
            Assert.That(logs, Does.Contain($"\"Id\": \"{enableResponse.Id}\""));
            Assert.That(logs, Does.Contain("\"StatusBeforeClear\": \"Expired\""));
        }

        [Test]
        public async Task ClearAll_WhenPhysicsFlaggedMarkerEnabledWithoutHit_EmitsClearedWithoutHitPhysicsDiagnostics()
        {
            // Verifies the --all clear path also broadens the diagnostics condition beyond Expired.
            PausePointResponse enableResponse = await EnablePausePointByFileLineAsync(PhysicsFixtureFilePath, PhysicsFixtureLine);
            Assert.That(enableResponse.Success, Is.True);
            VibeLogger.ClearMemoryLogs();

            ClearPausePointTool tool = new();
            JObject parameters = new() { ["all"] = true };
            await tool.ExecuteAsync(parameters, CancellationToken.None);

            string logs = VibeLogger.GetLogsForAi("pause_point_cleared_without_hit_physics");
            Assert.That(logs, Does.Contain("pause_point_cleared_without_hit_physics"));
            Assert.That(logs, Does.Contain($"\"Id\": \"{enableResponse.Id}\""));
            Assert.That(logs, Does.Contain("\"StatusBeforeClear\": \"Enabled\""));
        }

        [Test]
        public async Task Clear_WhenPhysicsFlaggedMarkerClearedViaStatusBridge_EmitsClearedWithoutHitPhysicsDiagnostics()
        {
            // Verifies the bridge clear path (PausePointStatusBridgeCommand.Clear, used by
            // await-pause-point's self-timeout auto-clear) emits the same zero-hit physics
            // diagnostic as the direct tool clear path. The field incident that motivated this
            // diagnostic (Block.cs:29, 2026-07-22) is itself cleared through this bridge, not
            // PausePointUseCase.Clear, so the bridge path must not stay silent.
            PausePointResponse enableResponse = await EnablePausePointByFileLineAsync(PhysicsFixtureFilePath, PhysicsFixtureLine);
            Assert.That(enableResponse.Success, Is.True);
            VibeLogger.ClearMemoryLogs();

            JObject bridgeParameters = new() { ["Id"] = enableResponse.Id };
            PausePointStatusBridgeCommand.Clear(bridgeParameters);

            string logs = VibeLogger.GetLogsForAi("pause_point_cleared_without_hit_physics");
            Assert.That(logs, Does.Contain("pause_point_cleared_without_hit_physics"));
            Assert.That(logs, Does.Contain($"\"Id\": \"{enableResponse.Id}\""));
            Assert.That(logs, Does.Contain("\"StatusBeforeClear\": \"Enabled\""));
        }

        [Test]
        public async Task ClearAll_WhenPhysicsFlaggedMarkerAlreadyClearedViaStatusBridge_DoesNotLogStaleDiagnostics()
        {
            // Verifies the bridge clear path logs the zero-hit physics diagnostic exactly once
            // (through the shared OnClearResolved registry hook, which also removes the id from
            // PhysicsFlaggedDeclaringTypesById), and that a later clear --all does not re-log it
            // for the same id: the dictionary no longer carries a stale entry once the hook has
            // fired for it.
            PausePointResponse enableResponse = await EnablePausePointByFileLineAsync(PhysicsFixtureFilePath, PhysicsFixtureLine);
            Assert.That(enableResponse.Success, Is.True);
            VibeLogger.ClearMemoryLogs();

            JObject bridgeParameters = new() { ["Id"] = enableResponse.Id };
            PausePointStatusBridgeCommand.Clear(bridgeParameters);

            string logsAfterBridgeClear = VibeLogger.GetLogsForAi("pause_point_cleared_without_hit_physics");
            Assert.That(logsAfterBridgeClear, Does.Contain("pause_point_cleared_without_hit_physics"));
            VibeLogger.ClearMemoryLogs();

            ClearPausePointTool tool = new();
            JObject parameters = new() { ["all"] = true };
            await tool.ExecuteAsync(parameters, CancellationToken.None);

            string logsAfterClearAll = VibeLogger.GetLogsForAi("pause_point_cleared_without_hit_physics");
            Assert.That(logsAfterClearAll, Does.Not.Contain("pause_point_cleared_without_hit_physics"));
        }

        [Test]
        public void PausePointStatusBridge_WhenMarkerExpired_ReturnsRecoveryAction()
        {
            // Verifies pause-point-status exposes enough data to re-arm an expired marker without guesswork.
            UloopPausePointRegistry.Enable("jump", 1);
            _nowUtc = _nowUtc.AddSeconds(2);
            JObject parameters = new()
            {
                ["id"] = "jump"
            };

            PausePointStatusResponse response = PausePointStatusBridgeCommand.Execute(parameters);

            Assert.That(response.Expired, Is.True);
            Assert.That(response.EnabledAtUtc, Is.EqualTo("2026-06-03T00:00:00.0000000Z"));
            Assert.That(response.RemainingMilliseconds, Is.EqualTo(0));
            Assert.That(response.Generation, Is.EqualTo(1));
            Assert.That(response.EditorState.CapturedAt, Is.EqualTo(UloopPausePointEditorStateCapturedAt.Current));
            Assert.That(
                response.RecommendedNextAction,
                Is.EqualTo("Re-enable the marker with a longer --timeout-seconds and trigger the code path again; clearing the expired marker first is not required."));
        }

        [Test]
        public void PausePointStatusBridge_WhenPausePointHitWithCapturedVariables_ReturnsCapturedVariables()
        {
            // Verifies the CLI status bridge surfaces captured variables and the truncated flag
            // from the registry snapshot, not just the marker/hit bookkeeping fields.
            UloopPausePointRegistry.Enable("jump", 30);
            UloopCapturedVariable[] capturedVariables =
            {
                new("speed", UloopCapturedVariableScope.Local, "System.Int32", "5", string.Empty, string.Empty, 0, false)
            };
            UloopPausePointRegistry.HitWithCapturedVariables("jump", capturedVariables, true);
            JObject parameters = new() { ["id"] = "jump" };

            PausePointStatusResponse response = PausePointStatusBridgeCommand.Execute(parameters);

            Assert.That(response.CapturedVariablesTruncated, Is.True);
            Assert.That(response.CapturedVariables.Select(v => v.Name), Is.EquivalentTo(new[] { "speed" }));
            Assert.That(response.CapturedVariables[0].Value, Is.EqualTo("5"));
        }

        [Test]
        public void PausePointStatusBridge_Extend_PushesExpiryToRequestedMinimumRemaining()
        {
            // Verifies the internal extend-pause-point-status bridge command reaches the registry
            // extension so a slow await-pause-point round trip can push a marker's deadline out.
            UloopPausePointRegistry.Enable("jump", 10);
            _nowUtc = _nowUtc.AddSeconds(5);
            JObject parameters = new()
            {
                ["id"] = "jump",
                ["minimumRemainingSeconds"] = 30
            };

            PausePointStatusResponse response = PausePointStatusBridgeCommand.Extend(parameters);

            Assert.That(response.RemainingMilliseconds, Is.EqualTo(30000));
        }

        [Test]
        public void Enable_WhenSamePausePointWasHit_ClearsLatestHitSnapshot()
        {
            // Verifies re-enabling a marker does not leave stale hit details for input tools.
            UloopPausePointRegistry.Enable("jump", 30);
            UloopPausePoint.Pause("jump");

            UloopPausePointRegistry.Enable("jump", 30);

            Assert.That(UloopPausePointRegistry.GetLatestHitSnapshot(), Is.Null);
        }

        [Test]
        public void ClearAll_WhenPausePointWasHit_ClearsTerminalStatus()
        {
            // Verifies bulk clear hides stale terminal hit status from future waits.
            UloopPausePointRegistry.Enable("jump", 30);
            UloopPausePoint.Pause("jump");

            UloopPausePointClearAllResult result = UloopPausePointRegistry.ClearAll();
            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus("jump");

            Assert.That(result.ClearedCount, Is.EqualTo(1));
            Assert.That(snapshot.Status, Is.EqualTo(UloopPausePointStatus.Cleared));
            Assert.That(snapshot.IsHit, Is.False);
            Assert.That(UloopPausePointRegistry.GetLatestHitSnapshot(), Is.Null);
        }

        [Test]
        public async Task Enable_WhenIdIsEmpty_ReturnsValidationFailureResponse()
        {
            // Verifies empty id surfaces as a Success=false response instead of a JSON-RPC error.
            EnablePausePointTool tool = new();
            JObject parameters = new()
            {
                ["id"] = string.Empty,
                ["timeoutSeconds"] = 30
            };

            PausePointResponse response = (PausePointResponse)await tool.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.Message, Is.EqualTo("Id must not be null or empty."));
        }

        [Test]
        public async Task Enable_WhenTimeoutSecondsIsZero_ReturnsValidationFailureResponse()
        {
            // Verifies non-positive TimeoutSeconds surface as a Success=false response instead of a JSON-RPC error.
            EnablePausePointTool tool = new();
            JObject parameters = new()
            {
                ["id"] = "jump",
                ["timeoutSeconds"] = 0
            };

            PausePointResponse response = (PausePointResponse)await tool.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.Message, Is.EqualTo("TimeoutSeconds must be greater than zero."));
        }

        [Test]
        public async Task Clear_WhenIdIsEmptyAndAllIsFalse_ReturnsValidationFailureResponse()
        {
            // Verifies empty id on clear-pause-point surfaces as a Success=false response.
            ClearPausePointTool tool = new();
            JObject parameters = new()
            {
                ["id"] = string.Empty,
                ["all"] = false
            };

            PausePointResponse response = (PausePointResponse)await tool.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.Message, Is.EqualTo("Id must not be null or empty."));
        }

        [Test]
        public void HitWithCapturedVariables_WhenPausePointIsEnabled_StoresCapturedVariablesInSnapshot()
        {
            // Verifies the source-pause-point hit path threads captured variables through to the snapshot.
            UloopPausePointRegistry.Enable("jump", 30);
            UloopCapturedVariable[] capturedVariables =
            {
                new("speed", UloopCapturedVariableScope.Local, "System.Int32", "5", string.Empty, string.Empty, 0, false)
            };

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.HitWithCapturedVariables(
                "jump", capturedVariables, true);

            Assert.That(snapshot.CapturedVariables, Is.EqualTo(capturedVariables));
            Assert.That(snapshot.CapturedVariablesTruncated, Is.True);
            Assert.That(_pauseController.PauseCount, Is.EqualTo(1));
        }

        /// <summary>
        /// What: caller frames passed to HitWithCapturedFrame appear on the latest snapshot.
        /// </summary>
        [Test]
        public void HitWithCapturedFrame_WhenCallerFramesAreProvided_StoresThemOnLatestSnapshot()
        {
            UloopPausePointRegistry.Enable("jump", 30);
            UloopPausePointCapturedVariableFrame frame = CreateEmptyCapturedFrame();
            UloopPausePointCallerFrame[] callerFrames =
            {
                new("Game.Input.HandleJump", "Assets/Scripts/Input.cs", 10),
                new("Game.Player.Update", "Assets/Scripts/Player.cs", 20),
            };

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.HitWithCapturedFrame(
                "jump", frame, Array.Empty<UloopCapturedVariable>(), false, callerFrames);

            Assert.That(snapshot.CallerFrames, Is.EqualTo(callerFrames));
        }

        /// <summary>
        /// What: each history frame stores the caller frames from that hit, not only the latest.
        /// </summary>
        [Test]
        public void HitWithCapturedFrame_WhenMultipleHitsAreRecorded_StoresCallerFramesOnEachHistoryFrame()
        {
            UloopPausePointRegistry.Enable("jump", 30, UloopPausePointCaptureMode.Trace);
            UloopPausePointCapturedVariableFrame frame = CreateEmptyCapturedFrame();
            UloopPausePointCallerFrame[] firstCallerFrames =
            {
                new("Game.Input.HandleJump", "Assets/Scripts/Input.cs", 10),
            };
            UloopPausePointCallerFrame[] secondCallerFrames =
            {
                new("Game.AI.Tick", "Assets/Scripts/AI.cs", 44),
                new("Game.World.Update", "Assets/Scripts/World.cs", 8),
            };

            UloopPausePointRegistry.HitWithCapturedFrame(
                "jump", frame, Array.Empty<UloopCapturedVariable>(), false, firstCallerFrames);
            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.HitWithCapturedFrame(
                "jump", frame, Array.Empty<UloopCapturedVariable>(), false, secondCallerFrames);

            Assert.That(snapshot.CallerFrames, Is.EqualTo(secondCallerFrames));
            Assert.That(snapshot.CapturedVariableHistory, Has.Count.EqualTo(2));
            Assert.That(snapshot.CapturedVariableHistory[0].CallerFrames, Is.EqualTo(firstCallerFrames));
            Assert.That(snapshot.CapturedVariableHistory[1].CallerFrames, Is.EqualTo(secondCallerFrames));
        }

        /// <summary>
        /// What: Hit(string) records an empty caller-frame list because that path has no stack capture.
        /// </summary>
        [Test]
        public void Hit_WhenCalledWithoutCallerFrames_ReportsEmptyCallerFrames()
        {
            UloopPausePointRegistry.Enable("jump", 30);

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.Hit("jump");

            Assert.That(snapshot.CallerFrames, Is.Empty);
        }

        /// <summary>
        /// What: HitWithCapturedVariables records an empty caller-frame list because that path
        /// has no stack capture.
        /// </summary>
        [Test]
        public void HitWithCapturedVariables_WhenCalledWithoutCallerFrames_ReportsEmptyCallerFrames()
        {
            UloopPausePointRegistry.Enable("jump", 30);
            UloopCapturedVariable[] capturedVariables =
            {
                new("speed", UloopCapturedVariableScope.Local, "System.Int32", "5", string.Empty, string.Empty, 0, false)
            };

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.HitWithCapturedVariables(
                "jump", capturedVariables, false);

            Assert.That(snapshot.CallerFrames, Is.Empty);
        }

        /// <summary>
        /// What: PausePointStatusResponse maps caller frames onto both the latest capture and
        /// each history frame.
        /// </summary>
        [Test]
        public void StatusFromSnapshot_WhenCallerFramesArePresent_MapsTopLevelAndHistory()
        {
            UloopPausePointRegistry.Enable("jump", 30);
            UloopPausePointCallerFrame[] callerFrames =
            {
                new("Game.Input.HandleJump", "Assets/Scripts/Input.cs", 10),
            };
            UloopPausePointRegistry.HitWithCapturedFrame(
                "jump",
                CreateEmptyCapturedFrame(),
                Array.Empty<UloopCapturedVariable>(),
                false,
                callerFrames);

            PausePointStatusResponse response =
                PausePointStatusResponse.FromSnapshot(UloopPausePointRegistry.GetStatus("jump"));

            Assert.That(response.CallerFrames, Has.Count.EqualTo(1));
            Assert.That(response.CallerFrames[0].Method, Is.EqualTo("Game.Input.HandleJump"));
            Assert.That(response.CallerFrames[0].File, Is.EqualTo("Assets/Scripts/Input.cs"));
            Assert.That(response.CallerFrames[0].Line, Is.EqualTo(10));
            Assert.That(response.CapturedVariableHistory, Has.Count.EqualTo(1));
            Assert.That(response.CapturedVariableHistory[0].CallerFrames, Has.Count.EqualTo(1));
            Assert.That(
                response.CapturedVariableHistory[0].CallerFrames[0].Method,
                Is.EqualTo("Game.Input.HandleJump"));
        }

        /// <summary>
        /// What: PausePointResponse maps caller frames onto history frames only (no top-level
        /// CallerFrames on the enable/clear payload).
        /// </summary>
        [Test]
        public void FromSnapshot_WhenCallerFramesArePresent_MapsHistoryFramesOnly()
        {
            UloopPausePointRegistry.Enable("jump", 30);
            UloopPausePointCallerFrame[] callerFrames =
            {
                new("Game.Input.HandleJump", "Assets/Scripts/Input.cs", 10),
            };
            UloopPausePointRegistry.HitWithCapturedFrame(
                "jump",
                CreateEmptyCapturedFrame(),
                Array.Empty<UloopCapturedVariable>(),
                false,
                callerFrames);

            PausePointResponse response =
                PausePointResponse.FromSnapshot(UloopPausePointRegistry.GetStatus("jump"));

            Assert.That(response.CapturedVariableHistory, Has.Count.EqualTo(1));
            Assert.That(response.CapturedVariableHistory[0].CallerFrames, Has.Count.EqualTo(1));
            Assert.That(
                response.CapturedVariableHistory[0].CallerFrames[0].Method,
                Is.EqualTo("Game.Input.HandleJump"));
            Assert.That(response.CapturedVariableHistory[0].CallerFrames[0].File, Is.EqualTo("Assets/Scripts/Input.cs"));
            Assert.That(response.CapturedVariableHistory[0].CallerFrames[0].Line, Is.EqualTo(10));
        }

        [Test]
        public void TryGetCapturedValue_WhenLatestHitStoredRawFrame_ReturnsLiveReferences()
        {
            // Verifies raw capture exposes live objects for the latest hit only.
            UloopPausePointRegistry.Enable("jump", 30);
            List<int> scores = new() { 10, 20, 30 };
            UloopPausePointCapturedVariableFrame frame = new(
                new[]
                {
                    new UloopPausePointCapturedVariableEntry("scores", UloopCapturedVariableScope.Local, scores),
                    new UloopPausePointCapturedVariableEntry("empty", UloopCapturedVariableScope.Local, null)
                },
                false,
                System.Array.Empty<string>(),
                0);
            UloopCapturedVariable[] capturedVariables =
            {
                new("scores", UloopCapturedVariableScope.Local, "System.Collections.Generic.List`1[System.Int32]", "[10,20,30]", string.Empty, string.Empty, 0, false)
            };

            UloopPausePointRegistry.HitWithCapturedFrame(
                "jump", frame, capturedVariables, false, Array.Empty<UloopPausePointCallerFrame>());

            (bool foundScores, object scoresValue) = UloopPausePoint.TryGetCapturedValue("scores");
            (bool foundNull, object nullValue) = UloopPausePoint.TryGetCapturedValue("empty");
            (bool foundMissing, object missingValue) = UloopPausePoint.TryGetCapturedValue("missing");

            Assert.That(foundScores, Is.True);
            Assert.That(scoresValue, Is.SameAs(scores));
            Assert.That(foundNull, Is.True);
            Assert.That(nullValue, Is.Null);
            Assert.That(foundMissing, Is.False);
            Assert.That(missingValue, Is.Null);
            Assert.That(UloopPausePoint.GetCapturedPausePointId(), Is.EqualTo("jump"));
            Assert.That(UloopPausePoint.GetCapturedNames(), Is.EqualTo(new[] { "scores", "empty" }));
        }

        [Test]
        public void TryGetCapturedValue_WhenRegistryClearsLatestHit_ReturnsNotFound()
        {
            // Verifies clear and reset paths drop raw references instead of leaving stale handles.
            UloopPausePointRegistry.Enable("jump", 30);
            UloopPausePointCapturedVariableFrame frame = new(
                new[] { new UloopPausePointCapturedVariableEntry("speed", UloopCapturedVariableScope.Local, 5) },
                false,
                System.Array.Empty<string>(),
                0);
            UloopPausePointRegistry.HitWithCapturedFrame(
                "jump", frame, Array.Empty<UloopCapturedVariable>(), false, Array.Empty<UloopPausePointCallerFrame>());

            UloopPausePointRegistry.Clear("jump");

            (bool found, object value) = UloopPausePoint.TryGetCapturedValue("speed");
            Assert.That(found, Is.False);
            Assert.That(value, Is.Null);
            Assert.That(UloopPausePoint.GetCapturedPausePointId(), Is.Empty);
        }

        [Test]
        public void TryGetCapturedValue_WhenNewHitReplacesPrevious_ExposesLatestSnapshotOnly()
        {
            // Verifies only the latest hit snapshot is held, matching _latestHitSnapshot semantics.
            UloopPausePointRegistry.Enable("jump", 30);
            UloopPausePointRegistry.Enable("land", 30);
            UloopPausePointCapturedVariableFrame jumpFrame = new(
                new[] { new UloopPausePointCapturedVariableEntry("speed", UloopCapturedVariableScope.Local, 1) },
                false,
                System.Array.Empty<string>(),
                0);
            UloopPausePointCapturedVariableFrame landFrame = new(
                new[] { new UloopPausePointCapturedVariableEntry("speed", UloopCapturedVariableScope.Local, 2) },
                false,
                System.Array.Empty<string>(),
                0);

            UloopPausePointRegistry.HitWithCapturedFrame("jump", jumpFrame, Array.Empty<UloopCapturedVariable>(), false, Array.Empty<UloopPausePointCallerFrame>());
            UloopPausePointRegistry.HitWithCapturedFrame("land", landFrame, Array.Empty<UloopCapturedVariable>(), false, Array.Empty<UloopPausePointCallerFrame>());

            (bool found, object value) = UloopPausePoint.TryGetCapturedValue("speed");
            Assert.That(found, Is.True);
            Assert.That(value, Is.EqualTo(2));
            Assert.That(UloopPausePoint.GetCapturedPausePointId(), Is.EqualTo("land"));
        }

        [Test]
        public void TryGetCapturedValue_WhenUnrelatedPausePointIsCleared_KeepsLatestHitRawCapture()
        {
            // Verifies Clear(id) only drops raw refs when id matches the latest hit snapshot.
            UloopPausePointRegistry.Enable("jump", 30);
            UloopPausePointRegistry.Enable("land", 30);
            UloopPausePointCapturedVariableFrame landFrame = new(
                new[] { new UloopPausePointCapturedVariableEntry("speed", UloopCapturedVariableScope.Local, 7) },
                false,
                System.Array.Empty<string>(),
                0);
            UloopPausePointRegistry.HitWithCapturedFrame("land", landFrame, Array.Empty<UloopCapturedVariable>(), false, Array.Empty<UloopPausePointCallerFrame>());

            UloopPausePointRegistry.Clear("jump");

            (bool found, object value) = UloopPausePoint.TryGetCapturedValue("speed");
            Assert.That(found, Is.True);
            Assert.That(value, Is.EqualTo(7));
            Assert.That(UloopPausePoint.GetCapturedPausePointId(), Is.EqualTo("land"));
        }

        [Test]
        public void TryGetCapturedValue_WhenSamePausePointIsReenabledWhilePaused_KeepsLatestHitRawCapture()
        {
            // Verifies re-enabling the same id while still paused (e.g. to refresh its timeout
            // during a step session) keeps the held raw capture instead of clearing it.
            UloopPausePointRegistry.Enable("jump", 30);
            UloopPausePointCapturedVariableFrame frame = new(
                new[] { new UloopPausePointCapturedVariableEntry("speed", UloopCapturedVariableScope.Local, 1) },
                false,
                System.Array.Empty<string>(),
                0);
            UloopPausePointRegistry.HitWithCapturedFrame("jump", frame, Array.Empty<UloopCapturedVariable>(), false, Array.Empty<UloopPausePointCallerFrame>());

            UloopPausePointRegistry.Enable("jump", 30);

            (bool found, object value) = UloopPausePoint.TryGetCapturedValue("speed");
            Assert.That(found, Is.True);
            Assert.That(value, Is.EqualTo(1));
            Assert.That(UloopPausePoint.GetCapturedPausePointId(), Is.EqualTo("jump"));
        }

        [Test]
        public void TryGetCapturedValue_WhenSamePausePointIsReenabledThenCleared_StillClearsRawCapture()
        {
            // Verifies Clear(id) after a same-id re-enable still drops the raw capture holder,
            // even though the re-enable already reset the hit snapshot bookkeeping.
            UloopPausePointRegistry.Enable("jump", 30);
            UloopPausePointCapturedVariableFrame frame = new(
                new[] { new UloopPausePointCapturedVariableEntry("speed", UloopCapturedVariableScope.Local, 1) },
                false,
                System.Array.Empty<string>(),
                0);
            UloopPausePointRegistry.HitWithCapturedFrame("jump", frame, Array.Empty<UloopCapturedVariable>(), false, Array.Empty<UloopPausePointCallerFrame>());

            UloopPausePointRegistry.Enable("jump", 30);
            UloopPausePointRegistry.Clear("jump");

            (bool found, object value) = UloopPausePoint.TryGetCapturedValue("speed");
            Assert.That(found, Is.False);
            Assert.That(value, Is.Null);
            Assert.That(UloopPausePoint.GetCapturedPausePointId(), Is.Empty);
        }

        [Test]
        public void Hit_WhenPausePointIsEnabled_ReportsEmptyCapturedVariables()
        {
            // Verifies the plain marker path (no source pause point) reports an empty, non-null list.
            UloopPausePointRegistry.Enable("jump", 30);

            UloopPausePoint.Pause("jump");

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus("jump");
            Assert.That(snapshot.CapturedVariables, Is.Empty);
            Assert.That(snapshot.CapturedVariablesTruncated, Is.False);
        }

        [Test]
        public void IsArmed_WhenPausePointIsEnabled_ReturnsTrue()
        {
            // Verifies the injected Capture code's fast path recognizes an armed marker.
            UloopPausePointRegistry.Enable("jump", 30);

            Assert.That(UloopPausePointRegistry.IsArmed("jump"), Is.True);
        }

        [Test]
        public void IsArmed_WhenPausePointIsNotEnabled_ReturnsFalse()
        {
            // Verifies the injected Capture code's fast path no-ops for an id that was never enabled.
            Assert.That(UloopPausePointRegistry.IsArmed("jump"), Is.False);
        }

        [Test]
        public void IsArmed_WhenPausePointWasAlreadyHit_ReturnsFalse()
        {
            // Verifies a one-shot marker disarms itself so a second pass through the same line no-ops.
            UloopPausePointRegistry.Enable("jump", 30);
            UloopPausePoint.Pause("jump");

            Assert.That(UloopPausePointRegistry.IsArmed("jump"), Is.False);
        }

        [Test]
        public void PauseMethod_WhenSourceIsScanned_UsesUnityEditorConditionalWithoutDebugBreak()
        {
            // Verifies the public marker follows Unity's conditional call-site removal pattern.
            string sourcePath = Path.Combine(
                Directory.GetCurrentDirectory(),
                "Packages/src/Runtime/PausePoints/UloopPausePoint.cs");
            string source = File.ReadAllText(sourcePath);

            Assert.That(source, Does.Contain("[Conditional(\"UNITY_EDITOR\")]"));
            Assert.That(source, Does.Contain("public static void Pause(string id)"));
            Assert.That(source, Does.Not.Contain("Debug.Break"));
        }

        [Test]
        public async Task Enable_WhenPlayModeInactiveAndDomainReloadEnabled_ReturnsWarning()
        {
            // Verifies PlayMode entry risk is reported only when Domain Reload can clear the marker.
            EditorSettings.enterPlayModeOptionsEnabled = false;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.None;

            PausePointResponse response = await EnablePausePointAsync("jump");

            Assert.That(response.Warning, Does.Contain("Domain Reload is enabled"));
            Assert.That(response.Warning, Does.Contain("keep Domain Reload disabled"));
        }

        [Test]
        public async Task Enable_WhenPlayModeInactiveAndDomainReloadDisabled_ReturnsNoWarning()
        {
            // Verifies the normal no-domain-reload workflow does not suggest re-arming after Play starts.
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;

            PausePointResponse response = await EnablePausePointAsync("dash");

            Assert.That(response.Warning, Is.Empty);
        }

        // NOTE: Enabling by File/Line is rejected in Debug-only when
        // CompilationPipeline.codeOptimization == CodeOptimization.Release. There is no seam to
        // fake that Editor-global static property in an EditMode test, and flipping it for real
        // would trigger a recompilation mid-test (forbidden by this repo's Unity Freeze Prevention
        // guardrails). This branch is verified manually/E2E instead (see PR 6).

        [Test]
        public async Task Enable_WhenFileAndLineResolveToRealMethod_PatchesAndCapturesVariablesOnHit()
        {
            // Verifies the File/Line path resolves a real fixture method, patches it via Harmony
            // through the full public tool surface, and a subsequent call to the patched method
            // hits the registry with its locals, parameters, the synthetic "this" entry, and
            // instance field captured.
            PausePointResponse response = await EnablePausePointByFileLineAsync(FixtureFilePath, FixtureLine);

            Assert.That(response.Success, Is.True);
            Assert.That(response.Id, Is.EqualTo($"{FixtureFilePath}:{FixtureLine}"));
            Assert.That(response.ResolvedLine, Is.EqualTo(FixtureLine));
            Assert.That(response.ResolvedLineText, Is.EqualTo("return sum;"));
            Assert.That(response.ResolvedMethod, Does.Contain("Add"));
            Assert.That(response.SnapshotTiming, Is.EqualTo(SourcePausePointConstants.PreLineSnapshotTimingNote));

            EnableBySourceLocationFixture fixture = new();
            int sum = fixture.Add(2, 3);

            Assert.That(sum, Is.EqualTo(5));
            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus(response.Id);
            Assert.That(snapshot.IsHit, Is.True);
            Assert.That(
                snapshot.CapturedVariables.Select(v => v.Name),
                Is.EquivalentTo(new[] { "left", "right", "sum", "this", "Tag" }));
        }

        /// <summary>
        /// What: an enable failure response carries the live editor state instead of a
        /// zero-filled default (IsPlaying/CapturedAt must flow from the registry's pause controller).
        /// </summary>
        [Test]
        public void Enable_UnresolvableFile_CarriesLiveEditorState()
        {
            PausePointUseCase useCase = new();
            PausePointResponse response = useCase.Enable(new EnablePausePointSchema
            {
                File = "Assets/UloopNoSuchFileForEditorStateTest.cs",
                Line = 1
            });

            Assert.That(response.Success, Is.False);
            Assert.That(response.EditorState.CapturedAt, Is.EqualTo(UloopPausePointEditorStateCapturedAt.Current),
                "Error responses must capture the editor state instead of returning the zero-filled default.");
            Assert.That(response.EditorState.IsPlaying, Is.True,
                "SetUp's fake controller pins IsPlaying to true, so a zero-filled default (false) is detectable.");
        }

        [Test]
        public async Task Enable_WhenIdAndFileBothProvided_ReturnsValidationFailureResponse()
        {
            // Verifies Id and File/Line are mutually exclusive.
            EnablePausePointTool tool = new();
            JObject parameters = new()
            {
                ["id"] = "jump",
                ["file"] = FixtureFilePath,
                ["line"] = FixtureLine,
                ["timeoutSeconds"] = 30
            };

            PausePointResponse response = (PausePointResponse)await tool.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.Message, Is.EqualTo("Specify either Id or File and Line, not both."));
        }

        [Test]
        public async Task Enable_WhenFileProvidedWithoutLine_ReturnsValidationFailureResponse()
        {
            // Verifies File requires Line to be provided together.
            EnablePausePointTool tool = new();
            JObject parameters = new()
            {
                ["file"] = FixtureFilePath,
                ["timeoutSeconds"] = 30
            };

            PausePointResponse response = (PausePointResponse)await tool.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.Message, Is.EqualTo("File and Line must both be provided together."));
        }

        [Test]
        public async Task Enable_WhenLineProvidedWithoutFile_ReturnsValidationFailureResponse()
        {
            // Verifies Line requires File to be provided together.
            EnablePausePointTool tool = new();
            JObject parameters = new()
            {
                ["line"] = FixtureLine,
                ["timeoutSeconds"] = 30
            };

            PausePointResponse response = (PausePointResponse)await tool.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.Message, Is.EqualTo("File and Line must both be provided together."));
        }

        [Test]
        public async Task Enable_WhenLineHasNoSequencePoint_ReturnsResolverErrorAsValidationFailure()
        {
            // Verifies a line with no sequence point on or after it (deliberately far past the
            // fixture file's end) surfaces the Resolver's error message as a Success=false
            // response instead of throwing.
            EnablePausePointTool tool = new();
            JObject parameters = new()
            {
                ["file"] = FixtureFilePath,
                ["line"] = 9999,
                ["timeoutSeconds"] = 30
            };

            PausePointResponse response = (PausePointResponse)await tool.ExecuteAsync(parameters, CancellationToken.None);

            Assert.That(response.Success, Is.False);
            Assert.That(response.Message, Does.Contain("No sequence point found on or after line"));
        }

        [Test]
        public async Task Clear_WhenSpecificIdCleared_CallsPatcherUnpatchSoTheIdCanBeFreshlyRePatched()
        {
            // Verifies PausePointUseCase.Clear actually calls SourcePausePointPatcher.Unpatch (not
            // just the registry): after clearing, re-Patch-ing the same id with a deliberately
            // stale Mvid must reach the Patcher's stale-assembly gate again, which only runs when
            // the id is no longer in the Patcher's ledger (an "already patched" id short-circuits
            // before that gate ever runs, per SourcePausePointPatcherTests coverage from PR 4).
            SourcePausePointResolveResult resolveResult = SourcePausePointResolver.Resolve(FixtureFilePath, FixtureLine);
            Assert.That(resolveResult.Success, Is.True);
            string id = $"{FixtureFilePath}:{FixtureLine}";

            UloopPausePointRegistry.Enable(id, 30);
            Assert.That(SourcePausePointPatcher.Patch(id, resolveResult.Resolution).Success, Is.True);

            ClearPausePointTool clearTool = new();
            JObject clearParameters = new() { ["id"] = id, ["all"] = false };
            await clearTool.ExecuteAsync(clearParameters, CancellationToken.None);

            SourcePausePointPatchResult rePatchResult = SourcePausePointPatcher.Patch(id, WithStaleMvid(resolveResult.Resolution));

            Assert.That(rePatchResult.Success, Is.False);
            Assert.That(rePatchResult.FailureReason, Is.EqualTo(SourcePausePointPatchFailureReason.StaleAssembly));
        }

        [Test]
        public async Task ClearAll_WhenSourcePausePointsExist_CallsPatcherUnpatchAllSoIdsCanBeFreshlyRePatched()
        {
            // Verifies PausePointUseCase.Clear(All) calls SourcePausePointPatcher.UnpatchAll,
            // using the same stale-Mvid gate signal as the --id case above to prove the ledger
            // entry was actually removed rather than only clearing the registry.
            SourcePausePointResolveResult resolveResult = SourcePausePointResolver.Resolve(FixtureFilePath, FixtureLine);
            Assert.That(resolveResult.Success, Is.True);
            string id = $"{FixtureFilePath}:{FixtureLine}";

            UloopPausePointRegistry.Enable(id, 30);
            Assert.That(SourcePausePointPatcher.Patch(id, resolveResult.Resolution).Success, Is.True);

            ClearPausePointTool clearTool = new();
            JObject clearParameters = new() { ["all"] = true };
            await clearTool.ExecuteAsync(clearParameters, CancellationToken.None);

            SourcePausePointPatchResult rePatchResult = SourcePausePointPatcher.Patch(id, WithStaleMvid(resolveResult.Resolution));

            Assert.That(rePatchResult.Success, Is.False);
            Assert.That(rePatchResult.FailureReason, Is.EqualTo(SourcePausePointPatchFailureReason.StaleAssembly));
        }

        [Test]
        public void PausePointStatusBridgeCommand_Clear_CallsPatcherUnpatchSoTheIdCanBeFreshlyRePatched()
        {
            // Verifies the CLI bridge's Clear (the path Go's await-pause-point timeout
            // auto-clear and clear-pause-point-status hit) also calls
            // SourcePausePointPatcher.Unpatch, using the same stale-Mvid gate signal as the tool
            // tests above to prove the ledger entry was actually removed.
            SourcePausePointResolveResult resolveResult = SourcePausePointResolver.Resolve(FixtureFilePath, FixtureLine);
            Assert.That(resolveResult.Success, Is.True);
            string id = $"{FixtureFilePath}:{FixtureLine}";

            UloopPausePointRegistry.Enable(id, 30);
            Assert.That(SourcePausePointPatcher.Patch(id, resolveResult.Resolution).Success, Is.True);

            JObject bridgeParameters = new() { ["Id"] = id };
            PausePointStatusBridgeCommand.Clear(bridgeParameters);

            SourcePausePointPatchResult rePatchResult = SourcePausePointPatcher.Patch(id, WithStaleMvid(resolveResult.Resolution));

            Assert.That(rePatchResult.Success, Is.False);
            Assert.That(rePatchResult.FailureReason, Is.EqualTo(SourcePausePointPatchFailureReason.StaleAssembly));
        }

        private const string FixtureFilePath = "Assets/Tests/Editor/PausePointToolsFixture.cs";
        private const int FixtureLine = 12;
        private const string PhysicsFixtureFilePath = "Assets/Tests/Editor/PausePointToolsPhysicsFixture.cs";
        private const int PhysicsFixtureLine = 11;

        private static UloopPausePointCapturedVariableFrame CreateEmptyCapturedFrame()
        {
            return new UloopPausePointCapturedVariableFrame(
                Array.Empty<UloopPausePointCapturedVariableEntry>(),
                false,
                Array.Empty<string>(),
                0);
        }

        private static SourcePausePointResolution WithStaleMvid(SourcePausePointResolution resolution)
        {
            return new SourcePausePointResolution(
                resolution.AssemblyName,
                Guid.NewGuid().ToString(),
                resolution.MetadataToken,
                resolution.MethodDisplayName,
                resolution.IsStatic,
                resolution.IsDeclaringTypeValueType,
                resolution.InstructionIndex,
                resolution.IlOffset,
                resolution.ResolvedLine,
                resolution.ResolvedEndLine,
                resolution.CompiledMethodStartLine,
                resolution.CompiledMethodEndLine,
                resolution.Locals,
                resolution.Parameters);
        }

        private static async Task<PausePointResponse> EnablePausePointAsync(string id)
        {
            EnablePausePointTool tool = new();
            JObject parameters = new()
            {
                ["id"] = id,
                ["timeoutSeconds"] = 30
            };

            PausePointResponse response = (PausePointResponse)await tool.ExecuteAsync(parameters, CancellationToken.None);
            return response;
        }

        private static async Task<PausePointResponse> EnablePausePointByFileLineAsync(string file, int line)
        {
            EnablePausePointTool tool = new();
            JObject parameters = new()
            {
                ["file"] = file,
                ["line"] = line,
                ["timeoutSeconds"] = 30
            };

            PausePointResponse response = (PausePointResponse)await tool.ExecuteAsync(parameters, CancellationToken.None);
            return response;
        }

        /// <summary>
        /// Test double that records pause requests without mutating Unity Editor state.
        /// </summary>
        private sealed class FakePauseController : IUloopPausePointPauseController
        {
            public bool IsPlaying { get; private set; } = true;
            public bool IsPaused { get; private set; }
            public int PauseCount { get; private set; }
            public int ResumeCount { get; private set; }

            public void Pause()
            {
                PauseCount++;
                IsPaused = true;
            }

            public void Resume()
            {
                ResumeCount++;
                IsPaused = false;
            }

            // Simulates an external unpause (control-play-mode's Play/Stop, or the Editor's own
            // pause button) that never calls back into this registry's Resume().
            public void ResumeExternally()
            {
                IsPaused = false;
            }

            // Simulates a manual pause set outside the pause-point workflow (control-play-mode
            // --action Pause, or the Editor's own pause button). It never opens a pause window, so
            // clear must leave it untouched. PauseCount is not incremented because no pause-point
            // hit requested it.
            public void PauseExternally()
            {
                IsPaused = true;
            }
        }
    }
}
