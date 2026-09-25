using System;
using System.Collections.Generic;
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

        private const string ExampleResolvedMethod = "ExampleType.ExampleMethod";

        private const string ExpectedCompiledLineMapWarning =
            "--line resolved against the last compiled source, not the edited file: "
            + "'Assets/Scripts/Example.cs' has active hot-reload patches and the resolved method "
            + "'ExampleType.ExampleMethod' is not patched by this reload. Verify ResolvedLineText "
            + "matches the statement you meant, or run 'uloop compile' and re-enable.";

        private const string ExpectedCompiledLineMapMatchedWarning =
            "No drift is visible at this line: the statement text at the resolved line is "
            + "identical in the edited file. 'Assets/Scripts/Example.cs' has active hot-reload "
            + "patches and the resolved method 'ExampleType.ExampleMethod' is not patched by "
            + "this reload, so --line resolved against the last compiled source, not the edited file.";

        private const string ResolveFailureFile =
            "Assets/Tests/Editor/PausePointCompiledLineMapWarningTests.cs";

        private const int LinePastTheEndOfTheFile = 999999;

        private const string CompiledMethodSpanFixtureFile =
            "Assets/Tests/Editor/SourcePausePointResolver/Fixtures/CompiledMethodSpanFixture.cs";

        private const int CompiledMethodSpanFixtureBlankLine = 12;

        private const string MissingEditedLineFile =
            "Assets/Tests/Editor/SourcePausePointResolver/Fixtures/DoesNotExistEditedLineRead.cs";

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
            Assert.That(warning, Does.Not.Contain("last compiled source"));
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
                Line = 19,
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.SingleShot
            });

            Assert.That(response.Success, Is.False);
            Assert.That(response.ErrorCode, Is.EqualTo(SourcePausePointConstants.ErrorCodeResolveFailed));
            string expectedMessage =
                "No sequence point found on or after line 19 in '" + file + "'."
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
        /// What: a line inside a patched method whose shim lookup has no PDB bytes is refused as
        /// patched by hot reload, because the compiled body no longer runs and the patch cannot be
        /// resolved to a statement.
        /// </summary>
        [Test]
        public void Enable_WhenPatchedMethodHasNoPdbBytes_RefusesAsPatchedByHotReload()
        {
            string absolutePath = Path.Combine(
                UnityCliLoopPathResolver.GetProjectRoot(),
                ResolveFailureFile);
            string diskSource = File.ReadAllText(absolutePath);
            int requestedLine = FindLineNumberContaining(
                diskSource,
                "compiled-line-drift" + "-probe-unique") + 1;
            Assert.That(requestedLine, Is.GreaterThan(1));

            using (HotReloadSidePortScope hotReloadSideScope = new HotReloadSidePortScope())
            {
                hotReloadSideScope.Port.ShimLookupForFile =
                    _ => CreatePdbUnavailableLookup(CompiledLineDriftProbeMethod(), requestedLine);
                PausePointResponse response = new PausePointUseCase().Enable(new EnablePausePointSchema
                {
                    File = ResolveFailureFile,
                    Line = requestedLine,
                    TimeoutSeconds = 30,
                    Mode = UloopPausePointCaptureMode.SingleShot
                });

                Assert.That(response.Success, Is.False);
                Assert.That(response.ErrorCode, Is.EqualTo("PAUSE_POINT_PATCHED_BY_HOT_RELOAD"));
                Assert.That(
                    response.Message,
                    Is.EqualTo(
                        string.Format(
                            SourcePausePointConstants.HotReloadPatchedMethodPdbUnavailableWarningFormat,
                            "PausePointCompiledLineMapWarningTests.CompiledLineDriftProbe",
                            requestedLine)));
                Assert.That(response.RecommendedNextAction, Does.Contain("uloop compile"));
                Assert.That(response.Message, Does.Contain("cannot be placed on the running code"));
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
