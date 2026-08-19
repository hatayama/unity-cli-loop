using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.Tests.PausePointToolsFixtures;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies enable-pause-point warns when the resolved type has hot-reload added fields.
    /// </summary>
    [TestFixture]
    public sealed class PausePointAddedFieldWarningTests
    {
        private const string FixtureFilePath = "Assets/Tests/Editor/PausePointToolsFixture.cs";
        private const int FixtureLine = 12;

        [SetUp]
        public void SetUp()
        {
            UloopPausePointRegistry.ConfigureForTests(new FakePausePointPauseController(), () => DateTime.UtcNow);
        }

        [TearDown]
        public void TearDown()
        {
            SourcePausePointPatcher.UnpatchAll();
            UloopPausePointRegistry.ResetForTests();
            HotReloadAddedFieldRegistry.ClearAll();
        }

        /// <summary>
        /// What: enabling a pause point on a type that has hot-reload added fields appends the
        /// CapturedVariables warning with an exact full-string match.
        /// </summary>
        [Test]
        public void Enable_WhenDeclaringTypeHasAddedFields_AppendsCapturedVariablesWarning()
        {
            string typeName = typeof(EnableBySourceLocationFixture).FullName;
            HotReloadAddedFieldRegistry.ReplaceForFile(
                FixtureFilePath,
                new[] { typeName + ".beta", typeName + ".alpha" });

            PausePointResponse response = new PausePointUseCase().Enable(new EnablePausePointSchema
            {
                File = FixtureFilePath,
                Line = FixtureLine,
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.SingleShot
            });

            Assert.That(
                response.Success,
                Is.True,
                response.ErrorCode + " / " + response.Message);
            string addedFieldsWarning = string.Format(
                SourcePausePointConstants.HotReloadAddedFieldsNotCapturedWarningFormat,
                typeName,
                2,
                "alpha, beta");
            string expectedWarning = PausePointUseCase.MergeWarnings(
                PausePointUseCase.MergeWarnings(
                    PausePointUseCase.CreateEnableWarning(),
                    SourcePausePointConstants.SmallMethodInliningRiskWarning),
                addedFieldsWarning);
            Assert.That(response.Warning, Is.EqualTo(expectedWarning));
        }

        /// <summary>
        /// What: enabling a pause point on a type with no added fields yields only the usual
        /// enable warnings, with an exact full-string match and no added-field sentence.
        /// </summary>
        [Test]
        public void Enable_WhenDeclaringTypeHasNoAddedFields_OmitsCapturedVariablesWarning()
        {
            PausePointResponse response = new PausePointUseCase().Enable(new EnablePausePointSchema
            {
                File = FixtureFilePath,
                Line = FixtureLine,
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.SingleShot
            });

            Assert.That(
                response.Success,
                Is.True,
                response.ErrorCode + " / " + response.Message);
            string expectedWarning = PausePointUseCase.MergeWarnings(
                PausePointUseCase.CreateEnableWarning(),
                SourcePausePointConstants.SmallMethodInliningRiskWarning);
            Assert.That(response.Warning, Is.EqualTo(expectedWarning));
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
