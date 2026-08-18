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
            UloopPausePointSnapshot snapshot = CreateExpiredSnapshot(string.Empty);

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
            UloopPausePointSnapshot snapshot = CreateExpiredSnapshot(string.Empty);

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
            UloopPausePointSnapshot snapshot = CreateExpiredSnapshot(existingAction);

            PausePointResponse toolResponse = PausePointResponse.FromSnapshot(snapshot);
            PausePointStatusResponse statusResponse = PausePointStatusResponse.FromSnapshot(snapshot);

            Assert.That(toolResponse.RecommendedNextAction, Is.EqualTo(existingAction));
            Assert.That(statusResponse.RecommendedNextAction, Is.EqualTo(existingAction));
        }

        private static UloopPausePointSnapshot CreateExpiredSnapshot(string recommendedNextAction)
        {
            return new UloopPausePointSnapshot(
                "jump",
                UloopPausePointStatus.Expired,
                false,
                false,
                0,
                30,
                UloopPausePointCaptureMode.SingleShot,
                20,
                15,
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
                null);
        }
    }
}
