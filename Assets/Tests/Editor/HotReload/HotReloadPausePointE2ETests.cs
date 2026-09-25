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

        // A body the transform worker skips: it calls into the base class.
        private const string ComputeWithBaseCallStatement = "return base.BaseSeed() + _secret + delta + 100;";

        // Each scenario reloads one copy twice, the way a user reloads the same file again.
        private const string LeftBehindCopyFileName = "E2E_left_behind.cs";
        private const string SkippedOnlyCopyFileName = "E2E_skipped_only.cs";
        private const string EditedAfterReloadCopyFileName = "E2E_edited_after_reload.cs";

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
        /// method still enables through the edited line map onto its compiled body, so the added-method refusal does not
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

            PausePointResponse withoutMethod = EnableContinuous(compiledStatementLine);

            Assert.That(withoutMethod.Success, Is.False);
            Assert.That(withoutMethod.Message, Does.Contain("which hot reload added"));

            PausePointResponse withMethod = EnableContinuousInMethod(compiledStatementLine, "VisibleSibling");

            Assert.That(withMethod.Success, Is.False);
            Assert.That(withMethod.Message, Does.Contain("which hot reload added"));
            Assert.That(withMethod.Message, Does.Contain(addedAtLine.Label));
        }

        /// <summary>
        /// What: after a reload patched ComputeWithPrivate and a later reload of the same copy
        /// skipped it while patching another method, a line of ComputeWithPrivate is refused as a
        /// body an earlier reload left running, naming its Skipped row, and nothing is armed.
        /// </summary>
        [Test]
        public async Task PatchThenSkip_LineOfTheSkippedMethod_RefusesAsLeftBehind()
        {
            string onDisk = File.ReadAllText(ResolveFixtureAbsolutePath());
            int enableLine = FindLineNumber(onDisk, "return _secret + delta;");
            await HotReloadFromEditedSourceAsync(
                ShiftComputeWithPrivateDownTwoLines(BuildEditedComputePlusHundred(onDisk)),
                LeftBehindCopyFileName);
            HotReloadOrchestratorResult skipping = await HotReloadFromEditedSourceAsync(
                ShiftComputeWithPrivateDownTwoLines(
                    ReplaceSummarizeCellsTotal(ReplaceComputeWithPrivateBody(onDisk, ComputeWithBaseCallStatement))),
                LeftBehindCopyFileName);
            string skippedLabel = FindSkippedLabel(skipping, nameof(HotReloadE2EFixture.ComputeWithPrivate));

            PausePointResponse enable = EnableContinuous(enableLine);

            AssertLeftBehindRefusal(enable, enableLine, skippedLabel);
        }

        /// <summary>
        /// What: after a reload patched ComputeWithPrivate and a later reload of the same copy only
        /// skipped it, so no new shim generation was built, a line inside the older generation's
        /// span and the attribute line above it, outside that span, are both refused as left behind.
        /// </summary>
        [Test]
        public async Task SkippedOnlyReload_LinesOfTheSkippedMethod_RefuseAsLeftBehind()
        {
            string onDisk = File.ReadAllText(ResolveFixtureAbsolutePath());
            int statementLine = FindLineNumber(onDisk, "return _secret + delta;");
            int attributeLine = FindLineNumber(onDisk, "public int ComputeWithPrivate(int delta)") - 1;
            Assert.That(onDisk.Replace("\r\n", "\n").Split('\n')[attributeLine - 1], Does.Contain("[MethodImpl"));
            await HotReloadFromEditedSourceAsync(
                ShiftComputeWithPrivateDownTwoLines(BuildEditedComputePlusHundred(onDisk)),
                SkippedOnlyCopyFileName);
            HotReloadOrchestratorResult skipping = await HotReloadFromEditedSourceAsync(
                ShiftComputeWithPrivateDownTwoLines(ReplaceComputeWithPrivateBody(onDisk, ComputeWithBaseCallStatement)),
                SkippedOnlyCopyFileName,
                requirePatched: false);
            string skippedLabel = FindSkippedLabel(skipping, nameof(HotReloadE2EFixture.ComputeWithPrivate));

            PausePointResponse insideTheStaleSpan = EnableContinuous(statementLine);
            PausePointResponse aboveTheStaleSpan = EnableContinuous(attributeLine);

            AssertLeftBehindRefusal(insideTheStaleSpan, statementLine, skippedLabel);
            AssertLeftBehindRefusal(aboveTheStaleSpan, attributeLine, skippedLabel);
        }

        /// <summary>
        /// What: after a reload patched ComputeWithPrivate and the copy it read was edited again
        /// without reloading, the attribute line above the method is refused as patched-source-
        /// changed, whose reload records the method's edited range again, and nothing is armed.
        /// </summary>
        [Test]
        public async Task PatchThenEdit_LineAboveThePatchedMethod_RefusesWithPatchedSourceChanged()
        {
            string onDisk = File.ReadAllText(ResolveFixtureAbsolutePath());
            int attributeLine = FindLineNumber(onDisk, "public int ComputeWithPrivate(int delta)") - 1;
            await HotReloadFromEditedSourceAsync(
                ShiftComputeWithPrivateDownTwoLines(BuildEditedComputePlusHundred(onDisk)),
                EditedAfterReloadCopyFileName);
            OverwriteEditedCopy(
                EditedAfterReloadCopyFileName,
                ShiftComputeWithPrivateDownTwoLines(ReplaceComputeWithPrivateBody(onDisk, "return _secret + delta + 300;")));

            PausePointResponse enable = EnableContinuous(attributeLine);

            Assert.That(enable.Success, Is.False, enable.ErrorCode + " / " + enable.Message);
            Assert.That(enable.ErrorCode, Is.EqualTo(SourcePausePointConstants.ErrorCodePatchedSourceChanged));
            Assert.That(
                enable.Message,
                Is.EqualTo(
                    string.Format(
                        SourcePausePointConstants.PatchedSourceChangedOnDiskMessageFormat,
                        FixtureProjectRelativePath,
                        attributeLine)));
            Assert.That(enable.RecommendedNextAction, Is.EqualTo(SourcePausePointConstants.PatchedSourceChangedOnDiskHint));
            Assert.That(UloopPausePointRegistry.GetActiveCount(), Is.EqualTo(0));
        }

        /// <summary>
        /// What: a reload that skipped CallsBase with no earlier patch leaves its compiled body
        /// running, so an unchanged line of it still arms the compiled code.
        /// </summary>
        [Test]
        public async Task SkipWithoutEarlierPatch_UnchangedLineOfTheSkippedMethod_ArmsTheCompiledBody()
        {
            string onDisk = File.ReadAllText(ResolveFixtureAbsolutePath());
            int enableLine = FindLineNumber(onDisk, "return base.BaseSeed() + 1;");
            string edited = onDisk.Replace(
                "return base.BaseSeed() + 1;",
                "return base.BaseSeed() + 2;",
                StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk));
            HotReloadOrchestratorResult skipping = await HotReloadFromEditedSourceAsync(
                edited,
                "E2E_skip_without_patch.cs",
                requirePatched: false);
            FindSkippedLabel(skipping, nameof(HotReloadE2EFixture.CallsBase));

            PausePointResponse enable = EnableContinuous(enableLine);

            Assert.That(enable.Success, Is.True, enable.ErrorCode + " / " + enable.Message);
            Assert.That(UloopPausePointRegistry.GetStatus(enable.Id).RetargetedToHotReloadPatch, Is.False);
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

        // Two comment lines above ComputeWithPrivate put every line of the copy below them two
        // lines lower than in the project file, so a span of that copy read as a project line
        // misses the method.
        private static string ShiftComputeWithPrivateDownTwoLines(string source)
        {
            const string original =
                "        [MethodImpl(MethodImplOptions.NoInlining)]\n        public int ComputeWithPrivate(int delta)";
            string edited = source.Replace(
                original,
                "        // Moved down by the edited copy.\n        // Moved down by the edited copy.\n" + original,
                StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(source));
            return edited;
        }

        private static string ReplaceComputeWithPrivateBody(string source, string statement)
        {
            string edited = source.Replace(
                "public int ComputeWithPrivate(int delta)\n        {\n            return _secret + delta;\n        }",
                "public int ComputeWithPrivate(int delta)\n        {\n            " + statement + "\n        }",
                StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(source));
            return edited;
        }

        private static string ReplaceSummarizeCellsTotal(string source)
        {
            string edited = source.Replace(
                "int total = cells.Count;",
                "int total = cells.Count + 1;",
                StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(source));
            return edited;
        }

        private static void OverwriteEditedCopy(string fileName, string source)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            File.WriteAllText(
                Path.Combine(projectRoot, HotReloadConstants.TestSourcesRelativeDirectory, fileName),
                source);
        }

        private static string FindSkippedLabel(HotReloadOrchestratorResult result, string methodName)
        {
            HotReloadMethodOutcome skipped = result.Methods.FirstOrDefault(
                m => m.Kind == HotReloadMethodOutcomeKind.Skipped && m.Method.Contains(methodName));
            Assert.That(skipped, Is.Not.Null, FormatHotReloadOutcomes(result));
            return skipped.Method;
        }

        private static void AssertLeftBehindRefusal(PausePointResponse enable, int line, string skippedLabel)
        {
            Assert.That(enable.Success, Is.False, enable.ErrorCode + " / " + enable.Message);
            Assert.That(enable.ErrorCode, Is.EqualTo(SourcePausePointConstants.ErrorCodePausePointPatchedByHotReload));
            Assert.That(
                enable.Message,
                Is.EqualTo(
                    string.Format(
                        SourcePausePointConstants.HotReloadLeftBehindMethodRefusalMessageFormat,
                        line,
                        nameof(HotReloadE2EFixture) + "." + nameof(HotReloadE2EFixture.ComputeWithPrivate),
                        SourcePausePointConstants.HotReloadLeftBehindSkippedVerb,
                        skippedLabel)));
            Assert.That(
                enable.RecommendedNextAction,
                Is.EqualTo(SourcePausePointConstants.HotReloadLeftBehindMethodRefusalNextAction));
            Assert.That(UloopPausePointRegistry.GetActiveCount(), Is.EqualTo(0));
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
