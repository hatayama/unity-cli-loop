using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the compiled-line-map warning text for files that have active hot-reload patches.
    /// </summary>
    [TestFixture]
    public sealed class PausePointCompiledLineMapWarningTests
    {
        private const string ForwardSlashFile = "Assets/Scripts/Example.cs";

        private const string ResolveFailureFile =
            "Assets/Tests/Editor/PausePointCompiledLineMapWarningTests.cs";

        private const int UnresolvableLine = 999999;

        private const string ExpectedWarning =
            "'Assets/Scripts/Example.cs' has active hot-reload patches. For methods this reload did not patch, --line "
            + "resolves against the last compiled source, not the edited file. Verify "
            + "ResolvedMethod and ResolvedLineText, or run 'uloop compile' and re-enable.";

        private const string ExpectedResolveFailureWarning =
            "'Assets/Tests/Editor/PausePointCompiledLineMapWarningTests.cs' has active hot-reload patches. For methods this reload did not patch, --line "
            + "resolves against the last compiled source, not the edited file. Verify "
            + "ResolvedMethod and ResolvedLineText, or run 'uloop compile' and re-enable.";

        [SetUp]
        public void SetUp()
        {
            UloopPausePointRegistry.ConfigureForTests(new FakePausePointPauseController(), () => DateTime.UtcNow);
        }

        [TearDown]
        public void TearDown()
        {
            UloopPausePointRegistry.ResetForTests();
        }

        /// <summary>
        /// Verifies an active-patch file produces the compiled-line-map warning with forward slashes.
        /// </summary>
        [Test]
        public void BuildCompiledLineMapWarningOrEmpty_WhenPatchesAreActive_ReturnsFormattedWarning()
        {
            string warning = PausePointUseCase.BuildCompiledLineMapWarningOrEmpty(true, ForwardSlashFile);

            Assert.That(warning, Is.EqualTo(ExpectedWarning));
        }

        /// <summary>
        /// Verifies a backslash path is normalized before it is interpolated into the warning.
        /// </summary>
        [Test]
        public void BuildCompiledLineMapWarningOrEmpty_WhenFileUsesBackslashes_NormalizesToForwardSlashes()
        {
            string warning = PausePointUseCase.BuildCompiledLineMapWarningOrEmpty(true, "Assets\\Scripts\\Example.cs");

            Assert.That(warning, Is.EqualTo(ExpectedWarning));
        }

        /// <summary>
        /// Verifies the helper stays silent when the file has no active hot-reload patches.
        /// </summary>
        [Test]
        public void BuildCompiledLineMapWarningOrEmpty_WhenPatchesAreInactive_ReturnsEmpty()
        {
            string warning = PausePointUseCase.BuildCompiledLineMapWarningOrEmpty(false, ForwardSlashFile);

            Assert.That(warning, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Verifies a resolve-failure enable response includes the compiled-line-map warning
        /// only when GetShimLookupForFile returns a lookup for that file.
        /// </summary>
        [Test]
        public void Enable_WhenResolveFailsAndFileHasActivePatches_IncludesCompiledLineMapWarning()
        {
            Func<string, HotReloadShimFileLookup> previous =
                HotReloadPausePointCoordination.GetShimLookupForFile;
            HotReloadShimFileLookup stubLookup = new HotReloadShimFileLookup(
                Array.Empty<byte>(),
                Array.Empty<byte>(),
                null,
                Array.Empty<HotReloadShimMethodLookup>());

            try
            {
                HotReloadPausePointCoordination.GetShimLookupForFile = _ => stubLookup;
                PausePointResponse withPatches = EnableUnresolvableLine();

                Assert.That(withPatches.Success, Is.False);
                Assert.That(withPatches.ErrorCode, Is.EqualTo(SourcePausePointConstants.ErrorCodeResolveFailed));
                Assert.That(withPatches.Warning, Is.EqualTo(ExpectedResolveFailureWarning));

                HotReloadPausePointCoordination.GetShimLookupForFile = _ => null;
                PausePointResponse withoutPatches = EnableUnresolvableLine();

                Assert.That(withoutPatches.Success, Is.False);
                Assert.That(withoutPatches.ErrorCode, Is.EqualTo(SourcePausePointConstants.ErrorCodeResolveFailed));
                Assert.That(withoutPatches.Warning, Is.EqualTo(string.Empty));
            }
            finally
            {
                HotReloadPausePointCoordination.GetShimLookupForFile = previous;
            }
        }

        private static PausePointResponse EnableUnresolvableLine()
        {
            return new PausePointUseCase().Enable(new EnablePausePointSchema
            {
                File = ResolveFailureFile,
                Line = UnresolvableLine,
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.SingleShot
            });
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
                // Why zero: Unity's isPaused is a bool; Option B Resume must fully clear pause.
                PauseCount = 0;
            }
        }
    }
}
