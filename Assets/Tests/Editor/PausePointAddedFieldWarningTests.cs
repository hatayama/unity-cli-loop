using System;
using System.Collections.Generic;

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

        private HotReloadSidePortScope _hotReloadSideScope;

        [SetUp]
        public void SetUp()
        {
            UloopPausePointRegistry.ConfigureForTests(new FakePausePointPauseController(), () => DateTime.UtcNow);

            // This assembly must not reference the hot-reload tool, so the added fields are
            // published through the same coordination port the hot-reload side installs.
            _hotReloadSideScope = new HotReloadSidePortScope();
        }

        [TearDown]
        public void TearDown()
        {
            SourcePausePointPatcher.UnpatchAll();
            UloopPausePointRegistry.ResetForTests();
            _hotReloadSideScope.Dispose();
        }

        /// <summary>
        /// Publishes added field simple names for one type, sorted the way the hot-reload
        /// domain sorts them.
        /// </summary>
        private void PublishAddedFields(string typeName, IReadOnlyList<string> simpleFieldNames)
        {
            _hotReloadSideScope.Port.AddedFieldsForType = queriedTypeName =>
                string.Equals(queriedTypeName, typeName, StringComparison.Ordinal)
                    ? simpleFieldNames
                    : Array.Empty<string>();
        }

        /// <summary>
        /// What: enabling a pause point on a type that has hot-reload added fields appends the
        /// CapturedVariables warning with an exact full-string match.
        /// </summary>
        [Test]
        public void Enable_WhenDeclaringTypeHasAddedFields_AppendsCapturedVariablesWarning()
        {
            string typeName = typeof(EnableBySourceLocationFixture).FullName;
            PublishAddedFields(typeName, new[] { "alpha", "beta" });

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
            string expectedWarning = PausePointEnableWarnings.MergeWarnings(
                PausePointEnableWarnings.MergeWarnings(
                    PausePointEnableWarnings.CreateEnableWarning(),
                    SourcePausePointConstants.SmallMethodInliningRiskWarning),
                addedFieldsWarning);
            Assert.That(response.Warning, Is.EqualTo(expectedWarning));
        }

        /// <summary>
        /// What: the added-fields warning does not recommend execute-dynamic-code as a read
        /// path and instead states that those names are invisible to it.
        /// </summary>
        [Test]
        public void Enable_WhenDeclaringTypeHasAddedFields_WarningDoesNotRecommendExecuteDynamicCode()
        {
            string typeName = typeof(EnableBySourceLocationFixture).FullName;
            PublishAddedFields(typeName, new[] { "alpha", "beta" });

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
            Assert.That(
                response.Warning,
                Does.Not.Contain(
                    "Read them via a patched method body or 'uloop execute-dynamic-code' instead"));
            Assert.That(
                response.Warning,
                Does.Contain("not visible to 'uloop execute-dynamic-code'"));
            Assert.That(
                response.Warning,
                Does.Contain("Read them from a patched method body"));
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
            string expectedWarning = PausePointEnableWarnings.MergeWarnings(
                PausePointEnableWarnings.CreateEnableWarning(),
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
