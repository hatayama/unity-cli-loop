using System;
using System.IO;
using System.Linq;
using System.Reflection;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.Tests.SourcePausePointPatcherFixtures;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the post-line snapshot timing end-to-end: the resolver picks the end of the
    /// requested statement, the patcher injects there, and the capture observes the values the
    /// statement produced without firing for paths that skipped the statement.
    /// </summary>
    [TestFixture]
    public sealed class SourcePausePointPostLineCaptureTests
    {
        private const string FixturePath = "Assets/Tests/Editor/SourcePausePointPatcher/Fixtures/PatcherPostLineFixture.cs";
        private const int AssignmentLine = 12;
        private const int SquaredLine = 23;
        private const int ThrowLine = 29;

        private FakePausePointPauseController _pauseController;
        private HotReloadSidePortScope _hotReloadSideScope;

        [SetUp]
        public void SetUp()
        {
            _pauseController = new FakePausePointPauseController();
            UloopPausePointRegistry.ConfigureForTests(_pauseController, () => DateTime.UtcNow);
            _hotReloadSideScope = new HotReloadSidePortScope();
        }

        [TearDown]
        public void TearDown()
        {
            _hotReloadSideScope.Dispose();
            SourcePausePointPatcher.UnpatchAll();
            UloopPausePointRegistry.ResetForTests();
        }

        [Test]
        public void PostLine_CapturesValueAssignedByTheResolvedLine()
        {
            // Verifies post-line timing observes the assignment made on the resolved line itself,
            // where pre-line timing would still report the previous value.
            const string id = "post-line-assignment";
            SourcePausePointResolveResult resolveResult = SourcePausePointResolver.Resolve(
                FixturePath, AssignmentLine, null, SourcePausePointSnapshotTiming.PostLine);
            Assert.That(resolveResult.Success, Is.True, resolveResult.ErrorMessage);
            Assert.That(resolveResult.Resolution.ResolvedLine, Is.EqualTo(AssignmentLine));
            Assert.That(resolveResult.Resolution.SnapshotTiming, Is.EqualTo(SourcePausePointSnapshotTiming.PostLine));

            UloopPausePointRegistry.Enable(id, 30);
            SourcePausePointPatchResult patchResult = SourcePausePointPatcher.Patch(id, resolveResult.Resolution);
            Assert.That(patchResult.Success, Is.True, patchResult.ErrorMessage);
            Assert.That(SourcePausePointPatcher.RequestById[id].SnapshotTiming, Is.EqualTo(SourcePausePointSnapshotTiming.PostLine));

            int result = PatcherPostLineFixture.Double(3);

            Assert.That(result, Is.EqualTo(7));
            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus(id);
            Assert.That(snapshot.IsHit, Is.True);
            Assert.That(snapshot.CapturedVariables.First(v => v.Name == "doubled").Value, Is.EqualTo("6"));
        }

        [Test]
        public void PreLine_StillCapturesValueBeforeTheResolvedLine()
        {
            // Verifies the default timing is unchanged: the same line captures the pre-assignment value.
            const string id = "pre-line-assignment";
            SourcePausePointResolveResult resolveResult = SourcePausePointResolver.Resolve(FixturePath, AssignmentLine);
            Assert.That(resolveResult.Success, Is.True, resolveResult.ErrorMessage);
            Assert.That(resolveResult.Resolution.SnapshotTiming, Is.EqualTo(SourcePausePointSnapshotTiming.PreLine));

            UloopPausePointRegistry.Enable(id, 30);
            Assert.That(SourcePausePointPatcher.Patch(id, resolveResult.Resolution).Success, Is.True);

            PatcherPostLineFixture.Double(3);

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus(id);
            Assert.That(snapshot.IsHit, Is.True);
            Assert.That(snapshot.CapturedVariables.First(v => v.Name == "doubled").Value, Is.EqualTo("0"));
        }

        [Test]
        public void PostLine_DoesNotHitWhenAnEarlierReturnSkipsTheResolvedLine()
        {
            // Verifies the post-line site is the statement's own end, not the following join point:
            // an early return that never executed the line must not trigger the marker.
            const string id = "post-line-early-return";
            SourcePausePointResolveResult resolveResult = SourcePausePointResolver.Resolve(
                FixturePath, SquaredLine, null, SourcePausePointSnapshotTiming.PostLine);
            Assert.That(resolveResult.Success, Is.True, resolveResult.ErrorMessage);

            UloopPausePointRegistry.Enable(id, 30, UloopPausePointCaptureMode.Continuous);
            Assert.That(SourcePausePointPatcher.Patch(id, resolveResult.Resolution).Success, Is.True);

            Assert.That(PatcherPostLineFixture.SquareUnlessNegative(-1), Is.EqualTo(-1));
            Assert.That(UloopPausePointRegistry.GetStatus(id).IsHit, Is.False);

            Assert.That(PatcherPostLineFixture.SquareUnlessNegative(4), Is.EqualTo(16));
            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus(id);
            Assert.That(snapshot.IsHit, Is.True);
            Assert.That(snapshot.CapturedVariables.First(v => v.Name == "squared").Value, Is.EqualTo("16"));
        }

        [Test]
        public void ReenablingWithTheOtherTiming_ReplacesTheInjectionInsteadOfReusingIt()
        {
            // Verifies a marker re-enabled on the same line with post-line timing captures at the
            // new site rather than silently keeping the pre-line injection.
            const string id = "timing-switch";
            SourcePausePointResolveResult preLine = SourcePausePointResolver.Resolve(FixturePath, AssignmentLine);
            UloopPausePointRegistry.Enable(id, 30);
            Assert.That(SourcePausePointPatcher.Patch(id, preLine.Resolution).Success, Is.True);

            SourcePausePointResolveResult postLine = SourcePausePointResolver.Resolve(
                FixturePath, AssignmentLine, null, SourcePausePointSnapshotTiming.PostLine);
            UloopPausePointRegistry.Enable(id, 30);
            Assert.That(SourcePausePointPatcher.Patch(id, postLine.Resolution).Success, Is.True);

            PatcherPostLineFixture.Double(3);

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus(id);
            Assert.That(snapshot.IsHit, Is.True);
            Assert.That(snapshot.CapturedVariables.First(v => v.Name == "doubled").Value, Is.EqualTo("6"));
        }

        [Test]
        public void PostLine_OnAlwaysThrowingLine_FailsToResolve()
        {
            // Verifies a statement that always throws is rejected for post-line timing instead of
            // silently capturing before it.
            SourcePausePointResolveResult resolveResult = SourcePausePointResolver.Resolve(
                FixturePath, ThrowLine, null, SourcePausePointSnapshotTiming.PostLine);

            Assert.That(resolveResult.Success, Is.False);
            Assert.That(resolveResult.FailureReason, Is.EqualTo(SourcePausePointResolveFailureReason.PostLineAlwaysThrows));
            Assert.That(resolveResult.ErrorMessage, Does.Contain("--snapshot-timing pre-line"));
        }

        /// <summary>
        /// What: enabling post-line timing on a statement that always throws names that line of the
        /// file on disk and points at pre-line timing or another line, not at a compile or a
        /// different path form, which cannot change the outcome.
        /// </summary>
        [Test]
        public void Enable_PostLineOnAlwaysThrowingLine_PointsAtPreLineTimingOrAnotherLine()
        {
            string fixtureSource = File.ReadAllText(
                Path.Combine(UnityCliLoopPathResolver.GetProjectRoot(), FixturePath));
            _hotReloadSideScope.Port.VerifiedSnapshotSource = (file, dllPath) => fixtureSource;

            PausePointResponse response = new PausePointUseCase().Enable(new EnablePausePointSchema
            {
                File = FixturePath,
                Line = ThrowLine,
                SnapshotTiming = SourcePausePointConstants.PostLineSnapshotTimingValue,
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.SingleShot
            });

            Assert.That(response.Success, Is.False);
            Assert.That(response.ErrorCode, Is.EqualTo(SourcePausePointConstants.ErrorCodeResolveFailed));
            Assert.That(
                response.Message,
                Is.EqualTo(string.Format(SourcePausePointConstants.PostLineAlwaysThrowsMessageFormat, ThrowLine, FixturePath)));
            Assert.That(
                response.RecommendedNextAction,
                Is.EqualTo(SourcePausePointConstants.PostLineAlwaysThrowsRecommendedNextAction));
        }

        private sealed class FakePausePointPauseController : IUloopPausePointPauseController
        {
            public int PauseCount { get; private set; }
            public bool IsPlaying => true;
            public bool IsPaused => PauseCount > 0;

            public void Pause()
            {
                PauseCount++;
            }

            public void Resume()
            {
                PauseCount = 0;
            }
        }
    }
}
