using System;
using System.IO;

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
        }

        /// <summary>
        /// What: an active-patch file produces the success-path compiled-line-map warning.
        /// </summary>
        [Test]
        public void BuildCompiledLineMapWarningOrEmpty_WhenPatchesAreActive_ReturnsFormattedWarning()
        {
            string warning = PausePointUseCase.BuildCompiledLineMapWarningOrEmpty(true, ForwardSlashFile);

            Assert.That(
                warning,
                Is.EqualTo(
                    string.Format(
                        SourcePausePointConstants.HotReloadCompiledLineMapWarningFormat,
                        ForwardSlashFile)));
        }

        /// <summary>
        /// What: a backslash path is normalized before it is interpolated into the success warning.
        /// </summary>
        [Test]
        public void BuildCompiledLineMapWarningOrEmpty_WhenFileUsesBackslashes_NormalizesToForwardSlashes()
        {
            string warning = PausePointUseCase.BuildCompiledLineMapWarningOrEmpty(true, "Assets\\Scripts\\Example.cs");

            Assert.That(
                warning,
                Is.EqualTo(
                    string.Format(
                        SourcePausePointConstants.HotReloadCompiledLineMapWarningFormat,
                        ForwardSlashFile)));
        }

        /// <summary>
        /// What: the success helper stays silent when the file has no active hot-reload patches.
        /// </summary>
        [Test]
        public void BuildCompiledLineMapWarningOrEmpty_WhenPatchesAreInactive_ReturnsEmpty()
        {
            string warning = PausePointUseCase.BuildCompiledLineMapWarningOrEmpty(false, ForwardSlashFile);

            Assert.That(warning, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// What: resolve-failure warning names compiled-line drift without pointing at
        /// ResolvedMethod or ResolvedLineText, which stay empty on that failure.
        /// </summary>
        [Test]
        public void BuildCompiledLineMapResolveFailureWarningOrEmpty_WhenPatchesAreActive_ReturnsFormattedWarning()
        {
            string warning = PausePointUseCase.BuildCompiledLineMapResolveFailureWarningOrEmpty(
                true,
                ForwardSlashFile);

            Assert.That(
                warning,
                Is.EqualTo(
                    string.Format(
                        SourcePausePointConstants.HotReloadCompiledLineMapResolveFailureWarningFormat,
                        ForwardSlashFile)));
        }

        /// <summary>
        /// What: a backslash path is normalized before it is interpolated into the
        /// resolve-failure warning.
        /// </summary>
        [Test]
        public void BuildCompiledLineMapResolveFailureWarningOrEmpty_WhenFileUsesBackslashes_NormalizesToForwardSlashes()
        {
            string warning = PausePointUseCase.BuildCompiledLineMapResolveFailureWarningOrEmpty(
                true,
                "Assets\\Scripts\\Example.cs");

            Assert.That(
                warning,
                Is.EqualTo(
                    string.Format(
                        SourcePausePointConstants.HotReloadCompiledLineMapResolveFailureWarningFormat,
                        ForwardSlashFile)));
        }

        /// <summary>
        /// What: the resolve-failure helper stays silent when the file has no active patches.
        /// </summary>
        [Test]
        public void BuildCompiledLineMapResolveFailureWarningOrEmpty_WhenPatchesAreInactive_ReturnsEmpty()
        {
            string warning = PausePointUseCase.BuildCompiledLineMapResolveFailureWarningOrEmpty(
                false,
                ForwardSlashFile);

            Assert.That(warning, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// What: trim-unequal compiled vs edited text at the same resolved line formats the
        /// drift warning from the constant.
        /// </summary>
        [Test]
        public void BuildCompiledLineDriftWarningOrEmpty_WhenTextsDiffer_ReturnsFormattedWarning()
        {
            string warning = PausePointUseCase.BuildCompiledLineDriftWarningOrEmpty(
                "  return 1;  ",
                "return 2;",
                ForwardSlashFile,
                17);

            Assert.That(
                warning,
                Is.EqualTo(
                    string.Format(
                        SourcePausePointConstants.HotReloadCompiledLineMapLineDriftWarningFormat,
                        ForwardSlashFile,
                        17,
                        "return 1;",
                        "return 2;")));
        }

        /// <summary>
        /// What: trim-equal compiled vs edited text is not drift.
        /// </summary>
        [Test]
        public void BuildCompiledLineDriftWarningOrEmpty_WhenTextsMatchAfterTrim_ReturnsEmpty()
        {
            string warning = PausePointUseCase.BuildCompiledLineDriftWarningOrEmpty(
                "  return 1;  ",
                "return 1;",
                ForwardSlashFile,
                17);

            Assert.That(warning, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// What: a missing compiled or edited line skips the comparison instead of warning.
        /// </summary>
        [Test]
        public void BuildCompiledLineDriftWarningOrEmpty_WhenEitherSideIsEmpty_ReturnsEmpty()
        {
            Assert.That(
                PausePointUseCase.BuildCompiledLineDriftWarningOrEmpty(
                    string.Empty,
                    "return 1;",
                    ForwardSlashFile,
                    17),
                Is.EqualTo(string.Empty));
            Assert.That(
                PausePointUseCase.BuildCompiledLineDriftWarningOrEmpty(
                    "return 1;",
                    string.Empty,
                    ForwardSlashFile,
                    17),
                Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// What: enable on an unpatched method in a hot-reloaded file merges the exact drift
        /// warning and sets the drift next-action when compiled vs edited text differ.
        /// </summary>
        [Test]
        public void Enable_WhenCompiledLineDriftsFromEditedFile_AddsDriftWarningAndNextAction()
        {
            Func<string, HotReloadShimFileLookup> previousLookup =
                HotReloadPausePointCoordination.GetShimLookupForFile;
            Func<string, string> previousSnapshot =
                HotReloadPausePointCoordination.GetVerifiedSnapshotSourceForFile;
            HotReloadShimFileLookup stubLookup = new HotReloadShimFileLookup(
                Array.Empty<byte>(),
                Array.Empty<byte>(),
                null,
                Array.Empty<HotReloadShimMethodLookup>());

            string absolutePath = Path.Combine(
                UnityCliLoopPathResolver.GetProjectRoot(),
                ResolveFailureFile);
            string diskSource = File.ReadAllText(absolutePath);
            int markerLine = FindLineNumberContaining(
                diskSource,
                "compiled-line-drift" + "-probe-unique");
            Assert.That(markerLine, Is.GreaterThan(0));
            int requestedLine = markerLine + 1;

            string[] snapshotLines = diskSource.Replace("\r\n", "\n").Split('\n');
            snapshotLines[requestedLine - 1] = "            return 0;";
            string snapshotSource = string.Join("\n", snapshotLines);

            try
            {
                HotReloadPausePointCoordination.GetShimLookupForFile = _ => stubLookup;
                HotReloadPausePointCoordination.GetVerifiedSnapshotSourceForFile = _ => snapshotSource;

                PausePointResponse response = new PausePointUseCase().Enable(new EnablePausePointSchema
                {
                    File = ResolveFailureFile,
                    Line = requestedLine,
                    TimeoutSeconds = 30,
                    Mode = UloopPausePointCaptureMode.SingleShot
                });

                Assert.That(
                    response.Success,
                    Is.True,
                    response.ErrorCode + " / " + response.Message + " / " + response.RecommendedNextAction);
                string expectedDrift = string.Format(
                    SourcePausePointConstants.HotReloadCompiledLineMapLineDriftWarningFormat,
                    ResolveFailureFile,
                    response.ResolvedLine,
                    "return 0;",
                    "return 424242;");
                Assert.That(response.Warning, Does.Contain(expectedDrift));
                Assert.That(
                    response.RecommendedNextAction,
                    Is.EqualTo(SourcePausePointConstants.HotReloadCompiledLineMapLineDriftNextAction));
            }
            finally
            {
                HotReloadPausePointCoordination.GetShimLookupForFile = previousLookup;
                HotReloadPausePointCoordination.GetVerifiedSnapshotSourceForFile = previousSnapshot;
            }
        }

        /// <summary>
        /// What: a resolve-failure enable response with active hot-reload patches uses the
        /// failure warning and next-action constants, not the success-path wording.
        /// </summary>
        [Test]
        public void Enable_WhenResolveFailsAndFileHasActivePatches_UsesResolveFailureWarningAndNextAction()
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
                Assert.That(
                    withPatches.Warning,
                    Is.EqualTo(
                        string.Format(
                            SourcePausePointConstants.HotReloadCompiledLineMapResolveFailureWarningFormat,
                            ResolveFailureFile)));
                Assert.That(
                    withPatches.RecommendedNextAction,
                    Is.EqualTo(SourcePausePointConstants.HotReloadCompiledLineMapResolveFailureNextAction));

                HotReloadPausePointCoordination.GetShimLookupForFile = _ => null;
                PausePointResponse withoutPatches = EnableUnresolvableLine();

                Assert.That(withoutPatches.Success, Is.False);
                Assert.That(withoutPatches.ErrorCode, Is.EqualTo(SourcePausePointConstants.ErrorCodeResolveFailed));
                Assert.That(withoutPatches.Warning, Is.EqualTo(string.Empty));
                Assert.That(
                    withoutPatches.RecommendedNextAction,
                    Is.EqualTo(SourcePausePointConstants.ResolveFailedRecommendedNextAction));
            }
            finally
            {
                HotReloadPausePointCoordination.GetShimLookupForFile = previous;
            }
        }

        internal static int CompiledLineDriftProbe()
        {
            // compiled-line-drift-probe-unique
            return 424242;
        }

        private static int FindLineNumberContaining(string source, string fragment)
        {
            string[] lines = source.Replace("\r\n", "\n").Split('\n');
            for (int index = 0; index < lines.Length; index++)
            {
                if (lines[index].Contains(fragment))
                {
                    return index + 1;
                }
            }

            return -1;
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
