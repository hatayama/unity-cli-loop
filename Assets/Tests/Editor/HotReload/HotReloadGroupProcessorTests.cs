using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEditor.Compilation;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

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
        private const string CoverageCallerPath = "Assets/CoverageCaller.cs";
        private const string CoverageCallerWorkerSourcePath = "/worker-copy/CoverageCaller.cs";
        private const string CoverageSiblingPath = "Assets/CoverageSibling.cs";
        private const string CoverageSiblingWorkerSourcePath = "/worker-copy/CoverageSibling.cs";
        private const string SkippedMethodLabel = "Coverage.Host.Skippable()";
        private const string FailedMethodLabel = "Coverage.Host.Unpatchable()";
        private const string FileLevelRowLabel = "(file)";
        private const string ParseErrorText =
            "BrokenNoticeSource.cs(3,1): error CS1022: Type or namespace definition, or end-of-file expected";


        private HotReloadDomainTestScope _scope;

        [SetUp]
        public void SetUp()
        {
            _scope = new HotReloadDomainTestScope();
        }

        [TearDown]
        public void TearDown()
        {
            _scope.Dispose();
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
            using IDisposable editorStateScope = HotReloadServicesTestScope.BeginWithEditorState(
                new HotReloadStubEditorStateSnapshotCapture(() => new HotReloadEditorStateSnapshot(true, false, false)));
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
            using IDisposable editorStateScope = HotReloadServicesTestScope.BeginWithEditorState(
                new HotReloadStubEditorStateSnapshotCapture(() => {
                cancellation.Cancel();
                return new HotReloadEditorStateSnapshot(false, false, false);
            }));

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
            using IDisposable editorStateScope = HotReloadServicesTestScope.BeginWithEditorState(
                new HotReloadStubEditorStateSnapshotCapture(() => new HotReloadEditorStateSnapshot(false, true, false)));
            HotReloadApplyContext context = CreateContext(evidence);
            int revertCalls = 0;

            bool didRevert = await HotReloadCompositionRoot.Services.GroupProcessor.RevalidateBeforeRevertAsync(
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
            using IDisposable editorStateScope = HotReloadServicesTestScope.BeginWithEditorState(
                new HotReloadStubEditorStateSnapshotCapture(() => {
                cancellation.Cancel();
                return new HotReloadEditorStateSnapshot(false, false, false);
            }));

            Assert.ThrowsAsync<TaskCanceledException>(async () =>
                await HotReloadCompositionRoot.Services.GroupProcessor.RevalidateBeforeRevertAsync(
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
            // The resolver normalizes a package path, which the run entry point captures for.
            // This test calls the resolver directly, so it captures here instead.
            HotReloadCompositionRoot.Services.PackageRootCapture.CaptureCurrent();
            HotReloadRunAccumulator run = new HotReloadRunAccumulator(
                HotReloadCompositionRoot.Services.Domain,
                HotReloadCompositionRoot.Services.Patcher,
                HotReloadCompositionRoot.Services.UnityMessageForwarding,
                autoRefreshHeldAtStart: false);
            HotReloadInputResolutionSlot slot = new HotReloadInputResolutionSlot();
            List<(int InputIndex, string AssemblyName, string ProjectRelativePath)> plannerInput =
                new List<(int InputIndex, string AssemblyName, string ProjectRelativePath)>();

            new HotReloadInputFileResolver(
                HotReloadCompositionRoot.Services.Domain,
                HotReloadCompositionRoot.Services.PackageRootCapture,
                HotReloadCompositionRoot.Services.EditorStateSnapshotCapture).ResolveInputFile(
                MissingNewSourcePath,
                0,
                null,
                null,
                "new-source-planning",
                run,
                slot,
                plannerInput);

            Assert.That(slot.Result, Is.Null);
            Assert.That(slot.GroupFile, Is.Not.Null);
            Assert.That(slot.GroupFile.NewSourceMembershipEvidence, Is.Not.Null);
            Assert.That(plannerInput, Has.Count.EqualTo(1));

            using IDisposable editorStateScope = HotReloadServicesTestScope.BeginWithEditorState(
                new HotReloadStubEditorStateSnapshotCapture(() => new HotReloadEditorStateSnapshot(false, true, false)));
            int revertCalls = 0;
            bool didRevert = await HotReloadCompositionRoot.Services.GroupProcessor.RevalidateBeforeRevertAsync(
                new[] { slot.GroupFile },
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
            using IDisposable editorStateScope = HotReloadServicesTestScope.BeginWithEditorState(
                new HotReloadStubEditorStateSnapshotCapture(() => new HotReloadEditorStateSnapshot(false, false, true)));

            HotReloadGroupCompileResult result = await HotReloadShimFirstCompile.ResolveEntriesToPatchAsync(
                HotReloadCompositionRoot.Services.GroupStageCollaborators,
                context,
                CreateEmptyGateResult(),
                CancellationToken.None);

            Assert.That(result.Outcome, Is.EqualTo(HotReloadGroupCompileOutcome.Failed));
            Assert.That(new HotReloadDomainTestAccess().HasAddedMemberGeneration(file.ProjectRelativePath), Is.True);
            Assert.That(HotReloadCompositionRoot.Services.Domain.IsActiveMember(file.ProjectRelativePath, PersistedAddedMemberKey), Is.True);
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
                HotReloadCompositionRoot.Services.GroupStageCollaborators,
                context,
                CreateEmptyGateResult(),
                CancellationToken.None);

            Assert.That(result.Outcome, Is.EqualTo(HotReloadGroupCompileOutcome.ReadyWithoutMethods));
            Assert.That(new HotReloadDomainTestAccess().HasAddedMemberGeneration(file.ProjectRelativePath), Is.True);
            Assert.That(HotReloadCompositionRoot.Services.Domain.IsActiveMember(file.ProjectRelativePath, PersistedAddedMemberKey), Is.False);
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

            HotReloadGroupNotices.AppendPerFileWorkerNotices(
                new List<HotReloadGroupFile> { brokenFile, healthyFile },
                rows);

            Assert.That(brokenFile.SkipApply, Is.True);
            Assert.That(CountFileFailedRows(brokenFile), Is.EqualTo(1));
            Assert.That(healthyFile.SkipApply, Is.False);
            Assert.That(CountFileFailedRows(healthyFile), Is.EqualTo(0));
        }

        /// <summary>
        /// What: a file already skipped for parse errors stays skipped when the isolation plan
        /// does not name it, and a file the plan names becomes skipped. The stage accumulates,
        /// it never clears an earlier decision.
        /// </summary>
        [Test]
        public void AccumulateAtomicSkipApply_WhenPlanDoesNotNameAnAlreadySkippedFile_KeepsItSkipped()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            Assembly compilationAssembly = FindCompilationAssembly();
            HotReloadGroupFile parseErrorFile = CreateFile(BrokenSourcePath, projectRoot, compilationAssembly);
            HotReloadGroupFile isolationFailedFile = CreateFile(HealthySourcePath, projectRoot, compilationAssembly);
            parseErrorFile.SkipApply = true;
            TransformWorkerEntryDto brokenEntry = CreateAtomicEntry(HealthySourcePath);
            HotReloadFileAtomicIsolationPlan plan = HotReloadFileAtomicIsolationPlan.Build(
                new[] { CreateAtomicEntry(BrokenSourcePath), brokenEntry },
                CreateAtomicAttribution(brokenEntry),
                new HotReloadCompileFailureNoteSources(Array.Empty<TransformWorkerSkippedDto>(), Array.Empty<HotReloadRefusedIntroducedType>()),
                CreateAtomicGroupFilePaths(BrokenSourcePath, HealthySourcePath),
                new[] { BrokenSourcePath, HealthySourcePath });

            HotReloadShimFirstCompile.AccumulateAtomicSkipApply(
                new[] { parseErrorFile, isolationFailedFile },
                plan);

            Assert.That(plan.IsFailedFile(BrokenSourcePath), Is.False);
            Assert.That(parseErrorFile.SkipApply, Is.True);
            Assert.That(isolationFailedFile.SkipApply, Is.True);
        }

        /// <summary>
        /// What: the isolation retry's per-file rows replace the first pass's added field and
        /// const names, so the apply commits what the retry re-classified.
        /// </summary>
        [Test]
        public void AdoptRetryAddedMemberNames_WhenRetryReturnsOtherNames_ReplacesTheFirstPassNames()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            Assembly compilationAssembly = FindCompilationAssembly();
            HotReloadGroupFile file = CreateFile(BrokenSourcePath, projectRoot, compilationAssembly);
            file.AddedFieldNames = new[] { "Sample.Host.firstPassField" };
            file.AddedConstNames = new[] { "Sample.Host.FirstPassConst" };
            file.AddedFieldDeclarations = new[]
            {
                CreateDeclarationDto("Sample.Host", "firstPassField", typeof(int), isStatic: false)
            };
            TransformWorkerFileOutputDto retryFile = new TransformWorkerFileOutputDto
            {
                projectRelativePath = BrokenSourcePath,
                addedFieldNames = new[] { "Sample.Host.retryField" },
                addedConstNames = new[] { "Sample.Host.RetryConst" },
                addedFieldDeclarations = new[]
                {
                    CreateDeclarationDto("Sample.Host", "retryField", typeof(string), isStatic: true)
                }
            };

            HotReloadShimFirstCompile.AdoptRetryAddedMemberNames(new[] { file }, new[] { retryFile });

            Assert.That(file.AddedFieldNames, Is.EqualTo(new[] { "Sample.Host.retryField" }));
            Assert.That(file.AddedConstNames, Is.EqualTo(new[] { "Sample.Host.RetryConst" }));
            Assert.That(file.AddedFieldDeclarations.Length, Is.EqualTo(1));
            Assert.That(file.AddedFieldDeclarations[0].fieldName, Is.EqualTo("retryField"));
            Assert.That(file.AddedFieldDeclarations[0].isStatic, Is.True);
        }

        /// <summary>
        /// What: a file the run left with no entry commits the worker row's added field names
        /// when no retry replaced them.
        /// </summary>
        [Test]
        public async Task ResolveEntriesToPatchAsync_WhenNoRetryReplacedTheNames_ClearsWithTheWorkerRowNames()
        {
            HotReloadNewSourceMembershipEvidence evidence = CaptureCurrentMembershipEvidence();
            HotReloadApplyContext context = CreateEmptyEntriesContext(evidence);
            HotReloadGroupFile file = context.Files[0];
            file.FileOutput.addedFieldNames = new[] { "Sample.Host.workerField" };
            file.AddedFieldNames = null;

            HotReloadGroupCompileResult result = await HotReloadShimFirstCompile.ResolveEntriesToPatchAsync(
                HotReloadCompositionRoot.Services.GroupStageCollaborators,
                context,
                CreateEmptyGateResult(),
                CancellationToken.None);

            Assert.That(result.Outcome, Is.EqualTo(HotReloadGroupCompileOutcome.ReadyWithoutMethods));
            Assert.That(file.ClearedAddedFieldNames, Is.EqualTo(new[] { "Sample.Host.workerField" }));
        }

        /// <summary>
        /// What: a retry's added field names win over the worker row when the run leaves the file
        /// with no entry, so a field the retry no longer emits is not resurrected.
        /// </summary>
        [Test]
        public async Task ResolveEntriesToPatchAsync_WhenARetryReplacedTheNames_ClearsWithTheRetryNames()
        {
            HotReloadNewSourceMembershipEvidence evidence = CaptureCurrentMembershipEvidence();
            HotReloadApplyContext context = CreateEmptyEntriesContext(evidence);
            HotReloadGroupFile file = context.Files[0];
            file.FileOutput.addedFieldNames = new[] { "Sample.Host.workerField" };
            file.AddedFieldNames = new[] { "Sample.Host.retryField" };

            HotReloadGroupCompileResult result = await HotReloadShimFirstCompile.ResolveEntriesToPatchAsync(
                HotReloadCompositionRoot.Services.GroupStageCollaborators,
                context,
                CreateEmptyGateResult(),
                CancellationToken.None);

            Assert.That(result.Outcome, Is.EqualTo(HotReloadGroupCompileOutcome.ReadyWithoutMethods));
            Assert.That(file.ClearedAddedFieldNames, Is.EqualTo(new[] { "Sample.Host.retryField" }));
        }

        /// <summary>
        /// What: the declarations a retry re-classified reach the ledger through the apply, with
        /// the store key the worker formed, and a nested declaring type is found by the reflection
        /// spelling the Editor looks it up with.
        /// </summary>
        [Test]
        public async Task ResolveEntriesToPatchAsync_WhenARetryReplacedTheNames_CommitsTheRetryDeclarations()
        {
            HotReloadNewSourceMembershipEvidence evidence = CaptureCurrentMembershipEvidence();
            HotReloadApplyContext context = CreateEmptyEntriesContext(evidence);
            HotReloadGroupFile file = context.Files[0];
            file.FileOutput.addedFieldNames = new[] { "Sample.Outer+Inner.workerField" };
            file.FileOutput.addedFieldDeclarations = new[]
            {
                CreateDeclarationDto("Sample.Outer/Inner", "workerField", typeof(int), isStatic: false)
            };
            file.AddedFieldNames = new[] { "Sample.Outer+Inner.retryField" };
            file.AddedFieldDeclarations = new[]
            {
                CreateDeclarationDto("Sample.Outer/Inner", "retryField", typeof(string), isStatic: true)
            };

            HotReloadGroupCompileResult result = await HotReloadShimFirstCompile.ResolveEntriesToPatchAsync(
                HotReloadCompositionRoot.Services.GroupStageCollaborators,
                context,
                CreateEmptyGateResult(),
                CancellationToken.None);

            Assert.That(result.Outcome, Is.EqualTo(HotReloadGroupCompileOutcome.ReadyWithoutMethods));
            Assert.That(
                HotReloadCompositionRoot.Services.Domain.TryGetAddedFieldDeclaration(
                    "Sample.Outer+Inner",
                    "retryField",
                    out HotReloadAddedFieldDeclaration committed),
                Is.True,
                "The apply must commit the retry's declarations.");
            Assert.That(committed.StoreFieldKey, Is.EqualTo("Sample.Outer/Inner::retryField"));
            Assert.That(
                committed.DeclaredTypeAssemblyQualifiedName,
                Is.EqualTo(typeof(string).AssemblyQualifiedName));
            Assert.That(committed.IsStatic, Is.True);
            Assert.That(
                HotReloadCompositionRoot.Services.Domain.TryGetAddedFieldDeclaration(
                    "Sample.Outer+Inner",
                    "workerField",
                    out HotReloadAddedFieldDeclaration _),
                Is.False,
                "A first-pass declaration the retry replaced must not survive.");
        }

        /// <summary>
        /// What: a group whose transform worker fails returns unapplied results built from files
        /// that never received a worker output row, so the result's SourceContentSha256 is null.
        /// This is the worker-failure path (HotReloadGroupProcessor returns before the stage that
        /// assigns FileOutput), which is why the unapplied result still has to guard that read.
        /// </summary>
        [Test]
        public async Task ProcessGroupAsync_WhenTheWorkerFails_BuildsUnappliedResultsWithoutAWorkerRow()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            Assembly compilationAssembly = FindCompilationAssembly();
            HotReloadGroupFile file = CreateFile(BrokenSourcePath, projectRoot, compilationAssembly);
            // The stage that assigns it is the one this run never reaches.
            file.FileOutput = null;

            IReadOnlyList<HotReloadFileProcessResult> results;
            using (HotReloadServicesTestScope.BeginWithDependencies(collaborators =>
                HotReloadGroupProcessorDependencies.Create(
                    files => true,
                    (files, input, ct) => Task.FromResult(
                        HotReloadIntroducedTypePreparationResult.NoIntroducedTypes()),
                    (input, ct) => Task.FromResult(
                        TransformWorkerClientResult.Failure("transform worker failed")),
                    (context, ct) => HotReloadGroupProcessor.GateAndCompileAsync(collaborators, context, ct),
                    (context, compileResult, entriesToPatch) => HotReloadGroupEntryPreparation.PrepareGroup(
                        collaborators, context, compileResult, entriesToPatch),
                    collaborators.EntryApplier.ApplyPreparedEntries)))
            {
                results = await HotReloadCompositionRoot.Services.GroupProcessor.ProcessGroupAsync(
                    new[] { file },
                    "worker-failure-test",
                    CancellationToken.None);
            }

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].SourceContentSha256, Is.Null);
            Assert.That(results[0].PatchedCount, Is.EqualTo(0));
            Assert.That(CountFileFailedRows(file), Is.EqualTo(1));
        }

        private static TransformWorkerEntryDto CreateAtomicEntry(string projectRelativePath)
        {
            return new TransformWorkerEntryDto
            {
                sourceProjectRelativePath = projectRelativePath,
                typeMetadataName = "Sample.Host",
                methodName = "Body",
                parameterTypeFullNames = Array.Empty<string>(),
                genericArity = 0,
                patchKind = HotReloadConstants.PatchKindDelegation
            };
        }

        private static HotReloadShimErrorAttribution.ShimCompileErrorAttribution CreateAtomicAttribution(
            TransformWorkerEntryDto failedEntry)
        {
            Dictionary<TransformWorkerEntryDto, List<string>> errorMessagesByEntry =
                new Dictionary<TransformWorkerEntryDto, List<string>>
                {
                    { failedEntry, new List<string> { "CS0103: name not found (line 12)" } }
                };
            return new HotReloadShimErrorAttribution.ShimCompileErrorAttribution(errorMessagesByEntry);
        }

        private static HotReloadGroupFilePaths CreateAtomicGroupFilePaths(params string[] projectRelativePaths)
        {
            List<(string ProjectRelativePath, string AssemblyResolvePath)> files =
                new List<(string ProjectRelativePath, string AssemblyResolvePath)>();
            foreach (string projectRelativePath in projectRelativePaths)
            {
                files.Add((projectRelativePath, projectRelativePath));
            }

            return new HotReloadGroupFilePaths(files);
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
                HotReloadCompositionRoot.Services.GroupStageCollaborators,
                context,
                CreateEmptyGateResult(),
                CancellationToken.None);

            Assert.That(result.Outcome, Is.EqualTo(HotReloadGroupCompileOutcome.ReadyWithoutMethods));
            Assert.That(HotReloadCompositionRoot.Services.Domain.IsActiveMember(file.ProjectRelativePath, PersistedAddedMemberKey), Is.True);
            Assert.That(file.ClearedAddedFieldNames, Is.Null);
        }

        /// <summary>
        /// What: the first run that reports removed members for a file prints the full warning.
        /// </summary>
        [Test]
        public void AppendRemovedMemberNotices_WhenTheFileHasNotReportedBefore_PrintsTheFullWarning()
        {
            string warning = AppendRemovedMembersRun("Alpha", "Beta");

            Assert.That(
                warning,
                Is.EqualTo(string.Format(
                    HotReloadConstants.RemovedMembersWarningFormat,
                    "Alpha, Beta")));
        }

        /// <summary>
        /// What: a later run whose removed members are exactly the previous run's set collapses to
        /// the continuation line instead of reprinting the full warning, which is what buried the
        /// warnings a run produced for the first time.
        /// </summary>
        [Test]
        public void AppendRemovedMemberNotices_WhenTheSameSetIsReportedAgain_CollapsesToTheContinuationLine()
        {
            AppendRemovedMembersRun("Alpha", "Beta");

            // The second run edits a different method, so the removed set is unchanged while the
            // run itself is not a repeat - a case the line-shift continuation cannot express.
            string secondWarning = AppendRemovedMembersRun("Beta", "Alpha");

            Assert.That(
                secondWarning,
                Is.EqualTo(string.Format(
                    CultureInfo.InvariantCulture,
                    HotReloadConstants.ContinuingRemovedMembersWarningFormat,
                    2,
                    "Alpha, Beta")));
        }

        /// <summary>
        /// What: a run whose removed set differs from the recorded one prints the full warning
        /// again, so a newly removed member is never hidden behind a continuation line.
        /// </summary>
        [Test]
        public void AppendRemovedMemberNotices_WhenTheSetChanges_PrintsTheFullWarningAgain()
        {
            AppendRemovedMembersRun("Alpha");

            string secondWarning = AppendRemovedMembersRun("Alpha", "Beta");

            Assert.That(
                secondWarning,
                Is.EqualTo(string.Format(
                    HotReloadConstants.RemovedMembersWarningFormat,
                    "Alpha, Beta")));
        }

        /// <summary>
        /// What: RevertAll clears the recorded set inside the same domain, so the next run is a
        /// first run again. Fails if the revert stops clearing the record.
        /// </summary>
        [Test]
        public void AppendRemovedMemberNotices_AfterRevertAll_PrintsTheFullWarningAgain()
        {
            AppendRemovedMembersRun("Alpha");

            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            string secondWarning = AppendRemovedMembersRun("Alpha");

            Assert.That(
                secondWarning,
                Is.EqualTo(string.Format(
                    HotReloadConstants.RemovedMembersWarningFormat,
                    "Alpha")));
        }

        /// <summary>
        /// What: a regenerated domain starts with no record, so the run after a domain reload
        /// prints the full warning. Fails if the record is held anywhere wider than one domain.
        /// </summary>
        [Test]
        public void AppendRemovedMemberNotices_AfterTheDomainIsRegenerated_PrintsTheFullWarningAgain()
        {
            AppendRemovedMembersRun("Alpha");

            using (new HotReloadDomainTestScope())
            {
                string secondWarning = AppendRemovedMembersRun("Alpha");

                Assert.That(
                    secondWarning,
                    Is.EqualTo(string.Format(
                        HotReloadConstants.RemovedMembersWarningFormat,
                        "Alpha")));
            }
        }

        /// <summary>
        /// What: a run that reports nothing for the file drops the record, so the run after it is
        /// a first run rather than a continuation of a set nothing reported in between.
        /// </summary>
        [Test]
        public void AppendRemovedMemberNotices_WhenARunReportsNothing_PrintsTheFullWarningAgain()
        {
            AppendRemovedMembersRun("Alpha");

            string emptyRunWarning = AppendRemovedMembersRun();
            string thirdWarning = AppendRemovedMembersRun("Alpha");

            Assert.That(emptyRunWarning, Is.Null);
            Assert.That(
                thirdWarning,
                Is.EqualTo(string.Format(
                    HotReloadConstants.RemovedMembersWarningFormat,
                    "Alpha")));
        }

        /// <summary>
        /// What: a run that lists the same file twice prints the full warning for both entries,
        /// because the second entry is not a later run. Fails if the first entry's record is what
        /// the second entry is compared against.
        /// </summary>
        [Test]
        public void AppendRemovedMemberNotices_WhenOneRunListsTheSameFileTwice_PrintsTheFullWarningForBoth()
        {
            IReadOnlyList<string> warnings = AppendDuplicateFileRemovedMembersRun(
                new[] { "Alpha", "Beta" },
                new[] { "Alpha", "Beta" });

            string fullWarning = string.Format(HotReloadConstants.RemovedMembersWarningFormat, "Alpha, Beta");
            Assert.That(warnings, Is.EqualTo(new[] { fullWarning, fullWarning }));
        }

        /// <summary>
        /// What: the set a run that listed the same file twice reported is still recorded when the
        /// run ends, so the next run with the same set collapses to the continuation line. Fails if
        /// the record is no longer written once it is deferred to the end of the run.
        /// </summary>
        [Test]
        public void AppendRemovedMemberNotices_AfterARunListedTheSameFileTwice_CollapsesTheNextRunToTheContinuationLine()
        {
            AppendDuplicateFileRemovedMembersRun(new[] { "Alpha", "Beta" }, new[] { "Alpha", "Beta" });

            string nextWarning = AppendRemovedMembersRun("Alpha", "Beta");

            Assert.That(
                nextWarning,
                Is.EqualTo(string.Format(
                    CultureInfo.InvariantCulture,
                    HotReloadConstants.ContinuingRemovedMembersWarningFormat,
                    2,
                    "Alpha, Beta")));
        }

        /// <summary>
        /// What: when one run lists the same file twice with different removed sets, the set of
        /// the later entry is what the run records, so the next run continues from it. Fails if
        /// the first entry's set wins.
        /// </summary>
        [Test]
        public void AppendRemovedMemberNotices_WhenOneRunListsTheSameFileTwiceWithDifferentSets_RecordsTheLaterSet()
        {
            AppendDuplicateFileRemovedMembersRun(new[] { "Alpha" }, new[] { "Alpha", "Beta" });

            string nextWarning = AppendRemovedMembersRun("Alpha", "Beta");

            Assert.That(
                nextWarning,
                Is.EqualTo(string.Format(
                    CultureInfo.InvariantCulture,
                    HotReloadConstants.ContinuingRemovedMembersWarningFormat,
                    2,
                    "Alpha, Beta")));
        }

        /// <summary>
        /// What: a run that staged nothing for the file leaves the record alone, so the run after
        /// it still continues from the earlier report. Fails if ending such a run clears or
        /// overwrites the record.
        /// </summary>
        [Test]
        public void ApplyTo_RunThatStagedNothingForTheFile_KeepsTheRecord()
        {
            AppendRemovedMembersRun("Alpha");

            new HotReloadRunDisplayedRemovedMembers().ApplyTo(HotReloadCompositionRoot.Services.Patcher);
            string thirdWarning = AppendRemovedMembersRun("Alpha");

            Assert.That(
                thirdWarning,
                Is.EqualTo(string.Format(
                    CultureInfo.InvariantCulture,
                    HotReloadConstants.ContinuingRemovedMembersWarningFormat,
                    1,
                    "Alpha")));
        }

        // Runs the removed-member notices for one file of a fresh run context against the domain
        // installed right now, ends the run the way the orchestrator does, and returns the
        // removed-members warning that run produced.
        private static string AppendRemovedMembersRun(params string[] removedMemberNames)
        {
            HotReloadRunDisplayedRemovedMembers run = new HotReloadRunDisplayedRemovedMembers();
            HotReloadApplyContext context = CreateContext(displayedRemovedMembers: run);
            HotReloadGroupFile file = context.Files[0];
            file.FileOutput.removedMembers = CreateRemovedFields(removedMemberNames);

            HotReloadGroupNotices.AppendRemovedMemberNotices(
                HotReloadCompositionRoot.Services.Patcher,
                context,
                CreateEmptyGateResult());
            run.ApplyTo(HotReloadCompositionRoot.Services.Patcher);

            return FindRemovedMembersWarning(file);
        }

        // Runs one input that lists a file twice the way the orchestrator does: each copy in its
        // own group of the same run, and the record written once the run ends. Returns the
        // removed-members warning of each copy in input order.
        private static IReadOnlyList<string> AppendDuplicateFileRemovedMembersRun(
            string[] firstRemovedMemberNames,
            string[] secondRemovedMemberNames)
        {
            HotReloadRunDisplayedRemovedMembers run = new HotReloadRunDisplayedRemovedMembers();
            HotReloadApplyContext firstContext = CreateContext(displayedRemovedMembers: run);
            HotReloadApplyContext secondContext = CreateContext(displayedRemovedMembers: run);
            HotReloadGroupFile first = firstContext.Files[0];
            HotReloadGroupFile second = secondContext.Files[0];
            first.FileOutput.removedMembers = CreateRemovedFields(firstRemovedMemberNames);
            second.FileOutput.removedMembers = CreateRemovedFields(secondRemovedMemberNames);

            HotReloadGroupNotices.AppendRemovedMemberNotices(
                HotReloadCompositionRoot.Services.Patcher,
                firstContext,
                CreateEmptyGateResult());
            HotReloadGroupNotices.AppendRemovedMemberNotices(
                HotReloadCompositionRoot.Services.Patcher,
                secondContext,
                CreateEmptyGateResult());
            run.ApplyTo(HotReloadCompositionRoot.Services.Patcher);

            return new[] { FindRemovedMembersWarning(first), FindRemovedMembersWarning(second) };
        }

        private static TransformWorkerRemovedMemberDto[] CreateRemovedFields(string[] removedMemberNames)
        {
            List<TransformWorkerRemovedMemberDto> removedMembers = new List<TransformWorkerRemovedMemberDto>();
            foreach (string removedMemberName in removedMemberNames)
            {
                removedMembers.Add(new TransformWorkerRemovedMemberDto
                {
                    kind = HotReloadConstants.RemovedMemberKindField,
                    name = removedMemberName
                });
            }

            return removedMembers.ToArray();
        }

        private static string FindRemovedMembersWarning(HotReloadGroupFile file)
        {
            foreach (string warning in file.Sinks.Warnings)
            {
                if (warning.Contains("Alpha", StringComparison.Ordinal)
                    || warning.Contains("Beta", StringComparison.Ordinal))
                {
                    return warning;
                }
            }

            return null;
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
                return await HotReloadCompositionRoot.Services.GroupProcessor.CompleteApplyAfterCoverageAsync(
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
                return HotReloadServicesTestScope.BeginWithDependencies(collaborators =>
                    HotReloadGroupProcessorDependencies.Create(
                        files => HotReloadGroupProcessor.TryAppendNewSourceMembershipFailure(collaborators, files),
                        (files, input, ct) =>
                            HotReloadIntroducedTypePreparation.PrepareAsync(collaborators, files, input, ct),
                        collaborators.TransformWorkerClient.RunAsync,
                        (context, ct) => HotReloadGroupProcessor.GateAndCompileAsync(collaborators, context, ct),
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
                        CallerTypeMetadataName = new HotReloadMetadataTypeName("Coverage.Host"),
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
                        CallerTypeMetadataName = new HotReloadMetadataTypeName("Coverage.Host"),
                        CallerMethodName = "Caller",
                        CallerParameterTypeFullNames = Array.Empty<string>(),
                        TargetMethodKey = TargetKey
                    }
                },
                exemptions);
        }

        /// <summary>
        /// What: the evidence a new source was applied under reaches the domain when the run
        /// records its applied sources, so a later reload can re-check the file's assembly.
        /// </summary>
        [Test]
        public void RecordAppliedSourceHashes_FileResultCarriesEvidence_RecordsItForThatPath()
        {
            HotReloadRunAccumulator run = new HotReloadRunAccumulator(
                HotReloadCompositionRoot.Services.Domain,
                HotReloadCompositionRoot.Services.Patcher,
                HotReloadCompositionRoot.Services.UnityMessageForwarding,
                autoRefreshHeldAtStart: false);
            HotReloadNewSourceMembershipEvidence evidence = CreateChangedMembershipEvidence();

            run.Add(CoverageCallerPath, CreateAppliedResult(evidence));
            run.RecordAppliedSourceHashes();

            Assert.That(
                HotReloadCompositionRoot.Services.Domain.AppliedSources.TryGetNewSourceMembershipEvidence(CoverageCallerPath),
                Is.SameAs(evidence));
        }

        /// <summary>
        /// What: a later result for the same file that carries no evidence leaves the recorded one
        /// in place. A result can end before its target is resolved, and erasing the evidence then
        /// would strand a new source that an earlier run already proved belongs to the assembly.
        /// </summary>
        [Test]
        public void RecordAppliedSourceHashes_LaterResultWithoutEvidence_KeepsTheRecordedOne()
        {
            HotReloadNewSourceMembershipEvidence evidence = CreateChangedMembershipEvidence();
            HotReloadRunAccumulator first = new HotReloadRunAccumulator(
                HotReloadCompositionRoot.Services.Domain,
                HotReloadCompositionRoot.Services.Patcher,
                HotReloadCompositionRoot.Services.UnityMessageForwarding,
                autoRefreshHeldAtStart: false);
            first.Add(CoverageCallerPath, CreateAppliedResult(evidence));
            first.RecordAppliedSourceHashes();

            HotReloadRunAccumulator second = new HotReloadRunAccumulator(
                HotReloadCompositionRoot.Services.Domain,
                HotReloadCompositionRoot.Services.Patcher,
                HotReloadCompositionRoot.Services.UnityMessageForwarding,
                autoRefreshHeldAtStart: false);
            second.Add(CoverageCallerPath, CreateAppliedResult(null));
            second.RecordAppliedSourceHashes();

            Assert.That(
                HotReloadCompositionRoot.Services.Domain.AppliedSources.TryGetNewSourceMembershipEvidence(CoverageCallerPath),
                Is.SameAs(evidence));
        }

        /// <summary>
        /// What: a result that ended without a worker hash, such as a worker that produced no file
        /// output, leaves the file's earlier applied-source record in place, because the run
        /// touched none of the file's patches.
        /// </summary>
        [Test]
        public void RecordAppliedSourceHashes_FailedResultWithoutWorkerHash_KeepsTheEarlierRecord()
        {
            HotReloadDomain domain = HotReloadCompositionRoot.Services.Domain;
            domain.AppliedSources.RecordAppliedSource(CoverageCallerPath, "earlier-hash", false, "/worker-copy/Recorded.cs", Array.Empty<HotReloadUnappliedRow>());
            HotReloadRunAccumulator run = new HotReloadRunAccumulator(
                domain,
                HotReloadCompositionRoot.Services.Patcher,
                HotReloadCompositionRoot.Services.UnityMessageForwarding,
                autoRefreshHeldAtStart: false);

            run.Add(
                CoverageCallerPath,
                new HotReloadFileProcessResult(
                    new List<HotReloadMethodOutcome>
                    {
                        HotReloadMethodOutcome.Failed("(file)", "worker produced no output", CoverageCallerPath)
                    },
                    new List<string>(),
                    patchedCount: 0,
                    sourceContentSha256: null));
            run.RecordAppliedSourceHashes();

            Assert.That(domain.AppliedSources.TryGetAppliedSource(CoverageCallerPath), Is.EqualTo(("earlier-hash", false)));
        }

        /// <summary>
        /// What: a result the run decides to forget clears the file's applied-source record and
        /// the membership evidence recorded with it, so no earlier run's record outlives it.
        /// </summary>
        [Test]
        public void RecordAppliedSourceHashes_ResultWithNoRowsOrAddedFields_ClearsTheRecordAndEvidence()
        {
            HotReloadDomain domain = HotReloadCompositionRoot.Services.Domain;
            HotReloadRunAccumulator first = new HotReloadRunAccumulator(
                domain,
                HotReloadCompositionRoot.Services.Patcher,
                HotReloadCompositionRoot.Services.UnityMessageForwarding,
                autoRefreshHeldAtStart: false);
            first.Add(CoverageCallerPath, CreateAppliedResult(CreateChangedMembershipEvidence()));
            first.RecordAppliedSourceHashes();
            Assert.That(domain.AppliedSources.TryGetAppliedSource(CoverageCallerPath), Is.Not.Null, "Precondition: the first run must record the file.");

            HotReloadRunAccumulator second = new HotReloadRunAccumulator(
                domain,
                HotReloadCompositionRoot.Services.Patcher,
                HotReloadCompositionRoot.Services.UnityMessageForwarding,
                autoRefreshHeldAtStart: false);
            second.Add(
                CoverageCallerPath,
                new HotReloadFileProcessResult(
                    new List<HotReloadMethodOutcome>(),
                    new List<string>(),
                    patchedCount: 0,
                    sourceContentSha256: "later-hash"));
            second.RecordAppliedSourceHashes();

            Assert.That(domain.AppliedSources.TryGetAppliedSource(CoverageCallerPath), Is.Null);
            Assert.That(domain.AppliedSources.TryGetNewSourceMembershipEvidence(CoverageCallerPath), Is.Null);
        }

        /// <summary>
        /// What: a file whose result has Skipped and Failed rows is recorded with those rows, in the
        /// order the result reports them, and with the path the worker read the file from.
        /// </summary>
        [Test]
        public void RecordAppliedSourceHashes_PartiallyAppliedFile_RecordsItsSkippedAndFailedRows()
        {
            HotReloadDomain domain = HotReloadCompositionRoot.Services.Domain;
            HotReloadRunAccumulator run = CreateRunAccumulator(domain);

            run.Add(
                CoverageCallerPath,
                CreateHashedResult(
                    "partial-hash",
                    CoverageCallerWorkerSourcePath,
                    HotReloadMethodOutcome.Skipped(SkippedMethodLabel, "needs a compile", CoverageCallerPath),
                    HotReloadMethodOutcome.Patched(CallerKey, CoverageCallerPath),
                    HotReloadMethodOutcome.Failed(FailedMethodLabel, "could not patch", CoverageCallerPath)));
            run.RecordAppliedSourceHashes();

            HotReloadAppliedSourceRecord record = domain.AppliedSources.FindRecordForRequestedPath(CoverageCallerPath);
            Assert.That(record, Is.Not.Null);
            Assert.That(record.Hash, Is.EqualTo("partial-hash"));
            Assert.That(record.IsFullyApplied, Is.False);
            Assert.That(record.SourcePath, Is.EqualTo(CoverageCallerWorkerSourcePath));
            Assert.That(
                DescribeRows(record.UnappliedRows),
                Is.EqualTo(new[] { "Skipped " + SkippedMethodLabel, "Failed " + FailedMethodLabel }));
        }

        /// <summary>
        /// What: a fully applied file is recorded with no unapplied rows and with the path the
        /// worker read it from.
        /// </summary>
        [Test]
        public void RecordAppliedSourceHashes_FullyAppliedFile_RecordsNoUnappliedRows()
        {
            HotReloadDomain domain = HotReloadCompositionRoot.Services.Domain;
            HotReloadRunAccumulator run = CreateRunAccumulator(domain);

            run.Add(CoverageCallerPath, CreateAppliedResult(null));
            run.RecordAppliedSourceHashes();

            HotReloadAppliedSourceRecord record = domain.AppliedSources.FindRecordForRequestedPath(CoverageCallerPath);
            Assert.That(record, Is.Not.Null);
            Assert.That(record.IsFullyApplied, Is.True);
            Assert.That(record.SourcePath, Is.EqualTo(CoverageCallerWorkerSourcePath));
            Assert.That(record.UnappliedRows, Is.Empty);
        }

        /// <summary>
        /// What: a later result without a worker hash leaves the earlier record's rows in place with
        /// its hash, because that run touched none of the file's patches.
        /// </summary>
        [Test]
        public void RecordAppliedSourceHashes_KeptFile_LeavesTheEarlierUnappliedRows()
        {
            HotReloadDomain domain = HotReloadCompositionRoot.Services.Domain;
            HotReloadRunAccumulator first = CreateRunAccumulator(domain);
            first.Add(
                CoverageCallerPath,
                CreateHashedResult(
                    "partial-hash",
                    CoverageCallerWorkerSourcePath,
                    HotReloadMethodOutcome.Skipped(SkippedMethodLabel, "needs a compile", CoverageCallerPath)));
            first.RecordAppliedSourceHashes();

            HotReloadRunAccumulator second = CreateRunAccumulator(domain);
            second.Add(
                CoverageCallerPath,
                new HotReloadFileProcessResult(
                    new List<HotReloadMethodOutcome>
                    {
                        HotReloadMethodOutcome.Failed(FileLevelRowLabel, "worker produced no output", CoverageCallerPath)
                    },
                    new List<string>(),
                    patchedCount: 0,
                    sourceContentSha256: null));
            second.RecordAppliedSourceHashes();

            HotReloadAppliedSourceRecord record = domain.AppliedSources.FindRecordForRequestedPath(CoverageCallerPath);
            Assert.That(record, Is.Not.Null);
            Assert.That(record.Hash, Is.EqualTo("partial-hash"));
            Assert.That(DescribeRows(record.UnappliedRows), Is.EqualTo(new[] { "Skipped " + SkippedMethodLabel }));
        }

        /// <summary>
        /// What: a later result the run forgets drops the record together with its rows.
        /// </summary>
        [Test]
        public void RecordAppliedSourceHashes_ForgottenFile_DropsTheRecordWithItsRows()
        {
            HotReloadDomain domain = HotReloadCompositionRoot.Services.Domain;
            HotReloadRunAccumulator first = CreateRunAccumulator(domain);
            first.Add(
                CoverageCallerPath,
                CreateHashedResult(
                    "partial-hash",
                    CoverageCallerWorkerSourcePath,
                    HotReloadMethodOutcome.Skipped(SkippedMethodLabel, "needs a compile", CoverageCallerPath)));
            first.RecordAppliedSourceHashes();
            Assert.That(
                domain.AppliedSources.FindRecordForRequestedPath(CoverageCallerPath),
                Is.Not.Null,
                "Precondition: the first run must record the file.");

            HotReloadRunAccumulator second = CreateRunAccumulator(domain);
            second.Add(CoverageCallerPath, CreateHashedResult("later-hash", CoverageCallerWorkerSourcePath));
            second.RecordAppliedSourceHashes();

            Assert.That(domain.AppliedSources.FindRecordForRequestedPath(CoverageCallerPath), Is.Null);
        }

        /// <summary>
        /// What: two files in one run are each recorded with only their own rows and path.
        /// </summary>
        [Test]
        public void RecordAppliedSourceHashes_TwoFiles_KeepsEachFilesOwnRows()
        {
            HotReloadDomain domain = HotReloadCompositionRoot.Services.Domain;
            HotReloadRunAccumulator run = CreateRunAccumulator(domain);

            run.Add(
                CoverageCallerPath,
                CreateHashedResult(
                    "caller-hash",
                    CoverageCallerWorkerSourcePath,
                    HotReloadMethodOutcome.Skipped(SkippedMethodLabel, "needs a compile", CoverageCallerPath)));
            run.Add(
                CoverageSiblingPath,
                CreateHashedResult(
                    "sibling-hash",
                    CoverageSiblingWorkerSourcePath,
                    HotReloadMethodOutcome.Failed(FailedMethodLabel, "could not patch", CoverageSiblingPath)));
            run.RecordAppliedSourceHashes();

            HotReloadAppliedSourceRecord caller = domain.AppliedSources.FindRecordForRequestedPath(CoverageCallerPath);
            HotReloadAppliedSourceRecord sibling = domain.AppliedSources.FindRecordForRequestedPath(CoverageSiblingPath);
            Assert.That(caller, Is.Not.Null);
            Assert.That(sibling, Is.Not.Null);
            Assert.That(DescribeRows(caller.UnappliedRows), Is.EqualTo(new[] { "Skipped " + SkippedMethodLabel }));
            Assert.That(caller.SourcePath, Is.EqualTo(CoverageCallerWorkerSourcePath));
            Assert.That(DescribeRows(sibling.UnappliedRows), Is.EqualTo(new[] { "Failed " + FailedMethodLabel }));
            Assert.That(sibling.SourcePath, Is.EqualTo(CoverageSiblingWorkerSourcePath));
        }

        /// <summary>
        /// What: a hashed result whose only row is the file-level "(file)" failure a group failure
        /// leaves is recorded with that row as its label reads.
        /// </summary>
        [Test]
        public void RecordAppliedSourceHashes_GroupFailureWithAHash_RecordsTheFileRow()
        {
            HotReloadDomain domain = HotReloadCompositionRoot.Services.Domain;
            HotReloadRunAccumulator run = CreateRunAccumulator(domain);

            run.Add(
                CoverageCallerPath,
                CreateHashedResult(
                    "group-failure-hash",
                    CoverageCallerWorkerSourcePath,
                    HotReloadMethodOutcome.Failed(FileLevelRowLabel, "the worker stopped", CoverageCallerPath)));
            run.RecordAppliedSourceHashes();

            HotReloadAppliedSourceRecord record = domain.AppliedSources.FindRecordForRequestedPath(CoverageCallerPath);
            Assert.That(record, Is.Not.Null);
            Assert.That(record.IsFullyApplied, Is.False);
            Assert.That(DescribeRows(record.UnappliedRows), Is.EqualTo(new[] { "Failed " + FileLevelRowLabel }));
        }

        /// <summary>
        /// What: a sibling brought back to retry after a skip that again applies nothing drops the
        /// file's record together with its rows.
        /// </summary>
        [Test]
        public void RecordAppliedSourceHashes_RetriedSiblingThatAppliedNothing_DropsTheRecordWithItsRows()
        {
            HotReloadDomain domain = HotReloadCompositionRoot.Services.Domain;
            HotReloadRunAccumulator first = CreateRunAccumulator(domain);
            first.Add(
                CoverageSiblingPath,
                CreateHashedResult(
                    "partial-hash",
                    CoverageSiblingWorkerSourcePath,
                    HotReloadMethodOutcome.Skipped(SkippedMethodLabel, "needs a compile", CoverageSiblingPath)));
            first.RecordAppliedSourceHashes();
            Assert.That(
                domain.AppliedSources.FindRecordForRequestedPath(CoverageSiblingPath),
                Is.Not.Null,
                "Precondition: the first run must record the file.");

            HotReloadRunAccumulator retry = CreateRunAccumulator(domain);
            retry.NoteSiblingInclusion(CoverageSiblingPath, HotReloadSiblingInclusionReason.RetryAfterSkip);
            retry.AddReappliedSibling(
                CoverageSiblingPath,
                CreateHashedResult(
                    "partial-hash",
                    CoverageSiblingWorkerSourcePath,
                    HotReloadMethodOutcome.Skipped(SkippedMethodLabel, "needs a compile", CoverageSiblingPath)));
            retry.RecordAppliedSourceHashes();

            Assert.That(domain.AppliedSources.FindRecordForRequestedPath(CoverageSiblingPath), Is.Null);
        }

        /// <summary>
        /// What: the result names as active-patch siblings only the siblings that came back for
        /// their active changes, while every re-applied sibling stays in ReappliedSiblingPaths.
        /// </summary>
        [Test]
        public void BuildResult_ActivePatchSiblingPaths_ContainsOnlySiblingsIncludedForActiveChanges()
        {
            const string activeChangesPath = "Assets/ActiveChangesSibling.cs";
            const string retryAfterSkipPath = "Assets/RetryAfterSkipSibling.cs";
            const string companionPath = "Assets/CompanionSibling.cs";
            HotReloadRunAccumulator run = new HotReloadRunAccumulator(
                HotReloadCompositionRoot.Services.Domain,
                HotReloadCompositionRoot.Services.Patcher,
                HotReloadCompositionRoot.Services.UnityMessageForwarding,
                autoRefreshHeldAtStart: false);
            run.NoteSiblingInclusion(activeChangesPath, HotReloadSiblingInclusionReason.ActiveChanges);
            run.NoteSiblingInclusion(retryAfterSkipPath, HotReloadSiblingInclusionReason.RetryAfterSkip);
            run.NoteSiblingInclusion(companionPath, HotReloadSiblingInclusionReason.Companion);

            run.AddReappliedSibling(activeChangesPath, CreateEmptyResult());
            run.AddReappliedSibling(retryAfterSkipPath, CreateEmptyResult());
            run.AddReappliedSibling(companionPath, CreateEmptyResult());
            HotReloadOrchestratorResult result = run.BuildResult("correlation-active-patch-siblings");

            Assert.That(
                result.ReappliedSiblingPaths,
                Is.EquivalentTo(new[] { activeChangesPath, retryAfterSkipPath, companionPath }));
            Assert.That(result.ActivePatchSiblingPaths, Is.EquivalentTo(new[] { activeChangesPath }));
        }

        private static HotReloadRunAccumulator CreateRunAccumulator(HotReloadDomain domain)
        {
            return new HotReloadRunAccumulator(
                domain,
                HotReloadCompositionRoot.Services.Patcher,
                HotReloadCompositionRoot.Services.UnityMessageForwarding,
                autoRefreshHeldAtStart: false);
        }

        // A result the worker hashed, with the given rows: the shape FinishFileResult builds.
        private static HotReloadFileProcessResult CreateHashedResult(
            string sourceContentSha256,
            string workerSourcePath,
            params HotReloadMethodOutcome[] outcomes)
        {
            return new HotReloadFileProcessResult(
                new List<HotReloadMethodOutcome>(outcomes),
                new List<string>(),
                patchedCount: 0,
                sourceContentSha256: sourceContentSha256,
                workerSourcePath: workerSourcePath);
        }

        private static string[] DescribeRows(IReadOnlyList<HotReloadUnappliedRow> rows)
        {
            string[] described = new string[rows.Count];
            for (int index = 0; index < rows.Count; index++)
            {
                described[index] = rows[index].Kind + " " + rows[index].Label;
            }

            return described;
        }

        private static HotReloadFileProcessResult CreateEmptyResult()
        {
            return new HotReloadFileProcessResult(
                new List<HotReloadMethodOutcome>(),
                new List<string>(),
                patchedCount: 0,
                sourceContentSha256: null);
        }

        // A fully applied single-method result for the caller path: the shape the run stages an
        // applied-source record from.
        private static HotReloadFileProcessResult CreateAppliedResult(
            HotReloadNewSourceMembershipEvidence newSourceMembershipEvidence)
        {
            return new HotReloadFileProcessResult(
                new List<HotReloadMethodOutcome> { HotReloadMethodOutcome.Patched(CallerKey, CoverageCallerPath) },
                new List<string>(),
                patchedCount: 1,
                sourceContentSha256: "applied-source-hash",
                newSourceMembershipEvidence: newSourceMembershipEvidence,
                workerSourcePath: CoverageCallerWorkerSourcePath);
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
            HotReloadNewSourceMembershipEvidence newSourceMembershipEvidence = null,
            HotReloadRunDisplayedRemovedMembers displayedRemovedMembers = null)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            Assembly compilationAssembly = FindCompilationAssembly();
            HotReloadGroupFile callerFile = CreateFile(
                "Assets/CoverageCaller.cs", projectRoot, compilationAssembly, newSourceMembershipEvidence,
                displayedRemovedMembers);
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
                callerFile.Home, compilationAssembly.defines ?? Array.Empty<string>(),
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
                null,
                new HotReloadCompileFailureNoteSources(workerOutput.skipped, Array.Empty<HotReloadRefusedIntroducedType>()));
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
                context.Home,
                context.Defines,
                context.WorkerInput,
                emptyWorkerOutput,
                context.Files,
                null,
                new HotReloadCompileFailureNoteSources(emptyWorkerOutput.skipped, Array.Empty<HotReloadRefusedIntroducedType>()));
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

        // The worker spells the declaring type of a row the metadata way, so the fixture takes
        // that spelling and builds the store key from it exactly as the worker does.
        private static TransformWorkerAddedFieldDeclarationDto CreateDeclarationDto(
            string declaringTypeMetadataName,
            string fieldName,
            Type declaredType,
            bool isStatic)
        {
            return new TransformWorkerAddedFieldDeclarationDto
            {
                fieldKey = declaringTypeMetadataName + "::" + fieldName,
                declaringTypeMetadataName = declaringTypeMetadataName,
                fieldName = fieldName,
                declaredTypeAssemblyQualifiedName = declaredType.AssemblyQualifiedName,
                isStatic = isStatic
            };
        }

        private static HotReloadGroupFile CreateFile(
            string path,
            string projectRoot,
            Assembly compilationAssembly,
            HotReloadNewSourceMembershipEvidence newSourceMembershipEvidence = null,
            HotReloadRunDisplayedRemovedMembers displayedRemovedMembers = null)
        {
            // Why a real worker source with its hash: the commit boundary verifies each request
            // source still hashes to what the transform run reported, and refuses a file it
            // cannot compare.
            string workerSourcePath = HotReloadTestSourceWriter.WriteEditedSource(
                Path.GetFileName(path),
                "// " + path + "\n");
            HotReloadGroupFile file = new HotReloadGroupFile(
                path, workerSourcePath, path, AssemblyName, compilationAssembly,
                HotReloadTypeHome.ScriptAssembliesUnderProject(projectRoot, AssemblyName),
                projectRoot, new HotReloadFileSinks(new List<string>(), null, displayedRemovedMembers),
                newSourceMembershipEvidence);
            file.FileOutput = new TransformWorkerFileOutputDto
            {
                projectRelativePath = path,
                sourceContentSha256 = new HotReloadSourceContentHasher().ComputeContentHash(
                    File.ReadAllBytes(workerSourcePath)),
                removedMethodSignatures = Array.Empty<TransformWorkerRemovedMethodSignatureDto>()
            };
            file.SnapshotLabels = new HashSet<string>();
            file.SnapshotAddedLabels = new HashSet<string>();
            file.SnapshotForwardedUnityMessageLabels = new HashSet<string>();
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
            using IDisposable editorStateScope = HotReloadServicesTestScope.BeginWithEditorState(
                new HotReloadStubEditorStateSnapshotCapture(() => new HotReloadEditorStateSnapshot(false, false, false)));
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            Assembly compilationAssembly = FindCompilationAssembly();
            string targetDllPath = Path.Combine(
                projectRoot,
                "Library",
                "ScriptAssemblies",
                AssemblyName + ".dll");
            string failure = HotReloadNewSourceMembershipValidator.TryCapture(
                HotReloadCompositionRoot.Services.EditorStateSnapshotCapture,
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
            new HotReloadDomainTestAccess().RegisterAddedMember(
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
