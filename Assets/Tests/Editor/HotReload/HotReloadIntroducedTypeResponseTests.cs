using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers how the types a run introduces reach the public apply response.
    /// </summary>
    public class HotReloadIntroducedTypeResponseTests
    {
        [SetUp]
        public void SetUp()
        {
            HotReloadPatcher.RevertAll();
            HotReloadAutoRefreshHold.Sync(HotReloadPatcher.ActiveChangeCount);
        }

        // Why reverting here as well: a run of this class patches a fixture against an artifact
        // assembly that only lives while the run's replacement holder is open, so leaving the
        // patch active would fail the first later test that calls the fixture.
        [TearDown]
        public void TearDown()
        {
            HotReloadPatcher.RevertAll();
            HotReloadAutoRefreshHold.Sync(HotReloadPatcher.ActiveChangeCount);
        }

        /// <summary>
        /// Verifies that a run whose only change is a new type declaration names that type in the
        /// apply response, so a reload with no method to patch still reports what it introduced.
        /// </summary>
        [Test]
        public async Task Build_RunIntroducesAType_ReportsTheTypeAsAnIntroducedRow()
        {
            using (HotReloadIntroducedTypeHolder.BeginReplacement())
            {
                HotReloadIntroducedTypeHolder.Initialize();
                HotReloadResponse response = await RunIntroducingOnlyATypeAsync();

                Assert.That(
                    response.IntroducedTypes.Count,
                    Is.EqualTo(1),
                    "The run introduced exactly one type, so the response must carry one row.");
                HotReloadIntroducedTypeResult row = response.IntroducedTypes[0];
                Assert.That(row.Kind, Is.EqualTo("Introduced"));
                Assert.That(row.TypeName, Is.EqualTo(IntroducedTypeMetadataName));
                Assert.That(
                    row.FilePath,
                    Does.EndWith(HostFileName),
                    "The row must name the file that declares the type.");
                Assert.That(row.AssemblyName, Is.Not.Empty, "The row must name the assembly it belongs to.");
                Assert.That(row.Reason, Is.Empty, "An introduced type has no failure reason.");
                Assert.That(
                    response.ActiveIntroducedTypeTotal,
                    Is.EqualTo(1),
                    "The domain holds the one type this run introduced.");
            }
        }

        /// <summary>
        /// Verifies that an introduced type is never folded into the patch, added-field, or
        /// unchanged totals of the response, which count methods and fields only.
        /// </summary>
        [Test]
        public async Task Build_RunIntroducesAType_LeavesTheMethodAndFieldTotalsUntouched()
        {
            using (HotReloadIntroducedTypeHolder.BeginReplacement())
            {
                HotReloadIntroducedTypeHolder.Initialize();
                HotReloadResponse response = await RunIntroducingOnlyATypeAsync();

                Assert.That(
                    response.IntroducedTypes.Count,
                    Is.EqualTo(1),
                    "Precondition: the run had to introduce one type.");
                Assert.That(response.PatchedTotal, Is.EqualTo(0), "A type is not a patched method.");
                Assert.That(
                    response.ActivePatchTotal,
                    Is.EqualTo(0),
                    "A type is not an active patch.");
                Assert.That(response.AddedFieldTotal, Is.EqualTo(0), "A type is not an added field.");
                Assert.That(response.ClearedCount, Is.EqualTo(0), "A type clears no patch.");
            }
        }

        /// <summary>
        /// Verifies that a type failure fails the run and reports the type instead of a generic
        /// method row, so the response says which declaration the reload refused.
        /// </summary>
        [Test]
        public async Task Build_TypePreparationReportsATypeFailure_FailsTheRunWithATypeRow()
        {
            using (HotReloadIntroducedTypeHolder.BeginReplacement())
            {
                HotReloadIntroducedTypeHolder.Initialize();
                using (HotReloadGroupProcessorDependencies.BeginReplacement(
                    CreatePreparationDependencies(
                        HotReloadIntroducedTypePreparationResult.TypeFailures(
                            new[]
                            {
                                HotReloadIntroducedTypeOutcome.Failed(
                                    IntroducedTypeMetadataName,
                                    "SomeAssembly",
                                    "Assets/Example.cs",
                                    InjectedTypeFailureReason)
                            }))))
                {
                    HotReloadResponse response = await RunAgainstTheHostAsync();

                    Assert.That(
                        response.Success,
                        Is.False,
                        "A refused type declaration must fail the run.");
                    Assert.That(response.IntroducedTypes.Count, Is.EqualTo(1));
                    Assert.That(response.IntroducedTypes[0].Kind, Is.EqualTo("Failed"));
                    Assert.That(response.IntroducedTypes[0].Reason, Is.EqualTo(InjectedTypeFailureReason));
                    Assert.That(
                        response.RecommendedNextAction,
                        Is.Not.Empty,
                        "A failed run must recommend what to do next.");
                    Assert.That(
                        CountMethodFailures(response, InjectedTypeFailureReason),
                        Is.EqualTo(0),
                        "A type failure must not be reported a second time as a method row.");
                }
            }
        }

        /// <summary>
        /// Verifies that a preparation worker that fails to run is reported as the run-level
        /// failure it is, not as a type outcome: the preparation runs for every reload, including
        /// the ones that declare no type at all.
        /// </summary>
        [Test]
        public async Task Build_PreparationWorkerFails_ReportsTheFailureWithoutAnyTypeRow()
        {
            using (HotReloadIntroducedTypeHolder.BeginReplacement())
            {
                HotReloadIntroducedTypeHolder.Initialize();
                using (HotReloadGroupProcessorDependencies.BeginReplacement(
                    CreatePreparationDependencies(
                        HotReloadIntroducedTypePreparationResult.WorkerFailure(InjectedWorkerFailureReason))))
                {
                    HotReloadResponse response = await RunAgainstTheHostAsync();

                    Assert.That(response.Success, Is.False, "A failed preparation must fail the run.");
                    Assert.That(
                        response.IntroducedTypes.Count,
                        Is.EqualTo(0),
                        "A worker that never ran refused no declaration, so there is no type to report.");
                    Assert.That(
                        CountMethodFailures(response, InjectedWorkerFailureReason),
                        Is.GreaterThan(0),
                        "The run-level failure must still be reported against the files of the group.");
                }
            }
        }

        // The production pipeline with only the preparation stage replaced, which is the one stage
        // whose two failure kinds cannot both be provoked from a fixture the repository compiles.
        private static HotReloadGroupProcessorDependencies CreatePreparationDependencies(
            HotReloadIntroducedTypePreparationResult preparationResult)
        {
            return HotReloadGroupProcessorDependencies.Create(
                HotReloadGroupProcessor.TryAppendNewSourceMembershipFailure,
                (files, input, ct) => Task.FromResult(preparationResult),
                TransformWorkerClient.RunAsync,
                HotReloadGroupProcessor.GateAndCompileAsync,
                HotReloadGroupEntryPreparation.PrepareGroup,
                HotReloadEntryApplier.ApplyPreparedEntries);
        }

        private static async Task<HotReloadResponse> RunAgainstTheHostAsync()
        {
            string hostPath = FixturePath(HostFileName);
            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { hostPath },
                HotReloadTestSourceWriter.WriteEditedSource(
                    "IntroducedTypeFailureHost.cs",
                    InsertIntroducedType(File.ReadAllText(hostPath))),
                CancellationToken.None);
            return HotReloadApplyResponseBuilder.Build(result, null);
        }

        private static int CountMethodFailures(HotReloadResponse response, string reasonFragment)
        {
            int count = 0;
            foreach (HotReloadMethodResult method in response.Methods)
            {
                if (string.Equals(method.Kind, "Failed", StringComparison.Ordinal)
                    && method.Reason.Contains(reasonFragment, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        private const string InjectedTypeFailureReason =
            "The introduced type artifact could not be compiled.";

        private const string InjectedWorkerFailureReason =
            "Introduced-type preparation failed: the worker did not answer.";

        // The production route of an apply run: the orchestrator run, then the response builder
        // the tool calls with its result.
        private static async Task<HotReloadResponse> RunIntroducingOnlyATypeAsync()
        {
            string hostPath = FixturePath(HostFileName);
            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { hostPath },
                HotReloadTestSourceWriter.WriteEditedSource(
                    "IntroducedTypeResponseHost.cs",
                    InsertIntroducedType(File.ReadAllText(hostPath))),
                CancellationToken.None);

            Assert.That(
                FindFailureReason(result),
                Is.Null,
                "Precondition: a reload that only introduces a type must not fail a method.");
            return HotReloadApplyResponseBuilder.Build(result, null);
        }

        private static string FindFailureReason(HotReloadOrchestratorResult result)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Failed)
                {
                    return outcome.Reason;
                }
            }

            return null;
        }

        private static string InsertIntroducedType(string hostSource)
        {
            Assert.That(hostSource, Does.Contain(HostTypeAnchor), "Precondition: host type anchor must exist.");
            string introduced =
                "    public sealed class HotReloadCrossFileIntroducedValue\n"
                + "    {\n"
                + "        public int Read()\n"
                + "        {\n"
                + "            return 7;\n"
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

        private const string IntroducedTypeMetadataName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadCrossFileIntroducedValue";
    }
}
