using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEditor.Compilation;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Tests the group processor boundary between final coverage validation and application.
    /// </summary>
    public class HotReloadGroupProcessorTests
    {
        private const string AssemblyName = "UnityCLILoop.Tests.Editor.HotReload";
        private const string CallerKey = "Coverage.Host::Caller()";
        private const string TargetKey = "Coverage.Host::Target()";
        private const string MissingNewSourcePath = "Assets/Tests/Editor/HotReload/UncompiledNewScript.cs";
        private const string PersistedAddedMemberKey = "Coverage.Host::Persisted()";

        private Func<HotReloadEditorStateSnapshot> _previousSnapshotProvider;

        [SetUp]
        public void SetUp()
        {
            _previousSnapshotProvider = HotReloadEditorStateSnapshotProvider.CaptureForTesting;
        }

        [TearDown]
        public void TearDown()
        {
            HotReloadEditorStateSnapshotProvider.CaptureForTesting = _previousSnapshotProvider;
            HotReloadAddedMemberRegistry.Clear();
        }

        /// <summary>
        /// A two-file coverage loss fails both files before the application continuation runs.
        /// </summary>
        [Test]
        public async Task CompleteApplyAfterCoverageAsync_DroppedSourceLiveCaller_FailsEveryFileWithoutContinuation()
        {
            HotReloadApplyContext context = CreateContext();
            TransformWorkerEntryDto target = CreateTargetEntry("Assets/CoverageTarget.cs");
            HotReloadSignatureChangeGate.SignatureChangeGateResult gateResult = CreateGateResultWithoutExemptions();
            HotReloadGroupCompileResult compile = CreateCompile(target);
            int continuationCalls = 0;

            IReadOnlyList<HotReloadFileProcessResult> results =
                await HotReloadGroupProcessor.CompleteApplyAfterCoverageAsync(
                    context,
                    gateResult,
                    compile,
                    CancellationToken.None,
                    () =>
                    {
                        continuationCalls++;
                        return Task.FromResult<IReadOnlyList<HotReloadFileProcessResult>>(
                            Array.Empty<HotReloadFileProcessResult>());
                    });

            Assert.That(continuationCalls, Is.EqualTo(0));
            Assert.That(results, Has.Count.EqualTo(2));
            string[] expectedPaths = { "Assets/CoverageCaller.cs", "Assets/CoverageTarget.cs" };
            for (int index = 0; index < results.Count; index++)
            {
                HotReloadFileProcessResult result = results[index];
                Assert.That(result.PatchedCount, Is.EqualTo(0));
                Assert.That(result.Outcomes, Has.Count.EqualTo(1));
                Assert.That(result.Outcomes[0].Kind, Is.EqualTo(HotReloadMethodOutcomeKind.Failed));
                Assert.That(result.Outcomes[0].Method, Is.EqualTo("(signature-change-gate)"));
                Assert.That(result.Outcomes[0].Reason, Does.Contain(TargetKey));
                Assert.That(result.Outcomes[0].FilePath, Is.EqualTo(expectedPaths[index]));
            }
        }

        /// <summary>
        /// A final source-live caller permits the continuation and preserves its returned results.
        /// </summary>
        [Test]
        public async Task CompleteApplyAfterCoverageAsync_RetainedCaller_InvokesContinuationAndReturnsItsResults()
        {
            HotReloadApplyContext context = CreateContext();
            TransformWorkerEntryDto caller = CreateCallerEntry("Assets/CoverageCaller.cs");
            TransformWorkerEntryDto target = CreateTargetEntry("Assets/CoverageTarget.cs");
            HotReloadSignatureChangeGate.SignatureChangeGateResult gateResult = CreateGateResultWithoutExemptions();
            HotReloadGroupCompileResult compile = CreateCompile(caller, target);
            int continuationCalls = 0;
            IReadOnlyList<HotReloadFileProcessResult> expected =
                new[] { new HotReloadFileProcessResult(new List<HotReloadMethodOutcome>(), new List<string>(), 1) };

            IReadOnlyList<HotReloadFileProcessResult> results =
                await HotReloadGroupProcessor.CompleteApplyAfterCoverageAsync(
                    context,
                    gateResult,
                    compile,
                    CancellationToken.None,
                    () =>
                    {
                        continuationCalls++;
                        return Task.FromResult(expected);
                    });

            Assert.That(continuationCalls, Is.EqualTo(1));
            Assert.That(results, Is.SameAs(expected));
        }

        /// <summary>
        /// A deletion exemption carried by the gate covers a caller absent from final entries.
        /// </summary>
        [Test]
        public async Task CompleteApplyAfterCoverageAsync_DeletedCallerExemption_InvokesContinuationAndReturnsItsResults()
        {
            HotReloadApplyContext context = CreateContext();
            TransformWorkerEntryDto target = CreateTargetEntry("Assets/CoverageTarget.cs");
            HotReloadGroupCompileResult compile = CreateCompile(target);
            int continuationCalls = 0;
            IReadOnlyList<HotReloadFileProcessResult> expected =
                new[] { new HotReloadFileProcessResult(new List<HotReloadMethodOutcome>(), new List<string>(), 1) };

            IReadOnlyList<HotReloadFileProcessResult> results =
                await HotReloadGroupProcessor.CompleteApplyAfterCoverageAsync(
                    context,
                    CreateGateResultWithDeletedCallerExemption(),
                    compile,
                    CancellationToken.None,
                    () =>
                    {
                        continuationCalls++;
                        return Task.FromResult(expected);
                    });

            Assert.That(continuationCalls, Is.EqualTo(1));
            Assert.That(results, Is.SameAs(expected));
        }

        /// <summary>
        /// Membership evidence that changes after the worker prevents the production apply continuation from running.
        /// </summary>
        [Test]
        public async Task CompleteApplyAfterCoverageAsync_WhenNewSourceMembershipChanges_DoesNotInvokeContinuation()
        {
            HotReloadApplyContext context = CreateContext(CreateChangedMembershipEvidence());
            TransformWorkerEntryDto caller = CreateCallerEntry("Assets/CoverageCaller.cs");
            TransformWorkerEntryDto target = CreateTargetEntry("Assets/CoverageTarget.cs");
            HotReloadGroupCompileResult compile = CreateCompile(caller, target);
            int continuationCalls = 0;

            IReadOnlyList<HotReloadFileProcessResult> results =
                await HotReloadGroupProcessor.CompleteApplyAfterCoverageAsync(
                    context,
                    CreateGateResultWithoutExemptions(),
                    compile,
                    CancellationToken.None,
                    () =>
                    {
                        continuationCalls++;
                        return Task.FromResult<IReadOnlyList<HotReloadFileProcessResult>>(
                            Array.Empty<HotReloadFileProcessResult>());
                    });

            Assert.That(continuationCalls, Is.EqualTo(0));
            Assert.That(results, Has.Count.EqualTo(2));
            for (int index = 0; index < results.Count; index++)
            {
                Assert.That(results[index].Outcomes, Has.Count.EqualTo(1));
                Assert.That(results[index].Outcomes[0].Kind, Is.EqualTo(HotReloadMethodOutcomeKind.Failed));
                Assert.That(results[index].Outcomes[0].Reason, Does.Contain("compiled assembly changed"));
            }
        }

        /// <summary>
        /// A ready Editor state permits the final production continuation after revalidating actual membership evidence.
        /// </summary>
        [Test]
        public async Task CompleteApplyAfterCoverageAsync_WhenMembershipEvidenceStaysReady_InvokesContinuation()
        {
            HotReloadNewSourceMembershipEvidence evidence = CaptureCurrentMembershipEvidence();
            HotReloadApplyContext context = CreateContext(evidence);
            TransformWorkerEntryDto caller = CreateCallerEntry("Assets/CoverageCaller.cs");
            TransformWorkerEntryDto target = CreateTargetEntry("Assets/CoverageTarget.cs");
            int continuationCalls = 0;

            await HotReloadGroupProcessor.CompleteApplyAfterCoverageAsync(
                context,
                CreateGateResultWithoutExemptions(),
                CreateCompile(caller, target),
                CancellationToken.None,
                () =>
                {
                    continuationCalls++;
                    return Task.FromResult<IReadOnlyList<HotReloadFileProcessResult>>(
                        Array.Empty<HotReloadFileProcessResult>());
                });

            Assert.That(continuationCalls, Is.EqualTo(1));
        }

        /// <summary>
        /// An Editor state that becomes unsafe after evidence capture blocks the final production continuation.
        /// </summary>
        [Test]
        public async Task CompleteApplyAfterCoverageAsync_WhenEditorBecomesUnsafe_DoesNotInvokeContinuation()
        {
            HotReloadNewSourceMembershipEvidence evidence = CaptureCurrentMembershipEvidence();
            HotReloadEditorStateSnapshotProvider.CaptureForTesting = () =>
                new HotReloadEditorStateSnapshot(true, false, false);
            HotReloadApplyContext context = CreateContext(evidence);
            TransformWorkerEntryDto caller = CreateCallerEntry("Assets/CoverageCaller.cs");
            TransformWorkerEntryDto target = CreateTargetEntry("Assets/CoverageTarget.cs");
            int continuationCalls = 0;

            IReadOnlyList<HotReloadFileProcessResult> results =
                await HotReloadGroupProcessor.CompleteApplyAfterCoverageAsync(
                    context,
                    CreateGateResultWithoutExemptions(),
                    CreateCompile(caller, target),
                    CancellationToken.None,
                    () =>
                    {
                        continuationCalls++;
                        return Task.FromResult<IReadOnlyList<HotReloadFileProcessResult>>(
                            Array.Empty<HotReloadFileProcessResult>());
                    });

            Assert.That(continuationCalls, Is.EqualTo(0));
            Assert.That(results, Has.Count.EqualTo(2));
            Assert.That(results[0].Outcomes[0].Reason, Does.Contain("compiling"));
        }

        /// <summary>
        /// Cancellation before the final main-thread revalidation prevents the production continuation.
        /// </summary>
        [Test]
        public void CompleteApplyAfterCoverageAsync_WhenCancelled_DoesNotInvokeContinuation()
        {
            HotReloadApplyContext context = CreateContext();
            TransformWorkerEntryDto caller = CreateCallerEntry("Assets/CoverageCaller.cs");
            TransformWorkerEntryDto target = CreateTargetEntry("Assets/CoverageTarget.cs");
            int continuationCalls = 0;
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.ThrowsAsync<TaskCanceledException>(async () =>
                await HotReloadGroupProcessor.CompleteApplyAfterCoverageAsync(
                    context,
                    CreateGateResultWithoutExemptions(),
                    CreateCompile(caller, target),
                    cancellation.Token,
                    () =>
                    {
                        continuationCalls++;
                        return Task.FromResult<IReadOnlyList<HotReloadFileProcessResult>>(
                            Array.Empty<HotReloadFileProcessResult>());
                    }));

            Assert.That(continuationCalls, Is.EqualTo(0));
        }

        /// <summary>
        /// Cancellation that arrives during synchronous membership revalidation prevents the final continuation.
        /// </summary>
        [Test]
        public void CompleteApplyAfterCoverageAsync_WhenCancelledDuringRevalidation_DoesNotInvokeContinuation()
        {
            HotReloadNewSourceMembershipEvidence evidence = CaptureCurrentMembershipEvidence();
            HotReloadApplyContext context = CreateContext(evidence);
            TransformWorkerEntryDto caller = CreateCallerEntry("Assets/CoverageCaller.cs");
            TransformWorkerEntryDto target = CreateTargetEntry("Assets/CoverageTarget.cs");
            int continuationCalls = 0;
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            HotReloadEditorStateSnapshotProvider.CaptureForTesting = () =>
            {
                cancellation.Cancel();
                return new HotReloadEditorStateSnapshot(false, false, false);
            };

            Assert.ThrowsAsync<TaskCanceledException>(async () =>
                await HotReloadGroupProcessor.CompleteApplyAfterCoverageAsync(
                    context,
                    CreateGateResultWithoutExemptions(),
                    CreateCompile(caller, target),
                    cancellation.Token,
                    () =>
                    {
                        continuationCalls++;
                        return Task.FromResult<IReadOnlyList<HotReloadFileProcessResult>>(
                            Array.Empty<HotReloadFileProcessResult>());
                    }));

            Assert.That(continuationCalls, Is.EqualTo(0));
        }

        /// <summary>
        /// Unsafe membership evidence after a worker result prevents the production revert continuation.
        /// </summary>
        [Test]
        public async Task RevalidateBeforeRevertAsync_WhenEditorBecomesUnsafe_DoesNotInvokeRevert()
        {
            HotReloadNewSourceMembershipEvidence evidence = CaptureCurrentMembershipEvidence();
            HotReloadEditorStateSnapshotProvider.CaptureForTesting = () =>
                new HotReloadEditorStateSnapshot(false, true, false);
            HotReloadApplyContext context = CreateContext(evidence);
            int revertCalls = 0;

            bool didRevert = await HotReloadGroupProcessor.RevalidateBeforeRevertAsync(
                context.Files,
                CancellationToken.None,
                () => revertCalls++);

            Assert.That(didRevert, Is.False);
            Assert.That(revertCalls, Is.EqualTo(0));
            Assert.That(context.Files[0].Sinks.Outcomes[0].Reason, Does.Contain("importing assets"));
        }

        /// <summary>
        /// Cancellation during pre-revert membership revalidation prevents the revert continuation.
        /// </summary>
        [Test]
        public void RevalidateBeforeRevertAsync_WhenCancelledDuringRevalidation_DoesNotInvokeRevert()
        {
            HotReloadNewSourceMembershipEvidence evidence = CaptureCurrentMembershipEvidence();
            HotReloadApplyContext context = CreateContext(evidence);
            int revertCalls = 0;
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            HotReloadEditorStateSnapshotProvider.CaptureForTesting = () =>
            {
                cancellation.Cancel();
                return new HotReloadEditorStateSnapshot(false, false, false);
            };

            Assert.ThrowsAsync<TaskCanceledException>(async () =>
                await HotReloadGroupProcessor.RevalidateBeforeRevertAsync(
                    context.Files,
                    cancellation.Token,
                    () => revertCalls++));

            Assert.That(revertCalls, Is.EqualTo(0));
        }

        /// <summary>
        /// Planning a new source through the orchestrator retains evidence for the later pre-revert rejection.
        /// </summary>
        [Test]
        public async Task ResolveInputFile_WhenNewSourceIsPlanned_PreservesEvidenceForPreRevertRevalidation()
        {
            HotReloadRunAccumulator run = new HotReloadRunAccumulator();
            HotReloadFileProcessResult[] resultSlots = new HotReloadFileProcessResult[1];
            string[] resultPaths = new string[1];
            HotReloadGroupFile[] groupFiles = new HotReloadGroupFile[1];
            List<(int InputIndex, string AssemblyName, string ProjectRelativePath)> plannerInput =
                new List<(int InputIndex, string AssemblyName, string ProjectRelativePath)>();
            List<HotReloadMethodOutcome>[] deferredAlreadyActive = new List<HotReloadMethodOutcome>[1];

            HotReloadOrchestrator.ResolveInputFile(
                MissingNewSourcePath,
                0,
                null,
                null,
                "new-source-planning",
                run,
                resultSlots,
                resultPaths,
                groupFiles,
                plannerInput,
                deferredAlreadyActive);

            Assert.That(resultSlots[0], Is.Null);
            Assert.That(groupFiles[0], Is.Not.Null);
            Assert.That(groupFiles[0].NewSourceMembershipEvidence, Is.Not.Null);
            Assert.That(plannerInput, Has.Count.EqualTo(1));

            HotReloadEditorStateSnapshotProvider.CaptureForTesting = () =>
                new HotReloadEditorStateSnapshot(false, true, false);
            int revertCalls = 0;
            bool didRevert = await HotReloadGroupProcessor.RevalidateBeforeRevertAsync(
                new[] { groupFiles[0] },
                CancellationToken.None,
                () => revertCalls++);

            Assert.That(didRevert, Is.False);
            Assert.That(revertCalls, Is.EqualTo(0));
        }

        /// <summary>
        /// Empty entries preserve an active added-member generation when membership becomes unsafe.
        /// </summary>
        [Test]
        public async Task ResolveEntriesToPatchAsync_WhenEditorBecomesUnsafe_KeepsAddedMemberGeneration()
        {
            HotReloadNewSourceMembershipEvidence evidence = CaptureCurrentMembershipEvidence();
            HotReloadApplyContext context = CreateEmptyEntriesContext(evidence);
            HotReloadGroupFile file = context.Files[0];
            SeedActiveAddedMember(file.ProjectRelativePath);
            HotReloadEditorStateSnapshotProvider.CaptureForTesting = () =>
                new HotReloadEditorStateSnapshot(false, false, true);

            HotReloadGroupCompileResult result = await HotReloadShimFirstCompile.ResolveEntriesToPatchAsync(
                context,
                CreateEmptyGateResult(),
                CancellationToken.None);

            Assert.That(result.HasEntriesToApply, Is.False);
            Assert.That(HotReloadAddedMemberRegistry.HasGeneration(file.ProjectRelativePath), Is.True);
            Assert.That(HotReloadAddedMemberRegistry.IsActiveMember(file.ProjectRelativePath, PersistedAddedMemberKey), Is.True);
            Assert.That(file.ClearedAddedFieldNames, Is.Null);
        }

        /// <summary>
        /// Empty entries clear an active added-member generation when membership remains ready.
        /// </summary>
        [Test]
        public async Task ResolveEntriesToPatchAsync_WhenMembershipStaysReady_ClearsAddedMemberGeneration()
        {
            HotReloadNewSourceMembershipEvidence evidence = CaptureCurrentMembershipEvidence();
            HotReloadApplyContext context = CreateEmptyEntriesContext(evidence);
            HotReloadGroupFile file = context.Files[0];
            SeedActiveAddedMember(file.ProjectRelativePath);

            HotReloadGroupCompileResult result = await HotReloadShimFirstCompile.ResolveEntriesToPatchAsync(
                context,
                CreateEmptyGateResult(),
                CancellationToken.None);

            Assert.That(result.HasEntriesToApply, Is.False);
            Assert.That(HotReloadAddedMemberRegistry.HasGeneration(file.ProjectRelativePath), Is.True);
            Assert.That(HotReloadAddedMemberRegistry.IsActiveMember(file.ProjectRelativePath, PersistedAddedMemberKey), Is.False);
            Assert.That(file.ClearedAddedFieldNames, Is.Not.Null);
        }

        private static HotReloadSignatureChangeGate.SignatureChangeGateResult CreateGateResultWithoutExemptions()
        {
            return HotReloadSignatureChangeGate.SignatureChangeGateResult.WarningsOnly(
                new List<string>(),
                new List<HotReloadCallSiteScanner.CallSiteHit>
                {
                    new HotReloadCallSiteScanner.CallSiteHit
                    {
                        CallerAssemblyName = AssemblyName,
                        CallerMethodKey = CallerKey,
                        CallerTypeMetadataName = "Coverage.Host",
                        CallerMethodName = "Caller",
                        CallerParameterTypeFullNames = Array.Empty<string>(),
                        TargetMethodKey = TargetKey
                    }
                },
                new HashSet<HotReloadQualifiedMethodIdentity>());
        }

        private static HotReloadSignatureChangeGate.SignatureChangeGateResult CreateEmptyGateResult()
        {
            return HotReloadSignatureChangeGate.SignatureChangeGateResult.WarningsOnly(
                new List<string>(),
                new List<HotReloadCallSiteScanner.CallSiteHit>(),
                new HashSet<HotReloadQualifiedMethodIdentity>());
        }

        private static HotReloadSignatureChangeGate.SignatureChangeGateResult CreateGateResultWithDeletedCallerExemption()
        {
            HashSet<HotReloadQualifiedMethodIdentity> exemptions =
                new HashSet<HotReloadQualifiedMethodIdentity>
                {
                    new HotReloadQualifiedMethodIdentity(AssemblyName, CallerKey)
                };
            return HotReloadSignatureChangeGate.SignatureChangeGateResult.WarningsOnly(
                new List<string>(),
                new List<HotReloadCallSiteScanner.CallSiteHit>
                {
                    new HotReloadCallSiteScanner.CallSiteHit
                    {
                        CallerAssemblyName = AssemblyName,
                        CallerMethodKey = CallerKey,
                        CallerTypeMetadataName = "Coverage.Host",
                        CallerMethodName = "Caller",
                        CallerParameterTypeFullNames = Array.Empty<string>(),
                        TargetMethodKey = TargetKey
                    }
                },
                exemptions);
        }

        private static HotReloadGroupCompileResult CreateCompile(params TransformWorkerEntryDto[] entries)
        {
            return HotReloadGroupCompileResult.Apply(
                entries,
                HotReloadShimCompileResult.SuccessResult(
                    typeof(HotReloadGroupProcessorTests).Assembly,
                    new byte[] { 1 },
                    Array.Empty<byte>()));
        }

        private static HotReloadApplyContext CreateContext(
            HotReloadNewSourceMembershipEvidence newSourceMembershipEvidence = null)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            Assembly compilationAssembly = FindCompilationAssembly();
            HotReloadGroupFile callerFile = CreateFile(
                "Assets/CoverageCaller.cs", projectRoot, compilationAssembly, newSourceMembershipEvidence);
            HotReloadGroupFile targetFile = CreateFile(
                "Assets/CoverageTarget.cs", projectRoot, compilationAssembly);
            TransformWorkerEntryDto caller = CreateCallerEntry(callerFile.ProjectRelativePath);
            TransformWorkerEntryDto target = CreateTargetEntry(targetFile.ProjectRelativePath);
            TransformWorkerOutputDto workerOutput = new TransformWorkerOutputDto
            {
                entries = new[] { caller, target },
                skipped = Array.Empty<TransformWorkerSkippedDto>(),
                unchangedMethods = Array.Empty<TransformWorkerUnchangedMethodDto>(),
                files = new[] { callerFile.FileOutput, targetFile.FileOutput }
            };
            return new HotReloadApplyContext(
                projectRoot, AssemblyName, "coverage-test", compilationAssembly,
                callerFile.TargetDllPath, compilationAssembly.defines ?? Array.Empty<string>(),
                new TransformWorkerInputDto
                {
                    sources = new[]
                    {
                        new TransformWorkerSourceDto { projectRelativePath = callerFile.ProjectRelativePath },
                        new TransformWorkerSourceDto { projectRelativePath = targetFile.ProjectRelativePath }
                    }
                },
                workerOutput,
                new[] { callerFile, targetFile });
        }

        private static HotReloadApplyContext CreateEmptyEntriesContext(
            HotReloadNewSourceMembershipEvidence newSourceMembershipEvidence)
        {
            HotReloadApplyContext context = CreateContext(newSourceMembershipEvidence);
            context.Files[0].FileOutput.addedFieldNames = Array.Empty<string>();
            TransformWorkerOutputDto emptyWorkerOutput = new TransformWorkerOutputDto
            {
                shimSource = string.Empty,
                entries = Array.Empty<TransformWorkerEntryDto>(),
                skipped = Array.Empty<TransformWorkerSkippedDto>(),
                unchangedMethods = Array.Empty<TransformWorkerUnchangedMethodDto>(),
                files = context.WorkerOutput.files
            };
            return new HotReloadApplyContext(
                context.ProjectRoot,
                context.AssemblyName,
                context.CorrelationId,
                context.CompilationAssembly,
                context.TargetDllPath,
                context.Defines,
                context.WorkerInput,
                emptyWorkerOutput,
                context.Files);
        }

        private static Assembly FindCompilationAssembly()
        {
            foreach (Assembly assembly in CompilationPipeline.GetAssemblies())
            {
                if (assembly.name == AssemblyName)
                {
                    return assembly;
                }
            }

            Assert.Fail("Compilation assembly was not found.");
            return null;
        }

        private static HotReloadGroupFile CreateFile(
            string path,
            string projectRoot,
            Assembly compilationAssembly,
            HotReloadNewSourceMembershipEvidence newSourceMembershipEvidence = null)
        {
            HotReloadGroupFile file = new HotReloadGroupFile(
                path, path, path, AssemblyName, compilationAssembly,
                Path.Combine(projectRoot, "Library", "ScriptAssemblies", AssemblyName + ".dll"),
                projectRoot, new HotReloadFileSinks(new List<string>(), null), newSourceMembershipEvidence);
            file.FileOutput = new TransformWorkerFileOutputDto
            {
                projectRelativePath = path,
                removedMethodSignatures = Array.Empty<TransformWorkerRemovedMethodSignatureDto>()
            };
            file.SnapshotLabels = new HashSet<string>();
            file.SnapshotAddedLabels = new HashSet<string>();
            return file;
        }

        private static HotReloadNewSourceMembershipEvidence CreateChangedMembershipEvidence()
        {
            return new HotReloadNewSourceMembershipEvidence(
                "Assets/CoverageCaller.cs",
                AssemblyName,
                Path.Combine("Library", "ScriptAssemblies", AssemblyName + ".dll"),
                "different-mvid",
                null,
                Array.Empty<HotReloadNewSourceMembershipBoundary>());
        }

        private static HotReloadNewSourceMembershipEvidence CaptureCurrentMembershipEvidence()
        {
            HotReloadEditorStateSnapshotProvider.CaptureForTesting = () =>
                new HotReloadEditorStateSnapshot(false, false, false);
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            Assembly compilationAssembly = FindCompilationAssembly();
            string targetDllPath = Path.Combine(
                projectRoot,
                "Library",
                "ScriptAssemblies",
                AssemblyName + ".dll");
            string failure = HotReloadNewSourceMembershipValidator.TryCapture(
                projectRoot,
                MissingNewSourcePath,
                AssemblyName,
                compilationAssembly,
                targetDllPath,
                out HotReloadNewSourceMembershipEvidence evidence);

            Assert.That(failure, Is.Null);
            Assert.That(evidence, Is.Not.Null);
            return evidence;
        }

        private static void SeedActiveAddedMember(string projectRelativePath)
        {
            System.Reflection.MethodInfo shimMethod = typeof(HotReloadGroupProcessorTests).GetMethod(
                nameof(AddedMemberShim),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.That(shimMethod, Is.Not.Null);
            HotReloadAddedMemberRegistry.BeginFileGeneration(projectRelativePath);
            HotReloadAddedMemberRegistry.Register(
                projectRelativePath,
                PersistedAddedMemberKey,
                shimMethod,
                projectRelativePath);
        }

        private static void AddedMemberShim()
        {
        }

        private static TransformWorkerEntryDto CreateCallerEntry(string path)
        {
            return new TransformWorkerEntryDto
            {
                sourceProjectRelativePath = path,
                typeMetadataName = "Coverage.Host",
                methodName = "Caller",
                parameterTypeFullNames = Array.Empty<string>(),
                genericArity = 0,
                replacesCompiledMethod = true
            };
        }

        private static TransformWorkerEntryDto CreateTargetEntry(string path)
        {
            return new TransformWorkerEntryDto
            {
                sourceProjectRelativePath = path,
                typeMetadataName = "Coverage.Host",
                methodName = "Target",
                parameterTypeFullNames = Array.Empty<string>(),
                genericArity = 0,
                replacesCompiledMethod = true
            };
        }
    }
}
