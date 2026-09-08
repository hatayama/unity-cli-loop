using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers the editor-owned introduced type registry and resolver pair that activation runs through.
    /// </summary>
    public class HotReloadIntroducedTypeActivationTests
    {
        private Func<HotReloadEditorStateSnapshot> _previousSnapshotProvider;

        [SetUp]
        public void SetUp()
        {
            _previousSnapshotProvider = HotReloadEditorStateSnapshotProvider.CaptureForTesting;
            HotReloadPatcher.RevertAll();
            HotReloadAutoRefreshHold.SyncToActiveChanges();
        }

        // Why reverting here as well: a run of this class patches a fixture caller against an
        // artifact assembly that only lives while the run's prepared scope is open, so leaving
        // the patch active would fail the first later test that calls the fixture.
        [TearDown]
        public void TearDown()
        {
            HotReloadEditorStateSnapshotProvider.CaptureForTesting = _previousSnapshotProvider;
            HotReloadPatcher.RevertAll();
            HotReloadAutoRefreshHold.SyncToActiveChanges();
        }

        /// <summary>
        /// Verifies that the resolver the production holder exposes resolves what the registry it
        /// exposes activated, so both sides of the pair are the same instance.
        /// </summary>
        [Test]
        public void Holder_AfterInitialize_ResolverAndRegistryShareOneInstance()
        {
            HotReloadIntroducedTypeArtifact artifact = CreateArtifact();

            using (HotReloadIntroducedTypeHolder.BeginReplacement())
            {
                HotReloadIntroducedTypeHolder.Initialize();
                HotReloadIntroducedTypeHolder.Registry.RegisterPrepared(artifact);
                HotReloadIntroducedTypeHolder.Registry.Activate(artifact);

                Assert.That(
                    HotReloadIntroducedTypeHolder.Resolver.ResolveExact(artifact.AssemblyFullName),
                    Is.SameAs(artifact.Assembly));
            }
        }

        /// <summary>
        /// Verifies that Initialize publishes the introduced type names through the tool contract
        /// port, so a tool that cannot reference this assembly sees what the registry holds.
        /// </summary>
        [Test]
        public void Holder_AfterInitialize_PublishesActiveTypeNamesThroughTheToolContractPort()
        {
            HotReloadIntroducedTypeArtifact artifact = CreateArtifact();

            using (HotReloadIntroducedTypeHolder.BeginReplacement())
            {
                HotReloadIntroducedTypeHolder.Initialize();
                HotReloadIntroducedTypeHolder.Registry.RegisterPrepared(artifact);
                HotReloadIntroducedTypeHolder.Registry.Activate(artifact);

                Func<IReadOnlyList<string>> describe =
                    HotReloadIntroducedTypeCoordination.DescribeActiveTypeNames;
                Assert.That(describe, Is.Not.Null);

                List<string> expected = new List<string>();
                foreach (HotReloadIntroducedTypeDescriptor descriptor
                    in HotReloadIntroducedTypeHolder.Registry.DescribeActive())
                {
                    expected.Add(descriptor.MetadataName);
                }

                Assert.That(describe(), Is.EqualTo(expected));
                Assert.That(expected, Is.Not.Empty);
            }
        }

        /// <summary>
        /// Verifies that closing a replacement scope puts the original pair back, so a type
        /// activated inside the scope is no longer resolvable afterwards.
        /// </summary>
        [Test]
        public void Holder_ReplacementScopeClosed_RestoresTheOriginalPair()
        {
            HotReloadIntroducedTypeArtifact artifact = CreateArtifact();

            using (HotReloadIntroducedTypeHolder.BeginReplacement())
            {
                HotReloadIntroducedTypeHolder.Initialize();
                HotReloadIntroducedTypeRegistry scoped = HotReloadIntroducedTypeHolder.Registry;
                scoped.RegisterPrepared(artifact);
                scoped.Activate(artifact);
            }

            Assert.That(
                HotReloadIntroducedTypeHolder.Resolver.ResolveExact(artifact.AssemblyFullName),
                Is.Null);
        }

        /// <summary>
        /// Verifies that opening a replacement scope stops the resolver it took over from
        /// answering binds, and that closing the scope makes the original registry resolvable
        /// again through a live resolver.
        /// </summary>
        [Test]
        public void Holder_ReplacementScopeOpen_OnlyTheReplacementResolverAnswersBinds()
        {
            using (HotReloadIntroducedTypeHolder.BeginReplacement())
            {
                HotReloadIntroducedTypeHolder.Initialize();
                HotReloadIntroducedTypeRegistry outerRegistry = HotReloadIntroducedTypeHolder.Registry;
                HotReloadIntroducedTypeAssemblyResolver outerResolver = HotReloadIntroducedTypeHolder.Resolver;

                using (HotReloadIntroducedTypeHolder.BeginReplacement())
                {
                    HotReloadIntroducedTypeHolder.Initialize();
                    HotReloadIntroducedTypeAssemblyResolver innerResolver = HotReloadIntroducedTypeHolder.Resolver;
                    int innerBefore = innerResolver.ResolutionCount;
                    int outerBefore = outerResolver.ResolutionCount;

                    RequestUnknownAssembly();

                    Assert.That(innerResolver.ResolutionCount, Is.GreaterThan(innerBefore));
                    Assert.That(
                        outerResolver.ResolutionCount,
                        Is.EqualTo(outerBefore),
                        "The taken-over resolver must be unsubscribed, or two resolvers answer one bind.");
                }

                Assert.That(HotReloadIntroducedTypeHolder.Registry, Is.SameAs(outerRegistry));
                HotReloadIntroducedTypeAssemblyResolver restoredResolver = HotReloadIntroducedTypeHolder.Resolver;
                int restoredBefore = restoredResolver.ResolutionCount;

                RequestUnknownAssembly();

                Assert.That(restoredResolver.ResolutionCount, Is.GreaterThan(restoredBefore));
            }
        }

        /// <summary>
        /// Verifies that a type introduced in one edited file reaches the transform run of the same
        /// reload as a prepared artifact record, that the owner file's rows carry no member of the
        /// introduced declaration, and that the caller edited alongside it is patched against the
        /// artifact instead of the source that still declares the type.
        /// </summary>
        [Test]
        public async Task Run_TypeIntroducedWithItsCaller_PropagatesThePreparedArtifactToTheTransformRun()
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");
            string callerPath = FixturePath("HotReloadCrossFileAddedMemberCaller.cs");
            string hostProjectRelativePath = HotReloadPatchTargetSupport.ToProjectRelativeScriptPath(hostPath);
            TransformWorkerInputDto transformInput = null;
            TransformWorkerOutputDto transformOutput = null;
            HotReloadIntroducedTypeArtifact preparedArtifact = null;
            string ownerAssemblyName = null;

            using (HotReloadIntroducedTypeHolder.BeginReplacement())
            {
                HotReloadIntroducedTypeHolder.Initialize();
                using (HotReloadGroupProcessorDependencies.BeginReplacement(
                    HotReloadGroupProcessorDependencies.Create(
                        HotReloadGroupProcessor.TryAppendNewSourceMembershipFailure,
                        async (files, input, ct) =>
                        {
                            ownerAssemblyName = files[0].AssemblyName;
                            HotReloadIntroducedTypePreparationResult preparation =
                                await HotReloadIntroducedTypePreparation.PrepareAsync(files, input, ct);
                            preparedArtifact = preparation.Prepared?.Artifact;
                            return preparation;
                        },
                        async (input, ct) =>
                        {
                            transformInput = input;
                            TransformWorkerClientResult result = await TransformWorkerClient.RunAsync(input, ct);
                            transformOutput = result.Output;
                            return result;
                        },
                        HotReloadGroupProcessor.GateAndCompileAsync,
                        HotReloadGroupEntryPreparation.PrepareGroup,
                        HotReloadEntryApplier.ApplyPreparedEntries)))
                {
                    HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                        new[] { hostPath, callerPath },
                        contentPathOverride: null,
                        CancellationToken.None,
                        new Dictionary<string, string>
                        {
                            [hostPath] = HotReloadTestSourceWriter.WriteEditedSource(
                                "IntroducedTypePropagationHost.cs",
                                InsertIntroducedType(File.ReadAllText(hostPath))),
                            [callerPath] = HotReloadTestSourceWriter.WriteEditedSource(
                                "IntroducedTypePropagationCaller.cs",
                                CallIntroducedType(File.ReadAllText(callerPath)))
                        });

                    AssertCallerIsPatched(result);
                }
            }

            Assert.That(preparedArtifact, Is.Not.Null, "The run had to prepare the introduced type.");
            Assert.That(
                string.Equals(
                    FindDescriptor(preparedArtifact, IntroducedTypeMetadataName).OriginalAssemblyName,
                    ownerAssemblyName,
                    StringComparison.Ordinal),
                Is.True,
                "The descriptor identity the short-circuit branch keys on must be the file's assembly name.");
            AssertArtifactRecordReachedTheTransformRun(transformInput, preparedArtifact);
            AssertOwnerRowsCarryNoIntroducedMember(transformOutput, hostProjectRelativePath);
        }

        /// <summary>
        /// Verifies that an artifact active for another generation of the same compiled assembly
        /// is left out of the run's records, because its types were normalized back to an assembly
        /// this run no longer edits.
        /// </summary>
        [Test]
        public async Task Run_ActiveArtifactOfAnotherGeneration_IsNotOfferedToTheTransformRun()
        {
            string callerPath = FixturePath("HotReloadCrossFileAddedMemberCaller.cs");
            string callerAssemblyName = ResolveAssemblyName(callerPath);
            TransformWorkerInputDto transformInput = null;

            using (HotReloadIntroducedTypeHolder.BeginReplacement())
            {
                HotReloadIntroducedTypeHolder.Initialize();
                HotReloadIntroducedTypeArtifact staleArtifact = CreateArtifactForTarget(
                    callerAssemblyName,
                    "0000000000000000000000000000000000000000");
                HotReloadIntroducedTypeHolder.Registry.RegisterPrepared(staleArtifact);
                HotReloadIntroducedTypeHolder.Registry.Activate(staleArtifact);
                using (HotReloadGroupProcessorDependencies.BeginReplacement(
                    HotReloadGroupProcessorDependencies.Create(
                        HotReloadGroupProcessor.TryAppendNewSourceMembershipFailure,
                        HotReloadIntroducedTypePreparation.PrepareAsync,
                        (input, ct) =>
                        {
                            transformInput = input;
                            return TransformWorkerClient.RunAsync(input, ct);
                        },
                        HotReloadGroupProcessor.GateAndCompileAsync,
                        HotReloadGroupEntryPreparation.PrepareGroup,
                        HotReloadEntryApplier.ApplyPreparedEntries)))
                {
                    await HotReloadOrchestrator.RunAsync(
                        new[] { callerPath },
                        HotReloadTestSourceWriter.WriteEditedSource(
                            "IntroducedTypeStaleGenerationCaller.cs",
                            File.ReadAllText(callerPath).Replace(
                                "return 7;",
                                "return 8;",
                                StringComparison.Ordinal)),
                        CancellationToken.None);
                }
            }

            Assert.That(transformInput, Is.Not.Null, "The transform run had to be reached.");
            Assert.That(
                transformInput.introducedTypeArtifacts,
                Is.Empty,
                "A run must not be offered an artifact bound to another generation of its assembly.");
        }

        /// <summary>
        /// Verifies that a run introducing a type commits it: the prepared membership becomes
        /// active, and the caller patched in the same reload reads a value through the introduced
        /// type at runtime, which only resolves once the artifact assembly is active.
        /// </summary>
        [Test]
        public async Task Run_TypeIntroducedWithItsCaller_ActivatesTheTypeAndTheCallerReadsThroughIt()
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");
            string callerPath = FixturePath("HotReloadCrossFileAddedMemberCaller.cs");
            HotReloadIntroducedTypeArtifact preparedArtifact = null;

            using (HotReloadIntroducedTypeHolder.BeginReplacement())
            {
                HotReloadIntroducedTypeHolder.Initialize();
                using (HotReloadGroupProcessorDependencies.BeginReplacement(
                    CreateArtifactCapturingDependencies(artifact => preparedArtifact = artifact)))
                {
                    HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                        new[] { hostPath, callerPath },
                        contentPathOverride: null,
                        CancellationToken.None,
                        CreateIntroducedTypeEdits(hostPath, callerPath, "Activation"));

                    AssertCallerIsPatched(result);
                }

                Assert.That(preparedArtifact, Is.Not.Null, "The run had to prepare the introduced type.");
                Assert.That(
                    HotReloadIntroducedTypeHolder.Registry.PreparedCount,
                    Is.EqualTo(0),
                    "The commit boundary must move the prepared membership, not leave it prepared.");
                Assert.That(
                    HotReloadIntroducedTypeHolder.Registry.TryFindActive(
                        preparedArtifact.Descriptors,
                        out HotReloadIntroducedTypeArtifact activeArtifact),
                    Is.True,
                    "The committed run must leave the introduced type active.");
                Assert.That(activeArtifact, Is.SameAs(preparedArtifact));
                Assert.That(
                    new HotReloadCrossFileAddedMemberCaller().Call(new HotReloadCrossFileAddedMemberHost()),
                    Is.EqualTo(IntroducedValueThroughCaller),
                    "The patched caller must read its value through the introduced type.");
            }
        }

        /// <summary>
        /// Verifies that a later reload which declares a type this domain already introduced
        /// reuses the active type instead of introducing it again: the run succeeds, reports the
        /// declaration as already active, no second artifact becomes active, and the caller it
        /// patches still reads through the one type.
        /// </summary>
        [Test]
        public async Task Run_SameTypeDeclaredAgainByALaterReload_ReusesTheActiveTypeWithoutIntroducingItAgain()
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");
            string callerPath = FixturePath("HotReloadCrossFileAddedMemberCaller.cs");
            HotReloadIntroducedTypeArtifact firstArtifact = null;
            List<int> preparedDescriptorCounts = new List<int>();

            using (HotReloadIntroducedTypeHolder.BeginReplacement())
            {
                HotReloadIntroducedTypeHolder.Initialize();
                using (HotReloadGroupProcessorDependencies.BeginReplacement(
                    CreatePreparationCountingDependencies(
                        preparedDescriptorCounts,
                        artifact =>
                        {
                            if (firstArtifact == null)
                            {
                                firstArtifact = artifact;
                            }
                        })))
                {
                    HotReloadOrchestratorResult first = await HotReloadOrchestrator.RunAsync(
                        new[] { hostPath, callerPath },
                        contentPathOverride: null,
                        CancellationToken.None,
                        CreateIntroducedTypeEdits(hostPath, callerPath, "FirstIntroduction"));

                    AssertCallerIsPatched(first);
                    Assert.That(firstArtifact, Is.Not.Null, "The first run had to prepare the introduced type.");

                    HotReloadOrchestratorResult second = await HotReloadOrchestrator.RunAsync(
                        new[] { hostPath, callerPath },
                        contentPathOverride: null,
                        CancellationToken.None,
                        CreateReintroducingEdits(hostPath, callerPath));

                    Assert.That(
                        FindFailureReason(second, string.Empty),
                        Is.Null,
                        "A reload that declares an already introduced type must not fail.");
                    AssertCallerIsPatched(second);

                    HotReloadResponse response = HotReloadApplyResponseBuilder.Build(second, null);
                    Assert.That(
                        response.IntroducedTypes.Count,
                        Is.EqualTo(1),
                        "The second run bound one declaration from the active artifact.");
                    Assert.That(
                        response.IntroducedTypes[0].Kind,
                        Is.EqualTo("AlreadyActive"),
                        "A declaration bound from an active artifact was not introduced by this run.");
                    Assert.That(
                        response.IntroducedTypes[0].TypeName,
                        Is.EqualTo(IntroducedTypeMetadataName));
                }

                Assert.That(
                    preparedDescriptorCounts,
                    Is.EqualTo(new[] { 1, 0 }),
                    "The second run must introduce no type, because the one it declares is active.");
                Assert.That(
                    HotReloadIntroducedTypeHolder.Registry.ActiveCount,
                    Is.EqualTo(1),
                    "A re-declared type must not add a second active artifact.");
                Assert.That(
                    HotReloadIntroducedTypeHolder.Registry.TryFindActive(
                        firstArtifact.Descriptors,
                        out HotReloadIntroducedTypeArtifact activeArtifact),
                    Is.True);
                Assert.That(activeArtifact, Is.SameAs(firstArtifact));
                Assert.That(
                    new HotReloadCrossFileAddedMemberCaller().Call(new HotReloadCrossFileAddedMemberHost()),
                    Is.EqualTo(IntroducedValueThroughReintroducedCaller),
                    "The caller the second run patched must read through the active type.");
            }
        }

        /// <summary>
        /// Verifies that a later reload which redefines a type this domain already introduced is
        /// refused instead of binding the caller against the stale retained definition: stage one
        /// cannot replace an artifact assembly a live type was loaded from.
        /// </summary>
        [Test]
        public async Task Run_ActiveIntroducedTypeRedefined_FailsAndLeavesTheActiveTypeInPlace()
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");
            string callerPath = FixturePath("HotReloadCrossFileAddedMemberCaller.cs");
            HotReloadIntroducedTypeArtifact firstArtifact = null;

            using (HotReloadIntroducedTypeHolder.BeginReplacement())
            {
                HotReloadIntroducedTypeHolder.Initialize();
                using (HotReloadGroupProcessorDependencies.BeginReplacement(
                    CreateArtifactCapturingDependencies(artifact =>
                    {
                        if (artifact != null)
                        {
                            firstArtifact = artifact;
                        }
                    })))
                {
                    HotReloadOrchestratorResult first = await HotReloadOrchestrator.RunAsync(
                        new[] { hostPath, callerPath },
                        contentPathOverride: null,
                        CancellationToken.None,
                        CreateIntroducedTypeEdits(hostPath, callerPath, "BeforeRedefinition"));

                    AssertCallerIsPatched(first);
                    Assert.That(firstArtifact, Is.Not.Null, "The first run had to prepare the introduced type.");

                    HotReloadOrchestratorResult second = await HotReloadOrchestrator.RunAsync(
                        new[] { hostPath, callerPath },
                        contentPathOverride: null,
                        CancellationToken.None,
                        CreateRedefiningEdits(hostPath, callerPath));

                    Assert.That(
                        CountTypeFailures(second, "requires a compile"),
                        Is.EqualTo(1),
                        "A redefined introduced type must fail the reload with a compile hint.\n"
                        + DescribeOutcomes(second));
                    Assert.That(
                        FindTypeFailureOwner(second, "requires a compile"),
                        Does.EndWith(Path.GetFileName(hostPath)),
                        "The refusal must name the file that redefines the type.");
                    Assert.That(
                        CountFailures(second, "requires a compile"),
                        Is.EqualTo(0),
                        "A type failure must not be reported a second time as a method row.");
                }

                Assert.That(
                    HotReloadIntroducedTypeHolder.Registry.ActiveCount,
                    Is.EqualTo(1),
                    "A refused redefinition must leave the already active type in place.");
                Assert.That(
                    HotReloadIntroducedTypeHolder.Registry.PreparedCount,
                    Is.EqualTo(0),
                    "A refused redefinition must leave no prepared membership behind.");
                Assert.That(
                    HotReloadIntroducedTypeHolder.Registry.TryFindActive(
                        firstArtifact.Descriptors,
                        out HotReloadIntroducedTypeArtifact activeArtifact),
                    Is.True);
                Assert.That(activeArtifact, Is.SameAs(firstArtifact));
            }
        }

        /// <summary>
        /// Verifies that a file whose entries the group preflight could not resolve stops the run
        /// before the commit point, so a group that will patch nothing activates no introduced
        /// type and leaves the failed file's own resolution failure as the reported outcome.
        /// </summary>
        [Test]
        public async Task Run_OneFileFailsTheGroupPreflight_ActivatesNothing()
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");
            string callerPath = FixturePath("HotReloadCrossFileAddedMemberCaller.cs");

            using (HotReloadIntroducedTypeHolder.BeginReplacement())
            {
                HotReloadIntroducedTypeHolder.Initialize();
                using (HotReloadGroupProcessorDependencies.BeginReplacement(
                    CreateDependenciesWithFailedResolutionFor(Path.GetFileName(callerPath))))
                {
                    HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                        new[] { hostPath, callerPath },
                        contentPathOverride: null,
                        CancellationToken.None,
                        CreateIntroducedTypeEdits(hostPath, callerPath, "PreflightFailure"));

                    AssertNothingWasCommitted(result, UnresolvableEntryReason);
                }
            }
        }

        /// <summary>
        /// Verifies that a reload whose only change is a new type declaration still reaches the
        /// commit boundary and activates the type, instead of ending unapplied because the run
        /// has no method to patch.
        /// </summary>
        [Test]
        public async Task Run_OnlyATypeIsIntroduced_ActivatesTheTypeWithoutAnyMethodToPatch()
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");
            HotReloadIntroducedTypeArtifact preparedArtifact = null;
            HotReloadOrchestratorResult result;

            using (HotReloadIntroducedTypeHolder.BeginReplacement())
            {
                HotReloadIntroducedTypeHolder.Initialize();
                using (HotReloadGroupProcessorDependencies.BeginReplacement(
                    CreateArtifactCapturingDependencies(artifact => preparedArtifact = artifact)))
                {
                    result = await HotReloadOrchestrator.RunAsync(
                        new[] { hostPath },
                        HotReloadTestSourceWriter.WriteEditedSource(
                            "IntroducedTypeOnlyHost.cs",
                            InsertIntroducedType(File.ReadAllText(hostPath))),
                        CancellationToken.None);
                }

                Assert.That(preparedArtifact, Is.Not.Null, "The run had to prepare the introduced type.");
                Assert.That(
                    FindFailureReason(result, string.Empty),
                    Is.Null,
                    "A reload that only introduces a type must not fail any method.");
                Assert.That(
                    HotReloadIntroducedTypeHolder.Registry.PreparedCount,
                    Is.EqualTo(0),
                    "The commit boundary must move the prepared membership, not leave it prepared.");
                Assert.That(
                    HotReloadIntroducedTypeHolder.Registry.TryFindActive(
                        preparedArtifact.Descriptors,
                        out HotReloadIntroducedTypeArtifact activeArtifact),
                    Is.True,
                    "A run with no method to patch must still activate the type it introduced.");
                Assert.That(activeArtifact, Is.SameAs(preparedArtifact));
            }
        }

        /// <summary>
        /// Verifies that a reload whose introduced type does not compile reports the compiler
        /// error itself, with the file and the source position it belongs to, instead of one row
        /// that names neither the declaration nor what was wrong with it.
        /// </summary>
        [Test]
        public async Task Build_IntroducedTypeCompileFails_ReportsTheCompilerErrorWithItsOwnerFile()
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");
            HotReloadOrchestratorResult result;

            using (HotReloadIntroducedTypeHolder.BeginReplacement())
            {
                HotReloadIntroducedTypeHolder.Initialize();
                result = await HotReloadOrchestrator.RunAsync(
                    new[] { hostPath },
                    HotReloadTestSourceWriter.WriteEditedSource(
                        "IntroducedTypeCompileFailureHost.cs",
                        InsertUncompilableIntroducedType(File.ReadAllText(hostPath))),
                    CancellationToken.None);
            }

            HotReloadResponse response = HotReloadApplyResponseBuilder.Build(result, null);
            Assert.That(response.Success, Is.False, "A refused declaration must fail the run.");
            string ownerProjectRelativePath = HotReloadPatchTargetSupport.ToProjectRelativeScriptPath(hostPath);
            List<HotReloadIntroducedTypeResult> failedRows = new List<HotReloadIntroducedTypeResult>();
            foreach (HotReloadIntroducedTypeResult row in response.IntroducedTypes)
            {
                if (string.Equals(row.Kind, "Failed", StringComparison.Ordinal)
                    && string.Equals(row.FilePath, ownerProjectRelativePath, StringComparison.Ordinal))
                {
                    failedRows.Add(row);
                }
            }

            Assert.That(
                failedRows,
                Has.Count.EqualTo(1),
                "The failure must be reported once, against the file that declares the type.");
            Assert.That(failedRows[0].Reason, Does.StartWith("Introduced-type compilation failed: "));
            Assert.That(failedRows[0].Reason, Does.Contain("CS0117"));
            Assert.That(failedRows[0].Reason, Does.Contain("("));
            Assert.That(failedRows[0].Reason, Does.Contain("): "));
        }

        /// <summary>
        /// Verifies that a reload whose introduced type derives from a type an earlier reload
        /// introduced compiles its artifact against the active artifact: the base type lives in
        /// neither the compiled assembly nor this run's sources, so only the record of the
        /// retained assembly can supply it.
        /// </summary>
        [Test]
        public async Task Run_IntroducedTypeDerivesFromAnActiveOne_CompilesAgainstTheActiveArtifact()
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");
            string callerPath = FixturePath("HotReloadCrossFileAddedMemberCaller.cs");

            using (HotReloadIntroducedTypeHolder.BeginReplacement())
            {
                HotReloadIntroducedTypeHolder.Initialize();
                HotReloadOrchestratorResult baseRun = await HotReloadOrchestrator.RunAsync(
                    new[] { hostPath },
                    WriteBaseTypeSource(hostPath),
                    CancellationToken.None);

                Assert.That(
                    FindFailureReason(baseRun, string.Empty),
                    Is.Null,
                    "Precondition: the base type had to be introduced. " + DescribeOutcomes(baseRun));
                Assert.That(
                    HotReloadIntroducedTypeHolder.Registry.ActiveCount,
                    Is.EqualTo(1),
                    "Precondition: the base type had to become active.");

                HotReloadOrchestratorResult derivedRun = await HotReloadOrchestrator.RunAsync(
                    new[] { callerPath },
                    WriteDerivedTypeSource(callerPath),
                    CancellationToken.None);

                Assert.That(
                    FindFailureReason(derivedRun, string.Empty),
                    Is.Null,
                    "A declaration deriving from an active introduced type must compile. "
                        + DescribeOutcomes(derivedRun));
                Assert.That(
                    HotReloadIntroducedTypeHolder.Registry.ActiveCount,
                    Is.EqualTo(2),
                    "The derived type must become active alongside the base it was compiled against.");
                Assert.That(
                    new HotReloadCrossFileAddedMemberCaller().Call(new HotReloadCrossFileAddedMemberHost()),
                    Is.EqualTo(IntroducedValueThroughDerivedType),
                    "The patched caller must read its value through the derived introduced type.");
            }
        }

        /// <summary>
        /// Verifies that two files of one group declaring the same introduced type fail the run
        /// with a reported reason naming the type, instead of letting the artifact batch's
        /// uniqueness contract throw out of the reload.
        /// </summary>
        [Test]
        public async Task Run_SameTypeDeclaredByTwoFilesOfAGroup_FailsWithoutThrowing()
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");
            string callerPath = FixturePath("HotReloadCrossFileAddedMemberCaller.cs");

            using (HotReloadIntroducedTypeHolder.BeginReplacement())
            {
                HotReloadIntroducedTypeHolder.Initialize();
                HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                    new[] { hostPath, callerPath },
                    contentPathOverride: null,
                    CancellationToken.None,
                    CreateDoubleDeclaringEdits(hostPath, callerPath));

                Assert.That(
                    CountTypeFailures(result, "more than one file"),
                    Is.EqualTo(2),
                    "Each file that declares the type must report the refusal. "
                        + DescribeOutcomes(result));
                Assert.That(
                    CountFailures(result, "more than one file"),
                    Is.EqualTo(0),
                    "A type failure must not be reported a second time as a method row.");
                Assert.That(
                    HotReloadIntroducedTypeHolder.Registry.ActiveCount,
                    Is.EqualTo(0),
                    "A refused group must activate no type.");
                Assert.That(
                    HotReloadPatcher.ActivePatchCount,
                    Is.EqualTo(0),
                    "A refused group must apply no patch.");
            }
        }

        /// <summary>
        /// Verifies that a type declared by three files of one group is refused against all three,
        /// instead of naming only the first pair and hiding the third file.
        /// </summary>
        [Test]
        public async Task Run_SameTypeDeclaredByThreeFilesOfAGroup_ReportsEveryOwner()
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");
            string callerPath = FixturePath("HotReloadCrossFileAddedMemberCaller.cs");
            string holderPath = FixturePath("HotReloadCrossFileAddedMemberHolder.cs");

            using (HotReloadIntroducedTypeHolder.BeginReplacement())
            {
                HotReloadIntroducedTypeHolder.Initialize();
                HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                    new[] { hostPath, callerPath, holderPath },
                    contentPathOverride: null,
                    CancellationToken.None,
                    CreateTripleDeclaringEdits(hostPath, callerPath, holderPath));

                Assert.That(
                    CountTypeFailures(result, "more than one file"),
                    Is.EqualTo(3),
                    "Every file that declares the type must report the refusal. "
                        + DescribeOutcomes(result));
                Assert.That(
                    HotReloadIntroducedTypeHolder.Registry.ActiveCount,
                    Is.EqualTo(0),
                    "A refused group must activate no type.");
            }
        }

        /// <summary>
        /// Verifies that a group double-declaring two types reports every refused declaration,
        /// instead of stopping at the first repeated one and leaving the second unreported.
        /// </summary>
        [Test]
        public async Task Run_TwoTypesEachDeclaredByTwoFilesOfAGroup_ReportsEveryRefusal()
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");
            string callerPath = FixturePath("HotReloadCrossFileAddedMemberCaller.cs");

            using (HotReloadIntroducedTypeHolder.BeginReplacement())
            {
                HotReloadIntroducedTypeHolder.Initialize();
                HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                    new[] { hostPath, callerPath },
                    contentPathOverride: null,
                    CancellationToken.None,
                    CreateTwiceDoubleDeclaringEdits(hostPath, callerPath));

                Assert.That(
                    CountTypeFailures(result, "more than one file"),
                    Is.EqualTo(4),
                    "Both files must report the refusal of both types. " + DescribeOutcomes(result));
                Assert.That(
                    CountTypeFailures(result, "HotReloadCrossFileDoubleDeclaredSecond"),
                    Is.EqualTo(2),
                    "The second double-declared type must be named as well. "
                        + DescribeOutcomes(result));
                Assert.That(
                    HotReloadIntroducedTypeHolder.Registry.ActiveCount,
                    Is.EqualTo(0),
                    "A refused group must activate no type.");
            }
        }

        /// <summary>
        /// Verifies that an Editor that becomes busy after the shim compile stops the run at the
        /// commit boundary, leaving no type active and no patch applied.
        /// </summary>
        [Test]
        public async Task Run_EditorBecomesBusyBeforeTheCommitBoundary_ActivatesNothing()
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");
            string callerPath = FixturePath("HotReloadCrossFileAddedMemberCaller.cs");
            HotReloadOrchestratorResult result;

            using (HotReloadIntroducedTypeHolder.BeginReplacement())
            {
                HotReloadIntroducedTypeHolder.Initialize();
                using (HotReloadGroupProcessorDependencies.BeginReplacement(
                    CreateDependenciesWithAfterGateAction(
                        () => HotReloadEditorStateSnapshotProvider.CaptureForTesting =
                            () => new HotReloadEditorStateSnapshot(true, false, false))))
                {
                    result = await HotReloadOrchestrator.RunAsync(
                        new[] { hostPath, callerPath },
                        contentPathOverride: null,
                        CancellationToken.None,
                        CreateIntroducedTypeEdits(hostPath, callerPath, "EditorBusy"));
                }

                AssertNothingWasCommitted(result, "The Editor became busy");
            }
        }

        /// <summary>
        /// Verifies that a run whose target assembly no longer has the module version id the run
        /// read is stopped at the commit boundary, so a reload never activates a type normalized
        /// against a generation the domain has replaced.
        /// </summary>
        [Test]
        public async Task Run_TargetAssemblyRebuiltBeforeTheCommitBoundary_ActivatesNothing()
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");
            string callerPath = FixturePath("HotReloadCrossFileAddedMemberCaller.cs");

            using (HotReloadIntroducedTypeHolder.BeginReplacement())
            {
                HotReloadIntroducedTypeHolder.Initialize();
                using (HotReloadGroupProcessorDependencies.BeginReplacement(
                    CreateDependenciesWithRebuiltTargetAfterWorker()))
                {
                    HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                        new[] { hostPath, callerPath },
                        contentPathOverride: null,
                        CancellationToken.None,
                        CreateIntroducedTypeEdits(hostPath, callerPath, "TargetRebuilt"));

                    AssertNothingWasCommitted(result, "was rebuilt");
                }
            }
        }

        /// <summary>
        /// Verifies that an owner rewritten between the preparation run and the transform run
        /// stops the run at the commit boundary, because the artifact assembly no longer describes
        /// the source the transform read.
        /// </summary>
        [Test]
        public async Task Run_OwnerRewrittenBetweenPreparationAndTransform_ActivatesNothing()
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");
            string callerPath = FixturePath("HotReloadCrossFileAddedMemberCaller.cs");
            Dictionary<string, string> edits = CreateIntroducedTypeEdits(hostPath, callerPath, "OwnerDrift");
            HotReloadOrchestratorResult result;

            using (HotReloadIntroducedTypeHolder.BeginReplacement())
            {
                HotReloadIntroducedTypeHolder.Initialize();
                using (HotReloadGroupProcessorDependencies.BeginReplacement(
                    CreateDependenciesWithBeforeWorkerAction(() => AppendMarkerComment(edits[hostPath]))))
                {
                    result = await HotReloadOrchestrator.RunAsync(
                        new[] { hostPath, callerPath },
                        contentPathOverride: null,
                        CancellationToken.None,
                        edits);
                }

                AssertNothingWasCommitted(result, "between preparation and transform");
            }
        }

        /// <summary>
        /// Verifies that an introduced type owner the transform run reported no row for stops the
        /// run at the commit boundary, because a staleness window that cannot be compared has not
        /// been shown to be closed.
        /// </summary>
        [Test]
        public async Task Run_OwnerHasNoTransformRow_ActivatesNothing()
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");
            string callerPath = FixturePath("HotReloadCrossFileAddedMemberCaller.cs");

            using (HotReloadIntroducedTypeHolder.BeginReplacement())
            {
                HotReloadIntroducedTypeHolder.Initialize();
                using (HotReloadGroupProcessorDependencies.BeginReplacement(
                    CreateDependenciesWithUnverifiableOwner()))
                {
                    HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                        new[] { hostPath, callerPath },
                        contentPathOverride: null,
                        CancellationToken.None,
                        CreateIntroducedTypeEdits(hostPath, callerPath, "OwnerWithoutRow"));

                    AssertNothingWasCommitted(result, "cannot be verified");
                }
            }
        }

        /// <summary>
        /// Verifies that a request source whose transform run reported no source hash stops the
        /// run at the commit boundary as well, so no file of a group is applied on the strength of
        /// a comparison that never happened.
        /// </summary>
        [Test]
        public async Task Run_RequestSourceHasNoTransformHash_ActivatesNothing()
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");
            string callerPath = FixturePath("HotReloadCrossFileAddedMemberCaller.cs");

            using (HotReloadIntroducedTypeHolder.BeginReplacement())
            {
                HotReloadIntroducedTypeHolder.Initialize();
                using (HotReloadGroupProcessorDependencies.BeginReplacement(
                    CreateDependenciesWithBlankedHashFor(Path.GetFileName(callerPath))))
                {
                    HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                        new[] { hostPath, callerPath },
                        contentPathOverride: null,
                        CancellationToken.None,
                        CreateIntroducedTypeEdits(hostPath, callerPath, "RequestWithoutHash"));

                    AssertNothingWasCommitted(result, "request source");
                }
            }
        }

        /// <summary>
        /// Verifies that a request source rewritten after the transform run stops the run at the
        /// commit boundary, so a reload never applies code compiled from bytes that are gone.
        /// </summary>
        [Test]
        public async Task Run_RequestSourceRewrittenAfterTransform_ActivatesNothing()
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");
            string callerPath = FixturePath("HotReloadCrossFileAddedMemberCaller.cs");
            Dictionary<string, string> edits = CreateIntroducedTypeEdits(hostPath, callerPath, "RequestDrift");
            HotReloadOrchestratorResult result;

            using (HotReloadIntroducedTypeHolder.BeginReplacement())
            {
                HotReloadIntroducedTypeHolder.Initialize();
                using (HotReloadGroupProcessorDependencies.BeginReplacement(
                    CreateDependenciesWithAfterGateAction(() => AppendMarkerComment(edits[callerPath]))))
                {
                    result = await HotReloadOrchestrator.RunAsync(
                        new[] { hostPath, callerPath },
                        contentPathOverride: null,
                        CancellationToken.None,
                        edits);
                }

                AssertNothingWasCommitted(result, "changed after it was transformed");
            }
        }

        // A comment keeps the file compilable while changing every byte-derived hash of it.
        private static void AppendMarkerComment(string editedSourcePath)
        {
            File.AppendAllText(editedSourcePath, "\n// rewritten before the commit boundary\n");
        }

        private static void AssertNothingWasCommitted(
            HotReloadOrchestratorResult result,
            string reasonFragment)
        {
            Assert.That(
                FindFailureReason(result, reasonFragment),
                Is.Not.Null,
                "The run must report why it stopped at the commit boundary.");
            Assert.That(
                HotReloadIntroducedTypeHolder.Registry.PreparedCount,
                Is.EqualTo(0),
                "A run stopped at the commit boundary must leave no prepared membership.");
            Assert.That(
                HotReloadIntroducedTypeHolder.Registry.ActiveCount,
                Is.EqualTo(0),
                "A run stopped at the commit boundary must activate no type.");
            Assert.That(
                HotReloadPatcher.ActivePatchCount,
                Is.EqualTo(0),
                "A run stopped at the commit boundary must apply no patch.");
        }

        private static string DescribeOutcomes(HotReloadOrchestratorResult result)
        {
            System.Text.StringBuilder description = new System.Text.StringBuilder();
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                description.Append(outcome.Kind).Append(' ').Append(outcome.Method)
                    .Append(" :: ").Append(outcome.Reason).Append('\n');
            }

            return description.ToString();
        }

        private static int CountFailures(HotReloadOrchestratorResult result, string reasonFragment)
        {
            int count = 0;
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Failed
                    && outcome.Reason != null
                    && outcome.Reason.Contains(reasonFragment, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountTypeFailures(HotReloadOrchestratorResult result, string reasonFragment)
        {
            int count = 0;
            foreach (HotReloadIntroducedTypeOutcome outcome in result.IntroducedTypes)
            {
                if (outcome.Kind == HotReloadIntroducedTypeOutcomeKind.Failed
                    && outcome.Reason.Contains(reasonFragment, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        private static string FindTypeFailureOwner(HotReloadOrchestratorResult result, string reasonFragment)
        {
            foreach (HotReloadIntroducedTypeOutcome outcome in result.IntroducedTypes)
            {
                if (outcome.Kind == HotReloadIntroducedTypeOutcomeKind.Failed
                    && outcome.Reason.Contains(reasonFragment, StringComparison.Ordinal))
                {
                    return outcome.OwnerProjectRelativePath;
                }
            }

            return null;
        }

        private static string FindFailureReason(HotReloadOrchestratorResult result, string reasonFragment)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Failed
                    && outcome.Reason != null
                    && outcome.Reason.Contains(reasonFragment, StringComparison.Ordinal))
                {
                    return outcome.Reason;
                }
            }

            return null;
        }

        // Records how many introduced-type descriptors each run of a scope prepared, which is how
        // a run that reuses an already active type is told from one that introduces it again.
        private static HotReloadGroupProcessorDependencies CreatePreparationCountingDependencies(
            List<int> preparedDescriptorCounts,
            Action<HotReloadIntroducedTypeArtifact> captureArtifact)
        {
            return HotReloadGroupProcessorDependencies.Create(
                HotReloadGroupProcessor.TryAppendNewSourceMembershipFailure,
                async (files, input, ct) =>
                {
                    HotReloadIntroducedTypePreparationResult preparation =
                        await HotReloadIntroducedTypePreparation.PrepareAsync(files, input, ct);
                    HotReloadIntroducedTypeArtifact artifact = preparation.Prepared?.Artifact;
                    preparedDescriptorCounts.Add(artifact == null ? 0 : artifact.Descriptors.Count);
                    if (artifact != null)
                    {
                        captureArtifact(artifact);
                    }

                    return preparation;
                },
                TransformWorkerClient.RunAsync,
                HotReloadGroupProcessor.GateAndCompileAsync,
                HotReloadGroupEntryPreparation.PrepareGroup,
                HotReloadEntryApplier.ApplyPreparedEntries);
        }

        private static HotReloadGroupProcessorDependencies CreateArtifactCapturingDependencies(
            Action<HotReloadIntroducedTypeArtifact> captureArtifact)
        {
            return HotReloadGroupProcessorDependencies.Create(
                HotReloadGroupProcessor.TryAppendNewSourceMembershipFailure,
                async (files, input, ct) =>
                {
                    HotReloadIntroducedTypePreparationResult preparation =
                        await HotReloadIntroducedTypePreparation.PrepareAsync(files, input, ct);
                    captureArtifact(preparation.Prepared?.Artifact);
                    return preparation;
                },
                TransformWorkerClient.RunAsync,
                HotReloadGroupProcessor.GateAndCompileAsync,
                HotReloadGroupEntryPreparation.PrepareGroup,
                HotReloadEntryApplier.ApplyPreparedEntries);
        }

        // The window between the preparation run and the transform run, which is where an owner
        // rewrite makes the prepared artifact stale.
        private static HotReloadGroupProcessorDependencies CreateDependenciesWithBeforeWorkerAction(
            Action beforeWorker)
        {
            return HotReloadGroupProcessorDependencies.Create(
                HotReloadGroupProcessor.TryAppendNewSourceMembershipFailure,
                HotReloadIntroducedTypePreparation.PrepareAsync,
                (input, ct) =>
                {
                    beforeWorker();
                    return TransformWorkerClient.RunAsync(input, ct);
                },
                HotReloadGroupProcessor.GateAndCompileAsync,
                HotReloadGroupEntryPreparation.PrepareGroup,
                HotReloadEntryApplier.ApplyPreparedEntries);
        }

        // The window between the shim compile and the commit boundary, which is the last instant
        // an external change can invalidate a run that has mutated nothing yet.
        private static HotReloadGroupProcessorDependencies CreateDependenciesWithAfterGateAction(
            Action afterGate)
        {
            return HotReloadGroupProcessorDependencies.Create(
                HotReloadGroupProcessor.TryAppendNewSourceMembershipFailure,
                HotReloadIntroducedTypePreparation.PrepareAsync,
                TransformWorkerClient.RunAsync,
                async (context, ct) =>
                {
                    HotReloadGroupGateAndCompileResult gateAndCompile =
                        await HotReloadGroupProcessor.GateAndCompileAsync(context, ct);
                    afterGate();
                    return gateAndCompile;
                },
                HotReloadGroupEntryPreparation.PrepareGroup,
                HotReloadEntryApplier.ApplyPreparedEntries);
        }

        // The reason of the preflight failure this test injects at the PrepareGroupEntries seam.
        private const string UnresolvableEntryReason = "An entry of this file could not be resolved.";

        // Why the failure is injected: an entry that cannot be resolved against a shim assembly
        // the same run has just compiled from the same source cannot be produced from a fixture,
        // and what is under test is that the commit point is not reached, not how resolution
        // decides a file has failed.
        private static HotReloadGroupProcessorDependencies CreateDependenciesWithFailedResolutionFor(
            string failingFileName)
        {
            return HotReloadGroupProcessorDependencies.Create(
                HotReloadGroupProcessor.TryAppendNewSourceMembershipFailure,
                HotReloadIntroducedTypePreparation.PrepareAsync,
                TransformWorkerClient.RunAsync,
                HotReloadGroupProcessor.GateAndCompileAsync,
                (context, compileResult, entriesToPatch) =>
                {
                    IReadOnlyList<HotReloadPreparedGroupFile> prepared =
                        HotReloadGroupEntryPreparation.PrepareGroup(context, compileResult, entriesToPatch);
                    List<HotReloadPreparedGroupFile> replaced =
                        new List<HotReloadPreparedGroupFile>(prepared.Count);
                    bool failedOne = false;
                    foreach (HotReloadPreparedGroupFile preparedFile in prepared)
                    {
                        HotReloadPreparedGroupFile candidate =
                            FailResolutionOfMatchingFile(preparedFile, failingFileName);
                        failedOne = failedOne || !ReferenceEquals(candidate, preparedFile);
                        replaced.Add(candidate);
                    }

                    Assert.That(
                        failedOne,
                        Is.True,
                        "Precondition: the group had to resolve " + failingFileName + " to fail it.");
                    return replaced;
                },
                HotReloadEntryApplier.ApplyPreparedEntries);
        }

        private static HotReloadPreparedGroupFile FailResolutionOfMatchingFile(
            HotReloadPreparedGroupFile prepared,
            string failingFileName)
        {
            if (prepared.Kind != HotReloadGroupFilePreparationKind.Resolved
                || !prepared.File.ProjectRelativePath.EndsWith(failingFileName, StringComparison.Ordinal))
            {
                return prepared;
            }

            List<HotReloadMethodOutcome> failureOutcomes = new List<HotReloadMethodOutcome>
            {
                HotReloadMethodOutcome.Failed(
                    prepared.File.ProjectRelativePath,
                    UnresolvableEntryReason,
                    prepared.File.AssemblyResolvePath)
            };
            return HotReloadPreparedGroupFile.ResolutionFailed(
                prepared.File,
                prepared.Entries,
                HotReloadEntryResolution.Result.Failed(failureOutcomes));
        }

        // The window between the transform run and the commit boundary, in which the target
        // assembly can be rebuilt.
        //
        // Why the run's recorded module version id is changed instead of the file on disk: the
        // target assembly is loaded into this domain, and overwriting a mapped assembly file
        // could take the Editor down. The check under test compares the two, so making them
        // differ from this side observes the same condition. The change is made after the worker
        // has run, so nothing the worker decided is affected by it.
        private static HotReloadGroupProcessorDependencies CreateDependenciesWithRebuiltTargetAfterWorker()
        {
            return HotReloadGroupProcessorDependencies.Create(
                HotReloadGroupProcessor.TryAppendNewSourceMembershipFailure,
                HotReloadIntroducedTypePreparation.PrepareAsync,
                async (input, ct) =>
                {
                    TransformWorkerClientResult workerResult =
                        await TransformWorkerClient.RunAsync(input, ct);
                    input.targetAssemblyMvid = Guid.NewGuid().ToString("N");
                    return workerResult;
                },
                HotReloadGroupProcessor.GateAndCompileAsync,
                HotReloadGroupEntryPreparation.PrepareGroup,
                HotReloadEntryApplier.ApplyPreparedEntries);
        }

        // An artifact whose owner hash is keyed by a path the transform run reported no row for,
        // which is the shape the boundary cannot compare its two windows across.
        private static HotReloadGroupProcessorDependencies CreateDependenciesWithUnverifiableOwner()
        {
            return HotReloadGroupProcessorDependencies.Create(
                HotReloadGroupProcessor.TryAppendNewSourceMembershipFailure,
                async (files, input, ct) =>
                {
                    HotReloadIntroducedTypePreparationResult preparation =
                        await HotReloadIntroducedTypePreparation.PrepareAsync(files, input, ct);
                    if (preparation.Prepared == null)
                    {
                        return preparation;
                    }

                    return HotReloadIntroducedTypePreparationResult.WithPrepared(
                        new HotReloadPreparedIntroducedTypes(
                            preparation.Prepared.Artifact,
                            RekeyOwnerHashesToUnreportedPaths(preparation.Prepared.OwnerSourceHashes)));
                },
                TransformWorkerClient.RunAsync,
                HotReloadGroupProcessor.GateAndCompileAsync,
                HotReloadGroupEntryPreparation.PrepareGroup,
                HotReloadEntryApplier.ApplyPreparedEntries);
        }

        private static Dictionary<string, string> RekeyOwnerHashesToUnreportedPaths(
            IReadOnlyDictionary<string, string> ownerSourceHashes)
        {
            Dictionary<string, string> rekeyed = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> ownerHash in ownerSourceHashes)
            {
                rekeyed[ownerHash.Key + ".unreported.cs"] = ownerHash.Value;
            }

            return rekeyed;
        }

        // A worker output row that carries no source hash, which is what the transform client's
        // own coalescing leaves behind when the worker omits the field.
        private static HotReloadGroupProcessorDependencies CreateDependenciesWithBlankedHashFor(
            string fileName)
        {
            return HotReloadGroupProcessorDependencies.Create(
                HotReloadGroupProcessor.TryAppendNewSourceMembershipFailure,
                HotReloadIntroducedTypePreparation.PrepareAsync,
                async (input, ct) =>
                {
                    TransformWorkerClientResult workerResult =
                        await TransformWorkerClient.RunAsync(input, ct);
                    BlankSourceHashOf(workerResult.Output, fileName);
                    return workerResult;
                },
                HotReloadGroupProcessor.GateAndCompileAsync,
                HotReloadGroupEntryPreparation.PrepareGroup,
                HotReloadEntryApplier.ApplyPreparedEntries);
        }

        private static void BlankSourceHashOf(TransformWorkerOutputDto output, string fileName)
        {
            if (output == null)
            {
                return;
            }

            foreach (TransformWorkerFileOutputDto file in output.files)
            {
                if (file.projectRelativePath.EndsWith(fileName, StringComparison.Ordinal))
                {
                    file.sourceContentSha256 = string.Empty;
                    return;
                }
            }

            Assert.Fail("The worker output must hold a row for " + fileName + ".");
        }

        // The edited caller returns the introduced type's value plus the compiled host's value.
        private const int IntroducedValueThroughCaller = 8;

        // The second reload keeps the same introduced type and edits only the caller, so its
        // value shifts by one while the type it reads through must stay the one already active.
        private const int IntroducedValueThroughReintroducedCaller = 9;

        // The derived introduced type doubles the base's value, and the edited caller adds
        // the compiled host's value to it.
        private const int IntroducedValueThroughDerivedType = 15;

        private const string ValidateStage = "validate-membership";

        private const string PrepareStage = "prepare-introduced-types";

        private const string WorkerStage = "run-worker";

        private const string GateStage = "gate-and-compile";

        private const string PreflightStage = "prepare-group-entries";

        private const string ApplyStage = "apply-prepared-entries";

        /// <summary>
        /// Verifies that a run introducing a type walks its stages in the one order the design
        /// requires: owner membership validation, type preparation, the transform run, the gate
        /// and shim compile, the group-wide preflight, and only then the apply that mutates.
        /// </summary>
        [Test]
        public async Task Run_TypeIntroducedWithItsCaller_WalksTheGroupStagesInOrder()
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");
            string callerPath = FixturePath("HotReloadCrossFileAddedMemberCaller.cs");
            List<string> stages = new List<string>();

            using (HotReloadIntroducedTypeHolder.BeginReplacement())
            {
                HotReloadIntroducedTypeHolder.Initialize();
                using (HotReloadGroupProcessorDependencies.BeginReplacement(
                    CreateRecordingDependencies(stages)))
                {
                    HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                        new[] { hostPath, callerPath },
                        contentPathOverride: null,
                        CancellationToken.None,
                        CreateIntroducedTypeEdits(hostPath, callerPath, "StageOrder"));

                    AssertCallerIsPatched(result);
                }

                // The commit boundary that activates a run is a later stage, so the prepared
                // membership the run registered must be gone again once the run ends.
                Assert.That(
                    HotReloadIntroducedTypeHolder.Registry.PreparedCount,
                    Is.EqualTo(0),
                    "A finished run must leave no prepared membership behind.");
            }

            Assert.That(
                stages,
                Is.EqualTo(new[]
                {
                    ValidateStage,
                    PrepareStage,
                    WorkerStage,
                    GateStage,
                    PreflightStage,
                    ApplyStage
                }),
                "The group pipeline must reach its commit boundary only through this order.");
        }

        /// <summary>
        /// Verifies that a group whose files no longer belong to the assembly they were resolved
        /// against is dropped before any type is compiled into an artifact.
        /// </summary>
        [Test]
        public async Task Run_OwnerMembershipValidationFails_DoesNotPrepareIntroducedTypes()
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");
            string callerPath = FixturePath("HotReloadCrossFileAddedMemberCaller.cs");
            List<string> stages = new List<string>();

            using (HotReloadIntroducedTypeHolder.BeginReplacement())
            {
                HotReloadIntroducedTypeHolder.Initialize();
                using (HotReloadGroupProcessorDependencies.BeginReplacement(
                    CreateFailingMembershipDependencies(stages)))
                {
                    await HotReloadOrchestrator.RunAsync(
                        new[] { hostPath, callerPath },
                        contentPathOverride: null,
                        CancellationToken.None,
                        CreateIntroducedTypeEdits(hostPath, callerPath, "MembershipFailure"));
                }

                Assert.That(
                    HotReloadIntroducedTypeHolder.Registry.PreparedCount,
                    Is.EqualTo(0),
                    "A group dropped at owner validation must leave no prepared membership.");
                Assert.That(
                    HotReloadIntroducedTypeHolder.Registry.ActiveCount,
                    Is.EqualTo(0),
                    "A group dropped at owner validation must activate no type.");
            }

            Assert.That(
                stages,
                Is.EqualTo(new[] { ValidateStage }),
                "Owner validation failure must stop the run before the type preparation.");
        }

        /// <summary>
        /// Verifies that a failed type preparation stops the run before the transform worker and
        /// leaves the patch ledger and the introduced-type ledgers exactly as it found them.
        /// </summary>
        [Test]
        public async Task Run_IntroducedTypePreparationFails_DoesNotRunTheWorkerAndLeavesLedgersUnchanged()
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");
            string callerPath = FixturePath("HotReloadCrossFileAddedMemberCaller.cs");
            List<string> stages = new List<string>();
            int patchesBefore = HotReloadPatcher.ActivePatchCount;

            using (HotReloadIntroducedTypeHolder.BeginReplacement())
            {
                HotReloadIntroducedTypeHolder.Initialize();
                using (HotReloadGroupProcessorDependencies.BeginReplacement(
                    CreateFailingPreparationDependencies(stages)))
                {
                    await HotReloadOrchestrator.RunAsync(
                        new[] { hostPath, callerPath },
                        contentPathOverride: null,
                        CancellationToken.None,
                        CreateIntroducedTypeEdits(hostPath, callerPath, "PreparationFailure"));
                }

                Assert.That(
                    HotReloadIntroducedTypeHolder.Registry.PreparedCount,
                    Is.EqualTo(0),
                    "A failed preparation must leave no prepared membership behind.");
                Assert.That(
                    HotReloadIntroducedTypeHolder.Registry.ActiveCount,
                    Is.EqualTo(0),
                    "A failed preparation must activate no type.");
            }

            Assert.That(
                stages,
                Is.EqualTo(new[] { ValidateStage, PrepareStage }),
                "A failed preparation must stop the run before the transform worker.");
            Assert.That(
                HotReloadPatcher.ActivePatchCount,
                Is.EqualTo(patchesBefore),
                "A failed preparation must leave the patch ledger unchanged.");
        }

        private static Dictionary<string, string> CreateIntroducedTypeEdits(
            string hostPath,
            string callerPath,
            string label)
        {
            return new Dictionary<string, string>
            {
                [hostPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "IntroducedType" + label + "Host.cs",
                    InsertIntroducedType(File.ReadAllText(hostPath))),
                [callerPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "IntroducedType" + label + "Caller.cs",
                    CallIntroducedType(File.ReadAllText(callerPath)))
            };
        }

        // Why every stage delegates to production: the order under test is the production order,
        // so a recording decorator must not stand in for any stage of it.
        private static HotReloadGroupProcessorDependencies CreateRecordingDependencies(List<string> stages)
        {
            return HotReloadGroupProcessorDependencies.Create(
                files =>
                {
                    stages.Add(ValidateStage);
                    return HotReloadGroupProcessor.TryAppendNewSourceMembershipFailure(files);
                },
                (files, input, ct) =>
                {
                    stages.Add(PrepareStage);
                    return HotReloadIntroducedTypePreparation.PrepareAsync(files, input, ct);
                },
                (input, ct) =>
                {
                    stages.Add(WorkerStage);
                    return TransformWorkerClient.RunAsync(input, ct);
                },
                (context, ct) =>
                {
                    stages.Add(GateStage);
                    return HotReloadGroupProcessor.GateAndCompileAsync(context, ct);
                },
                (context, compileResult, entriesToPatch) =>
                {
                    stages.Add(PreflightStage);
                    return HotReloadGroupEntryPreparation.PrepareGroup(context, compileResult, entriesToPatch);
                },
                (context, compileResult, preparedFiles) =>
                {
                    stages.Add(ApplyStage);
                    return HotReloadEntryApplier.ApplyPreparedEntries(context, compileResult, preparedFiles);
                });
        }

        // Why the failure is injected instead of provoked: a group whose sources left their
        // assembly cannot be produced from a compiled fixture, and the stage under test is
        // "nothing runs after the refusal", not how the refusal is decided.
        private static HotReloadGroupProcessorDependencies CreateFailingMembershipDependencies(List<string> stages)
        {
            return HotReloadGroupProcessorDependencies.Create(
                files =>
                {
                    stages.Add(ValidateStage);
                    HotReloadGroupOutcomeRouter.AppendGroupFailure(
                        files,
                        "(file)",
                        "The compiled assembly changed while the group was being processed.");
                    return false;
                },
                (files, input, ct) =>
                {
                    stages.Add(PrepareStage);
                    return HotReloadIntroducedTypePreparation.PrepareAsync(files, input, ct);
                },
                (input, ct) =>
                {
                    stages.Add(WorkerStage);
                    return TransformWorkerClient.RunAsync(input, ct);
                },
                HotReloadGroupProcessor.GateAndCompileAsync,
                HotReloadGroupEntryPreparation.PrepareGroup,
                HotReloadEntryApplier.ApplyPreparedEntries);
        }

        // Why the failure is injected: an artifact compile failure cannot be provoked from a
        // fixture the repository keeps compiling, and the stage under test is that the run stops
        // before the worker with every ledger untouched.
        private static HotReloadGroupProcessorDependencies CreateFailingPreparationDependencies(List<string> stages)
        {
            return HotReloadGroupProcessorDependencies.Create(
                files =>
                {
                    stages.Add(ValidateStage);
                    return HotReloadGroupProcessor.TryAppendNewSourceMembershipFailure(files);
                },
                (files, input, ct) =>
                {
                    stages.Add(PrepareStage);
                    return Task.FromResult(
                        HotReloadIntroducedTypePreparationResult.TypeFailures(
                            new[]
                            {
                                HotReloadIntroducedTypeOutcome.Failed(
                                    string.Empty,
                                    string.Empty,
                                    string.Empty,
                                    "The introduced type artifact could not be compiled.")
                            }));
                },
                (input, ct) =>
                {
                    stages.Add(WorkerStage);
                    return TransformWorkerClient.RunAsync(input, ct);
                },
                HotReloadGroupProcessor.GateAndCompileAsync,
                HotReloadGroupEntryPreparation.PrepareGroup,
                HotReloadEntryApplier.ApplyPreparedEntries);
        }

        private static HotReloadIntroducedTypeArtifact CreateArtifactForTarget(
            string originalAssemblyName,
            string originalAssemblyMvid)
        {
            Assembly assembly = typeof(HotReloadIntroducedTypeActivationTests).Assembly;
            List<HotReloadIntroducedTypeDescriptor> descriptors =
                new List<HotReloadIntroducedTypeDescriptor>
                {
                    new HotReloadIntroducedTypeDescriptor(
                        originalAssemblyName,
                        originalAssemblyMvid,
                        "Example.StaleGeneration",
                        "Assets/Example.cs",
                        "fingerprint",
                        "public class StaleGeneration { }")
                };
            return new HotReloadIntroducedTypeArtifact(
                assembly,
                assembly.Location,
                assembly.Location,
                descriptors);
        }

        private static string ResolveAssemblyName(string scriptPath)
        {
            string projectRelativePath = HotReloadPatchTargetSupport.ToProjectRelativeScriptPath(scriptPath);
            return Path.GetFileNameWithoutExtension(
                UnityEditor.Compilation.CompilationPipeline.GetAssemblyNameFromScriptPath(projectRelativePath));
        }

        private const string IntroducedTypeMetadataName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadCrossFileIntroducedValue";

        private const string HostTypeAnchor = "    public sealed class HotReloadCrossFileAddedMemberHost";

        private const string CallerBodyAnchor = "return host.Value();";

        private const string HolderTypeAnchor =
            "    internal sealed class HotReloadCrossFileAddedMemberHolder";

        private const string CallerTypeAnchor =
            "    internal sealed class HotReloadCrossFileAddedMemberCaller";

        private static void AssertArtifactRecordReachedTheTransformRun(
            TransformWorkerInputDto transformInput,
            HotReloadIntroducedTypeArtifact preparedArtifact)
        {
            Assert.That(transformInput, Is.Not.Null, "The transform run had to be reached.");
            Assert.That(
                transformInput.introducedTypeArtifacts,
                Is.Not.Null.And.Not.Empty,
                "The transform run must see the artifact this run prepared.");
            foreach (TransformWorkerIntroducedTypeArtifactDto artifact in transformInput.introducedTypeArtifacts)
            {
                if (!string.Equals(artifact.assemblyFullName, preparedArtifact.AssemblyFullName, StringComparison.Ordinal))
                {
                    continue;
                }

                Assert.That(
                    File.Exists(artifact.referencePath),
                    Is.True,
                    "The record must point at the artifact assembly the preparation compiled.");
                return;
            }

            Assert.Fail("No record named the prepared artifact " + preparedArtifact.AssemblyFullName + ".");
        }

        private static void AssertOwnerRowsCarryNoIntroducedMember(
            TransformWorkerOutputDto transformOutput,
            string ownerProjectRelativePath)
        {
            Assert.That(transformOutput, Is.Not.Null, "The transform run had to produce an output.");
            foreach (TransformWorkerEntryDto entry in transformOutput.entries)
            {
                Assert.That(
                    IsIntroducedRow(entry.sourceProjectRelativePath, ownerProjectRelativePath, entry.typeMetadataName),
                    Is.False,
                    "The owner file must not carry an entry for the introduced declaration.");
            }

            foreach (TransformWorkerUnchangedMethodDto unchanged in transformOutput.unchangedMethods)
            {
                Assert.That(
                    IsIntroducedRow(
                        unchanged.sourceProjectRelativePath,
                        ownerProjectRelativePath,
                        unchanged.typeMetadataName),
                    Is.False,
                    "The owner file must not carry an unchanged-method row for the introduced declaration.");
            }

            foreach (TransformWorkerSkippedDto skipped in transformOutput.skipped)
            {
                Assert.That(
                    IsIntroducedRow(skipped.sourceProjectRelativePath, ownerProjectRelativePath, skipped.method),
                    Is.False,
                    "The owner file must not carry a skip row for the introduced declaration.");
            }
        }

        private static bool IsIntroducedRow(
            string rowProjectRelativePath,
            string ownerProjectRelativePath,
            string rowIdentity)
        {
            return string.Equals(rowProjectRelativePath, ownerProjectRelativePath, StringComparison.Ordinal)
                && rowIdentity != null
                && rowIdentity.Contains("HotReloadCrossFileIntroducedValue", StringComparison.Ordinal);
        }

        private static void AssertCallerIsPatched(HotReloadOrchestratorResult result)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Patched
                    && outcome.Method != null
                    && outcome.Method.Contains("Call", StringComparison.Ordinal))
                {
                    return;
                }
            }

            Assert.Fail("The caller edited against the introduced type must be patched.");
        }

        private static HotReloadIntroducedTypeDescriptor FindDescriptor(
            HotReloadIntroducedTypeArtifact artifact,
            string metadataName)
        {
            foreach (HotReloadIntroducedTypeDescriptor descriptor in artifact.Descriptors)
            {
                if (string.Equals(descriptor.MetadataName, metadataName, StringComparison.Ordinal))
                {
                    return descriptor;
                }
            }

            Assert.Fail("The prepared artifact must describe " + metadataName + ".");
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

        // A new type whose body names a member the compiled host does not declare, so the artifact
        // compilation fails with a CS0117 that belongs to the host file.
        private static string InsertUncompilableIntroducedType(string hostSource)
        {
            Assert.That(hostSource, Does.Contain(HostTypeAnchor), "Precondition: host type anchor must exist.");
            string introduced =
                "    public sealed class HotReloadCrossFileIntroducedBroken\n"
                + "    {\n"
                + "        public int Value()\n"
                + "        {\n"
                + "            return HotReloadCrossFileAddedMemberHost.NoSuchMemberForThisTest();\n"
                + "        }\n"
                + "    }\n"
                + "\n";
            return hostSource.Replace(HostTypeAnchor, introduced + HostTypeAnchor, StringComparison.Ordinal);
        }

        // The introduced type redefined with a different body, which is what makes its
        // declaration fingerprint differ from the one the retained assembly was compiled from.
        private static Dictionary<string, string> CreateRedefiningEdits(string hostPath, string callerPath)
        {
            return new Dictionary<string, string>
            {
                [hostPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "IntroducedTypeRedefinitionHost.cs",
                    InsertIntroducedType(File.ReadAllText(hostPath)).Replace(
                        "            return 7;",
                        "            return 70;",
                        StringComparison.Ordinal)),
                [callerPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "IntroducedTypeRedefinitionCaller.cs",
                    CallIntroducedType(File.ReadAllText(callerPath)))
            };
        }

        // The same introduced declaration as the first reload, with a caller edited differently so
        // the reload is not short-circuited as unchanged.
        private static Dictionary<string, string> CreateReintroducingEdits(string hostPath, string callerPath)
        {
            return new Dictionary<string, string>
            {
                [hostPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "IntroducedTypeReintroductionHost.cs",
                    InsertIntroducedType(File.ReadAllText(hostPath))),
                [callerPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "IntroducedTypeReintroductionCaller.cs",
                    File.ReadAllText(callerPath).Replace(
                        CallerBodyAnchor,
                        "return new HotReloadCrossFileIntroducedValue().Read() + host.Value() + 1;",
                        StringComparison.Ordinal))
            };
        }

        // The base of the two-reload chain: unsealed, so the later reload's declaration can
        // derive from the assembly this reload retains.
        private static string WriteBaseTypeSource(string hostPath)
        {
            string hostSource = File.ReadAllText(hostPath);
            Assert.That(hostSource, Does.Contain(HostTypeAnchor), "Precondition: host type anchor must exist.");
            string introduced =
                "    public class HotReloadCrossFileIntroducedBase\n"
                + "    {\n"
                + "        public int Read()\n"
                + "        {\n"
                + "            return 7;\n"
                + "        }\n"
                + "    }\n"
                + "\n";
            return HotReloadTestSourceWriter.WriteEditedSource(
                "IntroducedTypeBaseHost.cs",
                hostSource.Replace(HostTypeAnchor, introduced + HostTypeAnchor, StringComparison.Ordinal));
        }

        // The later reload's declaration, which names a base only the active artifact assembly
        // holds, plus the caller body that reads a value through it.
        private static string WriteDerivedTypeSource(string callerPath)
        {
            string callerSource = File.ReadAllText(callerPath);
            Assert.That(
                callerSource,
                Does.Contain(CallerTypeAnchor),
                "Precondition: caller type anchor must exist.");
            Assert.That(
                callerSource,
                Does.Contain(CallerBodyAnchor),
                "Precondition: caller body anchor must exist.");
            string introduced =
                "    public sealed class HotReloadCrossFileIntroducedDerived"
                + " : HotReloadCrossFileIntroducedBase\n"
                + "    {\n"
                + "        public int Doubled()\n"
                + "        {\n"
                + "            return Read() * 2;\n"
                + "        }\n"
                + "    }\n"
                + "\n";
            return HotReloadTestSourceWriter.WriteEditedSource(
                "IntroducedTypeDerivedCaller.cs",
                callerSource
                    .Replace(CallerTypeAnchor, introduced + CallerTypeAnchor, StringComparison.Ordinal)
                    .Replace(
                        CallerBodyAnchor,
                        "return new HotReloadCrossFileIntroducedDerived().Doubled() + host.Value();",
                        StringComparison.Ordinal));
        }

        // Both files of the group declare the same type, which is what makes the group offer the
        // artifact batch two records of one identity.
        private static Dictionary<string, string> CreateTripleDeclaringEdits(
            string hostPath,
            string callerPath,
            string holderPath)
        {
            string introduced = BuildDoubleDeclaredSource("HotReloadCrossFileTripleDeclared");
            string hostSource = File.ReadAllText(hostPath);
            string callerSource = File.ReadAllText(callerPath);
            string holderSource = File.ReadAllText(holderPath);
            Assert.That(hostSource, Does.Contain(HostTypeAnchor), "Precondition: host type anchor must exist.");
            Assert.That(
                callerSource,
                Does.Contain(CallerTypeAnchor),
                "Precondition: caller type anchor must exist.");
            Assert.That(
                holderSource,
                Does.Contain(HolderTypeAnchor),
                "Precondition: holder type anchor must exist.");
            return new Dictionary<string, string>
            {
                [hostPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "IntroducedTypeTripleDeclaredHost.cs",
                    hostSource.Replace(HostTypeAnchor, introduced + HostTypeAnchor, StringComparison.Ordinal)),
                [callerPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "IntroducedTypeTripleDeclaredCaller.cs",
                    callerSource.Replace(
                        CallerTypeAnchor, introduced + CallerTypeAnchor, StringComparison.Ordinal)),
                [holderPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "IntroducedTypeTripleDeclaredHolder.cs",
                    holderSource.Replace(
                        HolderTypeAnchor, introduced + HolderTypeAnchor, StringComparison.Ordinal))
            };
        }

        // Why two duplicated types and not a third file: the group only has to hold more than one
        // repeated identity for a collector that stops at the first one to lose the rest.
        private static Dictionary<string, string> CreateTwiceDoubleDeclaringEdits(
            string hostPath,
            string callerPath)
        {
            string callerSource = File.ReadAllText(callerPath);
            Assert.That(
                callerSource,
                Does.Contain(CallerTypeAnchor),
                "Precondition: caller type anchor must exist.");
            string hostSource = File.ReadAllText(hostPath);
            Assert.That(hostSource, Does.Contain(HostTypeAnchor), "Precondition: host type anchor must exist.");
            string introduced = BuildDoubleDeclaredSource("HotReloadCrossFileDoubleDeclaredFirst")
                + BuildDoubleDeclaredSource("HotReloadCrossFileDoubleDeclaredSecond");
            return new Dictionary<string, string>
            {
                [hostPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "IntroducedTypeTwiceDoubleDeclaredHost.cs",
                    hostSource.Replace(HostTypeAnchor, introduced + HostTypeAnchor, StringComparison.Ordinal)),
                [callerPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "IntroducedTypeTwiceDoubleDeclaredCaller.cs",
                    callerSource.Replace(
                        CallerTypeAnchor, introduced + CallerTypeAnchor, StringComparison.Ordinal))
            };
        }

        private static string BuildDoubleDeclaredSource(string typeName)
        {
            return "    public sealed class " + typeName + "\n"
                + "    {\n"
                + "        public int Read()\n"
                + "        {\n"
                + "            return 7;\n"
                + "        }\n"
                + "    }\n"
                + "\n";
        }

        private static Dictionary<string, string> CreateDoubleDeclaringEdits(
            string hostPath,
            string callerPath)
        {
            string callerSource = File.ReadAllText(callerPath);
            Assert.That(
                callerSource,
                Does.Contain(CallerTypeAnchor),
                "Precondition: caller type anchor must exist.");
            string introduced =
                "    public sealed class HotReloadCrossFileDoubleDeclared\n"
                + "    {\n"
                + "        public int Read()\n"
                + "        {\n"
                + "            return 7;\n"
                + "        }\n"
                + "    }\n"
                + "\n";
            string hostSource = File.ReadAllText(hostPath);
            Assert.That(hostSource, Does.Contain(HostTypeAnchor), "Precondition: host type anchor must exist.");
            return new Dictionary<string, string>
            {
                [hostPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "IntroducedTypeDoubleDeclaredHost.cs",
                    hostSource.Replace(HostTypeAnchor, introduced + HostTypeAnchor, StringComparison.Ordinal)),
                [callerPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "IntroducedTypeDoubleDeclaredCaller.cs",
                    callerSource.Replace(
                        CallerTypeAnchor, introduced + CallerTypeAnchor, StringComparison.Ordinal))
            };
        }

        private static string CallIntroducedType(string callerSource)
        {
            Assert.That(callerSource, Does.Contain(CallerBodyAnchor), "Precondition: caller body anchor must exist.");
            return callerSource.Replace(
                CallerBodyAnchor,
                "return new HotReloadCrossFileIntroducedValue().Read() + host.Value();",
                StringComparison.Ordinal);
        }

        private static string FixturePath(string fileName)
        {
            string path = Path.GetFullPath(
                Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", fileName));
            Assert.That(File.Exists(path), Is.True, "Fixture missing: " + path);
            return path;
        }

        private static void RequestUnknownAssembly()
        {
            Assert.Throws<FileNotFoundException>(() => Assembly.Load(
                new AssemblyName("MissingIntroducedTypeDependency" + Guid.NewGuid().ToString("N")
                    + ", Version=1.0.0.0, Culture=neutral, PublicKeyToken=null")));
        }

        private static HotReloadIntroducedTypeArtifact CreateArtifact()
        {
            Assembly assembly = typeof(HotReloadIntroducedTypeActivationTests).Assembly;
            List<HotReloadIntroducedTypeDescriptor> descriptors =
                new List<HotReloadIntroducedTypeDescriptor>
                {
                    new HotReloadIntroducedTypeDescriptor(
                        "OriginalAssembly",
                        "original-mvid",
                        "Example.Introduced",
                        "Assets/Example.cs",
                        "fingerprint",
                        "public class Introduced { }")
                };
            return new HotReloadIntroducedTypeArtifact(assembly, "artifact.dll", "artifact.pdb", descriptors);
        }
    }
}
