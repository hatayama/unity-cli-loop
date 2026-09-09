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
            HotReloadAutoRefreshHold.SyncToActiveChanges();
        }

        // Why reverting here as well: a run of this class patches a fixture against an artifact
        // assembly that only lives while the run's replacement holder is open, so leaving the
        // patch active would fail the first later test that calls the fixture.
        [TearDown]
        public void TearDown()
        {
            HotReloadPatcher.RevertAll();
            HotReloadAutoRefreshHold.SyncToActiveChanges();
        }

        /// <summary>
        /// Verifies that a run whose only change is a new type declaration names that type in the
        /// apply response, so a reload with no method to patch still reports what it introduced.
        /// </summary>
        [Test]
        public async Task Build_RunIntroducesAType_ReportsTheTypeAsAnIntroducedRow()
        {
            using (HotReloadCompositionRoot.BeginReplacement(HotReloadCompositionRoot.CreateProductionServices()))
            {
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
            using (HotReloadCompositionRoot.BeginReplacement(HotReloadCompositionRoot.CreateProductionServices()))
            {
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
        /// Verifies that a run whose only change is a new type says so, instead of reporting the
        /// unchanged methods of the file as "nothing to patch": the reload did change something.
        /// </summary>
        [Test]
        public async Task Build_RunIntroducesATypeBesideUnchangedMethods_ReportsTheTypeInTheMessage()
        {
            using (HotReloadCompositionRoot.BeginReplacement(HotReloadCompositionRoot.CreateProductionServices()))
            {
                HotReloadResponse response = await RunIntroducingOnlyATypeAsync();

                Assert.That(
                    response.UnchangedTotal,
                    Is.GreaterThan(0),
                    "Precondition: the owner must hold methods this run left unchanged.");
                Assert.That(response.Message, Does.Contain("introduced 1 type"));
                Assert.That(response.Message, Does.Not.Contain("nothing to patch"));
                Assert.That(response.Message, Does.Not.Contain("nothing was changed"));
                Assert.That(
                    response.RecommendedNextAction,
                    Is.Empty,
                    "A successful reload recommends nothing, and introducing a type is a success.");
            }
        }

        /// <summary>
        /// Verifies that a reload which only binds types this domain already holds does not claim
        /// to have introduced them.
        /// </summary>
        [Test]
        public async Task Build_RunOnlyBindsTypesTheDomainHolds_SaysItBoundThemInsteadOfIntroducing()
        {
            using (HotReloadCompositionRoot.BeginReplacement(HotReloadCompositionRoot.CreateProductionServices()))
            {
                await RunIntroducingOnlyATypeAsync();
                HotReloadResponse second = await RunIntroducingOnlyATypeAsync();

                Assert.That(
                    second.IntroducedTypes.Count,
                    Is.EqualTo(1),
                    "Precondition: the second run had to bind the active type. "
                        + second.Message);
                Assert.That(second.IntroducedTypes[0].Kind, Is.EqualTo("AlreadyActive"));
                Assert.That(second.Message, Does.Contain("bound 1 introduced type"));
                Assert.That(
                    second.Message,
                    Does.Not.Contain("introduced 1 type"),
                    "A type the domain already holds was not introduced by this run.");
            }
        }

        /// <summary>
        /// Verifies that a method failure after the types became active is recommended as a
        /// partial apply, because the types stay loaded whatever the methods did.
        /// </summary>
        [Test]
        public async Task Build_MethodFailsAfterTheTypesBecameActive_RecommendsAPartialApply()
        {
            using (HotReloadCompositionRoot.BeginReplacement(HotReloadCompositionRoot.CreateProductionServices()))
            {
                using (HotReloadGroupProcessorDependencies.BeginReplacement(
                    CreateFailingApplyDependencies()))
                {
                    HotReloadResponse response = await RunIntroducingATypeAndEditingABodyAsync();

                    Assert.That(response.Success, Is.False);
                    Assert.That(
                        response.PatchedTotal,
                        Is.EqualTo(0),
                        "Precondition: no method was patched, which is what makes the type count decide.");
                    Assert.That(
                        response.IntroducedTypes.Count,
                        Is.EqualTo(1),
                        "Precondition: the commit boundary had to activate the type. " + response.Message);
                    Assert.That(
                        response.RecommendedNextAction,
                        Is.EqualTo(HotReloadConstants.PartialApplyRecommendedNextAction),
                        "A run that left types active applied part of what was asked.");
                }
            }
        }

        /// <summary>
        /// Verifies that a run whose only type outcome is a declaration bound from an assembly an
        /// earlier reload retained is not called a partial apply: this run activated nothing, so
        /// there is nothing of it to keep or discard.
        /// </summary>
        [Test]
        public async Task Build_MethodFailsWhileOnlyRetainedTypesWereBound_RecommendsNoPartialApply()
        {
            using (HotReloadCompositionRoot.BeginReplacement(HotReloadCompositionRoot.CreateProductionServices()))
            {
                using (HotReloadGroupProcessorDependencies.BeginReplacement(
                    CreateAlreadyActiveWithFailingApplyDependencies()))
                {
                    HotReloadResponse response = await RunEditingOnlyABodyAsync();

                    Assert.That(response.Success, Is.False);
                    Assert.That(
                        response.PatchedTotal,
                        Is.EqualTo(0),
                        "Precondition: no method was patched, which is what makes the type count decide.");
                    Assert.That(
                        CountTypeRows(response, "AlreadyActive"),
                        Is.EqualTo(1),
                        "Precondition: the run has to carry the retained declaration. " + response.Message);
                    Assert.That(
                        response.RecommendedNextAction,
                        Is.EqualTo(HotReloadConstants.FailedWithNoApplyRecommendedNextAction),
                        "A run that activated nothing of its own applied no part of what was asked.");
                }
            }
        }

        /// <summary>
        /// Verifies that a run whose only failure is a refused declaration says so in the message,
        /// instead of reporting a method failure the response has no row for.
        /// </summary>
        [Test]
        public async Task Build_OnlyATypeFailed_SaysTheDeclarationsWereRefused()
        {
            using (HotReloadCompositionRoot.BeginReplacement(HotReloadCompositionRoot.CreateProductionServices()))
            {
                using (HotReloadGroupProcessorDependencies.BeginReplacement(
                    CreatePreparationDependencies(
                        HotReloadIntroducedTypePreparationResult.TypeFailures(
                            new[] { CreateInjectedTypeFailure() }))))
                {
                    HotReloadResponse response = await RunAgainstTheHostAsync();

                    Assert.That(
                        response.Message,
                        Does.StartWith(HotReloadConstants.IntroducedTypeFailureApplyMessage),
                        "A refusal is the whole failure of this run.");
                }
            }
        }

        /// <summary>
        /// Verifies that a run that failed both a declaration and a method points at both sections,
        /// so neither failure is left out of the message.
        /// </summary>
        [Test]
        public async Task Build_ATypeAndAMethodFailed_PointsAtBothSections()
        {
            using (HotReloadCompositionRoot.BeginReplacement(HotReloadCompositionRoot.CreateProductionServices()))
            {
                using (HotReloadGroupProcessorDependencies.BeginReplacement(
                    CreateTypeFailingApplyDependencies(failTheMethod: true)))
                {
                    HotReloadResponse response = await RunEditingOnlyABodyAsync();

                    Assert.That(
                        response.Message,
                        Does.StartWith(HotReloadConstants.IntroducedTypeAndMethodFailureApplyMessage),
                        "Both sections carry a failure of this run.");
                }
            }
        }

        /// <summary>
        /// Verifies that a refused declaration beside a patched method still reports the refusal as
        /// the failure of the run, rather than the success message the methods alone would give.
        /// </summary>
        [Test]
        public async Task Build_ATypeFailedBesideAPatchedMethod_StillReportsTheRefusal()
        {
            using (HotReloadCompositionRoot.BeginReplacement(HotReloadCompositionRoot.CreateProductionServices()))
            {
                using (HotReloadGroupProcessorDependencies.BeginReplacement(
                    CreateTypeFailingApplyDependencies(failTheMethod: false)))
                {
                    HotReloadResponse response = await RunEditingOnlyABodyAsync();

                    Assert.That(
                        response.PatchedTotal,
                        Is.EqualTo(1),
                        "Precondition: the edited body has to be patched for real. " + response.Message);
                    Assert.That(
                        response.Message,
                        Does.StartWith(HotReloadConstants.IntroducedTypeFailureApplyMessage),
                        "A refused declaration is a failure whatever the methods did.");
                    Assert.That(
                        response.RecommendedNextAction,
                        Is.EqualTo(HotReloadConstants.PartialApplyRecommendedNextAction),
                        "A patched method is part of what was asked.");
                }
            }
        }

        /// <summary>
        /// Verifies that a run that both patched a method and introduced a type reports the type
        /// rows alongside the method summary, instead of reporting only half of what it did.
        /// </summary>
        [Test]
        public async Task Build_RunPatchesAMethodAndIntroducesAType_ReportsBothInTheMessage()
        {
            using (HotReloadCompositionRoot.BeginReplacement(HotReloadCompositionRoot.CreateProductionServices()))
            {
                HotReloadResponse response = await RunIntroducingATypeAndEditingABodyAsync();

                Assert.That(response.Success, Is.True, response.Message);
                Assert.That(
                    response.PatchedTotal,
                    Is.EqualTo(1),
                    "Precondition: the edited body has to be patched for real. " + response.Message);
                Assert.That(
                    response.Message,
                    Does.Contain("IntroducedTypes=1"),
                    "A message the methods decided must still point at the type rows.");
            }
        }

        /// <summary>
        /// Verifies that a reload which introduces no type keeps both type fields off the wire, so
        /// the response shape of the vast majority of reloads does not change.
        /// </summary>
        [Test]
        public async Task Build_RunIntroducesNoType_OmitsBothTypeFieldsFromTheWire()
        {
            using (HotReloadCompositionRoot.BeginReplacement(HotReloadCompositionRoot.CreateProductionServices()))
            {
                string callerPath = FixturePath(CallerFileName);
                HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                    new[] { callerPath },
                    HotReloadTestSourceWriter.WriteEditedSource(
                        "IntroducedTypeAbsentCaller.cs",
                        EditTheCallerBody(File.ReadAllText(callerPath))),
                    CancellationToken.None);
                HotReloadResponse response = HotReloadApplyResponseBuilder.Build(result, null);

                Assert.That(
                    response.ShouldSerializeIntroducedTypes(),
                    Is.False,
                    "A reload with no type row must not serialize an empty list.");
                Assert.That(
                    response.ShouldSerializeActiveIntroducedTypeTotal(),
                    Is.False,
                    "A domain holding no introduced type must not serialize a zero total.");
            }
        }

        // A run the apply stage is reached by: the type gives the commit boundary something to
        // activate, and the edited body gives the group an entry to resolve.
        private static async Task<HotReloadResponse> RunIntroducingATypeAndEditingABodyAsync()
        {
            string hostPath = FixturePath(HostFileName);
            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { hostPath },
                HotReloadTestSourceWriter.WriteEditedSource(
                    "IntroducedTypeAndBodyHost.cs",
                    EditTheScaledBody(InsertIntroducedType(File.ReadAllText(hostPath)))),
                CancellationToken.None);
            return HotReloadApplyResponseBuilder.Build(result, null);
        }

        private static HotReloadIntroducedTypeOutcome CreateInjectedTypeFailure()
        {
            return HotReloadIntroducedTypeOutcome.Failed(
                IntroducedTypeMetadataName,
                "SomeAssembly",
                "Assets/Example.cs",
                InjectedTypeFailureReason);
        }

        // The production pipeline with the apply stage adding a refused declaration to the file's
        // own buffer, which is the carrier the response is built from, so the refusal reaches the
        // response beside whatever the methods did.
        private static HotReloadGroupProcessorDependencies CreateTypeFailingApplyDependencies(
            bool failTheMethod)
        {
            return HotReloadGroupProcessorDependencies.Create(
                HotReloadGroupProcessor.TryAppendNewSourceMembershipFailure,
                HotReloadIntroducedTypePreparation.PrepareAsync,
                TransformWorkerClient.RunAsync,
                HotReloadGroupProcessor.GateAndCompileAsync,
                HotReloadGroupEntryPreparation.PrepareGroup,
                (context, compile, preparedFiles) =>
                {
                    foreach (HotReloadGroupFile file in context.Files)
                    {
                        file.Sinks.IntroducedTypes.Add(CreateInjectedTypeFailure());
                        if (!failTheMethod)
                        {
                            continue;
                        }

                        file.Sinks.Outcomes.Add(
                            HotReloadMethodOutcome.Failed(
                                "InjectedMethod",
                                "The injected apply stage failed this method.",
                                file.ProjectRelativePath));
                    }

                    return failTheMethod
                        ? HotReloadFileEntryApplier.BuildUnappliedGroupResults(context.Files)
                        : HotReloadEntryApplier.ApplyPreparedEntries(context, compile, preparedFiles);
                });
        }

        // A run that reaches the apply stage without introducing a type of its own.
        private static async Task<HotReloadResponse> RunEditingOnlyABodyAsync()
        {
            string callerPath = FixturePath(CallerFileName);
            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { callerPath },
                HotReloadTestSourceWriter.WriteEditedSource(
                    "RetainedTypeOnlyCaller.cs",
                    EditTheCallerBody(File.ReadAllText(callerPath))),
                CancellationToken.None);
            return HotReloadApplyResponseBuilder.Build(result, null);
        }

        // The production pipeline with the preparation reporting one retained declaration and the
        // apply stage failing, so the run carries a type row it did not activate itself.
        private static HotReloadGroupProcessorDependencies CreateAlreadyActiveWithFailingApplyDependencies()
        {
            return HotReloadGroupProcessorDependencies.Create(
                HotReloadGroupProcessor.TryAppendNewSourceMembershipFailure,
                (files, input, ct) => Task.FromResult(
                    HotReloadIntroducedTypePreparationResult.NoIntroducedTypes(
                        new[]
                        {
                            HotReloadIntroducedTypeOutcome.AlreadyActive(
                                "Example.RetainedType",
                                "SomeAssembly",
                                files[0].ProjectRelativePath)
                        })),
                TransformWorkerClient.RunAsync,
                HotReloadGroupProcessor.GateAndCompileAsync,
                HotReloadGroupEntryPreparation.PrepareGroup,
                (context, compile, preparedFiles) =>
                {
                    foreach (HotReloadGroupFile file in context.Files)
                    {
                        file.Sinks.Outcomes.Add(
                            HotReloadMethodOutcome.Failed(
                                "InjectedMethod",
                                "The injected apply stage failed this method.",
                                file.ProjectRelativePath));
                    }

                    return HotReloadFileEntryApplier.BuildUnappliedGroupResults(context.Files);
                });
        }

        private static string EditTheScaledBody(string hostSource)
        {
            Assert.That(hostSource, Does.Contain(ScaledBodyAnchor), "Precondition: scaled body anchor must exist.");
            return hostSource.Replace(ScaledBodyAnchor, "return factor * 4;", StringComparison.Ordinal);
        }

        // The production pipeline with only the apply stage replaced, so the commit boundary runs
        // for real and the failure lands after the types became active.
        private static HotReloadGroupProcessorDependencies CreateFailingApplyDependencies()
        {
            return HotReloadGroupProcessorDependencies.Create(
                HotReloadGroupProcessor.TryAppendNewSourceMembershipFailure,
                HotReloadIntroducedTypePreparation.PrepareAsync,
                TransformWorkerClient.RunAsync,
                HotReloadGroupProcessor.GateAndCompileAsync,
                HotReloadGroupEntryPreparation.PrepareGroup,
                (context, compile, preparedFiles) =>
                {
                    foreach (HotReloadGroupFile file in context.Files)
                    {
                        file.Sinks.Outcomes.Add(
                            HotReloadMethodOutcome.Failed(
                                "InjectedMethod",
                                "The injected apply stage failed this method.",
                                file.ProjectRelativePath));
                    }

                    return HotReloadFileEntryApplier.BuildUnappliedGroupResults(context.Files);
                });
        }

        private static string EditTheCallerBody(string callerSource)
        {
            Assert.That(callerSource, Does.Contain(CallerBodyAnchor), "Precondition: caller body anchor must exist.");
            return callerSource.Replace(
                CallerBodyAnchor,
                "return host.Value() + 3;",
                StringComparison.Ordinal);
        }

        private const string CallerFileName = "HotReloadCrossFileAddedMemberCaller.cs";

        private const string CallerBodyAnchor = "return host.Value();";

        private const string ScaledBodyAnchor = "return factor;";

        /// <summary>
        /// Verifies that a declaration this stage cannot introduce is reported as a warning of the
        /// file that declares it and leaves the run successful, because the declaration stays in
        /// the source and only a compile can make it available.
        /// </summary>
        [Test]
        public async Task Build_DeclarationCannotBeIntroduced_WarnsWithoutFailingTheRun()
        {
            using (HotReloadCompositionRoot.BeginReplacement(HotReloadCompositionRoot.CreateProductionServices()))
            {
                string hostPath = FixturePath(HostFileName);
                HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                    new[] { hostPath },
                    HotReloadTestSourceWriter.WriteEditedSource(
                        "IntroducedTypeNoticeHost.cs",
                        InsertUnintroducibleDeclaration(File.ReadAllText(hostPath))),
                    CancellationToken.None);
                HotReloadResponse response = HotReloadApplyResponseBuilder.Build(result, null);

                Assert.That(
                    response.Success,
                    Is.True,
                    "A declaration that is simply not introduced must not fail the reload.");
                Assert.That(
                    response.IntroducedTypes.Count,
                    Is.EqualTo(0),
                    "Nothing was introduced, so there is no type row to report.");
                Assert.That(
                    FindWarning(response, "requires a compile"),
                    Is.Not.Null,
                    "The run must say why the declaration is not available. "
                        + string.Join(" | ", response.Warnings));
                Assert.That(
                    FindWarning(response, "requires a compile"),
                    Does.Contain(HostFileName),
                    "The warning must name the file that declares it.");
            }
        }

        private static string FindWarning(HotReloadResponse response, string fragment)
        {
            foreach (string warning in response.Warnings)
            {
                if (warning.Contains(fragment, StringComparison.Ordinal))
                {
                    return warning;
                }
            }

            return null;
        }

        // A delegate declaration is a type this stage does not introduce, and the worker reports
        // it without refusing the run.
        private static string InsertUnintroducibleDeclaration(string hostSource)
        {
            Assert.That(hostSource, Does.Contain(HostTypeAnchor), "Precondition: host type anchor must exist.");
            return hostSource.Replace(
                HostTypeAnchor,
                "    public delegate int HotReloadCrossFileIntroducedDelegate(int value);\n\n" + HostTypeAnchor,
                StringComparison.Ordinal);
        }

        /// <summary>
        /// Verifies that a type failure fails the run and reports the type instead of a generic
        /// method row, so the response says which declaration the reload refused.
        /// </summary>
        [Test]
        public async Task Build_TypePreparationReportsATypeFailure_FailsTheRunWithATypeRow()
        {
            using (HotReloadCompositionRoot.BeginReplacement(HotReloadCompositionRoot.CreateProductionServices()))
            {
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
        /// Verifies that a refused declaration does not take the reload's other findings with it:
        /// the declarations bound from a retained assembly and the non-fatal notices of the same
        /// preparation are still reported beside the refusal.
        /// </summary>
        [Test]
        public async Task Build_TypeFailureBesideOtherFindings_KeepsTheReusesAndNotices()
        {
            using (HotReloadCompositionRoot.BeginReplacement(HotReloadCompositionRoot.CreateProductionServices()))
            {
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
                            },
                            new[]
                            {
                                HotReloadIntroducedTypeOutcome.AlreadyActive(
                                    "Example.RetainedType",
                                    "SomeAssembly",
                                    "Assets/Example.cs")
                            },
                            new[]
                            {
                                new HotReloadIntroducedTypeNotice(
                                    "Assets/Example.cs",
                                    InjectedNoticeText)
                            }))))
                {
                    HotReloadResponse response = await RunAgainstTheHostAsync();

                    Assert.That(response.Success, Is.False, "A refused declaration must fail the run.");
                    Assert.That(
                        CountTypeRows(response, "AlreadyActive"),
                        Is.EqualTo(1),
                        "A declaration bound from a retained assembly is still bound.");
                    Assert.That(
                        FindWarning(response, InjectedNoticeText),
                        Is.Not.Null,
                        "A notice of the same preparation must still reach the caller.");
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
            using (HotReloadCompositionRoot.BeginReplacement(HotReloadCompositionRoot.CreateProductionServices()))
            {
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

        private static int CountTypeRows(HotReloadResponse response, string kind)
        {
            int count = 0;
            foreach (HotReloadIntroducedTypeResult row in response.IntroducedTypes)
            {
                if (string.Equals(row.Kind, kind, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
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

        private const string InjectedNoticeText =
            "This declaration will keep not working until the next compile.";

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
