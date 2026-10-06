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
    /// Covers how a group leaves out a default-selected file whose only change is added enum
    /// members: how often the worker runs, what the rest of the group receives, and the results
    /// the group returns.
    /// </summary>
    public sealed class HotReloadGroupProcessorLeaveOutTests
    {
        private const string AssemblyName = "UnityCLILoop.Tests.Editor.HotReload";
        private const string EnumPath = "Assets/Tests/Editor/HotReload/LeaveOutKind.cs";
        private const string OtherEnumPath = "Assets/Tests/Editor/HotReload/LeaveOutMode.cs";
        private const string CallerPath = "Assets/Tests/Editor/HotReload/LeaveOutCaller.cs";
        private const string OtherCallerPath = "Assets/Tests/Editor/HotReload/LeaveOutOtherCaller.cs";
        private const string SiblingPath = "Assets/Tests/Editor/HotReload/LeaveOutSibling.cs";
        private const string EnumMemberName = "LeaveOut.Kind.Third";
        private const string EnumDriftWarning =
            "enum member LeaveOut.Kind.Third exists only in the edited source, not in the compiled assembly.";
        private const string LeftOutWarningStart = "Left '" + EnumPath + "' out of this reload";
        private const string FileLevelRowLabel = "(file)";

        /// <summary>
        /// Which flag says the enum-only file also declares a new type.
        /// </summary>
        public enum NewTypeFlag
        {
            Introduced,
            Refused
        }

        private HotReloadDomainTestScope _scope;
        private string _projectRoot;
        private Assembly _compilationAssembly;

        [SetUp]
        public void SetUp()
        {
            _scope = new HotReloadDomainTestScope();
            _projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            _compilationAssembly = FindCompilationAssembly();
        }

        [TearDown]
        public void TearDown()
        {
            _scope.Dispose();
        }

        /// <summary>
        /// What: an enum-only file that no skipped row names stays in the run, and the worker runs once.
        /// </summary>
        [Test]
        public async Task ProcessGroupAsync_DefaultSelectedEnumOnlyFileWithoutASplitRow_RunsTheWorkerOnce()
        {
            HotReloadGroupFile enumFile = CreateDefaultFile(EnumPath);
            HotReloadGroupFile callerFile = CreateDefaultFile(CallerPath);
            FakeWorker worker = new FakeWorker(EnumPath) { ReportsSplit = false };
            GateRecorder gate = new GateRecorder();

            await RunGroupAsync(new[] { enumFile, callerFile }, worker, gate);

            Assert.That(worker.Inputs, Has.Count.EqualTo(1));
            Assert.That(gate.Contexts, Has.Count.EqualTo(1));
            Assert.That(gate.Contexts[0].Files, Is.EqualTo(new[] { enumFile, callerFile }));
        }

        /// <summary>
        /// What: an enum-only file that holds an added member in the domain stays in the run, since
        /// the group reads which files hold changes from the domain itself.
        /// </summary>
        [Test]
        public async Task ProcessGroupAsync_DefaultSelectedEnumOnlyFileHoldingAnAddedMember_KeepsItInTheRun()
        {
            SeedActiveAddedMember(EnumPath);
            HotReloadGroupFile enumFile = CreateDefaultFile(EnumPath);
            HotReloadGroupFile callerFile = CreateDefaultFile(CallerPath);
            FakeWorker worker = new FakeWorker(EnumPath);
            GateRecorder gate = new GateRecorder();

            await RunGroupAsync(new[] { enumFile, callerFile }, worker, gate);

            Assert.That(worker.Inputs, Has.Count.EqualTo(1));
            Assert.That(gate.Contexts[0].Files, Is.EqualTo(new[] { enumFile, callerFile }));
        }

        /// <summary>
        /// What: an enum-only file that also declares a new type, introduced or refused, stays in
        /// the run, since leaving the owner out would drop its type from the group.
        /// </summary>
        [TestCase(NewTypeFlag.Introduced)]
        [TestCase(NewTypeFlag.Refused)]
        public async Task ProcessGroupAsync_DefaultSelectedEnumOnlyFileDeclaringANewType_KeepsItInTheRun(NewTypeFlag flag)
        {
            HotReloadGroupFile enumFile = CreateDefaultFile(EnumPath);
            enumFile.DeclaresIntroducedType = flag == NewTypeFlag.Introduced;
            enumFile.DeclaresRefusedIntroducedType = flag == NewTypeFlag.Refused;
            HotReloadGroupFile callerFile = CreateDefaultFile(CallerPath);
            FakeWorker worker = new FakeWorker(EnumPath);
            GateRecorder gate = new GateRecorder();

            await RunGroupAsync(new[] { enumFile, callerFile }, worker, gate);

            Assert.That(worker.Inputs, Has.Count.EqualTo(1));
            Assert.That(gate.Contexts[0].Files, Is.EqualTo(new[] { enumFile, callerFile }));
        }

        /// <summary>
        /// What: when the rerun without the enum-only file fails, the rest of the group fails with
        /// the rerun's error, the left-out file keeps its left-out result, and the results stay in
        /// the group's order.
        /// </summary>
        [Test]
        public async Task ProcessGroupAsync_LeaveOutRetryFails_FailsTheRestAndKeepsTheOrder()
        {
            HotReloadGroupFile enumFile = CreateDefaultFile(EnumPath);
            HotReloadGroupFile callerFile = CreateDefaultFile(CallerPath);
            FakeWorker worker = new FakeWorker(EnumPath) { RetryFailure = "rerun failed" };
            GateRecorder gate = new GateRecorder();

            IReadOnlyList<HotReloadFileProcessResult> results =
                await RunGroupAsync(new[] { enumFile, callerFile }, worker, gate);

            Assert.That(worker.Inputs, Has.Count.EqualTo(2));
            Assert.That(gate.Contexts, Is.Empty);
            AssertResultsFollow(results, new[] { enumFile, callerFile });
            Assert.That(CountFileFailedRows(callerFile), Is.EqualTo(1));
            Assert.That(CountFileFailedRows(enumFile), Is.EqualTo(0));
            Assert.That(CountWarnings(results[0], LeftOutWarningStart), Is.EqualTo(1));
        }

        /// <summary>
        /// What: a cancellation during the rerun propagates and stops the group before the gate, so
        /// nothing is reverted or applied.
        /// </summary>
        [Test]
        public async Task ProcessGroupAsync_LeaveOutRetryCancelled_ThrowsWithoutReachingTheGate()
        {
            HotReloadGroupFile enumFile = CreateDefaultFile(EnumPath);
            HotReloadGroupFile callerFile = CreateDefaultFile(CallerPath);
            FakeWorker worker = new FakeWorker(EnumPath) { CancelsRetry = true };
            GateRecorder gate = new GateRecorder();

            // Why await and catch instead of Assert.ThrowsAsync: that one waits synchronously on
            // the main thread, which the EditMode guardrails rule out.
            OperationCanceledException cancellation = null;
            try
            {
                await RunGroupAsync(new[] { enumFile, callerFile }, worker, gate);
            }
            catch (OperationCanceledException exception)
            {
                cancellation = exception;
            }

            Assert.That(cancellation, Is.Not.Null, "The cancellation must propagate out of the group.");
            Assert.That(worker.Inputs, Has.Count.EqualTo(2));
            Assert.That(gate.Contexts, Is.Empty);
        }

        /// <summary>
        /// What: two enum-only files named by the split are both left out by one rerun.
        /// </summary>
        [Test]
        public async Task ProcessGroupAsync_TwoEnumOnlyFiles_LeavesBothOutInOneRetry()
        {
            HotReloadGroupFile enumFile = CreateDefaultFile(EnumPath);
            HotReloadGroupFile otherEnumFile = CreateDefaultFile(OtherEnumPath);
            HotReloadGroupFile callerFile = CreateDefaultFile(CallerPath);
            FakeWorker worker = new FakeWorker(EnumPath, OtherEnumPath);
            GateRecorder gate = new GateRecorder();

            IReadOnlyList<HotReloadFileProcessResult> results =
                await RunGroupAsync(new[] { enumFile, otherEnumFile, callerFile }, worker, gate);

            Assert.That(worker.Inputs, Has.Count.EqualTo(2));
            Assert.That(CollectSourcePaths(worker.Inputs[1]), Is.EqualTo(new[] { CallerPath }));
            Assert.That(gate.Contexts[0].Files, Is.EqualTo(new[] { callerFile }));
            AssertResultsFollow(results, new[] { enumFile, otherEnumFile, callerFile });
        }

        /// <summary>
        /// What: wherever the left-out file sits among the inputs, including right before a
        /// re-applied sibling, each result stays at its file's position and the rest of the group
        /// keeps its order.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public async Task ProcessGroupAsync_EnumOnlyFileLeftOut_ReturnsResultsInInputOrder(int enumPosition)
        {
            HotReloadGroupFile callerFile = CreateDefaultFile(CallerPath);
            HotReloadGroupFile otherCallerFile = CreateDefaultFile(OtherCallerPath);
            List<HotReloadGroupFile> remaining = new List<HotReloadGroupFile> { callerFile, otherCallerFile };
            HotReloadGroupFile enumFile = CreateDefaultFile(EnumPath);
            List<HotReloadGroupFile> group = new List<HotReloadGroupFile>(remaining);
            group.Insert(enumPosition, enumFile);
            HotReloadGroupFile sibling = CreateSibling(callerFile, SiblingPath);
            group.Add(sibling);
            remaining.Add(sibling);
            FakeWorker worker = new FakeWorker(EnumPath);
            GateRecorder gate = new GateRecorder();

            IReadOnlyList<HotReloadFileProcessResult> results = await RunGroupAsync(group, worker, gate);

            AssertResultsFollow(results, group);
            Assert.That(gate.Contexts[0].Files, Is.EqualTo(remaining));
            Assert.That(
                CollectSourcePaths(worker.Inputs[1]),
                Is.EqualTo(new[] { CallerPath, OtherCallerPath, SiblingPath }));
        }

        /// <summary>
        /// What: the rest of the group is handed the rerun's files, input and output, not the first
        /// run's, so every later stage and its own retries see the group without the left-out file.
        /// </summary>
        [Test]
        public async Task ProcessGroupAsync_EnumOnlyFileLeftOut_HandsTheBodyTheRetryInputAndOutput()
        {
            HotReloadGroupFile enumFile = CreateDefaultFile(EnumPath);
            HotReloadGroupFile callerFile = CreateDefaultFile(CallerPath);
            FakeWorker worker = new FakeWorker(EnumPath);
            GateRecorder gate = new GateRecorder();

            await RunGroupAsync(new[] { enumFile, callerFile }, worker, gate);

            Assert.That(worker.Inputs, Has.Count.EqualTo(2));
            Assert.That(CollectSourcePaths(worker.Inputs[1]), Is.EqualTo(new[] { CallerPath }));
            Assert.That(worker.Inputs[1].changedSiblingSourcePaths, Is.SameAs(worker.Inputs[0].changedSiblingSourcePaths));
            Assert.That(gate.Contexts, Has.Count.EqualTo(1));
            HotReloadApplyContext context = gate.Contexts[0];
            Assert.That(context.Files, Is.EqualTo(new[] { callerFile }));
            Assert.That(context.WorkerInput, Is.SameAs(worker.Inputs[1]));
            Assert.That(context.WorkerOutput, Is.SameAs(worker.Outputs[1]));
        }

        /// <summary>
        /// What: the left-out file's result carries what the first run reported for it — its hash,
        /// added enum members, unchanged count and drift warning, each once — plus one warning that
        /// says it was left out.
        /// </summary>
        [Test]
        public async Task ProcessGroupAsync_EnumOnlyFileLeftOut_KeepsItsFirstPassResultAndWarnings()
        {
            HotReloadGroupFile enumFile = CreateDefaultFile(EnumPath);
            HotReloadGroupFile callerFile = CreateDefaultFile(CallerPath);
            FakeWorker worker = new FakeWorker(EnumPath);
            GateRecorder gate = new GateRecorder();

            IReadOnlyList<HotReloadFileProcessResult> results =
                await RunGroupAsync(new[] { enumFile, callerFile }, worker, gate);

            HotReloadFileProcessResult enumResult = results[0];
            Assert.That(enumResult.SourceContentSha256, Is.EqualTo(HashOf(EnumPath)));
            Assert.That(enumResult.AddedEnumMemberNames, Is.EqualTo(new[] { EnumMemberName }));
            Assert.That(enumResult.UnchangedMethodCount, Is.EqualTo(1));
            Assert.That(enumResult.PatchedCount, Is.EqualTo(0));
            Assert.That(CountWarnings(enumResult, EnumDriftWarning), Is.EqualTo(1));
            Assert.That(CountWarnings(enumResult, LeftOutWarningStart), Is.EqualTo(1));
            Assert.That(CountWarnings(results[1], LeftOutWarningStart), Is.EqualTo(0));
            Assert.That(enumResult.Outcomes, Is.Empty);
        }

        private async Task<IReadOnlyList<HotReloadFileProcessResult>> RunGroupAsync(
            IReadOnlyList<HotReloadGroupFile> files,
            FakeWorker worker,
            GateRecorder gate)
        {
            using (HotReloadServicesTestScope.BeginWithDependencies(collaborators =>
                HotReloadGroupProcessorDependencies.Create(
                    groupFiles => true,
                    (groupFiles, input, ct) => Task.FromResult(
                        HotReloadIntroducedTypePreparationResult.NoIntroducedTypes()),
                    worker.RunAsync,
                    gate.RecordAsync,
                    (context, compileResult, entriesToPatch) => HotReloadGroupEntryPreparation.PrepareGroup(
                        collaborators, context, compileResult, entriesToPatch),
                    collaborators.EntryApplier.ApplyPreparedEntries)))
            {
                return await HotReloadCompositionRoot.Services.GroupProcessor.ProcessGroupAsync(
                    files,
                    "leave-out-test",
                    new HotReloadRunTiming(),
                    CancellationToken.None);
            }
        }

        private HotReloadGroupFile CreateDefaultFile(string path)
        {
            HotReloadGroupFile file = new HotReloadGroupFile(
                path,
                WriteWorkerSource(path),
                path,
                AssemblyName,
                _compilationAssembly,
                HotReloadTypeHome.ScriptAssembliesUnderProject(_projectRoot, AssemblyName),
                _projectRoot,
                new HotReloadFileSinks(new List<string>(), null, new HotReloadRunStaleSignatureWarnings()));
            file.IsDefaultSelected = true;
            return file;
        }

        private static HotReloadGroupFile CreateSibling(HotReloadGroupFile template, string path)
        {
            return HotReloadGroupFile.ForActiveSibling(
                template,
                path,
                WriteWorkerSource(path),
                new HotReloadFileSinks(new List<string>(), null, new HotReloadRunStaleSignatureWarnings()),
                null,
                new HotReloadSiblingBaselineNotices());
        }

        private static string WriteWorkerSource(string path)
        {
            return HotReloadTestSourceWriter.WriteEditedSource(Path.GetFileName(path), "// " + path + "\n");
        }

        private static void AssertResultsFollow(
            IReadOnlyList<HotReloadFileProcessResult> results,
            IReadOnlyList<HotReloadGroupFile> files)
        {
            Assert.That(results, Has.Count.EqualTo(files.Count));
            for (int index = 0; index < files.Count; index++)
            {
                Assert.That(
                    results[index].WorkerSourcePath,
                    Is.EqualTo(Path.GetFullPath(files[index].WorkerSourcePath)),
                    "Result " + index + " must belong to " + files[index].ProjectRelativePath + ".");
            }
        }

        private static List<string> CollectSourcePaths(TransformWorkerInputDto input)
        {
            List<string> paths = new List<string>();
            foreach (TransformWorkerSourceDto source in input.sources)
            {
                paths.Add(source.projectRelativePath);
            }

            return paths;
        }

        private static int CountWarnings(HotReloadFileProcessResult result, string start)
        {
            int count = 0;
            foreach (string warning in result.Warnings)
            {
                if (warning.StartsWith(start, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountFileFailedRows(HotReloadGroupFile file)
        {
            int count = 0;
            foreach (HotReloadMethodOutcome outcome in file.Sinks.Outcomes)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Failed && outcome.Method == FileLevelRowLabel)
                {
                    count++;
                }
            }

            return count;
        }

        private static string HashOf(string path)
        {
            return "hash-of-" + path;
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

        private static void SeedActiveAddedMember(string projectRelativePath)
        {
            System.Reflection.MethodInfo shimMethod = typeof(HotReloadGroupProcessorLeaveOutTests).GetMethod(
                nameof(AddedMemberShim),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.That(shimMethod, Is.Not.Null);
            new HotReloadDomainTestAccess().RegisterAddedMember(
                projectRelativePath,
                "LeaveOut.Kind::Describe()",
                shimMethod,
                projectRelativePath);
        }

        private static void AddedMemberShim()
        {
        }

        /// <summary>
        /// Answers each run like the transform worker does for these fixtures: while an enum file
        /// is in the run, the caller's added method is skipped with a row naming the enum files of
        /// the run as the source of the split type; without one, nothing is skipped.
        /// </summary>
        private sealed class FakeWorker
        {
            private readonly HashSet<string> _enumPaths;

            internal FakeWorker(params string[] enumPaths)
            {
                _enumPaths = new HashSet<string>(enumPaths, StringComparer.Ordinal);
            }

            internal bool ReportsSplit { get; set; } = true;

            internal string RetryFailure { get; set; }

            internal bool CancelsRetry { get; set; }

            internal List<TransformWorkerInputDto> Inputs { get; } = new List<TransformWorkerInputDto>();

            internal List<TransformWorkerOutputDto> Outputs { get; } = new List<TransformWorkerOutputDto>();

            internal Task<TransformWorkerClientResult> RunAsync(TransformWorkerInputDto input, CancellationToken ct)
            {
                Inputs.Add(input);
                bool isRetry = Inputs.Count > 1;
                if (isRetry && CancelsRetry)
                {
                    return Task.FromCanceled<TransformWorkerClientResult>(new CancellationToken(true));
                }

                if (isRetry && RetryFailure != null)
                {
                    return Task.FromResult(TransformWorkerClientResult.Failure(RetryFailure));
                }

                TransformWorkerOutputDto output = BuildOutput(input);
                Outputs.Add(output);
                return Task.FromResult(TransformWorkerClientResult.SuccessResult(output));
            }

            private TransformWorkerOutputDto BuildOutput(TransformWorkerInputDto input)
            {
                List<TransformWorkerFileOutputDto> files = new List<TransformWorkerFileOutputDto>();
                List<TransformWorkerUnchangedMethodDto> unchanged = new List<TransformWorkerUnchangedMethodDto>();
                List<string> enumPathsInRun = new List<string>();
                foreach (TransformWorkerSourceDto source in input.sources)
                {
                    string path = source.projectRelativePath;
                    bool isEnum = _enumPaths.Contains(path);
                    files.Add(CreateFileOutput(path, isEnum));
                    if (!isEnum)
                    {
                        continue;
                    }

                    enumPathsInRun.Add(path);
                    unchanged.Add(new TransformWorkerUnchangedMethodDto
                    {
                        sourceProjectRelativePath = path,
                        typeMetadataName = "LeaveOut.KindNames",
                        methodName = "Describe",
                        parameterTypeFullNames = Array.Empty<string>()
                    });
                }

                List<TransformWorkerSkippedDto> skipped = new List<TransformWorkerSkippedDto>();
                if (ReportsSplit && enumPathsInRun.Count > 0)
                {
                    skipped.Add(CreateSplitRow(enumPathsInRun.ToArray()));
                }

                return new TransformWorkerOutputDto
                {
                    shimSource = string.Empty,
                    entries = Array.Empty<TransformWorkerEntryDto>(),
                    skipped = skipped.ToArray(),
                    files = files.ToArray(),
                    parseErrors = Array.Empty<string>(),
                    siblingConstDriftWarnings = Array.Empty<string>(),
                    unchangedMethods = unchanged.ToArray()
                };
            }

            private static TransformWorkerFileOutputDto CreateFileOutput(string path, bool isEnum)
            {
                return new TransformWorkerFileOutputDto
                {
                    projectRelativePath = path,
                    sourceContentSha256 = HashOf(path),
                    parseErrors = Array.Empty<string>(),
                    declarationDriftWarnings = isEnum ? new[] { EnumDriftWarning } : Array.Empty<string>(),
                    removedMembers = Array.Empty<TransformWorkerRemovedMemberDto>(),
                    removedMethodSignatures = Array.Empty<TransformWorkerRemovedMethodSignatureDto>(),
                    addedFieldNames = Array.Empty<string>(),
                    addedConstNames = Array.Empty<string>(),
                    addedEnumMemberNames = isEnum ? new[] { EnumMemberName } : Array.Empty<string>(),
                    introducedTypes = Array.Empty<TransformWorkerIntroducedTypeDto>(),
                    introducedTypeDiagnostics = Array.Empty<TransformWorkerReasonDto>(),
                    introducedTypeReuses = Array.Empty<TransformWorkerIntroducedTypeReuseDto>()
                };
            }

            private static TransformWorkerSkippedDto CreateSplitRow(string[] enumPathsInRun)
            {
                return new TransformWorkerSkippedDto
                {
                    sourceProjectRelativePath = CallerPath,
                    method = "LeaveOut.Caller.TakeKind(LeaveOut.Kind)",
                    reason = new TransformWorkerReasonDto
                    {
                        code = HotReloadWorkerReasonCode.AddedMethodCallsIntroducedMemberBoundToCompiledType,
                        args = new[] { "CS1503", "LeaveOut.Sink", "LeaveOut.Kind", string.Join(", ", enumPathsInRun) },
                        declaringFiles = enumPathsInRun
                    }
                };
            }
        }

        /// <summary>
        /// Stands in for the gate and first compile: records the context the rest of the group
        /// was handed and fails, so the group builds its unapplied results without compiling.
        /// </summary>
        private sealed class GateRecorder
        {
            internal List<HotReloadApplyContext> Contexts { get; } = new List<HotReloadApplyContext>();

            internal Task<HotReloadGroupGateAndCompileResult> RecordAsync(
                HotReloadApplyContext context,
                CancellationToken ct)
            {
                Contexts.Add(context);
                return Task.FromResult(HotReloadGroupGateAndCompileResult.Failed());
            }
        }
    }
}
