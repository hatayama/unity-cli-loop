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
            string addedFieldsWarning =
                "Hot reload added 2 field(s) to '" + typeName + "' (alpha, beta); they never appear "
                + "in CapturedVariables, and naming them as members in 'uloop execute-dynamic-code' "
                + "fails with CS1061 because it compiles against the compiled assembly. Read one there "
                + "with HotReloadAddedFieldWiring.TryReadInstanceField (TryReadStaticField for a "
                + "static field); references/added-field-wiring.md in the uloop-hot-reload skill "
                + "shows how.";
            string expectedWarning = PausePointEnableWarnings.MergeWarnings(
                PausePointEnableWarnings.MergeWarnings(
                    PausePointEnableWarnings.CreateEnableWarning(),
                    SourcePausePointConstants.SmallMethodInliningRiskWarning),
                addedFieldsWarning);
            Assert.That(response.Warning, Is.EqualTo(expectedWarning));
        }

        /// <summary>
        /// What: the added-fields warning neither recommends plain member access from
        /// execute-dynamic-code nor a patched method body, and names the wiring reader instead.
        /// </summary>
        [Test]
        public void Enable_WhenDeclaringTypeHasAddedFields_WarningPointsAtTheWiringReader()
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
                Does.Not.Contain("Read them from a patched method body"));
            Assert.That(
                response.Warning,
                Does.Contain(
                    "Read one there with HotReloadAddedFieldWiring.TryReadInstanceField "
                    + "(TryReadStaticField for a static field)"));
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
