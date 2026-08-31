using System;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.Runtime;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies Expired responses fill RecommendedNextAction when the snapshot leaves it empty.
    /// </summary>
    [TestFixture]
    public sealed class PausePointExpiredRecommendedNextActionTests
    {
        /// <summary>
        /// What: PausePointResponse.FromSnapshot fills the expired next-action when the snapshot action is empty.
        /// </summary>
        [Test]
        public void FromSnapshot_WhenExpiredAndActionEmpty_FillsExpiredRecommendedNextAction()
        {
            UloopPausePointSnapshot snapshot = CreateExpiredSnapshot(string.Empty, 0);

            PausePointResponse response = PausePointResponse.FromSnapshot(snapshot);

            Assert.That(response.Status, Is.EqualTo(UloopPausePointStatus.Expired));
            Assert.That(
                response.RecommendedNextAction,
                Is.EqualTo(SourcePausePointConstants.ExpiredRecommendedNextAction));
        }

        /// <summary>
        /// What: PausePointStatusResponse.FromSnapshot fills the same expired next-action on the CLI bridge path.
        /// </summary>
        [Test]
        public void StatusFromSnapshot_WhenExpiredAndActionEmpty_FillsExpiredRecommendedNextAction()
        {
            UloopPausePointSnapshot snapshot = CreateExpiredSnapshot(string.Empty, 0);

            PausePointStatusResponse response = PausePointStatusResponse.FromSnapshot(snapshot);

            Assert.That(response.Status, Is.EqualTo(UloopPausePointStatus.Expired));
            Assert.That(
                response.RecommendedNextAction,
                Is.EqualTo(SourcePausePointConstants.ExpiredRecommendedNextAction));
        }

        /// <summary>
        /// What: a non-empty snapshot action is left unchanged so registry-expired wording stays intact.
        /// </summary>
        [Test]
        public void FromSnapshot_WhenExpiredAndActionPresent_KeepsSnapshotAction()
        {
            const string existingAction = "Custom action preserved by test.";
            UloopPausePointSnapshot snapshot = CreateExpiredSnapshot(existingAction, 0);

            PausePointResponse toolResponse = PausePointResponse.FromSnapshot(snapshot);
            PausePointStatusResponse statusResponse = PausePointStatusResponse.FromSnapshot(snapshot);

            Assert.That(toolResponse.RecommendedNextAction, Is.EqualTo(existingAction));
            Assert.That(statusResponse.RecommendedNextAction, Is.EqualTo(existingAction));
        }

        /// <summary>
        /// What: PausePointResponse preserves MethodEntryCount from the runtime snapshot.
        /// </summary>
        [Test]
        public void FromSnapshot_WhenMethodEntryCountIsNonzero_PropagatesMethodEntryCount()
        {
            UloopPausePointSnapshot snapshot = CreateExpiredSnapshot(string.Empty, 3);

            PausePointResponse response = PausePointResponse.FromSnapshot(snapshot);

            Assert.That(response.MethodEntryCount, Is.EqualTo(3));
        }

        /// <summary>
        /// What: PausePointStatusResponse preserves MethodEntryCount from the runtime snapshot.
        /// </summary>
        [Test]
        public void StatusFromSnapshot_WhenMethodEntryCountIsNonzero_PropagatesMethodEntryCount()
        {
            UloopPausePointSnapshot snapshot = CreateExpiredSnapshot(string.Empty, 3);

            PausePointStatusResponse response = PausePointStatusResponse.FromSnapshot(snapshot);

            Assert.That(response.MethodEntryCount, Is.EqualTo(3));
        }

        /// <summary>
        /// What: PausePointResponse preserves all hit-when status fields from a runtime snapshot.
        /// </summary>
        [Test]
        public void FromSnapshot_WhenHitWhenFieldsArePresent_PropagatesFixedValues()
        {
            UloopPausePointSnapshot snapshot = CreateExpiredSnapshot(
                string.Empty,
                0,
                "speed > 5",
                3,
                "--hit-when could not find variable 'speed' in the captured frame.");

            PausePointResponse response = PausePointResponse.FromSnapshot(snapshot);

            Assert.That(response.HitWhen, Is.EqualTo("speed > 5"));
            Assert.That(response.HitWhenSkippedCount, Is.EqualTo(3));
            Assert.That(response.HitWhenErrorNote, Is.EqualTo("--hit-when could not find variable 'speed' in the captured frame."));
        }

        /// <summary>
        /// What: PausePointStatusResponse preserves all hit-when status fields from a runtime snapshot.
        /// </summary>
        [Test]
        public void StatusFromSnapshot_WhenHitWhenFieldsArePresent_PropagatesFixedValues()
        {
            UloopPausePointSnapshot snapshot = CreateExpiredSnapshot(
                string.Empty,
                0,
                "speed > 5",
                3,
                "--hit-when could not find variable 'speed' in the captured frame.");

            PausePointStatusResponse response = PausePointStatusResponse.FromSnapshot(snapshot);

            Assert.That(response.HitWhen, Is.EqualTo("speed > 5"));
            Assert.That(response.HitWhenSkippedCount, Is.EqualTo(3));
            Assert.That(response.HitWhenErrorNote, Is.EqualTo("--hit-when could not find variable 'speed' in the captured frame."));
        }

        private static UloopPausePointSnapshot CreateExpiredSnapshot(
            string recommendedNextAction,
            int methodEntryCount,
            string hitWhen = "",
            int hitWhenSkippedCount = 0,
            string hitWhenErrorNote = "")
        {
            return new UloopPausePointSnapshot(
                "jump",
                UloopPausePointStatus.Expired,
                false,
                false,
                0,
                methodEntryCount,
                30,
                UloopPausePointCaptureMode.SingleShot,
                20,
                15,
                2,
                Array.Empty<UloopPausePointCapturedHistoryFrame>(),
                0,
                true,
                "2026-06-03T00:00:00.0000000Z",
                31000,
                0,
                1,
                new UloopPausePointEditorStateSnapshot(
                    true,
                    false,
                    UloopPausePointEditorStateCapturedAt.Current),
                string.Empty,
                string.Empty,
                0,
                0,
                "Pause point expired.",
                recommendedNextAction,
                Array.Empty<UloopCapturedVariable>(),
                Array.Empty<UloopPausePointCallerFrame>(),
                false,
                Array.Empty<string>(),
                0,
                string.Empty,
                string.Empty,
                false,
                false,
                null,
                false,
                0,
                null,
                hitWhen,
                hitWhenSkippedCount,
                hitWhenErrorNote);
        }
    }
}
