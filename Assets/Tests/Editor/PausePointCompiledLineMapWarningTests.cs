using System;
using System.IO;
using System.Reflection;

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

        private const string GenericPatchedMethodsUseEditedFileSentence =
            "Methods currently patched by hot reload resolve against the edited file instead";

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
            string warning = PausePointEnableWarnings.BuildCompiledLineMapWarningOrEmpty(true, ForwardSlashFile);

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
            string warning = PausePointEnableWarnings.BuildCompiledLineMapWarningOrEmpty(true, "Assets\\Scripts\\Example.cs");

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
            string warning = PausePointEnableWarnings.BuildCompiledLineMapWarningOrEmpty(false, ForwardSlashFile);

            Assert.That(warning, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// What: resolve-failure warning names compiled-line drift without pointing at
        /// ResolvedMethod or ResolvedLineText, which stay empty on that failure.
        /// </summary>
        [Test]
        public void BuildCompiledLineMapResolveFailureWarningOrEmpty_WhenPatchesAreActive_ReturnsFormattedWarning()
        {
            string warning = PausePointEnableWarnings.BuildCompiledLineMapResolveFailureWarningOrEmpty(
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
            string warning = PausePointEnableWarnings.BuildCompiledLineMapResolveFailureWarningOrEmpty(
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
            string warning = PausePointEnableWarnings.BuildCompiledLineMapResolveFailureWarningOrEmpty(
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
            string warning = PausePointEnableWarnings.BuildCompiledLineDriftWarningOrEmpty(
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
            string warning = PausePointEnableWarnings.BuildCompiledLineDriftWarningOrEmpty(
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
                PausePointEnableWarnings.BuildCompiledLineDriftWarningOrEmpty(
                    string.Empty,
                    "return 1;",
                    ForwardSlashFile,
                    17),
                Is.EqualTo(string.Empty));
            Assert.That(
                PausePointEnableWarnings.BuildCompiledLineDriftWarningOrEmpty(
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
                SourcePausePointResolveResult spanResult = SourcePausePointResolver.Resolve(
                    ResolveFailureFile,
                    response.ResolvedLine);
                Assert.That(spanResult.Success, Is.True, spanResult.ErrorMessage);
                Assert.That(spanResult.Resolution.CompiledMethodStartLine, Is.GreaterThan(0));
                Assert.That(spanResult.Resolution.CompiledMethodEndLine, Is.GreaterThan(0));
                string expectedDrift = string.Format(
                    SourcePausePointConstants.HotReloadCompiledLineMapLineDriftWarningFormat,
                    ResolveFailureFile,
                    response.ResolvedLine,
                    "return 0;",
                    "return 424242;");
                expectedDrift = PausePointEnableWarnings.AppendCompiledMethodSpanToDriftWarningOrUnchanged(
                    expectedDrift,
                    response.ResolvedMethod,
                    spanResult.Resolution.CompiledMethodStartLine,
                    spanResult.Resolution.CompiledMethodEndLine);
                string expectedWarning = PausePointEnableWarnings.MergeWarnings(
                    PausePointEnableWarnings.MergeWarnings(
                        PausePointEnableWarnings.MergeWarnings(
                            PausePointEnableWarnings.CreateEnableWarning(),
                            PausePointEnableWarnings.BuildCompiledLineMapWarningOrEmpty(true, ResolveFailureFile)),
                        expectedDrift),
                    SourcePausePointConstants.SmallMethodInliningRiskWarning);
                Assert.That(response.Warning, Is.EqualTo(expectedWarning));
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
        /// What: a known compiled span is appended to a non-empty drift warning.
        /// </summary>
        [Test]
        public void AppendCompiledMethodSpanToDriftWarningOrUnchanged_WhenSpanIsKnown_AppendsSpanSentence()
        {
            string drift = string.Format(
                SourcePausePointConstants.HotReloadCompiledLineMapLineDriftWarningFormat,
                ForwardSlashFile,
                17,
                "return 1;",
                "return 2;");

            string warning = PausePointEnableWarnings.AppendCompiledMethodSpanToDriftWarningOrUnchanged(
                drift,
                "Example.Run",
                8,
                11);

            Assert.That(
                warning,
                Is.EqualTo(
                    drift + string.Format(
                        SourcePausePointConstants.HotReloadCompiledMethodSpanInLastCompiledSourceFormat,
                        "Example.Run",
                        8,
                        11)));
        }

        /// <summary>
        /// What: an unknown (0,0) compiled span leaves the drift warning unchanged.
        /// </summary>
        [Test]
        public void AppendCompiledMethodSpanToDriftWarningOrUnchanged_WhenSpanIsUnknown_LeavesWarningUnchanged()
        {
            string drift = string.Format(
                SourcePausePointConstants.HotReloadCompiledLineMapLineDriftWarningFormat,
                ForwardSlashFile,
                17,
                "return 1;",
                "return 2;");

            string warning = PausePointEnableWarnings.AppendCompiledMethodSpanToDriftWarningOrUnchanged(
                drift,
                "Example.Run",
                0,
                0);

            Assert.That(warning, Is.EqualTo(drift));
        }

        /// <summary>
        /// What: an empty drift warning stays empty even when a compiled span is known.
        /// </summary>
        [Test]
        public void AppendCompiledMethodSpanToDriftWarningOrUnchanged_WhenDriftIsEmpty_ReturnsEmpty()
        {
            string warning = PausePointEnableWarnings.AppendCompiledMethodSpanToDriftWarningOrUnchanged(
                string.Empty,
                "Example.Run",
                8,
                11);

            Assert.That(warning, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// What: retarget warning interpolates resolved method, requested line, and edited span.
        /// </summary>
        [Test]
        public void BuildRetargetedToHotReloadPatchWarningOrEmpty_WhenRetargeted_ReturnsFormattedWarning()
        {
            string warning = PausePointEnableWarnings.BuildRetargetedToHotReloadPatchWarningOrEmpty(
                true,
                "Example.Run",
                42,
                10,
                20);

            Assert.That(
                warning,
                Is.EqualTo(
                    string.Format(
                        SourcePausePointConstants.HotReloadRetargetedToEditedFileWarningFormat,
                        "Example.Run",
                        42,
                        10,
                        20)));
        }

        /// <summary>
        /// What: the retarget helper stays silent when the marker did not retarget.
        /// </summary>
        [Test]
        public void BuildRetargetedToHotReloadPatchWarningOrEmpty_WhenNotRetargeted_ReturnsEmpty()
        {
            string warning = PausePointEnableWarnings.BuildRetargetedToHotReloadPatchWarningOrEmpty(
                false,
                "Example.Run",
                42,
                10,
                20);

            Assert.That(warning, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// What: the PDB-unavailable helper interpolates method name and requested line.
        /// </summary>
        [Test]
        public void BuildPatchedMethodPdbUnavailableWarningOrEmpty_WhenUnavailable_ReturnsFormattedWarning()
        {
            string warning = PausePointEnableWarnings.BuildPatchedMethodPdbUnavailableWarningOrEmpty(
                true,
                "Example.Run",
                42);

            Assert.That(
                warning,
                Is.EqualTo(
                    string.Format(
                        SourcePausePointConstants.HotReloadPatchedMethodPdbUnavailableWarningFormat,
                        "Example.Run",
                        42)));
        }

        /// <summary>
        /// What: the PDB-unavailable helper stays silent when the shim PDB is present.
        /// </summary>
        [Test]
        public void BuildPatchedMethodPdbUnavailableWarningOrEmpty_WhenAvailable_ReturnsEmpty()
        {
            string warning = PausePointEnableWarnings.BuildPatchedMethodPdbUnavailableWarningOrEmpty(
                false,
                "Example.Run",
                42);

            Assert.That(warning, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// What: nearby compiled spans are formatted as a suffix on a resolve-failure message.
        /// </summary>
        [Test]
        public void AppendNearbyCompiledMethodsSuffix_WhenNearbyMethodsExist_AppendsFormattedSpans()
        {
            string errorMessage = "No sequence point found on or after line 9999 in 'file'.";
            SourcePausePointNearbyCompiledMethod[] nearby =
            {
                new SourcePausePointNearbyCompiledMethod("CompiledMethodSpanFixture.Target", 8, 11),
                new SourcePausePointNearbyCompiledMethod("CompiledMethodSpanFixture.OtherMethod", 15, 18)
            };

            string message = PausePointEnableWarnings.AppendNearbyCompiledMethodsSuffix(errorMessage, nearby);

            Assert.That(
                message,
                Is.EqualTo(
                    errorMessage
                    + SourcePausePointConstants.NearbyCompiledMethodsPrefix
                    + "'CompiledMethodSpanFixture.Target' spans lines 8-11"
                    + "; "
                    + "'CompiledMethodSpanFixture.OtherMethod' spans lines 15-18"
                    + "."));
        }

        /// <summary>
        /// What: an empty nearby list leaves the resolve-failure message unchanged.
        /// </summary>
        [Test]
        public void AppendNearbyCompiledMethodsSuffix_WhenNearbyListIsEmpty_LeavesMessageUnchanged()
        {
            string errorMessage = "No sequence point found on or after line 9999 in 'file'.";

            string message = PausePointEnableWarnings.AppendNearbyCompiledMethodsSuffix(
                errorMessage,
                Array.Empty<SourcePausePointNearbyCompiledMethod>());

            Assert.That(message, Is.EqualTo(errorMessage));
        }

        /// <summary>
        /// What: enable resolve-failure on a real PDB fixture appends the nearest compiled
        /// method span from that file.
        /// </summary>
        [Test]
        public void Enable_WhenResolveFails_AppendsNearbyCompiledMethodSpans()
        {
            const string file =
                "Assets/Tests/Editor/SourcePausePointResolver/Fixtures/CompiledMethodSpanFixture.cs";
            SourcePausePointResolveResult otherMethod = SourcePausePointResolver.Resolve(file, 16);
            Assert.That(otherMethod.Success, Is.True, otherMethod.ErrorMessage);
            Assert.That(otherMethod.Resolution.CompiledMethodStartLine, Is.GreaterThan(0));
            Assert.That(otherMethod.Resolution.CompiledMethodEndLine, Is.GreaterThan(0));

            PausePointResponse response = new PausePointUseCase().Enable(new EnablePausePointSchema
            {
                File = file,
                Line = 9999,
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.SingleShot
            });

            Assert.That(response.Success, Is.False);
            Assert.That(response.ErrorCode, Is.EqualTo(SourcePausePointConstants.ErrorCodeResolveFailed));
            string expectedMessage =
                "No sequence point found on or after line 9999 in '" + file + "'."
                + SourcePausePointConstants.NearbyCompiledMethodsPrefix
                + string.Format(
                    SourcePausePointConstants.NearbyCompiledMethodSpanFormat,
                    "CompiledMethodSpanFixture.OtherMethod",
                    otherMethod.Resolution.CompiledMethodStartLine,
                    otherMethod.Resolution.CompiledMethodEndLine)
                + ".";
            Assert.That(response.Message, Is.EqualTo(expectedMessage));
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

        /// <summary>
        /// What: a line inside a patched method with no shim PDB is a distinct Kind, not
        /// NotInPatchedMethod.
        /// </summary>
        [Test]
        public void ShimResolve_WhenLineIsInPatchedMethodButPdbBytesAreMissing_ReturnsPatchedMethodPdbUnavailable()
        {
            SourcePausePointShimResolution resolution = SourcePausePointShimResolver.Resolve(
                CreatePdbUnavailableLookup(CompiledLineDriftProbeMethod(), 10),
                ResolveFailureFile,
                10);

            Assert.That(
                resolution.Kind,
                Is.EqualTo(SourcePausePointShimResolveKind.PatchedMethodPdbUnavailable));
            Assert.That(
                resolution.MethodDisplayName,
                Is.EqualTo("PausePointCompiledLineMapWarningTests.CompiledLineDriftProbe"));
        }

        /// <summary>
        /// What: a patched method whose shim lookup has no PDB bytes falls through to compiled
        /// resolve and emits the dedicated warning instead of the generic edited-file sentence.
        /// </summary>
        [Test]
        public void Enable_WhenPatchedMethodHasNoPdbBytes_EmitsDedicatedWarningOnSuccess()
        {
            string absolutePath = Path.Combine(
                UnityCliLoopPathResolver.GetProjectRoot(),
                ResolveFailureFile);
            string diskSource = File.ReadAllText(absolutePath);
            int requestedLine = FindLineNumberContaining(
                diskSource,
                "compiled-line-drift" + "-probe-unique") + 1;
            Assert.That(requestedLine, Is.GreaterThan(1));

            Func<string, HotReloadShimFileLookup> previous =
                HotReloadPausePointCoordination.GetShimLookupForFile;
            try
            {
                HotReloadPausePointCoordination.GetShimLookupForFile =
                    _ => CreatePdbUnavailableLookup(CompiledLineDriftProbeMethod(), requestedLine);
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
                Assert.That(response.RetargetedToHotReloadPatch, Is.False);
                string dedicatedWarning = string.Format(
                    SourcePausePointConstants.HotReloadPatchedMethodPdbUnavailableWarningFormat,
                    "PausePointCompiledLineMapWarningTests.CompiledLineDriftProbe",
                    requestedLine);
                string expectedWarning = PausePointEnableWarnings.MergeWarnings(
                    PausePointEnableWarnings.MergeWarnings(
                        PausePointEnableWarnings.CreateEnableWarning(),
                        dedicatedWarning),
                    SourcePausePointConstants.SmallMethodInliningRiskWarning);
                Assert.That(response.Warning, Is.EqualTo(expectedWarning));
                Assert.That(response.Warning, Does.Not.Contain(GenericPatchedMethodsUseEditedFileSentence));
            }
            finally
            {
                HotReloadPausePointCoordination.GetShimLookupForFile = previous;
            }
        }

        /// <summary>
        /// What: resolve failure inside a patched method with no shim PDB uses the dedicated
        /// warning, not the generic compiled-line-map failure sentence.
        /// </summary>
        [Test]
        public void Enable_WhenPatchedMethodHasNoPdbBytes_EmitsDedicatedWarningOnResolveFailure()
        {
            Func<string, HotReloadShimFileLookup> previous =
                HotReloadPausePointCoordination.GetShimLookupForFile;
            try
            {
                HotReloadPausePointCoordination.GetShimLookupForFile =
                    _ => CreatePdbUnavailableLookup(CompiledLineDriftProbeMethod(), UnresolvableLine);
                PausePointResponse response = EnableUnresolvableLine();

                Assert.That(response.Success, Is.False);
                Assert.That(response.ErrorCode, Is.EqualTo(SourcePausePointConstants.ErrorCodeResolveFailed));
                Assert.That(
                    response.Warning,
                    Is.EqualTo(
                        string.Format(
                            SourcePausePointConstants.HotReloadPatchedMethodPdbUnavailableWarningFormat,
                            "PausePointCompiledLineMapWarningTests.CompiledLineDriftProbe",
                            UnresolvableLine)));
                Assert.That(response.Warning, Does.Not.Contain(GenericPatchedMethodsUseEditedFileSentence));
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

        private static HotReloadShimFileLookup CreatePdbUnavailableLookup(MethodBase patchedMethod, int line)
        {
            HotReloadShimMethodLookup[] methods =
            {
                new HotReloadShimMethodLookup(
                    patchedMethod,
                    patchedMethod,
                    false,
                    line,
                    line)
            };
            return new HotReloadShimFileLookup(
                Array.Empty<byte>(),
                null,
                null,
                methods);
        }

        private static MethodBase CompiledLineDriftProbeMethod()
        {
            MethodBase method = typeof(PausePointCompiledLineMapWarningTests).GetMethod(
                nameof(CompiledLineDriftProbe),
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return method;
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
