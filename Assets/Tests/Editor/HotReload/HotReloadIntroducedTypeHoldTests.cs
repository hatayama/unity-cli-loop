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
        [SetUp]
        public void SetUp()
        {
            HotReloadPatcher.RevertAll();
            ReleaseTheHold();
        }

        // Why releasing here: the hold flag of the test service is shared by every test of the
        // run, so a test that armed it would otherwise decide the first assert of the next one.
        [TearDown]
        public void TearDown()
        {
            HotReloadPatcher.RevertAll();
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

            using (HotReloadIntroducedTypeHolder.BeginReplacement())
            {
                HotReloadIntroducedTypeHolder.Initialize();
                await RunIntroducingOnlyATypeAsync();

                Assert.That(
                    HotReloadAutoRefreshHold.IsHeld,
                    Is.True,
                    "The run that introduced the type must arm the hold.");

                HotReloadAutoRefreshHold.ReconcileForTesting();
                Assert.That(
                    HotReloadAutoRefreshHold.IsHeld,
                    Is.True,
                    "The periodic reconcile must not release a hold the introduced type still needs.");

                HotReloadStatusExecutor.ExecuteStatus();
                Assert.That(
                    HotReloadAutoRefreshHold.IsHeld,
                    Is.True,
                    "A status query must not release the hold.");

                HotReloadResponse revert = HotReloadStatusExecutor.ExecuteRevertAll();
                Assert.That(
                    HotReloadAutoRefreshHold.IsHeld,
                    Is.True,
                    "A revert cannot unload the artifact assembly, so it must keep the hold.");
                Assert.That(revert.Message, Does.Contain("Domain Reload"));
            }
        }

        /// <summary>
        /// What: the hold is released once the domain holds no introduced type any more, so the
        /// type count arms the hold rather than pinning it on forever.
        /// </summary>
        [Test]
        public void ReconcileAfterTheTypesAreGone_ReleasesTheHold()
        {
            using (HotReloadIntroducedTypeHolder.BeginReplacement())
            {
                HotReloadIntroducedTypeHolder.Initialize();
                ActivateArtifactWithOneType();
                HotReloadAutoRefreshHold.ReconcileForTesting();

                Assert.That(
                    HotReloadAutoRefreshHold.IsHeld,
                    Is.True,
                    "Arrange: an active introduced type must arm the hold.");
            }

            // Leaving the replacement is what a Domain Reload does to the registry: the types of
            // the previous domain are gone with the assembly that carried them.
            HotReloadAutoRefreshHold.ReconcileForTesting();

            Assert.That(
                HotReloadAutoRefreshHold.IsHeld,
                Is.False,
                "A domain that holds no introduced type must let Auto Refresh run again.");
        }

        /// <summary>
        /// What: an introduced type is not counted as a patch — neither the pause-point patch
        /// count nor the reported active patch total moves when a type becomes active.
        /// </summary>
        [Test]
        public void ActiveIntroducedType_LeavesThePatchCountsAtZero()
        {
            using (HotReloadIntroducedTypeHolder.BeginReplacement())
            {
                HotReloadIntroducedTypeHolder.Initialize();
                ActivateArtifactWithOneType();

                Assert.That(HotReloadActiveChangeCounts.IntroducedTypeCount, Is.EqualTo(1));
                Assert.That(
                    ReadPausePointPatchCount(),
                    Is.EqualTo(0),
                    "The pause-point side asks for shims, which a type declaration never adds.");
                Assert.That(
                    HotReloadStatusExecutor.ExecuteStatus().ActivePatchTotal,
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
            using (HotReloadIntroducedTypeHolder.BeginReplacement())
            {
                HotReloadIntroducedTypeHolder.Initialize();

                Assert.That(HotReloadActiveChangeCounts.RuntimeChangeTotal, Is.EqualTo(0));

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

            using (HotReloadIntroducedTypeHolder.BeginReplacement())
            {
                HotReloadIntroducedTypeHolder.Initialize();

                Assert.That(reader(), Is.EqualTo(0), "An empty domain discards nothing.");

                ActivateArtifactWithOneType();

                Assert.That(
                    reader(),
                    Is.EqualTo(1),
                    "A type the reload introduced is discarded by the next Domain Reload too.");
            }
        }

        private static int ReadPausePointPatchCount()
        {
            Func<int> reader = HotReloadPausePointCoordination.GetActiveHotReloadPatchCount;
            Assert.That(reader, Is.Not.Null, "Precondition: the pause-point count must be wired.");
            return reader();
        }

        // The production route of an apply run: the orchestrator run, then the accumulator's
        // result build, which is where the run syncs the hold.
        private static async Task RunIntroducingOnlyATypeAsync()
        {
            string hostPath = FixturePath(HostFileName);
            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
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
                HotReloadActiveChangeCounts.IntroducedTypeCount,
                Is.EqualTo(1),
                "Precondition: the run must have activated its introduced type.");
        }

        private static void ActivateArtifactWithOneType()
        {
            HotReloadIntroducedTypeArtifact artifact = new HotReloadIntroducedTypeArtifact(
                typeof(HotReloadIntroducedTypeHoldTests).Assembly,
                "artifact.dll",
                "artifact.pdb",
                new List<HotReloadIntroducedTypeDescriptor>
                {
                    new HotReloadIntroducedTypeDescriptor(
                        "Fixture.Assembly",
                        "original-mvid",
                        "Fixture.HeldType",
                        "Assets/Tests/Fixture.cs",
                        "fingerprint-held",
                        "public class Held { }")
                });
            HotReloadIntroducedTypeHolder.Registry.RegisterPrepared(artifact);
            HotReloadIntroducedTypeHolder.Registry.Activate(artifact);
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
    }
}
