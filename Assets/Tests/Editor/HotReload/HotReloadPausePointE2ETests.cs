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
    /// (c) enable→patch→revert-all, (d) unchanged-convergence peel, (f) local-function ShimDirect,
    /// (g) a line inside an added method, (h) a compiled line beside an added method,
    /// (i) a compiled method whose last compiled lines an added method now covers.
    /// </summary>
    public class HotReloadPausePointE2ETests
    {
        private const string FixtureProjectRelativePath =
            "Assets/Tests/Editor/HotReload/HotReloadE2EFixtures.cs";

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

            HotReloadCompositionRoot.Services.Patcher.RevertAll();

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
            HotReloadOrchestratorResult converge = await HotReloadFromEditedSourceAsync(
                onDisk,
                "E2E_d_Unchanged.cs",
                requirePatched: false);
            Assert.That(HotReloadTool.BuildApplyResponse(converge).ClearedCount, Is.EqualTo(1));

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
                HotReloadPausePointCoordination.HotReloadSide?.GetShimLookupForFile(FixtureProjectRelativePath);
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

        /// <summary>
        /// What: (g) a line inside a method the reload added in the middle of the file is refused
        /// with a message naming that added method, with or without --method, instead of arming
        /// the next compiled method.
        /// </summary>
        [Test]
        public async Task PatchThenEnable_LineInsideAnAddedMethod_NamesTheAddedMethod()
        {
            string editedSource = BuildEditedComputeWithAddedMethod();
            int enableLine = FindLineNumber(editedSource, "return _secret + delta + 200;");
            Assert.That(enableLine, Is.GreaterThan(0));

            HotReloadOrchestratorResult result =
                await HotReloadFromEditedSourceAsync(editedSource, "E2E_g_AddedMethod.cs");
            Assert.That(
                result.Methods.Any(m => m.Kind == HotReloadMethodOutcomeKind.Added),
                Is.True,
                FormatHotReloadOutcomes(result));

            PausePointResponse enable = EnableContinuous(enableLine);

            Assert.That(enable.Success, Is.False);
            Assert.That(enable.ErrorCode, Is.EqualTo(SourcePausePointConstants.ErrorCodeResolveFailed));
            Assert.That(enable.Message, Does.Contain("AddedBoost"));
            Assert.That(enable.Message, Does.Contain("which hot reload added"));

            PausePointResponse enableWithMethod = EnableContinuousInMethod(enableLine, "AddedBoost");

            Assert.That(enableWithMethod.Success, Is.False);
            Assert.That(enableWithMethod.ErrorCode, Is.EqualTo(SourcePausePointConstants.ErrorCodeResolveFailed));
            Assert.That(enableWithMethod.Message, Does.Contain("AddedBoost"));
            Assert.That(enableWithMethod.Message, Does.Contain("which hot reload added"));
        }

        /// <summary>
        /// What: (h) in a file where the reload added a method, a line inside an untouched compiled
        /// method still enables on the compiled line map, so the added-method refusal does not
        /// swallow lines outside the added range.
        /// </summary>
        [Test]
        public async Task PatchThenEnable_CompiledMethodBesideAnAddedMethod_StillEnables()
        {
            string editedSource = BuildEditedComputeWithAddedMethod();
            int enableLine = FindLineNumber(editedSource, "public int VisibleSibling()") + 2;
            Assert.That(enableLine, Is.LessThan(FindLineNumber(editedSource, "public int ComputeWithPrivate(int delta)")));

            HotReloadOrchestratorResult result =
                await HotReloadFromEditedSourceAsync(editedSource, "E2E_h_AddedMethod.cs");
            Assert.That(
                result.Methods.Any(m => m.Kind == HotReloadMethodOutcomeKind.Added),
                Is.True,
                FormatHotReloadOutcomes(result));

            PausePointResponse enable = EnableContinuous(enableLine);

            Assert.That(enable.Success, Is.True, enable.Message + " / " + enable.RecommendedNextAction);
            Assert.That(UloopPausePointRegistry.GetStatus(enable.Id).RetargetedToHotReloadPatch, Is.False);
        }

        /// <summary>
        /// What: (i) after a reload that only added methods above a compiled method, a line the
        /// added methods now cover is refused as inside an added method both without --method and
        /// with --method naming the compiled method, because --line is an edited-file line and
        /// that line holds added code.
        /// </summary>
        [Test]
        public async Task AddOnlyReload_CompiledLineUnderAnAddedMethod_RefusesWithAndWithoutMethod()
        {
            string onDisk = File.ReadAllText(ResolveFixtureAbsolutePath());
            string editedSource = BuildEditedWithMethodsAddedAboveVisibleSibling(onDisk);
            int compiledStatementLine = FindLineNumber(onDisk, "public int VisibleSibling()") + 2;
            int editedStatementLine = FindLineNumber(editedSource, "public int VisibleSibling()") + 2;
            Assert.That(editedStatementLine, Is.GreaterThan(compiledStatementLine));

            HotReloadOrchestratorResult result = await HotReloadFromEditedSourceAsync(
                editedSource,
                "E2E_i_AddedAbove.cs",
                requirePatched: false);
            Assert.That(
                result.Methods.All(m => m.Kind == HotReloadMethodOutcomeKind.Added),
                Is.True,
                FormatHotReloadOutcomes(result));
            IHotReloadPausePointPort port = HotReloadPausePointCoordination.HotReloadSide;
            Assert.That(port.GetShimLookupForFile(FixtureProjectRelativePath), Is.Null);
            HotReloadAddedMethodAtLine addedAtLine =
                port.FindAddedMethodContainingLine(FixtureProjectRelativePath, compiledStatementLine);
            Assert.That(addedAtLine.Label, Does.Contain("AddedLead"));
            Assert.That(addedAtLine.MethodName, Is.EqualTo("AddedLead"));
            Assert.That(addedAtLine.DeclaringTypeName, Is.EqualTo(nameof(HotReloadE2EFixture)));
            Assert.That(addedAtLine.NestedOuterTypeName, Is.Null);
            Assert.That(port.HasActiveHotReloadChangesInFile(FixtureProjectRelativePath), Is.True);

            PausePointResponse withoutMethod = EnableContinuous(compiledStatementLine);

            Assert.That(withoutMethod.Success, Is.False);
            Assert.That(withoutMethod.Message, Does.Contain("which hot reload added"));

            PausePointResponse withMethod = EnableContinuousInMethod(compiledStatementLine, "VisibleSibling");

            Assert.That(withMethod.Success, Is.False);
            Assert.That(withMethod.Message, Does.Contain("which hot reload added"));
            Assert.That(withMethod.Message, Does.Contain(addedAtLine.Label));
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

        private static PausePointResponse EnableContinuousInMethod(int line, string method)
        {
            return new PausePointUseCase().Enable(new EnablePausePointSchema
            {
                File = FixtureProjectRelativePath,
                Line = line,
                Method = method,
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

        private static string BuildEditedComputeWithAddedMethod()
        {
            string onDisk = File.ReadAllText(ResolveFixtureAbsolutePath());
            const string original =
                "public int ComputeWithPrivate(int delta)\n        {\n            return _secret + delta;\n        }";
            const string replacement =
                "public int ComputeWithPrivate(int delta)\n        {\n            return AddedBoost(delta);\n        }\n"
                + "\n"
                + "        public int AddedBoost(int delta)\n"
                + "        {\n"
                + "            return _secret + delta + 200;\n"
                + "        }";
            string edited = onDisk.Replace(original, replacement, StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk));
            return edited;
        }

        private static string BuildEditedWithMethodsAddedAboveVisibleSibling(string onDisk)
        {
            const string original = "        public int VisibleSibling()\n";
            const string replacement =
                "        public int AddedLead(int delta)\n"
                + "        {\n"
                + "            int lead = delta + 300;\n"
                + "            return lead;\n"
                + "        }\n"
                + "\n"
                + "        public int AddedTrail(int delta)\n"
                + "        {\n"
                + "            int trail = delta + 400;\n"
                + "            return trail;\n"
                + "        }\n"
                + "\n"
                + original;
            string edited = onDisk.Replace(original, replacement, StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk));
            return edited;
        }

        private static async Task<HotReloadOrchestratorResult> HotReloadFromEditedSourceAsync(
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

            HotReloadOrchestratorResult result = await HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
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

            return result;
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
