using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// FB9 repro: a top-of-file insert plus a hot reload of one method must not make --line on
    /// an unpatched method resolve to a different method; --line is an edited-file line mapped
    /// onto the last compiled source.
    /// </summary>
    public class HotReloadPausePointLineDriftTests
    {
        private const string FixtureProjectRelativePath =
            "Assets/Tests/Editor/HotReload/HotReloadPausePointLineDriftFixture.cs";

        private const string RestoreSnapshotSentinel = "SENTINEL_RESTORE_LINE_TEXT";

        private HotReloadDomainTestScope _scope;

        [SetUp]
        public void SetUp()
        {
            _scope = new HotReloadDomainTestScope();
            UloopPausePointRegistry.ConfigureForTests(new FakePausePointPauseController(), () => DateTime.UtcNow);
        }

        [TearDown]
        public void TearDown()
        {
            _scope.Dispose();
            SourcePausePointPatcher.UnpatchAll();
            UloopPausePointRegistry.ResetForTests();
        }

        /// <summary>
        /// What: with a snapshot identical to the edited file, enable on UnpatchedTarget arms the
        /// requested line on the edited-file basis. In this harness the PDB line numbers match the file on disk, so the injected
        /// snapshot plays the compiled source (the roles are reversed from production).
        /// </summary>
        [Test]
        public async Task Enable_UnpatchedMethodWithIdenticalSnapshot_ArmsTheRequestedLineOnTheEditedFileBasis()
        {
            string onDisk = File.ReadAllText(ResolveFixtureAbsolutePath());
            int unpatchedLine = FindLineNumber(onDisk, "return 22;");
            Assert.That(unpatchedLine, Is.GreaterThan(0));
            await HotReloadFromEditedSourceAsync(
                BuildEditedSourceWithTopPaddingAndPatchedReturn(onDisk),
                "LineDriftIdenticalSnapshot.cs");

            PausePointResponse enable = EnableFixtureLineWithSnapshot(unpatchedLine, (file, dllPath) => onDisk);

            Assert.That(enable.Success, Is.True, enable.Message + " / " + enable.RecommendedNextAction);
            Assert.That(enable.ResolvedLine, Is.EqualTo(unpatchedLine));
            Assert.That(enable.ResolvedMethod, Does.Contain(nameof(HotReloadPausePointLineDriftFixture.UnpatchedTarget)));
            Assert.That(enable.LineBasis, Is.EqualTo("EditedFile"));
            Assert.That(enable.ResolvedLineText, Does.Contain("return 22;"));
            Assert.That(enable.RetargetedToHotReloadPatch, Is.False);
            Assert.That(enable.Warning ?? string.Empty, Does.Not.Contain("No verified source snapshot"));
        }

        /// <summary>
        /// What: when the compiled source had three fewer lines above the class, enable on
        /// AfterTarget's attribute line arms the mapped compiled line and reports the edited line.
        /// In this harness the PDB line numbers match the file on disk, so the injected
        /// snapshot plays the compiled source (the roles are reversed from production).
        /// </summary>
        [Test]
        public async Task Enable_UnpatchedMethodAfterTopOfFileInsert_ArmsTheMappedCompiledLineAndReportsTheEditedLine()
        {
            string onDisk = File.ReadAllText(ResolveFixtureAbsolutePath());
            int requestedLine = FindLineNumber(onDisk, "public int AfterTarget()") - 1;
            Assert.That(requestedLine, Is.GreaterThan(0));
            string snapshotWithInsert = InsertBlankLinesAfterLine(onDisk, 9, 3);
            await HotReloadFromEditedSourceAsync(
                BuildEditedSourceWithTopPaddingAndPatchedReturn(onDisk),
                "LineDriftShiftedSnapshot.cs");

            PausePointResponse enable = EnableFixtureLineWithSnapshot(requestedLine, (file, dllPath) => snapshotWithInsert);

            Assert.That(enable.Success, Is.True, enable.Message + " / " + enable.RecommendedNextAction);
            Assert.That(enable.ResolvedLine, Is.EqualTo(requestedLine));
            Assert.That(enable.ResolvedMethod, Does.Contain(nameof(HotReloadPausePointLineDriftFixture.AfterTarget)));
            Assert.That(enable.LineBasis, Is.EqualTo("EditedFile"));
        }

        /// <summary>
        /// What: a line whose statement text differs from the compiled snapshot is refused as not
        /// compiled instead of arming whatever compiled statement sits at that line number.
        /// In this harness the PDB line numbers match the file on disk, so the injected
        /// snapshot plays the compiled source (the roles are reversed from production).
        /// </summary>
        [Test]
        public async Task Enable_UnpatchedMethodWhoseLineChangedOnDisk_RefusesLineNotCompiled()
        {
            string onDisk = File.ReadAllText(ResolveFixtureAbsolutePath());
            int unpatchedLine = FindLineNumber(onDisk, "return 22;");
            Assert.That(unpatchedLine, Is.GreaterThan(0));
            string snapshotWithOtherReturn = onDisk.Replace("return 22;", "return 99;", StringComparison.Ordinal);
            await HotReloadFromEditedSourceAsync(
                BuildEditedSourceWithTopPaddingAndPatchedReturn(onDisk),
                "LineDriftChangedLine.cs");

            PausePointResponse enable = EnableFixtureLineWithSnapshot(unpatchedLine, (file, dllPath) => snapshotWithOtherReturn);

            Assert.That(enable.Success, Is.False);
            Assert.That(enable.ErrorCode, Is.EqualTo("PAUSE_POINT_LINE_NOT_COMPILED"));
            Assert.That(enable.Message, Does.Contain("is not in the last compiled source"));
            Assert.That(enable.RecommendedNextAction, Does.Contain("uloop compile"));
        }

        /// <summary>
        /// What: the same top-of-file insert still retargets a patched method onto the
        /// hot-reload body, emits the edited-file line-basis warning, and does not emit
        /// the compiled-line-map warning.
        /// </summary>
        [Test]
        public async Task Enable_PatchedMethodAfterTopOfFileInsert_DoesNotWarnAboutCompiledLineMap()
        {
            string onDisk = File.ReadAllText(ResolveFixtureAbsolutePath());
            string edited = BuildEditedSourceWithTopPaddingAndPatchedReturn(onDisk);
            int editedPatchLine = FindLineNumber(edited, "return 111;");
            Assert.That(editedPatchLine, Is.GreaterThan(0));

            await HotReloadFromEditedSourceAsync(edited, "LineDriftPatched.cs");

            PausePointResponse enable = new PausePointUseCase().Enable(new EnablePausePointSchema
            {
                File = FixtureProjectRelativePath,
                Line = editedPatchLine,
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.SingleShot
            });

            Assert.That(enable.Success, Is.True, enable.Message + " / " + enable.RecommendedNextAction);
            Assert.That(enable.RetargetedToHotReloadPatch, Is.True);
            Assert.That(enable.LineBasis, Is.EqualTo("EditedFile"));
            Assert.That(enable.ResolvedMethod, Does.Contain(nameof(HotReloadPausePointLineDriftFixture.PatchTarget)));
            Assert.That(enable.Warning ?? string.Empty, Does.Not.Contain("has active hot-reload patches"));

            HotReloadShimMethodLookup patchedEntry = FindPatchedShimEntry();
            string expectedWarning = PausePointEnableWarnings.MergeWarnings(
                PausePointEnableWarnings.MergeWarnings(
                    PausePointEnableWarnings.CreateEnableWarning(),
                    PausePointEnableWarnings.BuildRetargetedToHotReloadPatchWarningOrEmpty(
                        true,
                        enable.ResolvedMethod,
                        editedPatchLine,
                        patchedEntry.SourceStartLine,
                        patchedEntry.SourceEndLine)),
                SourcePausePointConstants.SmallMethodInliningRiskWarning);
            Assert.That(enable.Warning, Is.EqualTo(expectedWarning));
        }

        /// <summary>
        /// What: --method that names a compiled neighbor skips the patched shim entry and
        /// falls through to the compiled line map instead of retargeting onto the patch.
        /// </summary>
        [Test]
        public async Task Enable_PatchedLineWithUnpatchedMethodFilter_FallsThroughToCompiledResolver()
        {
            string onDisk = File.ReadAllText(ResolveFixtureAbsolutePath());
            string edited = BuildEditedSourceWithTopPaddingAndPatchedReturn(onDisk);
            int editedPatchLine = FindLineNumber(edited, "return 111;");
            Assert.That(editedPatchLine, Is.GreaterThan(0));

            await HotReloadFromEditedSourceAsync(edited, "LineDriftMethodFilter.cs");

            PausePointResponse enable = new PausePointUseCase().Enable(new EnablePausePointSchema
            {
                File = FixtureProjectRelativePath,
                Line = editedPatchLine,
                Method = nameof(HotReloadPausePointLineDriftFixture.AfterTarget),
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.SingleShot
            });

            Assert.That(enable.Success, Is.True, enable.Message + " / " + enable.RecommendedNextAction);
            Assert.That(enable.RetargetedToHotReloadPatch, Is.False);
            SourcePausePointResolveResult expected = SourcePausePointResolver.Resolve(
                FixtureProjectRelativePath,
                editedPatchLine,
                nameof(HotReloadPausePointLineDriftFixture.AfterTarget));
            Assert.That(expected.Success, Is.True, expected.ErrorMessage);
            Assert.That(enable.ResolvedMethod, Is.EqualTo(expected.Resolution.MethodDisplayName));
        }

        /// <summary>
        /// What: --method that names the patched method keeps enable on the shim path for
        /// both a simple name and Type.Method.
        /// </summary>
        [Test]
        public async Task Enable_PatchedLineWithMatchingMethodFilter_StaysOnShim()
        {
            string onDisk = File.ReadAllText(ResolveFixtureAbsolutePath());
            string edited = BuildEditedSourceWithTopPaddingAndPatchedReturn(onDisk);
            int editedPatchLine = FindLineNumber(edited, "return 111;");
            Assert.That(editedPatchLine, Is.GreaterThan(0));

            await HotReloadFromEditedSourceAsync(edited, "LineDriftMethodFilterMatch.cs");
            HotReloadShimMethodLookup patchedEntry = FindPatchedShimEntry();
            string expectedResolvedMethod = patchedEntry.OriginalMethod.ToString();

            PausePointResponse simple = EnablePatchedLineWithMethodFilter(
                editedPatchLine,
                nameof(HotReloadPausePointLineDriftFixture.PatchTarget));
            Assert.That(simple.Success, Is.True, simple.Message + " / " + simple.RecommendedNextAction);
            Assert.That(simple.RetargetedToHotReloadPatch, Is.True);
            Assert.That(simple.ResolvedMethod, Is.EqualTo(expectedResolvedMethod));

            SourcePausePointPatcher.UnpatchAll();
            UloopPausePointRegistry.ResetForTests();
            UloopPausePointRegistry.ConfigureForTests(new FakePausePointPauseController(), () => DateTime.UtcNow);

            PausePointResponse qualified = EnablePatchedLineWithMethodFilter(
                editedPatchLine,
                nameof(HotReloadPausePointLineDriftFixture) + "."
                + nameof(HotReloadPausePointLineDriftFixture.PatchTarget));
            Assert.That(qualified.Success, Is.True, qualified.Message + " / " + qualified.RecommendedNextAction);
            Assert.That(qualified.RetargetedToHotReloadPatch, Is.True);
            Assert.That(qualified.ResolvedMethod, Is.EqualTo(expectedResolvedMethod));
        }

        /// <summary>
        /// What: without a verified snapshot, enable on UnpatchedTarget falls back to compiled line
        /// numbers, says so in a warning, and leaves ResolvedLineText empty instead of reading the
        /// edited file on disk. In this harness the PDB line numbers match the file on disk, so the injected
        /// snapshot plays the compiled source (the roles are reversed from production).
        /// </summary>
        [Test]
        public async Task Enable_UnpatchedMethodWithoutSnapshot_FallsBackToCompiledLinesWithAWarning()
        {
            string onDisk = File.ReadAllText(ResolveFixtureAbsolutePath());
            int unpatchedLine = FindLineNumber(onDisk, "return 22;");
            Assert.That(unpatchedLine, Is.GreaterThan(0));
            await HotReloadFromEditedSourceAsync(
                BuildEditedSourceWithTopPaddingAndPatchedReturn(onDisk),
                "LineDriftNoSnapshot.cs");

            PausePointResponse enable = EnableFixtureLineWithSnapshot(unpatchedLine, (file, dllPath) => null);

            Assert.That(enable.Success, Is.True, enable.Message + " / " + enable.RecommendedNextAction);
            Assert.That(enable.ResolvedLine, Is.EqualTo(unpatchedLine));
            Assert.That(enable.LineBasis, Is.EqualTo("LastCompiledSource"));
            Assert.That(enable.Warning, Does.Contain("No verified source snapshot"));
            Assert.That(enable.ResolvedLineText, Is.Empty);
        }

        /// <summary>
        /// What: restore-after-revert fills ResolvedLineText from the (file, dll) snapshot
        /// Func, not from the edited file on disk.
        /// </summary>
        [Test]
        public async Task RevertAll_RestoreUsesSnapshotSentinel_NotDiskText()
        {
            PausePointResponse enable = await EnablePatchedLineThenPrepareRestoreAsync(
                "LineDriftRestoreSentinel.cs");
            HotReloadSidePortScope snapshotScope = new HotReloadSidePortScope();
            snapshotScope.Port.VerifiedSnapshotSource =
                (string file, string dllPath) =>
                {
                    Assert.That(file, Does.Contain("HotReloadPausePointLineDriftFixture.cs"));
                    Assert.That(dllPath, Is.Not.Null.And.Not.Empty);
                    return BuildSentinelSnapshot(RestoreSnapshotSentinel, 80);
                };
            try
            {
                HotReloadCompositionRoot.Services.Patcher.RevertAll();
            }
            finally
            {
                snapshotScope.Dispose();
            }

            UloopPausePointSnapshot afterRevert = UloopPausePointRegistry.GetStatus(enable.Id);
            Assert.That(afterRevert.RetargetedToHotReloadPatch, Is.False);
            Assert.That(afterRevert.ResolvedLineText, Is.EqualTo(RestoreSnapshotSentinel));
        }

        /// <summary>
        /// What: restore-after-revert leaves ResolvedLineText empty when hot reload has no
        /// (file, dll) snapshot to give, and does not fall back to disk.
        /// </summary>
        [Test]
        public async Task RevertAll_RestoreWithoutSnapshot_LeavesResolvedLineTextEmpty()
        {
            PausePointResponse enable = await EnablePatchedLineThenPrepareRestoreAsync(
                "LineDriftRestoreNoSnapshot.cs");
            HotReloadSidePortScope snapshotScope = new HotReloadSidePortScope();
            snapshotScope.Port.VerifiedSnapshotSource = (string file, string dllPath) => null;
            try
            {
                HotReloadCompositionRoot.Services.Patcher.RevertAll();
            }
            finally
            {
                snapshotScope.Dispose();
            }

            UloopPausePointSnapshot afterRevert = UloopPausePointRegistry.GetStatus(enable.Id);
            Assert.That(afterRevert.RetargetedToHotReloadPatch, Is.False);
            Assert.That(afterRevert.ResolvedLineText, Is.Empty);
        }

        private static PausePointResponse EnableFixtureLineWithSnapshot(
            int line,
            Func<string, string, string> verifiedSnapshotSource)
        {
            using (HotReloadSidePortScope snapshotScope = new HotReloadSidePortScope())
            {
                snapshotScope.Port.VerifiedSnapshotSource = verifiedSnapshotSource;
                return new PausePointUseCase().Enable(new EnablePausePointSchema
                {
                    File = FixtureProjectRelativePath,
                    Line = line,
                    TimeoutSeconds = 30,
                    Mode = UloopPausePointCaptureMode.SingleShot
                });
            }
        }

        private static string InsertBlankLinesAfterLine(string source, int line, int count)
        {
            List<string> lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
            lines.InsertRange(line, Enumerable.Repeat(string.Empty, count));
            return string.Join("\n", lines);
        }

        private static PausePointResponse EnablePatchedLineWithMethodFilter(int line, string methodFilter)
        {
            return new PausePointUseCase().Enable(new EnablePausePointSchema
            {
                File = FixtureProjectRelativePath,
                Line = line,
                Method = methodFilter,
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.SingleShot
            });
        }

        private static async Task<PausePointResponse> EnablePatchedLineThenPrepareRestoreAsync(
            string editedFileName)
        {
            string onDisk = File.ReadAllText(ResolveFixtureAbsolutePath());
            string edited = onDisk.Replace(
                "            return 11;",
                "            return 111;",
                StringComparison.Ordinal);
            int editedPatchLine = FindLineNumber(edited, "return 111;");
            Assert.That(editedPatchLine, Is.GreaterThan(0));
            await HotReloadFromEditedSourceAsync(edited, editedFileName);

            PausePointResponse enable = new PausePointUseCase().Enable(new EnablePausePointSchema
            {
                File = FixtureProjectRelativePath,
                Line = editedPatchLine,
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.Continuous
            });
            Assert.That(enable.Success, Is.True, enable.Message + " / " + enable.RecommendedNextAction);
            Assert.That(enable.RetargetedToHotReloadPatch, Is.True);
            return enable;
        }

        private static HotReloadShimMethodLookup FindPatchedShimEntry()
        {
            IHotReloadPausePointPort hotReloadSide = HotReloadPausePointCoordination.HotReloadSide;
            Assert.That(hotReloadSide, Is.Not.Null);
            HotReloadShimFileLookup lookup = hotReloadSide.GetShimLookupForFile(FixtureProjectRelativePath);
            Assert.That(lookup, Is.Not.Null);
            Assert.That(lookup.Methods, Is.Not.Null);

            HotReloadShimMethodLookup patchedEntry = null;
            foreach (HotReloadShimMethodLookup method in lookup.Methods)
            {
                if (method.OriginalMethod != null
                    && method.OriginalMethod.Name == nameof(HotReloadPausePointLineDriftFixture.PatchTarget))
                {
                    patchedEntry = method;
                    break;
                }
            }

            Assert.That(patchedEntry, Is.Not.Null);
            Assert.That(patchedEntry.SourceStartLine, Is.GreaterThan(0));
            Assert.That(patchedEntry.SourceEndLine, Is.GreaterThanOrEqualTo(patchedEntry.SourceStartLine));
            return patchedEntry;
        }

        private static string BuildSentinelSnapshot(string sentinel, int lineCount)
        {
            List<string> lines = new List<string>();
            for (int index = 0; index < lineCount; index++)
            {
                lines.Add(sentinel);
            }

            return string.Join("\n", lines);
        }

        private static string BuildEditedSourceWithTopPaddingAndPatchedReturn(string onDisk)
        {
            string padded = "// drift-pad-1\n// drift-pad-2\n// drift-pad-3\n" + onDisk;
            string edited = padded.Replace(
                "            return 11;",
                "            return 111;",
                StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk));
            return edited;
        }

        private static async Task HotReloadFromEditedSourceAsync(string editedSource, string fileName)
        {
            string fixturePath = ResolveFixtureAbsolutePath();
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string directory = Path.Combine(projectRoot, HotReloadConstants.TestSourcesRelativeDirectory);
            Directory.CreateDirectory(directory);
            string editedPath = Path.Combine(directory, fileName);
            File.WriteAllText(editedPath, editedSource);

            HotReloadOrchestratorResult result = await HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);
            Assert.That(
                result.Methods.Any(m => m.Kind == HotReloadMethodOutcomeKind.Failed),
                Is.False,
                FormatHotReloadOutcomes(result));
            Assert.That(
                result.Methods.Any(m => m.Kind == HotReloadMethodOutcomeKind.Patched),
                Is.True,
                FormatHotReloadOutcomes(result));
        }

        private static string ResolveFixtureAbsolutePath()
        {
            return Path.GetFullPath(
                Path.Combine(
                    Application.dataPath,
                    "Tests",
                    "Editor",
                    "HotReload",
                    "HotReloadPausePointLineDriftFixture.cs"));
        }

        private static int FindLineNumber(string source, string fragment)
        {
            string[] lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            for (int index = 0; index < lines.Length; index++)
            {
                if (lines[index].Contains(fragment, StringComparison.Ordinal))
                {
                    return index + 1;
                }
            }

            return -1;
        }

        private static string FormatHotReloadOutcomes(HotReloadOrchestratorResult result)
        {
            List<string> lines = new List<string>();
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                lines.Add(outcome.Kind + " " + outcome.Method + " :: " + outcome.Reason);
            }

            return string.Join("\n", lines);
        }

        private sealed class FakePausePointPauseController : IUloopPausePointPauseController
        {
            public bool IsPlaying => true;
            public bool IsPaused => false;

            public void Pause()
            {
            }

            public void Resume()
            {
            }
        }
    }
}
