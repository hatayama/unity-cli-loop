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
    /// FB9 repro: a top-of-file insert plus a hot reload of one method makes --line on an
    /// unpatched method resolve against the compiled line map (a different method).
    /// </summary>
    public class HotReloadPausePointLineDriftTests
    {
        private const string FixtureProjectRelativePath =
            "Assets/Tests/Editor/HotReload/HotReloadPausePointLineDriftFixture.cs";

        private const string HotReloadCompiledLineMapWarning =
            "'Assets/Tests/Editor/HotReload/HotReloadPausePointLineDriftFixture.cs' has active "
            + "hot-reload patches. For methods this reload did not patch, --line resolves against "
            + "the last compiled source, not the edited file. Verify ResolvedMethod and "
            + "ResolvedLineText, or run 'uloop compile' and re-enable.";

        [SetUp]
        public void SetUp()
        {
            UloopPausePointRegistry.ConfigureForTests(new FakePausePointPauseController(), () => DateTime.UtcNow);
        }

        [TearDown]
        public void TearDown()
        {
            HotReloadPatcher.RevertAll();
            SourcePausePointPatcher.UnpatchAll();
            UloopPausePointRegistry.ResetForTests();
        }

        /// <summary>
        /// What: after a 3-line top-of-file insert and a hot reload of PatchTarget only,
        /// enable on UnpatchedTarget's edited line resolves to AfterTarget, warns about the
        /// compiled line map, and fills ResolvedLineText from the compiled snapshot.
        /// </summary>
        [Test]
        public async Task Enable_UnpatchedMethodAfterTopOfFileInsert_WarnsAndFillsCompiledLineText()
        {
            string onDisk = File.ReadAllText(ResolveFixtureAbsolutePath());
            string edited = BuildEditedSourceWithTopPaddingAndPatchedReturn(onDisk);
            int compiledUnpatchedLine = FindLineNumber(onDisk, "return 22;");
            int editedUnpatchedLine = FindLineNumber(edited, "return 22;");
            int compiledAfterStart = FindLineNumber(onDisk, "public int AfterTarget()");
            Assert.That(compiledUnpatchedLine, Is.GreaterThan(0));
            Assert.That(editedUnpatchedLine, Is.EqualTo(compiledUnpatchedLine + 3));
            Assert.That(
                editedUnpatchedLine,
                Is.GreaterThanOrEqualTo(compiledAfterStart),
                "The edited UnpatchedTarget line must land inside AfterTarget's compiled range.");

            await HotReloadFromEditedSourceAsync(edited, "LineDriftUnpatched.cs");

            PausePointResponse enable = new PausePointUseCase().Enable(new EnablePausePointSchema
            {
                File = FixtureProjectRelativePath,
                Line = editedUnpatchedLine,
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.SingleShot
            });

            Assert.That(enable.Success, Is.True, enable.Message + " / " + enable.RecommendedNextAction);
            Assert.That(enable.RetargetedToHotReloadPatch, Is.False);
            Assert.That(enable.ResolvedMethod, Does.Contain(nameof(HotReloadPausePointLineDriftFixture.AfterTarget)));
            Assert.That(enable.ResolvedMethod, Does.Not.Contain(nameof(HotReloadPausePointLineDriftFixture.UnpatchedTarget)));
            Assert.That(enable.Warning, Does.Contain(HotReloadCompiledLineMapWarning));

            string compiledLineText = SourcePausePointSourceLineReader.ReadLineText(
                ResolveFixtureAbsolutePath(),
                enable.ResolvedLine);
            Assert.That(enable.ResolvedLineText, Is.EqualTo(compiledLineText));
            Assert.That(enable.ResolvedLineText, Is.Not.Empty);
        }

        /// <summary>
        /// What: the same top-of-file insert still retargets a patched method onto the
        /// hot-reload body and does not emit the compiled-line-map warning.
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
            Assert.That(enable.ResolvedMethod, Does.Contain(nameof(HotReloadPausePointLineDriftFixture.PatchTarget)));
            Assert.That(enable.Warning ?? string.Empty, Does.Not.Contain("has active hot-reload patches"));
        }

        /// <summary>
        /// What: compiled-side enable with an active hot-reload file but no verified snapshot
        /// leaves ResolvedLineText empty instead of reading the edited file on disk.
        /// </summary>
        [Test]
        public async Task Enable_UnpatchedMethodWithoutSnapshot_LeavesResolvedLineTextEmpty()
        {
            string onDisk = File.ReadAllText(ResolveFixtureAbsolutePath());
            string edited = BuildEditedSourceWithTopPaddingAndPatchedReturn(onDisk);
            int editedUnpatchedLine = FindLineNumber(edited, "return 22;");
            await HotReloadFromEditedSourceAsync(edited, "LineDriftNoSnapshot.cs");

            Func<string, string> previous = HotReloadPausePointCoordination.GetVerifiedSnapshotSourceForFile;
            HotReloadPausePointCoordination.GetVerifiedSnapshotSourceForFile = _ => null;
            PausePointResponse enable;
            try
            {
                enable = new PausePointUseCase().Enable(new EnablePausePointSchema
                {
                    File = FixtureProjectRelativePath,
                    Line = editedUnpatchedLine,
                    TimeoutSeconds = 30,
                    Mode = UloopPausePointCaptureMode.SingleShot
                });
            }
            finally
            {
                HotReloadPausePointCoordination.GetVerifiedSnapshotSourceForFile = previous;
            }

            Assert.That(enable.Success, Is.True, enable.Message + " / " + enable.RecommendedNextAction);
            Assert.That(enable.RetargetedToHotReloadPatch, Is.False);
            Assert.That(enable.ResolvedLineText, Is.Empty);
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

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
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
