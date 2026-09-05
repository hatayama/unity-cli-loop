using System;
using System.Collections.Generic;
using System.IO;
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
                    () =>
                    {
                        continuationCalls++;
                        return Task.FromResult(expected);
                    });

            Assert.That(continuationCalls, Is.EqualTo(1));
            Assert.That(results, Is.SameAs(expected));
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

        private static HotReloadApplyContext CreateContext()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            Assembly compilationAssembly = FindCompilationAssembly();
            HotReloadGroupFile callerFile = CreateFile(
                "Assets/CoverageCaller.cs", projectRoot, compilationAssembly);
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

        private static HotReloadGroupFile CreateFile(string path, string projectRoot, Assembly compilationAssembly)
        {
            HotReloadGroupFile file = new HotReloadGroupFile(
                path, path, path, AssemblyName, compilationAssembly,
                Path.Combine(projectRoot, "Library", "ScriptAssemblies", AssemblyName + ".dll"),
                projectRoot, new HotReloadFileSinks(new List<string>(), null));
            file.FileOutput = new TransformWorkerFileOutputDto
            {
                projectRelativePath = path,
                removedMethodSignatures = Array.Empty<TransformWorkerRemovedMethodSignatureDto>()
            };
            file.SnapshotLabels = new HashSet<string>();
            file.SnapshotAddedLabels = new HashSet<string>();
            return file;
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
