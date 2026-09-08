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
        private const string BrokenSourcePath = "Assets/Tests/Editor/HotReload/BrokenNoticeSource.cs";
        private const string HealthySourcePath = "Assets/Tests/Editor/HotReload/HealthyNoticeSource.cs";
        private const string ParseErrorText =
            "BrokenNoticeSource.cs(3,1): error CS1022: Type or namespace definition, or end-of-file expected";

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
        /// A two-file coverage loss fails both files before the apply stage runs.
        /// </summary>
        [Test]
        public async Task CompleteApplyAfterCoverageAsync_DroppedSourceLiveCaller_FailsEveryFileWithoutApplying()
        {
            HotReloadApplyContext context = CreateContext();
            TransformWorkerEntryDto target = CreateTargetEntry("Assets/CoverageTarget.cs");
            HotReloadSignatureChangeGate.SignatureChangeGateResult gateResult = CreateGateResultWithoutExemptions();
            HotReloadGroupCompileResult compile = CreateCompile(target);
            ApplyRecorder applyRecorder = new ApplyRecorder();

            IReadOnlyList<HotReloadFileProcessResult> results =
                await CompleteApplyWithRecorder(applyRecorder,
                    context,
                    gateResult,
                    compile,
                    CancellationToken.None);

            Assert.That(applyRecorder.Calls, Is.EqualTo(0));
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
        /// A final source-live caller permits the apply stage and preserves its returned results.
        /// </summary>
        [Test]
        public async Task CompleteApplyAfterCoverageAsync_RetainedCaller_AppliesAndReturnsItsResults()
        {
            HotReloadApplyContext context = CreateContext();
            TransformWorkerEntryDto caller = CreateCallerEntry("Assets/CoverageCaller.cs");
            TransformWorkerEntryDto target = CreateTargetEntry("Assets/CoverageTarget.cs");
            HotReloadSignatureChangeGate.SignatureChangeGateResult gateResult = CreateGateResultWithoutExemptions();
            HotReloadGroupCompileResult compile = CreateCompile(caller, target);
            ApplyRecorder applyRecorder = new ApplyRecorder();
            IReadOnlyList<HotReloadFileProcessResult> expected =
                new[] { new HotReloadFileProcessResult(new List<HotReloadMethodOutcome>(), new List<string>(), 1) };
            applyRecorder.Results = expected;

            IReadOnlyList<HotReloadFileProcessResult> results =
                await CompleteApplyWithRecorder(applyRecorder,
                    context,
                    gateResult,
                    compile,
                    CancellationToken.None);

            Assert.That(applyRecorder.Calls, Is.EqualTo(1));
            Assert.That(results, Is.SameAs(expected));
        }

        /// <summary>
        /// A deletion exemption carried by the gate covers a caller absent from final entries.
        /// </summary>
        [Test]
        public async Task CompleteApplyAfterCoverageAsync_DeletedCallerExemption_AppliesAndReturnsItsResults()
        {
            HotReloadApplyContext context = CreateContext();
            TransformWorkerEntryDto target = CreateTargetEntry("Assets/CoverageTarget.cs");
            HotReloadGroupCompileResult compile = CreateCompile(target);
            ApplyRecorder applyRecorder = new ApplyRecorder();
            IReadOnlyList<HotReloadFileProcessResult> expected =
                new[] { new HotReloadFileProcessResult(new List<HotReloadMethodOutcome>(), new List<string>(), 1) };
            applyRecorder.Results = expected;

            IReadOnlyList<HotReloadFileProcessResult> results =
                await CompleteApplyWithRecorder(applyRecorder,
                    context,
                    CreateGateResultWithDeletedCallerExemption(),
                    compile,
                    CancellationToken.None);

            Assert.That(applyRecorder.Calls, Is.EqualTo(1));
            Assert.That(results, Is.SameAs(expected));
        }

        /// <summary>
        /// Membership evidence that changes after the worker prevents the production apply stage from running.
        /// </summary>
        [Test]
        public async Task CompleteApplyAfterCoverageAsync_WhenNewSourceMembershipChanges_DoesNotApply()
        {
            HotReloadApplyContext context = CreateContext(CreateChangedMembershipEvidence());
            TransformWorkerEntryDto caller = CreateCallerEntry("Assets/CoverageCaller.cs");
            TransformWorkerEntryDto target = CreateTargetEntry("Assets/CoverageTarget.cs");
            HotReloadGroupCompileResult compile = CreateCompile(caller, target);
            ApplyRecorder applyRecorder = new ApplyRecorder();

            IReadOnlyList<HotReloadFileProcessResult> results =
                await CompleteApplyWithRecorder(applyRecorder,
                    context,
                    CreateGateResultWithoutExemptions(),
                    compile,
                    CancellationToken.None);

            Assert.That(applyRecorder.Calls, Is.EqualTo(0));
            Assert.That(results, Has.Count.EqualTo(2));
            for (int index = 0; index < results.Count; index++)
            {
                Assert.That(results[index].Outcomes, Has.Count.EqualTo(1));
                Assert.That(results[index].Outcomes[0].Kind, Is.EqualTo(HotReloadMethodOutcomeKind.Failed));
                Assert.That(results[index].Outcomes[0].Reason, Does.Contain("compiled assembly changed"));
            }
        }

        /// <summary>
        /// A ready Editor state permits the production apply stage after revalidating actual membership evidence.
        /// </summary>
        [Test]
        public async Task CompleteApplyAfterCoverageAsync_WhenMembershipEvidenceStaysReady_Applies()
        {
            HotReloadNewSourceMembershipEvidence evidence = CaptureCurrentMembershipEvidence();
            HotReloadApplyContext context = CreateContext(evidence);
            TransformWorkerEntryDto caller = CreateCallerEntry("Assets/CoverageCaller.cs");
            TransformWorkerEntryDto target = CreateTargetEntry("Assets/CoverageTarget.cs");
            ApplyRecorder applyRecorder = new ApplyRecorder();

            await CompleteApplyWithRecorder(applyRecorder,
                context,
                CreateGateResultWithoutExemptions(),
                CreateCompile(caller, target),
                CancellationToken.None);

            Assert.That(applyRecorder.Calls, Is.EqualTo(1));
        }

        /// <summary>
        /// An Editor state that becomes unsafe after evidence capture blocks the production apply stage.
        /// </summary>
        [Test]
        public async Task CompleteApplyAfterCoverageAsync_WhenEditorBecomesUnsafe_DoesNotApply()
        {
            HotReloadNewSourceMembershipEvidence evidence = CaptureCurrentMembershipEvidence();
            HotReloadEditorStateSnapshotProvider.CaptureForTesting = () =>
                new HotReloadEditorStateSnapshot(true, false, false);
            HotReloadApplyContext context = CreateContext(evidence);
            TransformWorkerEntryDto caller = CreateCallerEntry("Assets/CoverageCaller.cs");
            TransformWorkerEntryDto target = CreateTargetEntry("Assets/CoverageTarget.cs");
            ApplyRecorder applyRecorder = new ApplyRecorder();

            IReadOnlyList<HotReloadFileProcessResult> results =
                await CompleteApplyWithRecorder(applyRecorder,
                    context,
                    CreateGateResultWithoutExemptions(),
                    CreateCompile(caller, target),
                    CancellationToken.None);

            Assert.That(applyRecorder.Calls, Is.EqualTo(0));
            Assert.That(results, Has.Count.EqualTo(2));
            Assert.That(results[0].Outcomes[0].Reason, Does.Contain("compiling"));
        }

        /// <summary>
        /// Cancellation before the final main-thread revalidation prevents the production apply stage.
        /// </summary>
        [Test]
        public void CompleteApplyAfterCoverageAsync_WhenCancelled_DoesNotApply()
        {
            HotReloadApplyContext context = CreateContext();
            TransformWorkerEntryDto caller = CreateCallerEntry("Assets/CoverageCaller.cs");
            TransformWorkerEntryDto target = CreateTargetEntry("Assets/CoverageTarget.cs");
            ApplyRecorder applyRecorder = new ApplyRecorder();
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.ThrowsAsync<TaskCanceledException>(async () =>
                await CompleteApplyWithRecorder(applyRecorder,
                    context,
                    CreateGateResultWithoutExemptions(),
                    CreateCompile(caller, target),
                    cancellation.Token));

            Assert.That(applyRecorder.Calls, Is.EqualTo(0));
        }

        /// <summary>
        /// Cancellation that arrives during synchronous membership revalidation prevents the apply stage.
        /// </summary>
        [Test]
        public void CompleteApplyAfterCoverageAsync_WhenCancelledDuringRevalidation_DoesNotApply()
        {
            HotReloadNewSourceMembershipEvidence evidence = CaptureCurrentMembershipEvidence();
            HotReloadApplyContext context = CreateContext(evidence);
            TransformWorkerEntryDto caller = CreateCallerEntry("Assets/CoverageCaller.cs");
            TransformWorkerEntryDto target = CreateTargetEntry("Assets/CoverageTarget.cs");
            ApplyRecorder applyRecorder = new ApplyRecorder();
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            HotReloadEditorStateSnapshotProvider.CaptureForTesting = () =>
            {
                cancellation.Cancel();
                return new HotReloadEditorStateSnapshot(false, false, false);
            };

            Assert.ThrowsAsync<TaskCanceledException>(async () =>
                await CompleteApplyWithRecorder(applyRecorder,
                    context,
                    CreateGateResultWithoutExemptions(),
                    CreateCompile(caller, target),
                    cancellation.Token));

            Assert.That(applyRecorder.Calls, Is.EqualTo(0));
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
            HotReloadRunAccumulator run = new HotReloadRunAccumulator(autoRefreshHeldAtStart: false);
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

            Assert.That(result.Outcome, Is.EqualTo(HotReloadGroupCompileOutcome.Failed));
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

            Assert.That(result.Outcome, Is.EqualTo(HotReloadGroupCompileOutcome.ReadyWithoutMethods));
            Assert.That(HotReloadAddedMemberRegistry.HasGeneration(file.ProjectRelativePath), Is.True);
            Assert.That(HotReloadAddedMemberRegistry.IsActiveMember(file.ProjectRelativePath, PersistedAddedMemberKey), Is.False);
            Assert.That(file.ClearedAddedFieldNames, Is.Not.Null);
        }

        /// <summary>
        /// A file whose worker output carries parse errors is marked SkipApply and gets a
        /// "(file)" Failed row, while a file without parse errors keeps SkipApply false and gets
        /// no Failed row.
        /// </summary>
        [Test]
        public void AppendPerFileWorkerNotices_WhenFileOutputCarriesParseErrors_SetsSkipApplyAndFailedRow()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            Assembly compilationAssembly = FindCompilationAssembly();
            HotReloadGroupFile brokenFile = CreateFile(BrokenSourcePath, projectRoot, compilationAssembly);
            HotReloadGroupFile healthyFile = CreateFile(HealthySourcePath, projectRoot, compilationAssembly);
            TransformWorkerOutputDto output = new TransformWorkerOutputDto
            {
                shimSource = string.Empty,
                entries = Array.Empty<TransformWorkerEntryDto>(),
                skipped = Array.Empty<TransformWorkerSkippedDto>(),
                unchangedMethods = Array.Empty<TransformWorkerUnchangedMethodDto>(),
                files = new[]
                {
                    CreateWorkerFileOutput(BrokenSourcePath, new[] { ParseErrorText }),
                    CreateWorkerFileOutput(HealthySourcePath, Array.Empty<string>())
                },
                parseErrors = Array.Empty<string>(),
                siblingConstDriftWarnings = Array.Empty<string>()
            };
            HotReloadWorkerRowsByFile rows = HotReloadWorkerRowsByFile.Build(
                output,
                new List<string> { BrokenSourcePath, HealthySourcePath });

            HotReloadGroupProcessor.AppendPerFileWorkerNotices(
                new List<HotReloadGroupFile> { brokenFile, healthyFile },
                rows);

            Assert.That(brokenFile.SkipApply, Is.True);
            Assert.That(CountFileFailedRows(brokenFile), Is.EqualTo(1));
            Assert.That(healthyFile.SkipApply, Is.False);
            Assert.That(CountFileFailedRows(healthyFile), Is.EqualTo(0));
        }

        /// <summary>
        /// A file skipped for parse errors keeps its added-member generation even when the group
        /// resolves no entries, so the previous reload's patches stay active.
        /// </summary>
        [Test]
        public async Task ResolveEntriesToPatchAsync_WhenFileIsSkippedByParseErrors_KeepsAddedMemberGeneration()
        {
            HotReloadNewSourceMembershipEvidence evidence = CaptureCurrentMembershipEvidence();
            HotReloadApplyContext context = CreateEmptyEntriesContext(evidence);
            HotReloadGroupFile file = context.Files[0];
            SeedActiveAddedMember(file.ProjectRelativePath);
            file.SkipApply = true;

            HotReloadGroupCompileResult result = await HotReloadShimFirstCompile.ResolveEntriesToPatchAsync(
                context,
                CreateEmptyGateResult(),
                CancellationToken.None);

            Assert.That(result.Outcome, Is.EqualTo(HotReloadGroupCompileOutcome.ReadyWithoutMethods));
            Assert.That(HotReloadAddedMemberRegistry.IsActiveMember(file.ProjectRelativePath, PersistedAddedMemberKey), Is.True);
            Assert.That(file.ClearedAddedFieldNames, Is.Null);
        }

        private static int CountFileFailedRows(HotReloadGroupFile file)
        {
            int count = 0;
            foreach (HotReloadMethodOutcome outcome in file.Sinks.Outcomes)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Failed && outcome.Method == "(file)")
                {
                    count++;
                }
            }

            return count;
        }

        private static TransformWorkerFileOutputDto CreateWorkerFileOutput(
            string projectRelativePath,
            string[] parseErrors)
        {
            return new TransformWorkerFileOutputDto
            {
                projectRelativePath = projectRelativePath,
                sourceContentSha256 = "aaaa",
                parseErrors = parseErrors,
                declarationDriftWarnings = Array.Empty<string>(),
                removedMembers = Array.Empty<TransformWorkerRemovedMemberDto>(),
                removedMethodSignatures = Array.Empty<TransformWorkerRemovedMethodSignatureDto>()
            };
        }

        /// <summary>
        /// Runs the production post-coverage stage with the apply stage of the dependency bundle
        /// replaced by a recorder, so a test observes whether the group reached its commit
        /// boundary without patching anything.
        /// </summary>
        private static async Task<IReadOnlyList<HotReloadFileProcessResult>> CompleteApplyWithRecorder(
            ApplyRecorder recorder,
            HotReloadApplyContext context,
            HotReloadSignatureChangeGate.SignatureChangeGateResult gateResult,
            HotReloadGroupCompileResult compile,
            CancellationToken ct)
        {
            using (recorder.Install())
            {
                return await HotReloadGroupProcessor.CompleteApplyAfterCoverageAsync(
                    context,
                    gateResult,
                    compile,
                    ct);
            }
        }

        /// <summary>
        /// Counts the calls of the bundle's apply stage and hands back the results a test expects.
        /// </summary>
        private sealed class ApplyRecorder
        {
            internal int Calls { get; private set; }

            internal IReadOnlyList<HotReloadFileProcessResult> Results { get; set; } =
                Array.Empty<HotReloadFileProcessResult>();

            internal IDisposable Install()
            {
                return HotReloadGroupProcessorDependencies.BeginReplacement(
                    HotReloadGroupProcessorDependencies.Create(
                        HotReloadGroupProcessor.TryAppendNewSourceMembershipFailure,
                        HotReloadIntroducedTypePreparation.PrepareAsync,
                        TransformWorkerClient.RunAsync,
                        HotReloadGroupProcessor.GateAndCompileAsync,
                        (context, compileResult, entriesToPatch) =>
                            Array.Empty<HotReloadPreparedGroupFile>(),
                        (context, compileResult, preparedFiles) =>
                        {
                            Calls++;
                            return Results;
                        }));
            }
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
            return HotReloadGroupCompileResult.ReadyWithMethods(
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
                new[] { callerFile, targetFile },
                null);
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
                context.Files,
                null);
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
            // Why a real worker source with its hash: the commit boundary verifies each request
            // source still hashes to what the transform run reported, and refuses a file it
            // cannot compare.
            string workerSourcePath = HotReloadTestSourceWriter.WriteEditedSource(
                Path.GetFileName(path),
                "// " + path + "\n");
            HotReloadGroupFile file = new HotReloadGroupFile(
                path, workerSourcePath, path, AssemblyName, compilationAssembly,
                Path.Combine(projectRoot, "Library", "ScriptAssemblies", AssemblyName + ".dll"),
                projectRoot, new HotReloadFileSinks(new List<string>(), null), newSourceMembershipEvidence);
            file.FileOutput = new TransformWorkerFileOutputDto
            {
                projectRelativePath = path,
                sourceContentSha256 = HotReloadAppliedSourceLedger.ComputeContentHash(
                    File.ReadAllBytes(workerSourcePath)),
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

        /// <summary>
        /// A failed gate reports the file as failed and carries no worker retry. Its scan did run,
        /// because the failure is only reachable after the compiled call sites were scanned.
        /// </summary>
        [Test]
        public void SignatureChangeGateResult_Failed_ReportsTheFileFailedAfterScanning()
        {
            HotReloadSignatureChangeGate.SignatureChangeGateResult result =
                HotReloadSignatureChangeGate.SignatureChangeGateResult.Failed(
                    "gate failure",
                    new List<string> { TargetKey });

            Assert.That(result.FileFailed, Is.True);
            Assert.That(result.UsedWorkerRetry, Is.False);
            Assert.That(result.DidScan, Is.True);
            Assert.That(result.FailureMessage, Is.EqualTo("gate failure"));
            Assert.That(result.GatedReplacementMethodKeys, Is.EqualTo(new[] { TargetKey }));
        }

        /// <summary>
        /// A failed gate never reaches the stage that reads DidScan: the group pipeline turns it
        /// into a Failed gate-and-compile result, which carries no gate for that stage to read.
        /// </summary>
        [Test]
        public void GroupGateAndCompileResult_Failed_CarriesNoGateForTheCoverageStageToRead()
        {
            HotReloadGroupGateAndCompileResult result = HotReloadGroupGateAndCompileResult.Failed();

            Assert.That(result.Outcome, Is.EqualTo(HotReloadGroupGateAndCompileOutcome.Failed));
            Assert.That(result.Gate, Is.Null);
            Assert.That(result.Compile, Is.Null);
        }

        /// <summary>
        /// A retried gate reports the worker retry it consumed and does not fail the file.
        /// </summary>
        [Test]
        public void SignatureChangeGateResult_Retried_ReportsTheWorkerRetryWithoutFailingTheFile()
        {
            HotReloadSignatureChangeGate.SignatureChangeGateResult result =
                HotReloadSignatureChangeGate.SignatureChangeGateResult.Retried(
                    null,
                    new List<HotReloadMethodOutcome>(),
                    new List<string>(),
                    new List<HotReloadCallSiteScanner.CallSiteHit>(),
                    new HashSet<HotReloadQualifiedMethodIdentity>(),
                    new List<string>());

            Assert.That(result.UsedWorkerRetry, Is.True);
            Assert.That(result.FileFailed, Is.False);
            Assert.That(result.DidScan, Is.True);
        }

        /// <summary>
        /// A gate with nothing to scan reports no scan, so the coverage stage that depends on one
        /// is skipped.
        /// </summary>
        [Test]
        public void SignatureChangeGateResult_NoWork_ReportsThatNoScanRan()
        {
            HotReloadSignatureChangeGate.SignatureChangeGateResult result =
                HotReloadSignatureChangeGate.SignatureChangeGateResult.NoWork();

            Assert.That(result.DidScan, Is.False);
            Assert.That(result.FileFailed, Is.False);
            Assert.That(result.UsedWorkerRetry, Is.False);
        }
    }
}
