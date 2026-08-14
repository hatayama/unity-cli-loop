using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using HarmonyLib;

using Mono.Cecil;

using UnityEditor.Compilation;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

using Assembly = System.Reflection.Assembly;
using UnityCompilationAssembly = UnityEditor.Compilation.Assembly;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// End-to-end hot-reload pipeline: resolve assembly → worker → shim compile → match → patch.
    /// </summary>
    internal static class HotReloadOrchestrator
    {
        /// <summary>
        /// Runs hot reload for each path in <paramref name="files"/>.
        /// <paramref name="contentPathOverride"/> is test-only: when set, the worker reads that
        /// path while assembly resolution still uses <paramref name="files"/> (so edited copies
        /// can live under <c>Library/UloopHotReload/TestSources/</c> without provoking AssetDatabase).
        /// </summary>
        public static async Task<HotReloadOrchestratorResult> RunAsync(
            IReadOnlyList<string> files,
            string contentPathOverride,
            CancellationToken ct)
        {
            Debug.Assert(files != null, "files must not be null.");
            Debug.Assert(files.Count > 0, "files must not be empty.");

            List<HotReloadMethodOutcome> outcomes = new List<HotReloadMethodOutcome>();
            List<string> warnings = new List<string>();
            List<string> suppressedPausePointIds = new List<string>();
            List<string> retargetedPausePointIds = new List<string>();
            List<string> inlineRiskMethodLabels = new List<string>();
            int patchedTotal = 0;
            int unchangedTotal = 0;

            for (int index = 0; index < files.Count; index++)
            {
                ct.ThrowIfCancellationRequested();
                string filePath = files[index];
                string workerSourcePath = string.IsNullOrEmpty(contentPathOverride)
                    ? filePath
                    : contentPathOverride;

                // Why ConfigureAwait(false): UnityCliLoopTool forbids capturing Unity's
                // SynchronizationContext across awaits — while Play Mode is paused that context
                // does not run continuations, so a true resume would hang the tool forever.
                // ProcessFileAsync switches back via MainThreadSwitcher (EditorApplication.update
                // queue) before any main-thread-only editor API or Harmony patch.
                HotReloadFileProcessResult fileResult = await ProcessFileAsync(
                    filePath,
                    workerSourcePath,
                    ct).ConfigureAwait(false);

                outcomes.AddRange(fileResult.Outcomes);
                warnings.AddRange(fileResult.Warnings);
                AppendDistinct(suppressedPausePointIds, fileResult.SuppressedPausePointIds);
                AppendDistinct(retargetedPausePointIds, fileResult.RetargetedPausePointIds);
                AppendDistinct(inlineRiskMethodLabels, fileResult.InlineRiskMethodLabels);
                patchedTotal += fileResult.PatchedCount;
                unchangedTotal += fileResult.UnchangedMethodCount;
            }

            if (inlineRiskMethodLabels.Count > 0)
            {
                warnings.Add(
                    FormatInlineRiskAggregatedWarning(
                        inlineRiskMethodLabels.Count,
                        patchedTotal,
                        inlineRiskMethodLabels));
            }

            await MainThreadSwitcher.SwitchToMainThread(ct);
            return new HotReloadOrchestratorResult(
                outcomes,
                warnings,
                patchedTotal,
                HotReloadPatcher.ActivePatchCount,
                suppressedPausePointIds,
                unchangedTotal,
                retargetedPausePointIds);
        }

        private static async Task<HotReloadFileProcessResult> ProcessFileAsync(
            string assemblyResolvePath,
            string workerSourcePath,
            CancellationToken ct)
        {
            List<HotReloadMethodOutcome> outcomes = new List<HotReloadMethodOutcome>();
            List<string> warnings = new List<string>();
            List<string> suppressedPausePointIds = new List<string>();
            List<string> retargetedPausePointIds = new List<string>();

            // CompilationPipeline / Application.dataPath require the Unity main thread.
            await MainThreadSwitcher.SwitchToMainThread(ct);

            // CompilationPipeline.GetAssemblyNameFromScriptPath expects a project-relative path
            // (Assets/... or Packages/...) and returns a file name that already includes ".dll".
            string projectRelativePath = ToProjectRelativeScriptPath(assemblyResolvePath);
            string rawAssemblyName = CompilationPipeline.GetAssemblyNameFromScriptPath(projectRelativePath);
            if (string.IsNullOrEmpty(rawAssemblyName))
            {
                outcomes.Add(
                    HotReloadMethodOutcome.Failed(
                        "(file)",
                        "Script path is not part of any compiled assembly (Assets/Packages paths only): "
                        + assemblyResolvePath,
                        assemblyResolvePath));
                return new HotReloadFileProcessResult(outcomes, warnings, 0);
            }

            string assemblyName = Path.GetFileNameWithoutExtension(rawAssemblyName);
            UnityCompilationAssembly compilationAssembly = FindCompilationAssembly(assemblyName);
            if (compilationAssembly == null)
            {
                outcomes.Add(
                    HotReloadMethodOutcome.Failed(
                        "(file)",
                        "CompilationPipeline assembly not found: " + assemblyName,
                        assemblyResolvePath));
                return new HotReloadFileProcessResult(outcomes, warnings, 0);
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string targetDllPath = Path.Combine(
                projectRoot,
                HotReloadConstants.ScriptAssembliesRelativeDirectory,
                assemblyName + HotReloadConstants.CompiledAssemblyExtension);

            if (!File.Exists(targetDllPath))
            {
                outcomes.Add(
                    HotReloadMethodOutcome.Failed(
                        "(file)",
                        "Compiled assembly not found at '" + targetDllPath + "'. Compile the project first.",
                        assemblyResolvePath));
                return new HotReloadFileProcessResult(outcomes, warnings, 0);
            }

            string mvidGuardError = CheckMvidGuard(assemblyName, targetDllPath);
            if (mvidGuardError != null)
            {
                outcomes.Add(HotReloadMethodOutcome.Failed("(file)", mvidGuardError, assemblyResolvePath));
                return new HotReloadFileProcessResult(outcomes, warnings, 0);
            }

            string[] defines = compilationAssembly.defines ?? Array.Empty<string>();
            string[] referencePaths = BuildWorkerReferencePaths(compilationAssembly, targetDllPath);

            // Why projectRelativePath (not workerSourcePath): contentPathOverride E2E copies live
            // under Library/UloopHotReload/TestSources/ and are absent from the PDB document list.
            // Assembly resolution already computed the on-disk Assets/Packages path above.
            string snapshotSource = HotReloadSourceBaseline.LoadVerifiedSnapshotSource(
                projectRelativePath,
                targetDllPath);

            TransformWorkerInputDto workerInput = new TransformWorkerInputDto
            {
                sourcePath = Path.GetFullPath(workerSourcePath),
                defines = defines,
                referencePaths = referencePaths,
                targetTypesAssemblyPath = Path.GetFullPath(targetDllPath),
                snapshotSource = snapshotSource,
                projectRelativePath = projectRelativePath,
                assemblySourcePaths = BuildAssemblySourcePaths(projectRoot, compilationAssembly.sourceFiles)
            };

            TransformWorkerClientResult workerResult =
                await TransformWorkerClient.RunAsync(workerInput, ct).ConfigureAwait(false);
            if (!workerResult.Success)
            {
                outcomes.Add(
                    HotReloadMethodOutcome.Failed("(file)", workerResult.ErrorMessage, assemblyResolvePath));
                return new HotReloadFileProcessResult(outcomes, warnings, 0);
            }

            TransformWorkerOutputDto workerOutput = workerResult.Output;
            // Why after the worker: const-only / empty files have no patch candidates, so the
            // missing-baseline warning was pure noise (FB E). Emit only when the worker saw at
            // least one method or accessor row.
            if (snapshotSource == null
                && CountPatchCandidateRows(workerOutput) >= 1)
            {
                warnings.Add(
                    string.Format(
                        HotReloadConstants.NoVerifiedSourceSnapshotWarningFormat,
                        Path.GetFileName(projectRelativePath),
                        assemblyName));
            }

            if (workerOutput.baselineDisabledByDuplicateKeys)
            {
                warnings.Add(
                    string.Format(
                        HotReloadConstants.BaselineDisabledByDuplicateKeysWarningFormat,
                        Path.GetFileName(projectRelativePath),
                        assemblyName));
            }

            if (workerOutput.parseErrors != null)
            {
                foreach (string parseError in workerOutput.parseErrors)
                {
                    warnings.Add(parseError);
                }
            }

            if (workerOutput.skipped != null)
            {
                foreach (TransformWorkerSkippedDto skipped in workerOutput.skipped)
                {
                    outcomes.Add(
                        HotReloadMethodOutcome.Skipped(
                            skipped.method ?? "(unknown)",
                            skipped.reason ?? string.Empty,
                            assemblyResolvePath));
                }
            }

            if (workerOutput.declarationDriftWarnings != null)
            {
                // Surfaced before the empty-entries early return so const drift still reaches
                // the response when every method in the file is skipped or unchanged.
                foreach (string driftWarning in workerOutput.declarationDriftWarnings)
                {
                    warnings.Add(driftWarning);
                }
            }

            if (workerOutput.removedMembers != null && workerOutput.removedMembers.Length > 0)
            {
                warnings.Add(FormatRemovedMembersWarning(workerOutput.removedMembers));
            }

            TransformWorkerUnchangedMethodDto[] unchangedMethods =
                workerOutput.unchangedMethods ?? Array.Empty<TransformWorkerUnchangedMethodDto>();
            int unchangedMethodCount = unchangedMethods.Length;

            // Why before the empty-entries return: all-unchanged runs exit there, and those are
            // exactly the runs that must peel leftover patches so behavior converges to compiled IL.
            await MainThreadSwitcher.SwitchToMainThread(ct);
            RevertUnchangedPatches(assemblyName, unchangedMethods);

            if (string.IsNullOrEmpty(workerOutput.shimSource)
                || workerOutput.entries == null
                || workerOutput.entries.Length == 0)
            {
                return new HotReloadFileProcessResult(
                    outcomes, warnings, 0, unchangedMethodCount: unchangedMethodCount);
            }

            // BuildShimReferencePaths reads Application.dataPath / platform; stay on main thread.
            await MainThreadSwitcher.SwitchToMainThread(ct);
            bool includeHarmonyReference = NeedsHarmonyReference(workerOutput);
            ShimReferencePathsResult shimReferencePaths = TryBuildShimReferencePaths(
                compilationAssembly,
                targetDllPath,
                includeHarmonyReference);
            if (shimReferencePaths.ErrorMessage != null)
            {
                outcomes.Add(
                    HotReloadMethodOutcome.Failed(
                        "(file)",
                        shimReferencePaths.ErrorMessage,
                        assemblyResolvePath));
                return new HotReloadFileProcessResult(
                    outcomes, warnings, 0, unchangedMethodCount: unchangedMethodCount);
            }

            List<string> shimReferences = shimReferencePaths.References;
            HotReloadShimCompileResult compileResult = await HotReloadShimCompiler.CompileAndLoadAsync(
                workerOutput.shimSource,
                shimReferences,
                defines,
                projectRelativePath,
                ct).ConfigureAwait(false);

            TransformWorkerEntryDto[] entriesToPatch = workerOutput.entries;
            if (!compileResult.Success)
            {
                HotReloadShimIsolationResult isolation = await TryIsolateShimCompileFailureAsync(
                    workerInput,
                    workerOutput,
                    compileResult,
                    compilationAssembly,
                    targetDllPath,
                    defines,
                    assemblyResolvePath,
                    ct).ConfigureAwait(false);
                if (isolation == null)
                {
                    // Why: CompileAndLoadAsync appends "(line N)" only for diagnostics whose
                    // #line-mapped file matches projectRelativePath — scaffold-only errors stay
                    // bare. Single-entry failures skip isolation and always take this path.
                    // Why attribute single-entry failures: "(shim-compile)" hides which method
                    // body failed when the agent edited only one method.
                    string failureMethodLabel = "(shim-compile)";
                    if (entriesToPatch.Length == 1)
                    {
                        TransformWorkerEntryDto soleEntry = entriesToPatch[0];
                        failureMethodLabel = HotReloadPatcher.FormatMethodKeyParts(
                            soleEntry.typeMetadataName,
                            soleEntry.methodName,
                            soleEntry.parameterTypeFullNames ?? Array.Empty<string>(),
                            genericArity: 0);
                    }

                    outcomes.Add(
                        HotReloadMethodOutcome.Failed(
                            failureMethodLabel,
                            compileResult.ErrorMessage,
                            assemblyResolvePath));
                    return new HotReloadFileProcessResult(
                        outcomes, warnings, 0, unchangedMethodCount: unchangedMethodCount);
                }

                outcomes.AddRange(isolation.FailedMethodOutcomes);
                outcomes.AddRange(isolation.SkippedCallerOutcomes);
                if (isolation.RetryEntries.Length == 0)
                {
                    return new HotReloadFileProcessResult(
                        outcomes,
                        warnings,
                        0,
                        suppressedPausePointIds,
                        new List<string>(),
                        unchangedMethodCount,
                        retargetedPausePointIds);
                }

                entriesToPatch = isolation.RetryEntries;
                compileResult = isolation.RetryCompileResult;
            }

            // Harmony Patch/Unpatch and method resolution against loaded modules require main thread.
            await MainThreadSwitcher.SwitchToMainThread(ct);
            // Why before ApplyEntry: OnHotReloadPatchStateChanged(true) runs inside Apply and
            // pause-point retarget reads the shim registration — it must already see this
            // generation's bytes/methods.
            HotReloadShimRegistry.BeginFileGeneration(
                projectRelativePath,
                compileResult.AssemblyBytes,
                compileResult.PdbBytes,
                compileResult.Assembly);
            Dictionary<string, string> bindFailureReasonByShimTypeName =
                BindShimAccessors(compileResult.Assembly);
            List<string> inlineRiskMethodLabels = new List<string>();
            int patchedCount = 0;
            entriesToPatch = TakePatchableEntries(entriesToPatch, outcomes, assemblyResolvePath);
            foreach (TransformWorkerEntryDto entry in entriesToPatch)
            {
                HotReloadMethodOutcome outcome = ApplyEntry(
                    entry,
                    assemblyName,
                    compileResult.Assembly,
                    bindFailureReasonByShimTypeName,
                    assemblyResolvePath,
                    projectRelativePath,
                    inlineRiskMethodLabels,
                    suppressedPausePointIds,
                    retargetedPausePointIds);
                outcomes.Add(outcome);
                if (outcome.Kind == HotReloadMethodOutcomeKind.Patched)
                {
                    patchedCount++;
                }
            }

            return new HotReloadFileProcessResult(
                outcomes,
                warnings,
                patchedCount,
                suppressedPausePointIds,
                inlineRiskMethodLabels,
                unchangedMethodCount,
                retargetedPausePointIds);
        }

        // Peels leftover Harmony patches when the source again matches the verified baseline.
        // Resolve failures are silent: unchanged identities already matched compile-time IL.
        private static void RevertUnchangedPatches(
            string assemblyName,
            TransformWorkerUnchangedMethodDto[] unchangedMethods)
        {
            Debug.Assert(!string.IsNullOrEmpty(assemblyName), "assemblyName must not be null or empty.");
            Debug.Assert(unchangedMethods != null, "unchangedMethods must not be null.");

            for (int index = 0; index < unchangedMethods.Length; index++)
            {
                TransformWorkerUnchangedMethodDto unchanged = unchangedMethods[index];
                if (unchanged == null
                    || string.IsNullOrEmpty(unchanged.typeMetadataName)
                    || string.IsNullOrEmpty(unchanged.methodName)
                    || unchanged.parameterTypeFullNames == null)
                {
                    continue;
                }

                HotReloadMethodMatchResult matchResult = HotReloadMethodMatcher.Resolve(
                    assemblyName,
                    unchanged.typeMetadataName,
                    unchanged.methodName,
                    unchanged.parameterTypeFullNames);
                if (!matchResult.Success)
                {
                    continue;
                }

                HotReloadPatcher.Revert(matchResult.Method);
            }
        }

        private static HotReloadMethodOutcome ApplyEntry(
            TransformWorkerEntryDto entry,
            string assemblyName,
            Assembly shimAssembly,
            IReadOnlyDictionary<string, string> bindFailureReasonByShimTypeName,
            string filePath,
            string projectRelativePath,
            List<string> inlineRiskMethodLabels,
            List<string> suppressedPausePointIds,
            List<string> retargetedPausePointIds)
        {
            string[] parameterTypeFullNames = entry.parameterTypeFullNames ?? Array.Empty<string>();
            // Pre-Resolve label: same shape as --status (params + nested '+' normalization).
            // After Resolve, prefer FormatMethodKey(MethodBase) so reflection ToString() wins.
            // Why genericArity 0: TransformWorkerEntryDto has no arity field; open generics are
            // rare here, and Resolve replaces this label with FormatMethodKey(MethodBase).
            string methodLabel = HotReloadPatcher.FormatMethodKeyParts(
                entry.typeMetadataName,
                entry.methodName,
                parameterTypeFullNames,
                genericArity: 0);

            // Only "delegation" selects the forwarding patch; null/empty/anything else is transplant.
            HotReloadPatchShape patchShape = entry.patchKind == HotReloadConstants.PatchKindDelegation
                ? HotReloadPatchShape.Delegation
                : HotReloadPatchShape.Transplant;

            // Transplant entries never read accessor delegates, so a sibling bind failure in the
            // same shim type must not take them down; only delegation entries depend on the bind.
            if (patchShape == HotReloadPatchShape.Delegation
                && bindFailureReasonByShimTypeName.TryGetValue(entry.shimTypeName ?? string.Empty, out string bindFailureReason))
            {
                return HotReloadMethodOutcome.Failed(methodLabel, bindFailureReason, filePath);
            }

            HotReloadMethodMatchResult matchResult = HotReloadMethodMatcher.Resolve(
                assemblyName,
                entry.typeMetadataName,
                entry.methodName,
                parameterTypeFullNames);
            if (!matchResult.Success)
            {
                return HotReloadMethodOutcome.Failed(methodLabel, matchResult.ErrorMessage, filePath);
            }

            methodLabel = HotReloadPatcher.FormatMethodKey(matchResult.Method);

            Type shimType = FindShimType(shimAssembly, entry.shimTypeName);
            if (shimType == null)
            {
                return HotReloadMethodOutcome.Failed(
                    methodLabel,
                    "Shim type not found in compiled shim assembly: " + entry.shimTypeName,
                    filePath);
            }

            MethodInfo shimMethod = shimType.GetMethod(
                entry.shimMethodName,
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
            if (shimMethod == null)
            {
                // Fall back to a broader lookup — DeclaredOnly can miss when the compiler emits
                // unexpected metadata flags, but still prefer public static.
                shimMethod = shimType.GetMethod(
                    entry.shimMethodName,
                    BindingFlags.Public | BindingFlags.Static);
            }

            if (shimMethod == null)
            {
                return HotReloadMethodOutcome.Failed(
                    methodLabel,
                    "Shim method not found: " + entry.shimTypeName + "." + entry.shimMethodName,
                    filePath);
            }

            // Why before Apply: Apply notifies OnHotReloadPatchStateChanged(true) after the
            // ledger write; registration must already expose this method's shim for retarget.
            HotReloadShimRegistry.RegisterMethod(
                projectRelativePath,
                matchResult.Method,
                new HotReloadShimRegistry.MethodEntry(
                    shimMethod,
                    patchShape == HotReloadPatchShape.Delegation,
                    entry.sourceStartLine,
                    entry.sourceEndLine));
            HotReloadPatchResult patchResult = HotReloadPatcher.Apply(
                matchResult.Method,
                shimMethod,
                patchShape,
                projectRelativePath);
            if (!patchResult.Success)
            {
                HotReloadShimRegistry.RemoveMethod(matchResult.Method);
                return HotReloadMethodOutcome.Failed(methodLabel, patchResult.ErrorMessage, filePath);
            }

            AppendPausePointTransitionIds(
                matchResult.Method,
                suppressedPausePointIds,
                retargetedPausePointIds);

            // Inline risk is flagged per method but reported as one aggregated warning so
            // Warnings stay readable when many tiny methods are patched together.
            if (patchResult.InlineRiskDetected)
            {
                inlineRiskMethodLabels.Add(methodLabel);
            }

            return HotReloadMethodOutcome.Patched(methodLabel, filePath, entry.lifecycleNote);
        }

        private static string FormatInlineRiskAggregatedWarning(
            int atRiskCount,
            int patchedTotal,
            IReadOnlyList<string> methodLabels)
        {
            return HotReloadJitInliningRisk.FormatAggregatedWarning(atRiskCount, patchedTotal, methodLabels);
        }

        // Duplicate file inputs process the same source twice, producing duplicates across
        // per-file result lists; aggregated warnings must name each pause-point id / method
        // label once even then. Methods and PatchedTotal keep reflecting raw patch
        // operations on purpose.
        private static void AppendDistinct(List<string> target, IReadOnlyList<string> additions)
        {
            foreach (string addition in additions)
            {
                if (!target.Contains(addition))
                {
                    target.Add(addition);
                }
            }
        }

        // What: after Apply (+ retarget handler), splits armed markers into retargeted vs suppressed.
        // Expired skips are recorded as a pending-drain event inside SourcePausePointPatcher and
        // surfaced from HotReloadTools.BuildApplyResponse (same pattern as line-drift warnings).
        private static void AppendPausePointTransitionIds(
            MethodBase method,
            List<string> suppressedPausePointIds,
            List<string> retargetedPausePointIds)
        {
            IReadOnlyList<string> armedIds =
                HotReloadPausePointCoordination.GetArmedMarkerIdsOnMethod?.Invoke(method);
            if (armedIds == null || armedIds.Count == 0)
            {
                return;
            }

            IReadOnlyList<string> suppressedIds =
                HotReloadPausePointCoordination.GetSuppressedMarkerIdsOnMethod?.Invoke(method)
                ?? Array.Empty<string>();

            // The same method can be patched twice in one run (duplicate file inputs,
            // re-applied edits); the aggregated warning must list each marker id once.
            foreach (string armedId in armedIds)
            {
                bool suppressed = false;
                for (int index = 0; index < suppressedIds.Count; index++)
                {
                    if (suppressedIds[index] == armedId)
                    {
                        suppressed = true;
                        break;
                    }
                }

                if (suppressed)
                {
                    if (!suppressedPausePointIds.Contains(armedId))
                    {
                        suppressedPausePointIds.Add(armedId);
                    }
                }
                else if (!retargetedPausePointIds.Contains(armedId))
                {
                    retargetedPausePointIds.Add(armedId);
                }
            }
        }

        /// <summary>
        /// Invokes each shim type's binder (emitted when the type carries at least one accessor
        /// delegate) once, before any patch is applied, so no delegation shim can run with
        /// unbound accessor delegates. Returns bind failures keyed by shim type name; every
        /// delegation entry in a failed type becomes Failed instead of being patched.
        /// Internal so tests can pin the failure contract directly — an end-to-end bind failure
        /// cannot be fabricated once shim compilation has succeeded against the same assembly.
        /// </summary>
        internal static Dictionary<string, string> BindShimAccessors(Assembly shimAssembly)
        {
            Debug.Assert(shimAssembly != null, "shimAssembly must not be null.");

            Dictionary<string, string> failureReasonByShimTypeName = new Dictionary<string, string>();
            foreach (Type shimType in shimAssembly.GetTypes())
            {
                MethodInfo bindMethod = shimType.GetMethod(
                    HotReloadConstants.ShimBindAccessorsMethodName,
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly,
                    null,
                    Type.EmptyTypes,
                    null);
                if (bindMethod == null)
                {
                    continue;
                }

                try
                {
                    bindMethod.Invoke(null, null);
                }
                catch (TargetInvocationException invocationException)
                {
                    // Approved deviation from the no-try-catch rule: a bind failure (the source
                    // references a member the compiled assembly does not have yet) is an expected
                    // per-type outcome that must fail that type's methods with a remediation hint,
                    // not crash the whole hot-reload run. Nothing is swallowed — the cause becomes
                    // the Failed reason for every affected method.
                    Exception cause = invocationException.InnerException ?? invocationException;
                    failureReasonByShimTypeName[shimType.Name] =
                        "Accessor binding failed for shim type '" + shimType.Name + "': "
                        + cause.Message + " Run 'uloop compile' and retry.";
                }
            }

            return failureReasonByShimTypeName;
        }

        private static Type FindShimType(Assembly shimAssembly, string shimTypeName)
        {
            if (string.IsNullOrEmpty(shimTypeName))
            {
                return null;
            }

            // Prefer the short-name lookup used when shims are in the global namespace; fall back
            // to scanning because production emits shims into the original type's namespace.
            Type direct = shimAssembly.GetType(shimTypeName);
            if (direct != null)
            {
                return direct;
            }

            foreach (Type candidate in shimAssembly.GetTypes())
            {
                if (candidate.Name == shimTypeName)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static UnityCompilationAssembly FindCompilationAssembly(string assemblyName)
        {
            foreach (UnityCompilationAssembly assembly in CompilationPipeline.GetAssemblies())
            {
                if (assembly.name == assemblyName)
                {
                    return assembly;
                }
            }

            return null;
        }

        // Why Path.Combine then GetFullPath: Unity Assembly.sourceFiles are project-relative
        // (slash-separated). The worker cwd is Library/UloopHotReload/Worker/<hash>/, so it
        // can only open absolute paths. Normalization matches HotReloadSourceSnapshotter.
        private static string[] BuildAssemblySourcePaths(string projectRoot, string[] sourceFiles)
        {
            if (sourceFiles == null || sourceFiles.Length == 0)
            {
                return Array.Empty<string>();
            }

            string[] paths = new string[sourceFiles.Length];
            for (int index = 0; index < sourceFiles.Length; index++)
            {
                string normalizedRelativePath = sourceFiles[index].Replace('\\', '/');
                string absoluteSourcePath = Path.Combine(
                    projectRoot,
                    normalizedRelativePath.Replace('/', Path.DirectorySeparatorChar));
                paths[index] = Path.GetFullPath(absoluteSourcePath);
            }

            return paths;
        }

        private static string CheckMvidGuard(string assemblyName, string targetDllPath)
        {
            ReaderParameters readerParameters = new ReaderParameters { InMemory = true };
            using AssemblyDefinition assemblyDefinition =
                AssemblyDefinition.ReadAssembly(targetDllPath, readerParameters);
            string compiledMvid = assemblyDefinition.MainModule.Mvid.ToString();

            foreach (Assembly loaded in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (loaded.GetName().Name != assemblyName)
                {
                    continue;
                }

                if (loaded.ManifestModule.ModuleVersionId.ToString() != compiledMvid)
                {
                    return HotReloadConstants.StaleAssemblyHint;
                }

                return null;
            }

            return HotReloadConstants.AssemblyNotLoadedHint;
        }

        private static int CountPatchCandidateRows(TransformWorkerOutputDto workerOutput)
        {
            int entryCount = workerOutput.entries != null ? workerOutput.entries.Length : 0;
            int skippedCount = workerOutput.skipped != null ? workerOutput.skipped.Length : 0;
            int unchangedCount =
                workerOutput.unchangedMethods != null ? workerOutput.unchangedMethods.Length : 0;
            return entryCount + skippedCount + unchangedCount;
        }

        private static string[] BuildWorkerReferencePaths(
            UnityCompilationAssembly compilationAssembly,
            string targetDllPath)
        {
            List<string> paths = new List<string>();
            if (compilationAssembly.allReferences != null)
            {
                foreach (string reference in compilationAssembly.allReferences)
                {
                    if (!string.IsNullOrEmpty(reference) && File.Exists(reference))
                    {
                        paths.Add(Path.GetFullPath(reference));
                    }
                }
            }

            string fullTarget = Path.GetFullPath(targetDllPath);
            bool hasTarget = false;
            foreach (string path in paths)
            {
                if (string.Equals(path, fullTarget, StringComparison.OrdinalIgnoreCase))
                {
                    hasTarget = true;
                    break;
                }
            }

            if (!hasTarget)
            {
                paths.Add(fullTarget);
            }

            return paths.ToArray();
        }

        private static bool NeedsHarmonyReference(TransformWorkerOutputDto output)
        {
            return HasDelegationEntry(output.entries) || output.hasAccessorDelegates;
        }

        private static bool HasDelegationEntry(TransformWorkerEntryDto[] entries)
        {
            if (entries == null)
            {
                return false;
            }

            foreach (TransformWorkerEntryDto entry in entries)
            {
                if (entry.patchKind == HotReloadConstants.PatchKindDelegation)
                {
                    return true;
                }
            }

            return false;
        }

        // Why not ApplyEntry: added methods are absent from the compiled assembly, so Resolve
        // always fails. PR-2 replaces this Skipped outcome with Added.
        private static TransformWorkerEntryDto[] TakePatchableEntries(
            TransformWorkerEntryDto[] entries,
            List<HotReloadMethodOutcome> outcomes,
            string filePath)
        {
            if (entries == null || entries.Length == 0)
            {
                return Array.Empty<TransformWorkerEntryDto>();
            }

            List<TransformWorkerEntryDto> patchable = new List<TransformWorkerEntryDto>();
            foreach (TransformWorkerEntryDto entry in entries)
            {
                if (entry.patchKind == HotReloadConstants.PatchKindAddedMethod)
                {
                    string[] parameterTypeFullNames = entry.parameterTypeFullNames ?? Array.Empty<string>();
                    string methodLabel = HotReloadPatcher.FormatMethodKeyParts(
                        entry.typeMetadataName,
                        entry.methodName,
                        parameterTypeFullNames,
                        genericArity: 0);
                    outcomes.Add(
                        HotReloadMethodOutcome.Skipped(
                            methodLabel,
                            HotReloadConstants.AddedMethodDeferredSkipReason,
                            filePath));
                    continue;
                }

                patchable.Add(entry);
            }

            return patchable.ToArray();
        }

        private static string FormatRemovedMembersWarning(TransformWorkerRemovedMemberDto[] removedMembers)
        {
            List<string> names = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (TransformWorkerRemovedMemberDto removed in removedMembers)
            {
                if (removed == null || string.IsNullOrEmpty(removed.name) || !seen.Add(removed.name))
                {
                    continue;
                }

                names.Add(removed.name);
            }

            return string.Format(
                HotReloadConstants.RemovedMembersWarningFormat,
                string.Join(", ", names));
        }

        // Keep in sync with TransformWorkerProgram.BuildMethodKey (out-of-process worker side).
        private static string BuildMethodKey(TransformWorkerEntryDto entry)
        {
            return entry.typeMetadataName + "::" + entry.methodName + "("
                + string.Join(",", entry.parameterTypeFullNames ?? Array.Empty<string>()) + ")";
        }

        /// <summary>
        /// Retries the failed shim compile once, excluding the method(s) whose compiler errors can
        /// be attributed to them, so the rest of the file's methods can still patch. Returns null
        /// when isolation is not possible (unattributable errors, all/none of the entries failing,
        /// the retry worker run failing, or the retry compile failing) — the caller then falls back
        /// to a single Failed outcome (method-attributed when only one entry remains).
        /// </summary>
        private static async Task<HotReloadShimIsolationResult> TryIsolateShimCompileFailureAsync(
            TransformWorkerInputDto workerInput,
            TransformWorkerOutputDto workerOutput,
            HotReloadShimCompileResult compileResult,
            UnityCompilationAssembly compilationAssembly,
            string targetDllPath,
            string[] defines,
            string assemblyResolvePath,
            CancellationToken ct)
        {
            if (compileResult.Errors.Count == 0)
            {
                return null;
            }

            ShimCompileErrorAttribution attribution = AttributeErrorsToEntries(
                workerOutput.entries,
                compileResult.Errors,
                workerInput.projectRelativePath);
            if (attribution == null
                || attribution.FailedEntries.Count == 0
                || attribution.FailedEntries.Count == workerOutput.entries.Length)
            {
                // Unattributable errors (header/binder/using-level / scaffold path), or isolating
                // everyone / no one would not narrow the failure at all.
                return null;
            }

            List<HotReloadMethodOutcome> failedMethodOutcomes =
                BuildFailedMethodOutcomes(attribution, assemblyResolvePath);
            IsolationExclusions exclusions = BuildIsolationExclusions(
                attribution.FailedEntries,
                workerOutput.entries);
            List<HotReloadMethodOutcome> skippedCallerOutcomes = BuildSkippedCallerOutcomes(
                exclusions.CallerEntries,
                assemblyResolvePath);

            return await RunIsolationRetryAsync(
                workerInput,
                exclusions,
                failedMethodOutcomes,
                skippedCallerOutcomes,
                compilationAssembly,
                targetDllPath,
                defines,
                ct).ConfigureAwait(false);
        }

        private static async Task<HotReloadShimIsolationResult> RunIsolationRetryAsync(
            TransformWorkerInputDto workerInput,
            IsolationExclusions exclusions,
            List<HotReloadMethodOutcome> failedMethodOutcomes,
            List<HotReloadMethodOutcome> skippedCallerOutcomes,
            UnityCompilationAssembly compilationAssembly,
            string targetDllPath,
            string[] defines,
            CancellationToken ct)
        {
            TransformWorkerInputDto retryInput = new TransformWorkerInputDto
            {
                sourcePath = workerInput.sourcePath,
                defines = workerInput.defines,
                referencePaths = workerInput.referencePaths,
                targetTypesAssemblyPath = workerInput.targetTypesAssemblyPath,
                excludedMethodKeys = exclusions.ExcludedMethodKeys,
                excludedAddedMethodKeys = exclusions.ExcludedAddedMethodKeys,
                // Why copy: omitting snapshotSource would make the retry patch unedited methods
                // again and diverge the retry entries set from the first-pass isolation baseline.
                snapshotSource = workerInput.snapshotSource,
                projectRelativePath = workerInput.projectRelativePath,
                assemblySourcePaths = workerInput.assemblySourcePaths
            };

            TransformWorkerClientResult retryWorkerResult =
                await TransformWorkerClient.RunAsync(retryInput, ct).ConfigureAwait(false);
            if (!retryWorkerResult.Success)
            {
                return null;
            }

            // The first run already surfaced parseErrors / skipped / drift warnings; consuming
            // them again would duplicate every per-file report.
            TransformWorkerOutputDto retryOutput = retryWorkerResult.Output;
            if (string.IsNullOrEmpty(retryOutput.shimSource) || retryOutput.entries.Length == 0)
            {
                return new HotReloadShimIsolationResult(
                    failedMethodOutcomes,
                    skippedCallerOutcomes,
                    Array.Empty<TransformWorkerEntryDto>(),
                    null);
            }

            await MainThreadSwitcher.SwitchToMainThread(ct);
            bool includeHarmonyReference = NeedsHarmonyReference(retryOutput);
            ShimReferencePathsResult shimReferencePaths = TryBuildShimReferencePaths(
                compilationAssembly,
                targetDllPath,
                includeHarmonyReference);
            if (shimReferencePaths.ErrorMessage != null)
            {
                // First-pass publicize already succeeded, so a miss here is rare; abandon
                // isolation the same way as a retry compile failure.
                return null;
            }

            List<string> shimReferences = shimReferencePaths.References;
            HotReloadShimCompileResult retryCompileResult = await HotReloadShimCompiler.CompileAndLoadAsync(
                retryOutput.shimSource,
                shimReferences,
                defines,
                workerInput.projectRelativePath,
                ct).ConfigureAwait(false);
            if (!retryCompileResult.Success)
            {
                return null;
            }

            return new HotReloadShimIsolationResult(
                failedMethodOutcomes,
                skippedCallerOutcomes,
                retryOutput.entries,
                retryCompileResult);
        }

        private static List<HotReloadMethodOutcome> BuildFailedMethodOutcomes(
            ShimCompileErrorAttribution attribution,
            string assemblyResolvePath)
        {
            List<HotReloadMethodOutcome> failedMethodOutcomes = new List<HotReloadMethodOutcome>();
            foreach (TransformWorkerEntryDto failedEntry in attribution.FailedEntries)
            {
                // Why genericArity 0: TransformWorkerEntryDto has no arity field (see ApplyEntry).
                string methodLabel = HotReloadPatcher.FormatMethodKeyParts(
                    failedEntry.typeMetadataName,
                    failedEntry.methodName,
                    failedEntry.parameterTypeFullNames ?? Array.Empty<string>(),
                    genericArity: 0);
                List<string> entryErrorMessages = attribution.ErrorMessagesByEntry[failedEntry];
                failedMethodOutcomes.Add(
                    HotReloadMethodOutcome.Failed(
                        methodLabel,
                        HotReloadShimCompiler.ComposeShimCompileFailureMessage(entryErrorMessages),
                        assemblyResolvePath));
            }

            return failedMethodOutcomes;
        }

        private static IsolationExclusions BuildIsolationExclusions(
            IReadOnlyList<TransformWorkerEntryDto> failedEntries,
            TransformWorkerEntryDto[] allEntries)
        {
            HashSet<string> excludedKeys = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> excludedAddedMethodKeys = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> failedAddedMethodKeys = new HashSet<string>(StringComparer.Ordinal);
            List<TransformWorkerEntryDto> excludedCallerEntries = new List<TransformWorkerEntryDto>();
            foreach (TransformWorkerEntryDto failedEntry in failedEntries)
            {
                string methodKey = BuildMethodKey(failedEntry);
                if (failedEntry.patchKind == HotReloadConstants.PatchKindAddedMethod)
                {
                    // Why a separate set: dropping a healthy added shim via excludedMethodKeys
                    // leaves remaining callers with CS0103 (G1). A broken added body must still
                    // be excluded together with its callers so retry does not re-emit it.
                    failedAddedMethodKeys.Add(methodKey);
                    excludedAddedMethodKeys.Add(methodKey);
                    continue;
                }

                excludedKeys.Add(methodKey);
            }

            HashSet<string> failedEntryKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (TransformWorkerEntryDto failedEntry in failedEntries)
            {
                failedEntryKeys.Add(BuildMethodKey(failedEntry));
            }

            if (failedAddedMethodKeys.Count > 0 && allEntries != null)
            {
                foreach (TransformWorkerEntryDto entry in allEntries)
                {
                    if (entry.calledAddedMethodKeys == null)
                    {
                        continue;
                    }

                    string callerKey = BuildMethodKey(entry);
                    if (failedEntryKeys.Contains(callerKey))
                    {
                        continue;
                    }

                    bool callsFailedAdded = false;
                    foreach (string calledKey in entry.calledAddedMethodKeys)
                    {
                        if (failedAddedMethodKeys.Contains(calledKey))
                        {
                            callsFailedAdded = true;
                            break;
                        }
                    }

                    if (!callsFailedAdded)
                    {
                        continue;
                    }

                    excludedCallerEntries.Add(entry);
                    if (entry.patchKind == HotReloadConstants.PatchKindAddedMethod)
                    {
                        excludedAddedMethodKeys.Add(callerKey);
                    }
                    else
                    {
                        excludedKeys.Add(callerKey);
                    }
                }
            }

            string[] excludedMethodKeys = new string[excludedKeys.Count];
            excludedKeys.CopyTo(excludedMethodKeys);
            string[] excludedAddedKeys = new string[excludedAddedMethodKeys.Count];
            excludedAddedMethodKeys.CopyTo(excludedAddedKeys);
            return new IsolationExclusions(
                excludedMethodKeys,
                excludedAddedKeys,
                excludedCallerEntries);
        }

        private static List<HotReloadMethodOutcome> BuildSkippedCallerOutcomes(
            IReadOnlyList<TransformWorkerEntryDto> callerEntries,
            string assemblyResolvePath)
        {
            List<HotReloadMethodOutcome> skippedCallerOutcomes = new List<HotReloadMethodOutcome>();
            foreach (TransformWorkerEntryDto caller in callerEntries)
            {
                string methodLabel = HotReloadPatcher.FormatMethodKeyParts(
                    caller.typeMetadataName,
                    caller.methodName,
                    caller.parameterTypeFullNames ?? Array.Empty<string>(),
                    genericArity: 0);
                skippedCallerOutcomes.Add(
                    HotReloadMethodOutcome.Skipped(
                        methodLabel,
                        HotReloadConstants.IsolatedAddedMethodCallerSkipReason,
                        assemblyResolvePath));
            }

            return skippedCallerOutcomes;
        }

        /// <summary>
        /// Maps each shim compile error to the entry whose original-source [sourceStartLine,
        /// sourceEndLine] contains its #line-mapped location in the same user file. Returns null
        /// if any error is unattributable (wrong/empty file, scaffold path, or outside every
        /// entry range) — method isolation cannot fix those.
        /// </summary>
        private static ShimCompileErrorAttribution AttributeErrorsToEntries(
            TransformWorkerEntryDto[] entries,
            IReadOnlyList<HotReloadShimCompileError> errors,
            string projectRelativePath)
        {
            Dictionary<TransformWorkerEntryDto, List<string>> errorMessagesByEntry =
                new Dictionary<TransformWorkerEntryDto, List<string>>();
            foreach (HotReloadShimCompileError error in errors)
            {
                TransformWorkerEntryDto matchedEntry =
                    FindEntryForError(entries, error, projectRelativePath);
                if (matchedEntry == null)
                {
                    return null;
                }

                if (!errorMessagesByEntry.TryGetValue(matchedEntry, out List<string> messages))
                {
                    messages = new List<string>();
                    errorMessagesByEntry[matchedEntry] = messages;
                }

                // Why append the mapped line: ComposeShimCompileFailureMessage must still see the
                // "CSxxxx:" prefix for hint matching, while Failed outcomes need the original-file
                // line visible to the caller.
                messages.Add(error.Message + " (line " + error.Line + ")");
            }

            return new ShimCompileErrorAttribution(errorMessagesByEntry);
        }

        private static TransformWorkerEntryDto FindEntryForError(
            TransformWorkerEntryDto[] entries,
            HotReloadShimCompileError error,
            string projectRelativePath)
        {
            if (string.IsNullOrEmpty(error.File) || string.IsNullOrEmpty(projectRelativePath))
            {
                return null;
            }

            // Why suffix-tolerant compare (not ordinal equality): the three shim-compile backends
            // report file as #line literal, absolute path, or temp scaffold path depending on the
            // fallback stage — HotReloadSourcePathNormalizer already encodes that contract.
            if (!HotReloadSourcePathNormalizer.PathsReferToSameFile(error.File, projectRelativePath))
            {
                return null;
            }

            // Why reject multi-match: original-source ranges can share a line (unlike the old
            // shim-source ranges, which were structurally disjoint). First-match would exclude the
            // wrong method on isolation retry — treat ambiguity as unattributable.
            TransformWorkerEntryDto matchedEntry = null;
            int matchCount = 0;
            foreach (TransformWorkerEntryDto entry in entries)
            {
                bool hasKnownRange = entry.sourceStartLine > 0 && entry.sourceEndLine > 0;
                if (hasKnownRange && error.Line >= entry.sourceStartLine && error.Line <= entry.sourceEndLine)
                {
                    matchedEntry = entry;
                    matchCount++;
                    if (matchCount > 1)
                    {
                        return null;
                    }
                }
            }

            return matchedEntry;
        }

        private sealed class ShimCompileErrorAttribution
        {
            public IReadOnlyDictionary<TransformWorkerEntryDto, List<string>> ErrorMessagesByEntry { get; }
            public IReadOnlyList<TransformWorkerEntryDto> FailedEntries { get; }

            public ShimCompileErrorAttribution(Dictionary<TransformWorkerEntryDto, List<string>> errorMessagesByEntry)
            {
                ErrorMessagesByEntry = errorMessagesByEntry;
                FailedEntries = new List<TransformWorkerEntryDto>(errorMessagesByEntry.Keys);
            }
        }

        private sealed class IsolationExclusions
        {
            public string[] ExcludedMethodKeys { get; }
            public string[] ExcludedAddedMethodKeys { get; }
            public IReadOnlyList<TransformWorkerEntryDto> CallerEntries { get; }

            public IsolationExclusions(
                string[] excludedMethodKeys,
                string[] excludedAddedMethodKeys,
                IReadOnlyList<TransformWorkerEntryDto> callerEntries)
            {
                ExcludedMethodKeys = excludedMethodKeys;
                ExcludedAddedMethodKeys = excludedAddedMethodKeys;
                CallerEntries = callerEntries;
            }
        }

        /// <summary>
        /// Outcome of <see cref="TryIsolateShimCompileFailureAsync"/>. <see cref="RetryEntries"/>
        /// empty means the retry worker run produced nothing to patch (still a valid, non-null
        /// isolation — only <see cref="FailedMethodOutcomes"/> apply).
        /// </summary>
        private sealed class HotReloadShimIsolationResult
        {
            public List<HotReloadMethodOutcome> FailedMethodOutcomes { get; }
            public List<HotReloadMethodOutcome> SkippedCallerOutcomes { get; }
            public TransformWorkerEntryDto[] RetryEntries { get; }
            public HotReloadShimCompileResult RetryCompileResult { get; }

            public HotReloadShimIsolationResult(
                List<HotReloadMethodOutcome> failedMethodOutcomes,
                List<HotReloadMethodOutcome> skippedCallerOutcomes,
                TransformWorkerEntryDto[] retryEntries,
                HotReloadShimCompileResult retryCompileResult)
            {
                Debug.Assert(skippedCallerOutcomes != null, "skippedCallerOutcomes must not be null.");
                FailedMethodOutcomes = failedMethodOutcomes;
                SkippedCallerOutcomes = skippedCallerOutcomes;
                RetryEntries = retryEntries;
                RetryCompileResult = retryCompileResult;
            }
        }

        /// <summary>
        /// Result of <see cref="TryBuildShimReferencePaths"/>. Exactly one of
        /// <see cref="References"/> or <see cref="ErrorMessage"/> is set.
        /// </summary>
        private sealed class ShimReferencePathsResult
        {
            public List<string> References { get; }
            public string ErrorMessage { get; }

            public ShimReferencePathsResult(List<string> references, string errorMessage)
            {
                References = references;
                ErrorMessage = errorMessage;
            }
        }

        /// <summary>
        /// Builds shim compile references, converting Cecil assembly-resolution failures into an
        /// error message instead of letting them escape as UNITY_RPC_ERROR.
        /// </summary>
        private static ShimReferencePathsResult TryBuildShimReferencePaths(
            UnityCompilationAssembly compilationAssembly,
            string targetDllPath,
            bool includeHarmonyReference)
        {
            // Why catch only AssemblyResolutionException: publicize fails when Cecil cannot
            // resolve engine/netstandard types during Write; that is a per-file hot-reload
            // outcome, not an internal tool crash. Other exceptions must still Fail Fast.
            try
            {
                return new ShimReferencePathsResult(
                    BuildShimReferencePaths(
                        compilationAssembly,
                        targetDllPath,
                        includeHarmonyReference),
                    null);
            }
            catch (AssemblyResolutionException resolutionException)
            {
                return new ShimReferencePathsResult(
                    null,
                    "Publicizing referenced assemblies failed: " + resolutionException.Message
                    + " Hot reload could not build shim references for this file.");
            }
        }

        /// <summary>
        /// Publicize ScriptAssemblies references; leave engine/system DLLs untouched. Never include
        /// the original (non-publicized) target assembly. Harmony is added when the worker
        /// emitted a delegation entry or accessor delegates (addedMethod entries can need them).
        /// </summary>
        private static List<string> BuildShimReferencePaths(
            UnityCompilationAssembly compilationAssembly,
            string targetDllPath,
            bool includeHarmonyReference)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string scriptAssembliesDirectory = Path.GetFullPath(
                Path.Combine(projectRoot, HotReloadConstants.ScriptAssembliesRelativeDirectory));

            // Derive Cecil search dirs from Unity's actual compile references so publicize
            // resolves netstandard/engine modules without hardcoding Editor Contents layouts.
            IReadOnlyCollection<string> resolverSearchDirectories =
                ReferencePublicizer.CollectResolverSearchDirectories(compilationAssembly.allReferences);

            List<string> references = new List<string>();
            string publicizedTarget = ReferencePublicizer.GetOrCreatePublicizedCopy(
                targetDllPath,
                resolverSearchDirectories);
            references.Add(publicizedTarget);

            if (includeHarmonyReference)
            {
                references.Add(typeof(Harmony).Assembly.Location);
            }

            if (compilationAssembly.allReferences == null)
            {
                return references;
            }

            string fullTarget = Path.GetFullPath(targetDllPath);
            foreach (string reference in compilationAssembly.allReferences)
            {
                if (string.IsNullOrEmpty(reference) || !File.Exists(reference))
                {
                    continue;
                }

                string fullReference = Path.GetFullPath(reference);
                if (string.Equals(fullReference, fullTarget, StringComparison.OrdinalIgnoreCase))
                {
                    // Replaced by the publicized copy above.
                    continue;
                }

                string referenceFileName = Path.GetFileNameWithoutExtension(fullReference);
                if (IsUnderDirectory(fullReference, scriptAssembliesDirectory)
                    && HotReloadConstants.IsPublicizableProjectAssemblyFileName(referenceFileName))
                {
                    references.Add(
                        ReferencePublicizer.GetOrCreatePublicizedCopy(
                            fullReference,
                            resolverSearchDirectories));
                }
                else
                {
                    references.Add(fullReference);
                }
            }

            return references;
        }

        private static bool IsUnderDirectory(string fullPath, string directoryPath)
        {
            string normalizedPath = fullPath.Replace('\\', '/');
            string normalizedDirectory = directoryPath.Replace('\\', '/');
            StringComparison comparison = Application.platform == RuntimePlatform.WindowsEditor
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return normalizedPath.StartsWith(normalizedDirectory + "/", comparison);
        }

        private static string ToProjectRelativeScriptPath(string path)
        {
            Debug.Assert(!string.IsNullOrEmpty(path), "path must not be empty.");
            string normalized = path.Replace('\\', '/');
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/');
            if (!projectRoot.EndsWith("/", StringComparison.Ordinal))
            {
                projectRoot += "/";
            }

            string fullPath = Path.GetFullPath(path).Replace('\\', '/');
            StringComparison comparison = Application.platform == RuntimePlatform.WindowsEditor
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (fullPath.StartsWith(projectRoot, comparison))
            {
                return fullPath.Substring(projectRoot.Length);
            }

            // Already project-relative (Assets/... or Packages/...).
            return normalized;
        }

        private sealed class HotReloadFileProcessResult
        {
            public List<HotReloadMethodOutcome> Outcomes { get; }
            public List<string> Warnings { get; }
            public int PatchedCount { get; }
            public List<string> SuppressedPausePointIds { get; }
            public List<string> RetargetedPausePointIds { get; }
            public List<string> InlineRiskMethodLabels { get; }
            public int UnchangedMethodCount { get; }

            public HotReloadFileProcessResult(
                List<HotReloadMethodOutcome> outcomes,
                List<string> warnings,
                int patchedCount,
                List<string> suppressedPausePointIds = null,
                List<string> inlineRiskMethodLabels = null,
                int unchangedMethodCount = 0,
                List<string> retargetedPausePointIds = null)
            {
                Outcomes = outcomes;
                Warnings = warnings;
                PatchedCount = patchedCount;
                SuppressedPausePointIds = suppressedPausePointIds ?? new List<string>();
                InlineRiskMethodLabels = inlineRiskMethodLabels ?? new List<string>();
                UnchangedMethodCount = unchangedMethodCount;
                RetargetedPausePointIds = retargetedPausePointIds ?? new List<string>();
            }
        }
    }
}
