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
    /// End-to-end coexistence scenarios for hot-reload and source pause points through the
    /// orchestrator and pause-point tool layers (plan E To-Do 21).
    /// Scenario map: (a) Enable_OnHotReloadedTransplantBody_HitsAndCapturesAddedLocal and
    /// (b) ArmThenHotReload_AutoRetargetsAndHitsEditedBody and
    /// (e) Enable_OnHotReloadedAsyncBody_HitsEditedResult are pinned by
    /// HotReloadPausePointContractTests; this suite covers the remaining orderings —
    /// (c) enable→patch→revert-all, (d) unchanged-convergence peel, (f) local-function ShimDirect.
    /// </summary>
    public class HotReloadPausePointE2ETests
    {
        private const string FixtureProjectRelativePath =
            "Assets/Tests/Editor/HotReload/HotReloadE2EFixtures.cs";

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
        /// What: (c) after enable→patch retarget, RevertAll restores the compiled body so the
        /// marker hits the original value and clears RetargetedToHotReloadPatch.
        /// </summary>
        [Test]
        public async Task EnableThenPatchThenRevertAll_HitsOriginalAndClearsRetargeted()
        {
            string onDisk = File.ReadAllText(ResolveFixtureAbsolutePath());
            int enableLine = FindLineNumber(onDisk, "return _secret + delta;");
            Assert.That(enableLine, Is.GreaterThan(0));

            PausePointResponse enable = EnableContinuous(enableLine);
            Assert.That(enable.Success, Is.True, enable.Message);

            await HotReloadFromEditedSourceAsync(
                BuildEditedComputePlusHundred(onDisk),
                "E2E_c_BeforeRevert.cs");
            Assert.That(UloopPausePointRegistry.GetStatus(enable.Id).RetargetedToHotReloadPatch, Is.True);

            HotReloadPatcher.RevertAll();

            HotReloadE2EFixture fixture = new HotReloadE2EFixture();
            int result = fixture.ComputeWithPrivate(5);
            Assert.That(result, Is.EqualTo(fixture.SecretForAssert + 5));

            UloopPausePointSnapshot status = UloopPausePointRegistry.GetStatus(enable.Id);
            Assert.That(status.SuppressedByHotReload, Is.False);
            Assert.That(status.RetargetedToHotReloadPatch, Is.False);
            Assert.That(status.IsHit, Is.True);
        }

        /// <summary>
        /// What: (d) after a patch, re-running against the verified on-disk source peels the
        /// unchanged method and restores the marker onto the compiled body.
        /// </summary>
        [Test]
        public async Task UnchangedConvergence_AfterEdit_RestoresCompiledBodyHit()
        {
            string onDisk = File.ReadAllText(ResolveFixtureAbsolutePath());
            int enableLine = FindLineNumber(onDisk, "return _secret + delta;");
            Assert.That(enableLine, Is.GreaterThan(0));

            PausePointResponse enable = EnableContinuous(enableLine);
            Assert.That(enable.Success, Is.True, enable.Message);

            await HotReloadFromEditedSourceAsync(
                BuildEditedComputePlusHundred(onDisk),
                "E2E_d_Edited.cs");
            Assert.That(UloopPausePointRegistry.GetStatus(enable.Id).RetargetedToHotReloadPatch, Is.True);

            // Why on-disk content: matches the verified snapshot so ComputeWithPrivate is
            // unchanged and RevertUnchangedPatches peels the leftover patch.
            await HotReloadFromEditedSourceAsync(onDisk, "E2E_d_Unchanged.cs", requirePatched: false);

            UloopPausePointSnapshot afterConverge = UloopPausePointRegistry.GetStatus(enable.Id);
            Assert.That(afterConverge.SuppressedByHotReload, Is.False);
            Assert.That(afterConverge.RetargetedToHotReloadPatch, Is.False);

            HotReloadE2EFixture fixture = new HotReloadE2EFixture();
            int result = fixture.ComputeWithPrivate(5);
            Assert.That(result, Is.EqualTo(fixture.SecretForAssert + 5));
            Assert.That(UloopPausePointRegistry.GetStatus(enable.Id).IsHit, Is.True);
        }

        /// <summary>
        /// What: (f) enable on a line inside a hot-reloaded local function (ShimDirect) hits
        /// with the edited return value.
        /// </summary>
        [Test]
        public async Task PatchThenEnable_LocalFunctionBody_HitsEditedValue()
        {
            string editedSource = BuildEditedComputeWithLocalFunction();
            int enableLine = FindLineNumber(editedSource, "return _secret + delta + 100;");
            Assert.That(enableLine, Is.GreaterThan(0));

            await HotReloadFromEditedSourceAsync(editedSource, "E2E_f_LocalFunction.cs");

            HotReloadShimFileLookup lookup =
                HotReloadPausePointCoordination.GetShimLookupForFile?.Invoke(FixtureProjectRelativePath);
            Assert.That(lookup, Is.Not.Null);
            SourcePausePointShimResolution shimResolution =
                SourcePausePointShimResolver.Resolve(lookup, FixtureProjectRelativePath, enableLine);
            Assert.That(
                shimResolution.Kind,
                Is.EqualTo(SourcePausePointShimResolveKind.ShimDirect),
                shimResolution.ErrorMessage);

            PausePointResponse enable = EnableContinuous(enableLine);
            Assert.That(enable.Success, Is.True, enable.Message + " / " + enable.RecommendedNextAction);

            HotReloadE2EFixture fixture = new HotReloadE2EFixture();
            int result = fixture.ComputeWithPrivate(5);
            Assert.That(result, Is.EqualTo(fixture.SecretForAssert + 5 + 100));
            Assert.That(UloopPausePointRegistry.GetStatus(enable.Id).IsHit, Is.True);
        }

        private static PausePointResponse EnableContinuous(int line)
        {
            return new PausePointUseCase().Enable(new EnablePausePointSchema
            {
                File = FixtureProjectRelativePath,
                Line = line,
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.Continuous
            });
        }

        private static string BuildEditedComputePlusHundred(string onDisk)
        {
            string edited = onDisk.Replace(
                "public int ComputeWithPrivate(int delta)\n        {\n            return _secret + delta;\n        }",
                "public int ComputeWithPrivate(int delta)\n        {\n            return _secret + delta + 100;\n        }",
                StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk));
            return edited;
        }

        private static string BuildEditedComputeWithLocalFunction()
        {
            string onDisk = File.ReadAllText(ResolveFixtureAbsolutePath());
            const string original =
                "public int ComputeWithPrivate(int delta)\n        {\n            return _secret + delta;\n        }";
            const string replacement =
                "public int ComputeWithPrivate(int delta)\n        {\n"
                + "            int LocalBoost()\n"
                + "            {\n"
                + "                return _secret + delta + 100;\n"
                + "            }\n"
                + "\n"
                + "            return LocalBoost();\n"
                + "        }";
            string edited = onDisk.Replace(original, replacement, StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk));
            return edited;
        }

        private static async Task HotReloadFromEditedSourceAsync(
            string editedSource,
            string fileName,
            bool requirePatched = true)
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
            if (requirePatched)
            {
                Assert.That(
                    result.Methods.Any(m => m.Kind == HotReloadMethodOutcomeKind.Patched),
                    Is.True,
                    FormatHotReloadOutcomes(result));
            }
        }

        private static string ResolveFixtureAbsolutePath()
        {
            return Path.GetFullPath(
                Path.Combine(
                    Application.dataPath,
                    "Tests",
                    "Editor",
                    "HotReload",
                    "HotReloadE2EFixtures.cs"));
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
