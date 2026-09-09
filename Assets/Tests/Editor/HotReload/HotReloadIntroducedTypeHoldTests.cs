using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers whether the Auto Refresh hold counts the types a reload introduced. An introduced
    /// type lives in an artifact assembly the next Domain Reload unloads, so a run that patched no
    /// method at all still has something to lose to an Auto Refresh.
    /// </summary>
    public class HotReloadIntroducedTypeHoldTests
    {
        private HotReloadDomainTestScope _scope;

        [SetUp]
        public void SetUp()
        {
            _scope = new HotReloadDomainTestScope();
            ReleaseTheHold();
        }

        // Why releasing here: the hold flag of the test service is shared by every test of the
        // run, so a test that armed it would otherwise decide the first assert of the next one.
        [TearDown]
        public void TearDown()
        {
            _scope.Dispose();
            ReleaseTheHold();
        }

        /// <summary>
        /// What: a run whose only change is a new type declaration arms the Auto Refresh hold, and
        /// the hold survives the periodic reconcile, a status query, and a revert of every patch,
        /// because none of those can unload the assembly that carries the type.
        /// </summary>
        [Test]
        public async Task RunIntroducingOnlyAType_HoldsAutoRefreshUntilTheNextDomainReload()
        {
            Assert.That(
                HotReloadAutoRefreshHold.IsHeld,
                Is.False,
                "Precondition: no patch and no introduced type must leave Auto Refresh allowed.");

            using (HotReloadCompositionRoot.BeginReplacement(HotReloadCompositionRoot.CreateProductionServices()))
            {
                HotReloadOrchestratorResult result = await RunIntroducingOnlyATypeAsync();

                // Why the result and not the live flag: the 0.5s reconcile can fire while the run
                // is awaited, which would arm the hold even for a run that never synced one.
                Assert.That(
                    result.AutoRefreshHeld,
                    Is.True,
                    "The run that introduced the type must arm the hold.");

                HotReloadAutoRefreshHold.ReconcileForTesting();
                Assert.That(
                    HotReloadAutoRefreshHold.IsHeld,
                    Is.True,
                    "The periodic reconcile must not release a hold the introduced type still needs.");

                HotReloadCompositionRoot.Services.StatusExecutor.ExecuteStatus();
                Assert.That(
                    HotReloadAutoRefreshHold.IsHeld,
                    Is.True,
                    "A status query must not release the hold.");

                HotReloadResponse revert = HotReloadCompositionRoot.Services.StatusExecutor.ExecuteRevertAll();
                Assert.That(
                    HotReloadAutoRefreshHold.IsHeld,
                    Is.True,
                    "A revert cannot unload the artifact assembly, so it must keep the hold.");
                Assert.That(
                    revert.Message,
                    Is.EqualTo(
                        "No active hot-reload changes to revert. 1 introduced type(s) stay loaded "
                        + "until the next Domain Reload; a revert cannot unload the assembly that "
                        + "carries them. Auto Refresh stays held for them; run 'uloop compile' to "
                        + "release it."),
                    "A revert that leaves the hold armed must say so, name what stayed, and name "
                    + "the command that releases it.");
                Assert.That(
                    HotReloadAutoRefreshHoldConstants.NewlyArmedMessageSuffix,
                    Does.Not.Contain("or '--revert-all' to release it"),
                    "A revert that keeps the hold must not be offered as a way to release it.");
            }
        }

        /// <summary>
        /// What: the hold is released once the domain holds no introduced type any more, so the
        /// type count arms the hold rather than pinning it on forever.
        /// </summary>
        [Test]
        public void ReconcileAfterTheTypesAreGone_ReleasesTheHold()
        {
            using (HotReloadCompositionRoot.BeginReplacement(HotReloadCompositionRoot.CreateProductionServices()))
            {
                ActivateArtifactWithOneType();
                HotReloadAutoRefreshHold.ReconcileForTesting();

                Assert.That(
                    HotReloadAutoRefreshHold.IsHeld,
                    Is.True,
                    "Arrange: an active introduced type must arm the hold.");
            }

            // A second replacement is what a Domain Reload leaves behind: an empty registry. Why
            // not the registry the scope above restored: that is the live one of this Editor
            // session, which may hold types a developer reloaded before running the tests.
            using (HotReloadCompositionRoot.BeginReplacement(HotReloadCompositionRoot.CreateProductionServices()))
            {

                Assert.That(
                    HotReloadCompositionRoot.Services.Domain.IntroducedTypeCount,
                    Is.EqualTo(0),
                    "Arrange: the reloaded domain must hold no introduced type.");

                HotReloadAutoRefreshHold.ReconcileForTesting();

                Assert.That(
                    HotReloadAutoRefreshHold.IsHeld,
                    Is.False,
                    "A domain that holds no introduced type must let Auto Refresh run again.");
            }
        }

        /// <summary>
        /// What: a run that starts with the hold released reports the hold as newly armed even
        /// when the periodic reconcile armed it mid-run, because the suffix promises "the first
        /// apply that arms the hold" and the run is the one that made the type active.
        /// </summary>
        [Test]
        public void BuildResult_WhenTheReconcileArmsTheHoldMidRun_StillReportsTheRunAsNewlyArmed()
        {
            using (HotReloadCompositionRoot.BeginReplacement(HotReloadCompositionRoot.CreateProductionServices()))
            {
                HotReloadRunAccumulator run = new HotReloadRunAccumulator(
                    HotReloadCompositionRoot.Services.Domain,
                    HotReloadCompositionRoot.Services.Patcher,
                    autoRefreshHeldAtStart: HotReloadAutoRefreshHold.IsHeld);

                Assert.That(
                    HotReloadAutoRefreshHold.IsHeld,
                    Is.False,
                    "Arrange: the run must start with the hold released.");

                ActivateArtifactWithOneType();
                // The 0.5s reconcile firing between the type becoming active and the run building
                // its result: the hold is already armed by the time the run syncs it.
                HotReloadAutoRefreshHold.ReconcileForTesting();

                HotReloadOrchestratorResult result = run.BuildResult("correlation-newly-armed");

                Assert.That(result.AutoRefreshHeld, Is.True);
                Assert.That(
                    result.AutoRefreshHoldNewlyArmed,
                    Is.True,
                    "A run that found Auto Refresh allowed and left it held is the run that armed it.");
            }
        }

        /// <summary>
        /// What: a run that was already holding Auto Refresh before it started does not report the
        /// hold as newly armed, so the suffix does not repeat on every later apply.
        /// </summary>
        [Test]
        public void BuildResult_WhenTheHoldWasAlreadyArmedBeforeTheRun_DoesNotReportItAsNewlyArmed()
        {
            using (HotReloadCompositionRoot.BeginReplacement(HotReloadCompositionRoot.CreateProductionServices()))
            {
                ActivateArtifactWithOneType();
                HotReloadAutoRefreshHold.ReconcileForTesting();

                Assert.That(
                    HotReloadAutoRefreshHold.IsHeld,
                    Is.True,
                    "Arrange: the hold must already be armed when the run starts.");

                HotReloadRunAccumulator run = new HotReloadRunAccumulator(
                    HotReloadCompositionRoot.Services.Domain,
                    HotReloadCompositionRoot.Services.Patcher,
                    autoRefreshHeldAtStart: HotReloadAutoRefreshHold.IsHeld);

                HotReloadOrchestratorResult result = run.BuildResult("correlation-already-armed");

                Assert.That(result.AutoRefreshHeld, Is.True);
                Assert.That(
                    result.AutoRefreshHoldNewlyArmed,
                    Is.False,
                    "Repeating the suffix on every apply would tell the caller a hold was just armed.");
            }
        }

        /// <summary>
        /// What: an introduced type is not counted as a patch — the reported active patch total
        /// does not move when a type becomes active.
        /// </summary>
        [Test]
        public void ActiveIntroducedType_LeavesThePatchCountsAtZero()
        {
            using (HotReloadCompositionRoot.BeginReplacement(HotReloadCompositionRoot.CreateProductionServices()))
            {
                ActivateArtifactWithOneType();

                Assert.That(HotReloadCompositionRoot.Services.Domain.IntroducedTypeCount, Is.EqualTo(1));
                Assert.That(
                    HotReloadCompositionRoot.Services.StatusExecutor.ExecuteStatus().ActivePatchTotal,
                    Is.EqualTo(0),
                    "A type must not be reported as a patched method.");
            }
        }

        /// <summary>
        /// What: a domain with no patch and no introduced type totals no runtime change, which is
        /// what keeps an idle editor out of the hold.
        /// </summary>
        [Test]
        public void NoPatchAndNoIntroducedType_TotalsNoRuntimeChange()
        {
            using (HotReloadCompositionRoot.BeginReplacement(HotReloadCompositionRoot.CreateProductionServices()))
            {

                Assert.That(HotReloadCompositionRoot.Services.Domain.CountActiveChanges().RuntimeChangeTotal, Is.EqualTo(0));

                HotReloadAutoRefreshHold.ReconcileForTesting();

                Assert.That(HotReloadAutoRefreshHold.IsHeld, Is.False);
            }
        }

        /// <summary>
        /// What: the runtime-change port the domain-reload warnings read is wired at hot-reload
        /// startup and answers with the introduced types, not with the patch count alone.
        /// </summary>
        [Test]
        public void RuntimeChangePort_ReportsTheIntroducedTypesOfThisDomain()
        {
            Func<int> reader = HotReloadRuntimeChangeCoordination.GetActiveRuntimeChangeCount;
            Assert.That(
                reader,
                Is.Not.Null,
                "The hot-reload startup must publish the runtime-change count for the other tools.");

            using (HotReloadCompositionRoot.BeginReplacement(HotReloadCompositionRoot.CreateProductionServices()))
            {

                Assert.That(reader(), Is.EqualTo(0), "An empty domain discards nothing.");

                ActivateArtifactWithOneType();

                Assert.That(
                    reader(),
                    Is.EqualTo(1),
                    "A type the reload introduced is discarded by the next Domain Reload too.");


            }
        }

        /// <summary>
        /// What: a run that both patches a method and introduces a type totals the two together,
        /// so the count of what a domain reload discards adds the kinds up instead of reporting
        /// whichever kind happens to be larger.
        /// </summary>
        [Test]
        public async Task RunPatchingAMethodAndIntroducingAType_TotalsBothKindsOfChange()
        {
            using (HotReloadCompositionRoot.BeginReplacement(HotReloadCompositionRoot.CreateProductionServices()))
            {
                HotReloadOrchestratorResult result = await RunPatchingABodyAndIntroducingATypeAsync();

                Assert.That(
                    result.ActivePatchTotal,
                    Is.EqualTo(1),
                    "Precondition: the run must have patched exactly one method.");
                Assert.That(
                    HotReloadCompositionRoot.Services.Domain.IntroducedTypeCount,
                    Is.EqualTo(1),
                    "Precondition: the run must have introduced exactly one type.");
                Assert.That(
                    HotReloadRuntimeChangeCoordination.GetActiveRuntimeChangeCount(),
                    Is.EqualTo(2),
                    "One patch and one type are two changes to lose, not one.");
                Assert.That(HotReloadCompositionRoot.Services.Domain.CountActiveChanges().RuntimeChangeTotal, Is.EqualTo(2));
            }
        }

        // The production route of an apply run: the orchestrator run, then the accumulator's
        // result build, which is where the run syncs the hold.
        private static async Task<HotReloadOrchestratorResult> RunIntroducingOnlyATypeAsync()
        {
            string hostPath = FixturePath(HostFileName);
            HotReloadOrchestratorResult result = await HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                new[] { hostPath },
                HotReloadTestSourceWriter.WriteEditedSource(
                    "IntroducedTypeHoldHost.cs",
                    InsertIntroducedType(File.ReadAllText(hostPath))),
                CancellationToken.None);

            Assert.That(
                result.PatchedTotal,
                Is.EqualTo(0),
                "Precondition: this run must patch no method, so only the type can hold refresh.");
            Assert.That(
                HotReloadCompositionRoot.Services.Domain.IntroducedTypeCount,
                Is.EqualTo(1),
                "Precondition: the run must have activated its introduced type.");
            return result;
        }

        private static async Task<HotReloadOrchestratorResult> RunPatchingABodyAndIntroducingATypeAsync()
        {
            string hostPath = FixturePath(HostFileName);
            return await HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                new[] { hostPath },
                HotReloadTestSourceWriter.WriteEditedSource(
                    "IntroducedTypeHoldAndPatchHost.cs",
                    EditTheScaledBody(InsertIntroducedType(File.ReadAllText(hostPath)))),
                CancellationToken.None);
        }

        private static string EditTheScaledBody(string hostSource)
        {
            Assert.That(hostSource, Does.Contain(ScaledBodyAnchor), "Precondition: scaled body anchor must exist.");
            return hostSource.Replace(ScaledBodyAnchor, "return factor * 4;", StringComparison.Ordinal);
        }

        private static void ActivateArtifactWithOneType()
        {
            ActivateArtifact(CreateDescriptor("Fixture.HeldType"));
        }

        private static void ActivateArtifact(params HotReloadIntroducedTypeDescriptor[] descriptors)
        {
            HotReloadIntroducedTypeArtifact artifact = new HotReloadIntroducedTypeArtifact(
                typeof(HotReloadIntroducedTypeHoldTests).Assembly,
                "artifact.dll",
                "artifact.pdb",
                new List<HotReloadIntroducedTypeDescriptor>(descriptors));
            HotReloadCompositionRoot.Services.Domain.IntroducedTypes.RegisterPrepared(artifact);
            HotReloadCompositionRoot.Services.Domain.IntroducedTypes.Activate(artifact);
        }

        private static HotReloadIntroducedTypeDescriptor CreateDescriptor(string metadataName)
        {
            return new HotReloadIntroducedTypeDescriptor(
                "Fixture.Assembly",
                "original-mvid",
                metadataName,
                "Assets/Tests/Fixture.cs",
                "fingerprint-" + metadataName,
                "public class Held { }");
        }

        // Why the explicit zero: syncing against no change is the only way to release a hold
        // without waiting for the reconcile to observe an empty domain.
        private static void ReleaseTheHold()
        {
            HotReloadAutoRefreshHold.Sync(0);
        }

        private static string InsertIntroducedType(string hostSource)
        {
            Assert.That(hostSource, Does.Contain(HostTypeAnchor), "Precondition: host type anchor must exist.");
            string introduced =
                "    public sealed class HotReloadIntroducedTypeHoldValue\n"
                + "    {\n"
                + "        public int Read()\n"
                + "        {\n"
                + "            return 11;\n"
                + "        }\n"
                + "    }\n"
                + "\n";
            return hostSource.Replace(HostTypeAnchor, introduced + HostTypeAnchor, StringComparison.Ordinal);
        }

        private static string FixturePath(string fileName)
        {
            string path = Path.GetFullPath(
                Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", fileName));
            Assert.That(File.Exists(path), Is.True, "Fixture missing: " + path);
            return path;
        }

        private const string HostFileName = "HotReloadCrossFileAddedMemberHost.cs";

        private const string HostTypeAnchor = "    public sealed class HotReloadCrossFileAddedMemberHost";

        private const string ScaledBodyAnchor = "return factor;";
    }
}
