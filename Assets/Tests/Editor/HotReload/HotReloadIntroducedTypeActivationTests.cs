using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers the editor-owned introduced type registry and resolver pair that activation runs through.
    /// </summary>
    public class HotReloadIntroducedTypeActivationTests
    {
        [SetUp]
        public void SetUp()
        {
            HotReloadPatcher.RevertAll();
            HotReloadAutoRefreshHold.Sync(HotReloadPatcher.ActiveChangeCount);
        }

        // Why reverting here as well: a run of this class patches a fixture caller against an
        // artifact assembly that only lives while the run's prepared scope is open, so leaving
        // the patch active would fail the first later test that calls the fixture.
        [TearDown]
        public void TearDown()
        {
            HotReloadPatcher.RevertAll();
            HotReloadAutoRefreshHold.Sync(HotReloadPatcher.ActiveChangeCount);
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
                            preparedArtifact = preparation.Artifact;
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
                        HotReloadIntroducedTypePreparationResult.Failure(
                            "The introduced type artifact could not be compiled."));
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
