using System;
using System.Collections.Generic;
using System.Reflection;

using HarmonyLib;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// What the patcher guarantees about the domain when Harmony refuses a call, and what the
    /// totals a report prints are counted from. The failure guarantees are asserted one per test,
    /// so a state that happens to be right for an unrelated reason cannot hide a missing check.
    /// </summary>
    public class HotReloadPatcherContractTests
    {
        private const string FixtureProjectRelativePath = "Assets/Tests/Fixture.cs";
        private const string AddedFieldOnlyPath =
            "Assets/Tests/Editor/HotReload/PatcherContractAddedFieldHost.cs";

        /// <summary>Refuses every patch, so the apply path's failure branch is the one under test.</summary>
        private sealed class RefusingPatchHarmony : IHotReloadHarmony
        {
            public void Patch(MethodBase original, HarmonyMethod transpiler)
            {
                throw new InvalidOperationException("patch refused");
            }

            public void Unpatch(MethodBase original, HarmonyPatchType patchType, string harmonyId)
            {
            }

            public void UnpatchAll(string harmonyId)
            {
            }
        }

        /// <summary>
        /// What: a refused patch leaves no generation claiming the method, so a failed apply cannot
        /// be reported as an applied change.
        /// </summary>
        [Test]
        public void Apply_WhenHarmonyRefusesThePatch_LeavesNoActivePatch()
        {
            using (BeginReplacementWith(new RefusingPatchHarmony()))
            {
                HotReloadDomainTestAccess access = new HotReloadDomainTestAccess();
                Assert.That(
                    access.ApplyPatch(
                        GetPatchTarget(),
                        GetShim(),
                        HotReloadPatchShape.Transplant,
                        FixtureProjectRelativePath).Success,
                    Is.False,
                    "Precondition: the harmony double has to refuse this patch.");

                Assert.That(access.Domain.ActivePatchCount, Is.EqualTo(0));
                Assert.That(
                    access.Domain.FindGeneration(FixtureProjectRelativePath).IsPatchActive(GetPatchTarget()),
                    Is.False);
            }
        }

        /// <summary>
        /// What: a refused patch keeps the shim the apply run registered, because the generation is
        /// what a later attempt on the same file resolves that shim from.
        /// </summary>
        [Test]
        public void Apply_WhenHarmonyRefusesThePatch_KeepsTheShimRegistration()
        {
            using (BeginReplacementWith(new RefusingPatchHarmony()))
            {
                HotReloadDomainTestAccess access = new HotReloadDomainTestAccess();
                access.ApplyPatch(
                    GetPatchTarget(),
                    GetShim(),
                    HotReloadPatchShape.Transplant,
                    FixtureProjectRelativePath);

                Assert.That(
                    access.Domain.FindGeneration(FixtureProjectRelativePath).FindShim(GetPatchTarget()),
                    Is.EqualTo(GetShim()));
            }
        }

        /// <summary>
        /// What: a refused patch leaves no invocation count behind, so a report cannot show calls
        /// against a patch that never went live.
        /// </summary>
        [Test]
        public void Apply_WhenHarmonyRefusesThePatch_LeavesNoInvocationCount()
        {
            using (BeginReplacementWith(new RefusingPatchHarmony()))
            {
                string methodKey = HotReloadMethodKeys.FormatMethodLabel(GetPatchTarget());
                HotReloadInvocationRegistry.Increment(methodKey);
                Assert.That(
                    HotReloadInvocationRegistry.GetCount(methodKey),
                    Is.EqualTo(1L),
                    "Precondition: a count has to exist for the failed apply to clear.");

                new HotReloadDomainTestAccess().ApplyPatch(
                    GetPatchTarget(),
                    GetShim(),
                    HotReloadPatchShape.Transplant,
                    FixtureProjectRelativePath);

                Assert.That(HotReloadInvocationRegistry.GetCount(methodKey), Is.EqualTo(0L));
            }
        }

        /// <summary>
        /// What: by the time RevertAll tells pause point a method is no longer patched, the domain
        /// has already dropped that method — the order pause point re-instruments its markers in.
        /// </summary>
        [Test]
        public void RevertAll_TellsPausePointOnlyAfterTheDomainHasDroppedTheMethod()
        {
            using (new HotReloadDomainTestScope())
            {
                MethodInfo patched = GetPatchTarget();
                Assert.That(
                    new HotReloadDomainTestAccess().ApplyPatch(
                        patched,
                        GetShim(),
                        HotReloadPatchShape.Transplant,
                        FixtureProjectRelativePath).Success,
                    Is.True,
                    "Precondition: a live patch is what RevertAll then reports.");

                List<bool> generationStillClaimedTheMethod = new List<bool>();
                using (PausePointSidePortScope portScope = new PausePointSidePortScope())
                {
                    portScope.Port.HotReloadPatchStateChanged = (method, isPatched) =>
                    {
                        if (isPatched || !ReferenceEquals(method, patched))
                        {
                            return;
                        }

                        generationStillClaimedTheMethod.Add(
                            HotReloadCompositionRoot.Services.Domain.FindGenerationForMethod(method) != null);
                    };

                    HotReloadCompositionRoot.Services.Patcher.RevertAll();
                }

                Assert.That(
                    generationStillClaimedTheMethod,
                    Is.EqualTo(new[] { false }),
                    "The revert has to be reported exactly once, with the domain already emptied.");
            }
        }

        /// <summary>
        /// What: the active-change total counts patches and added members only, so a domain that
        /// also carries added fields still totals exactly the rows that are not added fields.
        /// </summary>
        [Test]
        public void CountActiveChanges_WithAnAddedFieldGeneration_MatchesTheNonFieldStatusRows()
        {
            using (new HotReloadDomainTestScope())
            {
                HotReloadDomainTestAccess access = new HotReloadDomainTestAccess();
                Assert.That(
                    access.ApplyPatch(
                        GetPatchTarget(),
                        GetShim(),
                        HotReloadPatchShape.Transplant,
                        FixtureProjectRelativePath).Success,
                    Is.True);
                access.ReplaceAddedFields(
                    AddedFieldOnlyPath,
                    new[] { "PatcherContractAddedFieldHost.count" });

                HotReloadResponse status = HotReloadCompositionRoot.Services.StatusExecutor.ExecuteStatus();

                Assert.That(
                    status.AddedFieldTotal,
                    Is.EqualTo(1),
                    "Precondition: the added-field generation has to reach the report.");
                Assert.That(
                    access.Domain.CountActiveChanges().PatchAndAddedMemberCount,
                    Is.EqualTo(CountRowsExcludingAddedFields(status)));
                Assert.That(status.ActivePatchTotal, Is.EqualTo(CountRowsExcludingAddedFields(status)));
            }
        }

        private static int CountRowsExcludingAddedFields(HotReloadResponse status)
        {
            int count = 0;
            foreach (HotReloadMethodResult row in status.Methods)
            {
                if (row.Kind != HotReloadConstants.AddedFieldKind)
                {
                    count++;
                }
            }

            return count;
        }

        private static IDisposable BeginReplacementWith(IHotReloadHarmony harmony)
        {
            HotReloadPackageRootCapture packageRootCapture = new HotReloadPackageRootCapture();
            packageRootCapture.CaptureCurrent();
            return HotReloadCompositionRoot.BeginReplacement(
                HotReloadCompositionRoot.CreateServices(
                    HotReloadCompositionRoot.CreateProductionDomain(),
                    harmony,
                    packageRootCapture,
                    new HotReloadEditorStateSnapshotCapture(),
                    TransformWorkerHost.Shared,
                    HotReloadGroupProcessorDependencies.CreateProduction));
        }

        private static MethodInfo GetPatchTarget()
        {
            return AccessTools.Method(
                typeof(HotReloadCoreFixture), nameof(HotReloadCoreFixture.ReplaceableCompute));
        }

        private static MethodInfo GetShim()
        {
            return AccessTools.Method(
                typeof(HotReloadHandwrittenShims),
                nameof(HotReloadHandwrittenShims.ReplaceableCompute__shim0));
        }
    }
}
