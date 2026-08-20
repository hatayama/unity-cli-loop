using NUnit.Framework;
using UnityEditor;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests pending-priority and unknown fallback for Play Mode stop reasons.
    /// </summary>
    public sealed class PlayModeStopReasonSessionStoreTests
    {
        [SetUp]
        public void SetUp()
        {
            PlayModeStopReasonSessionStore.ClearForTests();
        }

        [TearDown]
        public void TearDown()
        {
            PlayModeStopReasonSessionStore.ClearForTests();
        }

        /// <summary>
        /// What: SetPending overwrites a previous pending reason, including script-compilation.
        /// </summary>
        [Test]
        public void SetPending_WhenCalledAfterTrySetPending_OverwritesScriptCompilation()
        {
            PlayModeStopReasonSessionStore.TrySetPending("script-compilation");
            PlayModeStopReasonSessionStore.SetPending("cli-control-play-mode");

            Assert.That(PlayModeStopReasonSessionStore.PendingReason, Is.EqualTo("cli-control-play-mode"));
        }

        /// <summary>
        /// What: TrySetPending leaves an explicit pending reason in place.
        /// </summary>
        [Test]
        public void TrySetPending_WhenPendingAlreadySet_DoesNotOverwrite()
        {
            PlayModeStopReasonSessionStore.SetPending("cli-compile-stop-setting");
            PlayModeStopReasonSessionStore.TrySetPending("script-compilation");

            Assert.That(PlayModeStopReasonSessionStore.PendingReason, Is.EqualTo("cli-compile-stop-setting"));
        }

        /// <summary>
        /// What: ConfirmPending with no pending reason stores unknown plus the given timestamp.
        /// </summary>
        [Test]
        public void ConfirmPending_WhenNoPendingReason_StoresUnknown()
        {
            PlayModeStopReasonSessionStore.ConfirmPending("2026-01-01T00:00:00.0000000Z");

            PlayModeStopReasonRecord record = PlayModeStopReasonSessionStore.TryReadConfirmed();
            Assert.That(record.HasValue, Is.True);
            Assert.That(record.StoppedBy, Is.EqualTo("unknown"));
            Assert.That(record.StoppedAtUtc, Is.EqualTo("2026-01-01T00:00:00.0000000Z"));
            Assert.That(PlayModeStopReasonSessionStore.PendingReason, Is.Null);
        }

        /// <summary>
        /// What: ConfirmPending writes the pending reason and clears pending.
        /// </summary>
        [Test]
        public void ConfirmPending_WhenPendingIsSet_StoresThatReason()
        {
            PlayModeStopReasonSessionStore.SetPending("cli-run-tests-cancel");
            PlayModeStopReasonSessionStore.ConfirmPending("2026-01-02T00:00:00.0000000Z");

            PlayModeStopReasonRecord record = PlayModeStopReasonSessionStore.TryReadConfirmed();
            Assert.That(record.StoppedBy, Is.EqualTo("cli-run-tests-cancel"));
            Assert.That(record.StoppedAtUtc, Is.EqualTo("2026-01-02T00:00:00.0000000Z"));
            Assert.That(PlayModeStopReasonSessionStore.PendingReason, Is.Null);
        }
    }

    /// <summary>
    /// Tests that each Play Mode stop input path stamps the matching pending reason.
    /// </summary>
    public sealed class PlayModeStopReasonWiringTests
    {
        [SetUp]
        public void SetUp()
        {
            PlayModeStopReasonSessionStore.ClearForTests();
        }

        [TearDown]
        public void TearDown()
        {
            PlayModeStopReasonSessionStore.ClearForTests();
        }

        /// <summary>
        /// What: setting IsPlaying false on the editor state service stamps cli-control-play-mode.
        /// </summary>
        [Test]
        public void EditorStateService_WhenIsPlayingSetFalse_SetsPendingCliControlPlayMode()
        {
            ControlPlayModeEditorStateService service = new ControlPlayModeEditorStateService();

            service.IsPlaying = false;

            Assert.That(
                PlayModeStopReasonSessionStore.PendingReason,
                Is.EqualTo("cli-control-play-mode"));
        }

        /// <summary>
        /// What: compile StopPlayMode stamps cli-compile-stop-setting before exiting Play Mode.
        /// </summary>
        [Test]
        public void StopPlayMode_WhenInvoked_SetsPendingCliCompileStopSetting()
        {
            PlayModeCompilationPreparationService service = new PlayModeCompilationPreparationService();

            service.StopPlayMode();

            Assert.That(
                PlayModeStopReasonSessionStore.PendingReason,
                Is.EqualTo("cli-compile-stop-setting"));
        }

        /// <summary>
        /// What: run-tests cancel exit stamps cli-run-tests-cancel.
        /// </summary>
        [Test]
        public void StopPlayingForCancel_WhenInvoked_SetsPendingCliRunTestsCancel()
        {
            RunTestsCancelStopRestoreUnityHooks.StopPlayingForCancel();

            Assert.That(
                PlayModeStopReasonSessionStore.PendingReason,
                Is.EqualTo("cli-run-tests-cancel"));
        }

        /// <summary>
        /// What: compilationStarted stamps script-compilation only when pending is empty.
        /// </summary>
        [Test]
        public void HandleCompilationStarted_WhenPendingEmpty_SetsScriptCompilation()
        {
            PlayModeStopReasonSubscriber.HandleCompilationStarted(null);

            Assert.That(
                PlayModeStopReasonSessionStore.PendingReason,
                Is.EqualTo("script-compilation"));
        }

        /// <summary>
        /// What: compilationStarted does not replace an explicit pending reason.
        /// </summary>
        [Test]
        public void HandleCompilationStarted_WhenPendingAlreadySet_DoesNotOverwrite()
        {
            PlayModeStopReasonSessionStore.SetPending("cli-control-play-mode");

            PlayModeStopReasonSubscriber.HandleCompilationStarted(null);

            Assert.That(
                PlayModeStopReasonSessionStore.PendingReason,
                Is.EqualTo("cli-control-play-mode"));
        }

        /// <summary>
        /// What: ExitingPlayMode with no pending confirms unknown.
        /// </summary>
        [Test]
        public void HandlePlayModeStateChanged_WhenExitingPlayModeWithoutPending_ConfirmsUnknown()
        {
            PlayModeStopReasonSubscriber.HandlePlayModeStateChanged(PlayModeStateChange.ExitingPlayMode);

            PlayModeStopReasonRecord record = PlayModeStopReasonSessionStore.TryReadConfirmed();
            Assert.That(record.StoppedBy, Is.EqualTo("unknown"));
            Assert.That(record.StoppedAtUtc, Is.Not.Null.And.Not.Empty);
            Assert.That(PlayModeStopReasonSessionStore.PendingReason, Is.Null);
        }

        /// <summary>
        /// What: a non-exit play-mode event does not confirm pending.
        /// </summary>
        [Test]
        public void HandlePlayModeStateChanged_WhenNotExitingPlayMode_LeavesPending()
        {
            PlayModeStopReasonSessionStore.SetPending("cli-control-play-mode");

            PlayModeStopReasonSubscriber.HandlePlayModeStateChanged(PlayModeStateChange.EnteredPlayMode);

            Assert.That(PlayModeStopReasonSessionStore.PendingReason, Is.EqualTo("cli-control-play-mode"));
            Assert.That(PlayModeStopReasonSessionStore.TryReadConfirmed().HasValue, Is.False);
        }
    }
}
