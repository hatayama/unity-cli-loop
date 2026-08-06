using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using HarmonyLib;
using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for the hot-reload / source pause-point contract:
    /// shim-path enable after apply, restore arming after RevertAll, and suppress flags.
    /// </summary>
    public class HotReloadPausePointContractTests
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
        /// What: after a transplant hot-reload, enable-pause-point on an edited-body line hits
        /// and captures an added local (LocalBuilder hand-off) with the edited return value.
        /// </summary>
        [Test]
        public async Task Enable_OnHotReloadedTransplantBody_HitsAndCapturesAddedLocal()
        {
            string editedSource = BuildEditedComputeWithBoostedLocal();
            // Why return line (not the declaration): sequence points on the declaration can land
            // before the local enters the PDB scope, so capture would omit `boosted`.
            int enableLine = FindLineNumber(editedSource, "return boosted;");
            Assert.That(enableLine, Is.GreaterThan(0));

            await HotReloadFromEditedSourceAsync(editedSource, "ContractTransplantBoosted.cs");

            PausePointResponse enable = new PausePointUseCase().Enable(new EnablePausePointSchema
            {
                File = FixtureProjectRelativePath,
                Line = enableLine,
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.Continuous
            });
            Assert.That(enable.Success, Is.True, enable.Message + " / " + enable.RecommendedNextAction);
            Assert.That(enable.ResolvedLine, Is.GreaterThan(0));

            HotReloadE2EFixture fixture = new HotReloadE2EFixture();
            int result = fixture.ComputeWithPrivate(5);
            Assert.That(result, Is.EqualTo(fixture.SecretForAssert + 5 + 100));

            UloopPausePointSnapshot status = UloopPausePointRegistry.GetStatus(enable.Id);
            Assert.That(status.IsHit, Is.True);
            UloopCapturedVariable boosted = status.CapturedVariables.FirstOrDefault(v => v.Name == "boosted");
            Assert.That(boosted, Is.Not.Null, FormatCaptured(status));
            Assert.That(boosted.Value, Is.EqualTo(result.ToString()));
        }

        /// <summary>
        /// What: after a delegation hot-reload, enable on the shim body hits with synthetic
        /// "this" and never exposes __uloopInstance as a captured parameter name.
        /// </summary>
        [Test]
        public async Task Enable_OnHotReloadedDelegationBody_HitsWithoutUloopInstanceParameter()
        {
            string editedSource = BuildEditedLambdaPrivateDelegation();
            int enableLine = FindLineNumber(editedSource, "return pred(threshold) ? 7 : 0;");
            Assert.That(enableLine, Is.GreaterThan(0));

            await HotReloadFromEditedSourceAsync(editedSource, "ContractDelegationLambda.cs");
            HotReloadShimFileLookup lookup =
                HotReloadPausePointCoordination.GetShimLookupForFile?.Invoke(FixtureProjectRelativePath);
            Assert.That(lookup, Is.Not.Null);
            HotReloadShimMethodLookup lambdaEntry = lookup.Methods.FirstOrDefault(
                m => m.OriginalMethod != null
                     && m.OriginalMethod.Name == nameof(HotReloadE2EFixture.LambdaPrivate));
            Assert.That(lambdaEntry, Is.Not.Null);
            Assert.That(lambdaEntry.IsDelegation, Is.True);

            PausePointResponse enable = new PausePointUseCase().Enable(new EnablePausePointSchema
            {
                File = FixtureProjectRelativePath,
                Line = enableLine,
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.Continuous
            });
            Assert.That(enable.Success, Is.True, enable.Message + " / " + enable.RecommendedNextAction);

            HotReloadE2EFixture fixture = new HotReloadE2EFixture();
            Assert.That(fixture.LambdaPrivate(5), Is.EqualTo(7));

            UloopPausePointSnapshot status = UloopPausePointRegistry.GetStatus(enable.Id);
            Assert.That(status.IsHit, Is.True);
            Assert.That(
                status.CapturedVariables.Any(v => v.Name == "__uloopInstance"),
                Is.False,
                FormatCaptured(status));
            Assert.That(
                status.CapturedVariables.Any(v => v.Name == "this"),
                Is.True,
                FormatCaptured(status));
        }

        /// <summary>
        /// What: arm → transplant hot-reload auto-retargets (not suppress) and hits the edited body.
        /// </summary>
        [Test]
        public async Task ArmThenHotReload_AutoRetargetsAndHitsEditedBody()
        {
            string onDisk = File.ReadAllText(ResolveFixtureAbsolutePath());
            int enableLine = FindLineNumber(onDisk, "return _secret + delta;");
            Assert.That(enableLine, Is.GreaterThan(0));

            PausePointResponse enable = new PausePointUseCase().Enable(new EnablePausePointSchema
            {
                File = FixtureProjectRelativePath,
                Line = enableLine,
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.Continuous
            });
            Assert.That(enable.Success, Is.True, enable.Message);

            string editedSource = BuildEditedComputePlusHundred(onDisk);
            await HotReloadFromEditedSourceAsync(editedSource, "ContractArmThenPatch.cs");

            UloopPausePointSnapshot afterPatch = UloopPausePointRegistry.GetStatus(enable.Id);
            Assert.That(afterPatch.SuppressedByHotReload, Is.False);
            Assert.That(afterPatch.RetargetedToHotReloadPatch, Is.True);

            HotReloadE2EFixture fixture = new HotReloadE2EFixture();
            int result = fixture.ComputeWithPrivate(5);
            Assert.That(result, Is.EqualTo(fixture.SecretForAssert + 5 + 100));
            Assert.That(UloopPausePointRegistry.GetStatus(enable.Id).IsHit, Is.True);
        }

        /// <summary>
        /// What: a SingleShot marker that already hit (disarmed) is not retargeted or suppressed
        /// by a later hot-reload of the same method.
        /// </summary>
        [Test]
        public async Task HotReload_AfterSingleShotHit_DoesNotTouchDisarmedMarker()
        {
            string onDisk = File.ReadAllText(ResolveFixtureAbsolutePath());
            int enableLine = FindLineNumber(onDisk, "return _secret + delta;");
            Assert.That(enableLine, Is.GreaterThan(0));

            PausePointResponse enable = new PausePointUseCase().Enable(new EnablePausePointSchema
            {
                File = FixtureProjectRelativePath,
                Line = enableLine,
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.SingleShot
            });
            Assert.That(enable.Success, Is.True, enable.Message);

            HotReloadE2EFixture fixture = new HotReloadE2EFixture();
            Assert.That(fixture.ComputeWithPrivate(5), Is.EqualTo(fixture.SecretForAssert + 5));
            UloopPausePointSnapshot afterHit = UloopPausePointRegistry.GetStatus(enable.Id);
            Assert.That(afterHit.IsHit, Is.True);
            Assert.That(afterHit.IsEnabled, Is.False);

            await HotReloadFromEditedSourceAsync(
                BuildEditedComputePlusHundred(onDisk),
                "ContractSingleShotDisarmed.cs");

            UloopPausePointSnapshot afterPatch = UloopPausePointRegistry.GetStatus(enable.Id);
            Assert.That(afterPatch.RetargetedToHotReloadPatch, Is.False);
            Assert.That(afterPatch.SuppressedByHotReload, Is.False);
        }

        /// <summary>
        /// What: RevertAll after enable on a shim-only line cannot restore instrumentation and
        /// records RestoreAfterHotReloadRevertFailedReason exactly.
        /// </summary>
        [Test]
        public async Task RevertAll_AfterEnableOnShimOnlyLine_SuppressesWithRestoreReason()
        {
            string boostedEdit = BuildEditedComputeWithBoostedLocal();
            int enableLine = FindLineNumber(boostedEdit, "return boosted;");
            Assert.That(enableLine, Is.GreaterThan(0));
            await HotReloadFromEditedSourceAsync(boostedEdit, "ContractRestoreFailGen1.cs");

            PausePointResponse enable = new PausePointUseCase().Enable(new EnablePausePointSchema
            {
                File = FixtureProjectRelativePath,
                Line = enableLine,
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.Continuous
            });
            Assert.That(enable.Success, Is.True, enable.Message);
            Assert.That(enable.RetargetedToHotReloadPatch, Is.True);

            HotReloadPatcher.RevertAll();

            UloopPausePointSnapshot status = UloopPausePointRegistry.GetStatus(enable.Id);
            Assert.That(status.SuppressedByHotReload, Is.True);
            Assert.That(status.RetargetedToHotReloadPatch, Is.False);
            Assert.That(
                status.SuppressedByHotReloadReason,
                Is.EqualTo(SourcePausePointConstants.RestoreAfterHotReloadRevertFailedReason));
        }

        /// <summary>
        /// What: after enable on a patched body, a second hot-reload generation keeps the marker
        /// firing with the newer edited behavior (generation follow).
        /// </summary>
        [Test]
        public async Task ReApply_AfterEnable_FollowsNewGenerationAndHits()
        {
            string firstEdit = BuildEditedLambdaPrivateDelegation();
            int enableLine = FindLineNumber(firstEdit, "return pred(threshold) ? 7 : 0;");
            Assert.That(enableLine, Is.GreaterThan(0));
            await HotReloadFromEditedSourceAsync(firstEdit, "ContractDelegationFollowGen1.cs");

            PausePointResponse enable = new PausePointUseCase().Enable(new EnablePausePointSchema
            {
                File = FixtureProjectRelativePath,
                Line = enableLine,
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.Continuous
            });
            Assert.That(enable.Success, Is.True, enable.Message);
            Assert.That(enable.RetargetedToHotReloadPatch, Is.True);

            string secondEdit = firstEdit.Replace(
                "return pred(threshold) ? 7 : 0;",
                "return pred(threshold) ? 8 : 0;",
                StringComparison.Ordinal);
            await HotReloadFromEditedSourceAsync(secondEdit, "ContractDelegationFollowGen2.cs");

            UloopPausePointSnapshot afterSecond = UloopPausePointRegistry.GetStatus(enable.Id);
            Assert.That(afterSecond.SuppressedByHotReload, Is.False);
            Assert.That(afterSecond.RetargetedToHotReloadPatch, Is.True);

            HotReloadE2EFixture fixture = new HotReloadE2EFixture();
            Assert.That(fixture.LambdaPrivate(0), Is.EqualTo(8));
            Assert.That(UloopPausePointRegistry.GetStatus(enable.Id).IsHit, Is.True);
        }

        /// <summary>
        /// What: shrinking the patched method so the armed line falls outside the shim range
        /// suppresses with a non-empty reason mirrored on status Warning.
        /// </summary>
        [Test]
        public async Task HotReload_ArmedLineOutsidePatchedMethod_SuppressesWithReason()
        {
            string boostedEdit = BuildEditedComputeWithBoostedLocal();
            int enableLine = FindLineNumber(boostedEdit, "return boosted;");
            Assert.That(enableLine, Is.GreaterThan(0));
            await HotReloadFromEditedSourceAsync(boostedEdit, "ContractRetargetFailGen1.cs");

            PausePointResponse enable = new PausePointUseCase().Enable(new EnablePausePointSchema
            {
                File = FixtureProjectRelativePath,
                Line = enableLine,
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.Continuous
            });
            Assert.That(enable.Success, Is.True, enable.Message);

            // Why shrink: enableLine sits deep in the boosted body; the short +100 body ends
            // earlier, so shim resolve reports NotInPatchedMethod and retarget suppresses.
            string onDisk = File.ReadAllText(ResolveFixtureAbsolutePath());
            string shortEdit = BuildEditedComputePlusHundred(onDisk);
            Assert.That(
                FindLineNumber(shortEdit, "return _secret + delta + 100;"),
                Is.LessThan(enableLine));
            await HotReloadFromEditedSourceAsync(shortEdit, "ContractRetargetFailGen2.cs");

            UloopPausePointSnapshot status = UloopPausePointRegistry.GetStatus(enable.Id);
            Assert.That(status.SuppressedByHotReload, Is.True);
            Assert.That(status.SuppressedByHotReloadReason, Is.Not.Null.And.Not.Empty);
            Assert.That(status.RetargetedToHotReloadPatch, Is.False);
            // Warning mirroring of the reason is covered by PausePointStatusResponseContractTests.
        }

        /// <summary>
        /// What: enabling on an async hot-reloaded body (struct MoveNext ShimDirect) hits with
        /// the edited await result.
        /// </summary>
        [Test]
        public async Task Enable_OnHotReloadedAsyncBody_HitsEditedResult()
        {
            string onDisk = File.ReadAllText(ResolveFixtureAbsolutePath());
            string editedSource = onDisk.Replace(
                "public async Task<int> AsyncPrivateFieldAndMethod(int delta)\n        {\n"
                + "            await Task.Yield();\n"
                + "            return _secret + delta;\n"
                + "        }",
                "public async Task<int> AsyncPrivateFieldAndMethod(int delta)\n        {\n"
                + "            await Task.Yield();\n"
                + "            return _secret + delta + 100;\n"
                + "        }",
                StringComparison.Ordinal);
            Assert.That(editedSource, Is.Not.EqualTo(onDisk));
            int enableLine = FindLineNumber(editedSource, "return _secret + delta + 100;");
            Assert.That(enableLine, Is.GreaterThan(0));

            await HotReloadFromEditedSourceAsync(editedSource, "ContractAsyncMoveNext.cs");

            PausePointResponse enable = new PausePointUseCase().Enable(new EnablePausePointSchema
            {
                File = FixtureProjectRelativePath,
                Line = enableLine,
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.Continuous
            });
            Assert.That(enable.Success, Is.True, enable.Message + " / " + enable.RecommendedNextAction);

            HotReloadE2EFixture fixture = new HotReloadE2EFixture();
            int result = await fixture.AsyncPrivateFieldAndMethod(5);
            Assert.That(result, Is.EqualTo(fixture.SecretForAssert + 5 + 100));
            Assert.That(UloopPausePointRegistry.GetStatus(enable.Id).IsHit, Is.True);
        }

        /// <summary>
        /// What: RevertAll after enable on a hot-reloaded async body restores instrumentation
        /// onto the compiled MoveNext and hits with the original (non-edited) return value.
        /// </summary>
        [Test]
        public async Task RevertAll_AfterEnableOnHotReloadedAsyncBody_RestoresAndHitsOriginal()
        {
            string onDisk = File.ReadAllText(ResolveFixtureAbsolutePath());
            string editedSource = onDisk.Replace(
                "public async Task<int> AsyncPrivateFieldAndMethod(int delta)\n        {\n"
                + "            await Task.Yield();\n"
                + "            return _secret + delta;\n"
                + "        }",
                "public async Task<int> AsyncPrivateFieldAndMethod(int delta)\n        {\n"
                + "            await Task.Yield();\n"
                + "            return _secret + delta + 100;\n"
                + "        }",
                StringComparison.Ordinal);
            Assert.That(editedSource, Is.Not.EqualTo(onDisk));
            int enableLine = FindLineNumber(editedSource, "return _secret + delta + 100;");
            Assert.That(enableLine, Is.GreaterThan(0));

            await HotReloadFromEditedSourceAsync(editedSource, "ContractAsyncRestore.cs");

            PausePointResponse enable = new PausePointUseCase().Enable(new EnablePausePointSchema
            {
                File = FixtureProjectRelativePath,
                Line = enableLine,
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.Continuous
            });
            Assert.That(enable.Success, Is.True, enable.Message + " / " + enable.RecommendedNextAction);
            Assert.That(enable.RetargetedToHotReloadPatch, Is.True);

            HotReloadPatcher.RevertAll();

            UloopPausePointSnapshot afterRevert = UloopPausePointRegistry.GetStatus(enable.Id);
            Assert.That(afterRevert.SuppressedByHotReload, Is.False);
            Assert.That(afterRevert.RetargetedToHotReloadPatch, Is.False);

            HotReloadE2EFixture fixture = new HotReloadE2EFixture();
            int result = await fixture.AsyncPrivateFieldAndMethod(5);
            Assert.That(result, Is.EqualTo(fixture.SecretForAssert + 5));
            Assert.That(UloopPausePointRegistry.GetStatus(enable.Id).IsHit, Is.True);
        }

        /// <summary>
        /// What: Patching via synthetic resolution onto a hot-reload patched method still
        /// returns MethodPatchedByHotReload and mentions the requested line in the message.
        /// </summary>
        [Test]
        public void Patch_OnHotReloadedMethod_ReturnsMethodPatchedByHotReload()
        {
            MethodInfo original = AccessTools.Method(
                typeof(HotReloadPausePointContractFixture),
                nameof(HotReloadPausePointContractFixture.ReplaceableCompute));
            MethodInfo shim = AccessTools.Method(
                typeof(HotReloadPausePointContractShims),
                nameof(HotReloadPausePointContractShims.ReplaceableCompute__shim0));

            Assert.That(
                HotReloadPatcher.Apply(original, shim, HotReloadPatchShape.Transplant).Success,
                Is.True);

            const int requestedLine = 42;
            SourcePausePointPatchResult result = SourcePausePointPatcher.Patch(
                "contract-reject-on-patched",
                BuildSyntheticResolution(original, instructionIndex: 5000),
                normalizedFile: "Assets/Tests/Editor/HotReload/HotReloadPausePointContractFixture.cs",
                requestedLine: requestedLine);

            Assert.That(result.Success, Is.False);
            Assert.That(
                result.FailureReason,
                Is.EqualTo(SourcePausePointPatchFailureReason.MethodPatchedByHotReload));
            Assert.That(result.ErrorMessage, Does.Contain("line " + requestedLine));
            Assert.That(result.Hint, Does.Contain("uloop hot-reload --revert-all"));
        }

        /// <summary>
        /// What: enable on an unedited method in a hot-reloaded file still uses the compiled
        /// resolver (NotInPatchedMethod fallthrough) and hits.
        /// </summary>
        [Test]
        public async Task Enable_UneditedMethod_InHotReloadedFile_FallsThroughAndHits()
        {
            string editedSource = BuildEditedComputeWithBoostedLocal();
            await HotReloadFromEditedSourceAsync(editedSource, "ContractFallthroughUnedited.cs");

            // CallsMissingHelper is never part of the ComputeWithPrivate edit, so shim lookup
            // must NotInPatchedMethod-fall through to the compiled ScriptAssemblies resolver.
            string onDisk = File.ReadAllText(ResolveFixtureAbsolutePath());
            int enableLine = FindLineNumber(onDisk, "return value;");
            Assert.That(enableLine, Is.GreaterThan(0));

            PausePointResponse enable = new PausePointUseCase().Enable(new EnablePausePointSchema
            {
                File = FixtureProjectRelativePath,
                Line = enableLine,
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.Continuous
            });
            Assert.That(
                enable.Success,
                Is.True,
                enable.ErrorCode + " / " + enable.Message + " / " + enable.RecommendedNextAction);

            HotReloadE2EFixture fixture = new HotReloadE2EFixture();
            Assert.That(fixture.CallsMissingHelper(9), Is.EqualTo(9));
            Assert.That(UloopPausePointRegistry.GetStatus(enable.Id).IsHit, Is.True);
        }

        /// <summary>
        /// What: After RevertAll, Patching a pause point onto the previously patched method
        /// succeeds, proving the rejection is not permanent.
        /// </summary>
        [Test]
        public void Patch_AfterRevertAll_Succeeds()
        {
            MethodInfo original = AccessTools.Method(
                typeof(HotReloadPausePointContractFixture),
                nameof(HotReloadPausePointContractFixture.ReplaceableCompute));
            MethodInfo shim = AccessTools.Method(
                typeof(HotReloadPausePointContractShims),
                nameof(HotReloadPausePointContractShims.ReplaceableCompute__shim0));

            Assert.That(
                HotReloadPatcher.Apply(original, shim, HotReloadPatchShape.Transplant).Success,
                Is.True);
            HotReloadPatcher.RevertAll();

            SourcePausePointResolution resolution = BuildSyntheticResolution(original, instructionIndex: 0);
            SourcePausePointPatchResult result = SourcePausePointPatcher.Patch(
                "contract-ok-after-revert",
                resolution);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
        }

        /// <summary>
        /// What: a synthetic Apply without shim registration cannot retarget, so Apply still
        /// succeeds and the armed marker is reported SuppressedByHotReload.
        /// </summary>
        [Test]
        public void Apply_MethodWithDeepArmedMarker_SucceedsAndSuppresses()
        {
            const string id = "contract-deep-suppress";
            MethodInfo original = AccessTools.Method(
                typeof(HotReloadPausePointDeepFixture),
                nameof(HotReloadPausePointDeepFixture.DeepStatements));
            MethodInfo shim = AccessTools.Method(
                typeof(HotReloadPausePointContractShims),
                nameof(HotReloadPausePointContractShims.DeepStatements__shim0));

            UloopPausePointRegistry.Enable(id, 30);
            Assert.That(
                SourcePausePointPatcher.Patch(id, BuildSyntheticResolution(original, instructionIndex: 10)).Success,
                Is.True);

            HotReloadPatchResult applyResult = HotReloadPatcher.Apply(
                original, shim, HotReloadPatchShape.Delegation);
            Assert.That(applyResult.Success, Is.True, applyResult.ErrorMessage);
            Assert.That(UloopPausePointRegistry.GetStatus(id).SuppressedByHotReload, Is.True);
            Assert.That(
                UloopPausePointRegistry.GetStatus(id).SuppressedByHotReloadReason,
                Is.Not.Null.And.Not.Empty);
        }

        /// <summary>
        /// What: after two markers are retargeted onto a shim body, unpatching one sibling leaves
        /// the survivor firing on the edited body.
        /// </summary>
        [Test]
        public async Task Unpatch_SiblingMarker_WhileHotReloadPatched_SurvivorStillHits()
        {
            string editedSource = BuildEditedComputeWithBoostedLocal();
            int lineBoosted = FindLineNumber(editedSource, "int boosted = _secret + delta + 100;");
            int lineReturn = FindLineNumber(editedSource, "return boosted;");
            Assert.That(lineBoosted, Is.GreaterThan(0));
            Assert.That(lineReturn, Is.GreaterThan(lineBoosted));
            await HotReloadFromEditedSourceAsync(editedSource, "ContractSiblingSurvivor.cs");

            PausePointResponse enableBoosted = new PausePointUseCase().Enable(new EnablePausePointSchema
            {
                File = FixtureProjectRelativePath,
                Line = lineBoosted,
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.Continuous
            });
            PausePointResponse enableReturn = new PausePointUseCase().Enable(new EnablePausePointSchema
            {
                File = FixtureProjectRelativePath,
                Line = lineReturn,
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.Continuous
            });
            Assert.That(enableBoosted.Success, Is.True, enableBoosted.Message);
            Assert.That(enableReturn.Success, Is.True, enableReturn.Message);

            Assert.DoesNotThrow(() => SourcePausePointPatcher.Unpatch(enableBoosted.Id));

            HotReloadE2EFixture fixture = new HotReloadE2EFixture();
            int result = fixture.ComputeWithPrivate(5);
            Assert.That(result, Is.EqualTo(fixture.SecretForAssert + 5 + 100));
            Assert.That(UloopPausePointRegistry.GetStatus(enableReturn.Id).IsHit, Is.True);
            Assert.That(UloopPausePointRegistry.GetStatus(enableBoosted.Id).IsHit, Is.False);
        }

        /// <summary>
        /// What: RevertAll restores instrumentation onto the compiled body, clears retargeted,
        /// and the marker hits with the original (non-edited) return value.
        /// </summary>
        [Test]
        public async Task RevertAll_RestoresArmedMarkerInstrumentation()
        {
            string onDisk = File.ReadAllText(ResolveFixtureAbsolutePath());
            int enableLine = FindLineNumber(onDisk, "return _secret + delta;");
            Assert.That(enableLine, Is.GreaterThan(0));

            PausePointResponse enable = new PausePointUseCase().Enable(new EnablePausePointSchema
            {
                File = FixtureProjectRelativePath,
                Line = enableLine,
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.Continuous
            });
            Assert.That(enable.Success, Is.True, enable.Message);

            await HotReloadFromEditedSourceAsync(
                BuildEditedComputePlusHundred(onDisk),
                "ContractRestoreAfterRevert.cs");
            Assert.That(UloopPausePointRegistry.GetStatus(enable.Id).RetargetedToHotReloadPatch, Is.True);

            HotReloadPatcher.RevertAll();

            HotReloadE2EFixture fixture = new HotReloadE2EFixture();
            int result = fixture.ComputeWithPrivate(5);
            Assert.That(result, Is.EqualTo(fixture.SecretForAssert + 5));

            UloopPausePointSnapshot status = UloopPausePointRegistry.GetStatus(enable.Id);
            Assert.That(status.SuppressedByHotReload, Is.False);
            Assert.That(status.RetargetedToHotReloadPatch, Is.False);
            Assert.That(status.IsHit, Is.True);
            Assert.That(status.HitCount, Is.EqualTo(1));
        }

        private static SourcePausePointResolution BuildSyntheticResolution(
            MethodBase method,
            int instructionIndex)
        {
            return new SourcePausePointResolution(
                method.Module.Assembly.GetName().Name,
                method.Module.ModuleVersionId.ToString(),
                method.MetadataToken,
                method.ToString(),
                method.IsStatic,
                method.DeclaringType.IsValueType,
                instructionIndex,
                0,
                1,
                Array.Empty<SourcePausePointLocalVariable>(),
                Array.Empty<SourcePausePointParameter>());
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

        private static string BuildEditedComputeWithBoostedLocal()
        {
            string onDisk = File.ReadAllText(ResolveFixtureAbsolutePath());
            const string original =
                "public int ComputeWithPrivate(int delta)\n        {\n            return _secret + delta;\n        }";
            // Why the loop: Roslyn can elide a trivial local (`int x = …; return x;`) from both
            // IL and PDB; the loop keeps `boosted` as a real capturable slot for the LocalBuilder assert.
            const string replacement =
                "public int ComputeWithPrivate(int delta)\n        {\n"
                + "            int boosted = _secret + delta + 100;\n"
                + "            for (int i = 0; i < 1; i++)\n"
                + "            {\n"
                + "                boosted += i;\n"
                + "            }\n"
                + "\n"
                + "            return boosted;\n"
                + "        }";
            string edited = onDisk.Replace(original, replacement, StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk));
            return edited;
        }

        private static string BuildEditedLambdaPrivateDelegation()
        {
            string onDisk = File.ReadAllText(ResolveFixtureAbsolutePath());
            string edited = onDisk.Replace(
                "Func<int, bool> pred = v => v < _secret;\n            return pred(threshold) ? 1 : 0;",
                "Func<int, bool> pred = v => v < (_secret + 100);\n            return pred(threshold) ? 7 : 0;",
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

        private static string FormatCaptured(UloopPausePointSnapshot status)
        {
            List<string> lines = new List<string>();
            foreach (UloopCapturedVariable variable in status.CapturedVariables)
            {
                lines.Add(variable.Name + "=" + variable.Value);
            }

            return string.Join(", ", lines);
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

    /// <summary>
    /// NoInlining fixture used only by the hot-reload / pause-point contract tests.
    /// </summary>
    public class HotReloadPausePointContractFixture
    {
        // Why NoInlining: patch-target fixtures must not be inlined into the test method
        // that was JIT-compiled before the patch was applied.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ReplaceableCompute(int delta)
        {
            return -1 * delta;
        }
    }

    /// <summary>
    /// NoInlining fixture with enough independent statements for a deep InstructionIndex arm.
    /// </summary>
    public class HotReloadPausePointDeepFixture
    {
        // Why NoInlining: patch-target fixtures must not be inlined into the test method
        // that was JIT-compiled before the patch was applied.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int DeepStatements()
        {
            int a = 1;
            int b = 2;
            int c = 3;
            int d = 4;
            int e = 5;
            int f = 6;
            int g = 7;
            int h = 8;
            int i = 9;
            int j = 10;
            int k = 11;
            int l = 12;
            return a + b + c + d + e + f + g + h + i + j + k + l;
        }
    }

    /// <summary>
    /// Hand-written transplant / delegation shims for the contract fixtures.
    /// </summary>
    public static class HotReloadPausePointContractShims
    {
        public static int ReplaceableCompute__shim0(HotReloadPausePointContractFixture instance, int delta)
        {
            return delta + 42;
        }

        public static int DeepStatements__shim0(HotReloadPausePointDeepFixture instance)
        {
            return 99;
        }
    }
}
