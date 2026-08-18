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
        /// <paramref name="contentPathOverrides"/> is the per-file form of that hook.
        /// </summary>
        public static async Task<HotReloadOrchestratorResult> RunAsync(
            IReadOnlyList<string> files,
            string contentPathOverride,
            CancellationToken ct,
            IReadOnlyList<string> contentPathOverrides = null)
        {
            Debug.Assert(files != null, "files must not be null.");
            Debug.Assert(files.Count > 0, "files must not be empty.");

            List<HotReloadMethodOutcome> outcomes = new List<HotReloadMethodOutcome>();
            List<string> warnings = new List<string>();
            List<string> suppressedPausePointIds = new List<string>();
            List<string> retargetedPausePointIds = new List<string>();
            List<string> inlineRiskMethodLabels = new List<string>();
            List<string> addedFields = new List<string>();
            int patchedTotal = 0;
            int unchangedTotal = 0;
            // Why after the file loop (not inside ProcessFileAsync): duplicate paths in one
            // run must still apply twice; recording mid-run would short-circuit the second copy.
            Dictionary<string, (string Hash, bool IsFullyApplied)> appliedSourceHashByPath =
                new Dictionary<string, (string Hash, bool IsFullyApplied)>(StringComparer.Ordinal);

            for (int index = 0; index < files.Count; index++)
            {
                ct.ThrowIfCancellationRequested();
                string filePath = files[index];
                string workerSourcePath = ResolveWorkerSourcePath(
                    filePath,
                    contentPathOverride,
                    contentPathOverrides,
                    index);

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
                addedFields.AddRange(fileResult.AddedFieldNames);
                StageAppliedSourceHash(
                    appliedSourceHashByPath,
                    ToProjectRelativeScriptPath(filePath),
                    fileResult.SourceContentSha256,
                    fileResult.Outcomes);
            }

            foreach (KeyValuePair<string, (string Hash, bool IsFullyApplied)> pair in appliedSourceHashByPath)
            {
                HotReloadAppliedSourceLedger.Record(pair.Key, pair.Value.Hash, pair.Value.IsFullyApplied);
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
            addedFields.Sort(StringComparer.Ordinal);
            return new HotReloadOrchestratorResult(
                outcomes,
                warnings,
                patchedTotal,
                HotReloadPatcher.ActiveChangeCount,
                suppressedPausePointIds,
                unchangedTotal,
                retargetedPausePointIds,
                addedFields.ToArray());
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

            HotReloadUnchangedSourceDecision unchangedDecision = TryShortCircuitUnchangedAppliedSource(
                workerSourcePath,
                projectRelativePath,
                assemblyResolvePath,
                outcomes);
            if (unchangedDecision == HotReloadUnchangedSourceDecision.ShortCircuited)
            {
                return new HotReloadFileProcessResult(outcomes, warnings, 0);
            }

            if (unchangedDecision == HotReloadUnchangedSourceDecision.ReapplyNonBaseline)
            {
                warnings.Add(
                    string.Format(
                        HotReloadConstants.UnchangedSourceNonBaselineWarningFormat,
                        projectRelativePath));
            }

            // Why snapshot at this file's apply entry: multi-file runs process files
            // sequentially, and RevertUnchangedPatches / BeginFileGeneration mutate ledgers
            // after the worker. The worker itself does not.
            HashSet<string> snapshotLabels = CollectActiveLabelsForFile(projectRelativePath);

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
            string[] addedFieldNames = workerOutput.addedFieldNames;
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

            TransformWorkerUnchangedMethodDto[] unchangedMethods =
                workerOutput.unchangedMethods ?? Array.Empty<TransformWorkerUnchangedMethodDto>();
            int unchangedMethodCount = unchangedMethods.Length;

            // Why before the empty-entries return: all-unchanged runs exit there, and those are
            // exactly the runs that must peel leftover patches so behavior converges to compiled IL.
            await MainThreadSwitcher.SwitchToMainThread(ct);
            RevertUnchangedPatches(assemblyName, unchangedMethods);

            SignatureChangeGateResult gateResult = await TryApplySignatureChangeGateAsync(
                projectRoot,
                assemblyName,
                workerInput,
                workerOutput,
                compilationAssembly,
                targetDllPath,
                defines,
                assemblyResolvePath,
                projectRelativePath,
                ct).ConfigureAwait(false);
            // Why after the gate: a gated replacement is not applied, so listing it under
            // "Removed members stay present... edited bodies no longer call them" is false.
            string removedMembersWarning = FormatRemovedMembersWarning(
                workerOutput.removedMembers,
                workerOutput.removedMethodSignatures,
                gateResult.GatedReplacementMethodKeys);
            if (removedMembersWarning != null)
            {
                warnings.Add(removedMembersWarning);
            }

            if (gateResult.FileFailed)
            {
                // Why not apply first-pass entries: a gate retry null means the replacement was
                // not isolated. Falling through would apply the unguarded return-type change.
                outcomes.Add(
                    HotReloadMethodOutcome.Failed(
                        "(signature-change-gate)",
                        gateResult.FailureMessage,
                        assemblyResolvePath));
                return new HotReloadFileProcessResult(
                    outcomes,
                    warnings,
                    0,
                    unchangedMethodCount: unchangedMethodCount,
                    sourceContentSha256: workerOutput.sourceContentSha256);
            }

            outcomes.AddRange(gateResult.SkippedOutcomes);
            warnings.AddRange(gateResult.Warnings);

            TransformWorkerEntryDto[] entriesToPatch;
            HotReloadShimCompileResult compileResult;
            if (gateResult.UsedWorkerRetry)
            {
                addedFieldNames = gateResult.Isolation.AddedFieldNames;
                if (gateResult.Isolation.RetryEntries.Length == 0)
                {
                    return new HotReloadFileProcessResult(
                        outcomes,
                        warnings,
                        0,
                        suppressedPausePointIds,
                        new List<string>(),
                        unchangedMethodCount,
                        retargetedPausePointIds,
                        addedFieldNames: null,
                        sourceContentSha256: workerOutput.sourceContentSha256);
                }

                entriesToPatch = gateResult.Isolation.RetryEntries;
                compileResult = gateResult.Isolation.RetryCompileResult;
            }
            else if (string.IsNullOrEmpty(workerOutput.shimSource)
                || workerOutput.entries == null
                || workerOutput.entries.Length == 0)
            {
                // Why only on this success path: deleting an added method and restoring callers
                // yields empty entries, so the post-shim-compile BeginFileGeneration never runs.
                // Worker failure and shim-compile failure return earlier or later without
                // clearing — same as leaving existing Harmony patches in place when apply does
                // not succeed.
                HotReloadAddedMemberRegistry.BeginFileGeneration(projectRelativePath);
                return new HotReloadFileProcessResult(
                    outcomes,
                    warnings,
                    0,
                    unchangedMethodCount: unchangedMethodCount,
                    sourceContentSha256: workerOutput.sourceContentSha256);
            }
            else
            {
                ShimFirstCompileResult firstCompile = await CompileShimFirstPassAsync(
                    workerInput,
                    workerOutput,
                    compilationAssembly,
                    targetDllPath,
                    defines,
                    assemblyResolvePath,
                    ct).ConfigureAwait(false);
                if (firstCompile.AddedFieldNames != null)
                {
                    addedFieldNames = firstCompile.AddedFieldNames;
                }

                if (firstCompile.FileFailed)
                {
                    outcomes.AddRange(firstCompile.Outcomes);
                    return new HotReloadFileProcessResult(
                        outcomes,
                        warnings,
                        0,
                        unchangedMethodCount: unchangedMethodCount,
                        sourceContentSha256: workerOutput.sourceContentSha256);
                }

                outcomes.AddRange(firstCompile.Outcomes);
                if (firstCompile.EntriesToPatch.Length == 0)
                {
                    return new HotReloadFileProcessResult(
                        outcomes,
                        warnings,
                        0,
                        suppressedPausePointIds,
                        new List<string>(),
                        unchangedMethodCount,
                        retargetedPausePointIds,
                        addedFieldNames: null,
                        sourceContentSha256: workerOutput.sourceContentSha256);
                }

                entriesToPatch = firstCompile.EntriesToPatch;
                compileResult = firstCompile.CompileResult;
            }

            if (gateResult.DidScan)
            {
                // Why after entriesToPatch is final and before Harmony: isolation or a gate
                // retry can drop a covering caller without dropping the replacement. A third
                // worker run is not allowed (max two); fail the file instead of applying.
                List<string> lostReplacementKeys = FindSignatureChangeCoverageLosses(
                    entriesToPatch,
                    gateResult.Hits,
                    gateResult.ScanTargetKeys);
                if (lostReplacementKeys.Count > 0)
                {
                    outcomes.Add(
                        HotReloadMethodOutcome.Failed(
                            "(signature-change-gate)",
                            string.Format(
                                HotReloadConstants.SignatureChangeCoverageLostFailureFormat,
                                string.Join(", ", lostReplacementKeys)),
                            assemblyResolvePath));
                    return new HotReloadFileProcessResult(
                        outcomes,
                        warnings,
                        0,
                        unchangedMethodCount: unchangedMethodCount,
                        sourceContentSha256: workerOutput.sourceContentSha256);
                }
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
            HotReloadAddedMemberRegistry.BeginFileGeneration(projectRelativePath);
            Dictionary<string, string> bindFailureReasonByShimTypeName =
                BindShimAccessors(compileResult.Assembly);
            List<string> inlineRiskMethodLabels = new List<string>();
            int patchedCount = 0;
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

            // Why only this return: a still-declared added method is a first-pass entry, so
            // empty-entries BeginFileGeneration only clears members the source no longer
            // declares. The warning is only meaningful on the apply path that can drop a
            // still-declared added member by not re-Registering it.
            AppendDeactivatedPatchesWarning(
                warnings,
                snapshotLabels,
                projectRelativePath,
                workerOutput,
                outcomes);
            return new HotReloadFileProcessResult(
                outcomes,
                warnings,
                patchedCount,
                suppressedPausePointIds,
                inlineRiskMethodLabels,
                unchangedMethodCount,
                retargetedPausePointIds,
                addedFieldNames,
                workerOutput.sourceContentSha256);
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

                // Why pass unchanged.genericArity: Caller(int) and Caller<T>(int) share name
                // and parameters. Arity 0 would resolve the generic unchanged row to the
                // non-generic sibling and peel its live patch.
                HotReloadMethodMatchResult matchResult = HotReloadMethodMatcher.Resolve(
                    assemblyName,
                    unchanged.typeMetadataName,
                    unchanged.methodName,
                    unchanged.parameterTypeFullNames,
                    unchanged.genericArity);
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
            string methodLabel = HotReloadPatcher.FormatMethodKeyParts(
                entry.typeMetadataName,
                entry.methodName,
                parameterTypeFullNames,
                entry.genericArity);

            if (entry.patchKind == HotReloadConstants.PatchKindAddedMethod)
            {
                return ApplyAddedMethodEntry(
                    entry,
                    methodLabel,
                    shimAssembly,
                    bindFailureReasonByShimTypeName,
                    filePath,
                    projectRelativePath);
            }

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
                parameterTypeFullNames,
                entry.genericArity);
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

            (MethodInfo shimMethod, string shimLookupError) = FindShimMethod(shimType, entry.shimMethodName);
            if (shimMethod == null)
            {
                return HotReloadMethodOutcome.Failed(methodLabel, shimLookupError, filePath);
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

        private static HotReloadMethodOutcome ApplyAddedMethodEntry(
            TransformWorkerEntryDto entry,
            string methodLabel,
            Assembly shimAssembly,
            IReadOnlyDictionary<string, string> bindFailureReasonByShimTypeName,
            string filePath,
            string projectRelativePath)
        {
            // Added methods with accessors share the shim type's __BindAccessors; a bind
            // failure leaves those delegates unbound, so the entry must not be registered.
            if (bindFailureReasonByShimTypeName.TryGetValue(
                    entry.shimTypeName ?? string.Empty,
                    out string bindFailureReason))
            {
                return HotReloadMethodOutcome.Failed(methodLabel, bindFailureReason, filePath);
            }

            Type shimType = FindShimType(shimAssembly, entry.shimTypeName);
            if (shimType == null)
            {
                return HotReloadMethodOutcome.Failed(
                    methodLabel,
                    "Shim type not found in compiled shim assembly: " + entry.shimTypeName,
                    filePath);
            }

            (MethodInfo shimMethod, string shimLookupError) = FindShimMethod(shimType, entry.shimMethodName);
            if (shimMethod == null)
            {
                return HotReloadMethodOutcome.Failed(methodLabel, shimLookupError, filePath);
            }

            HotReloadAddedMemberRegistry.Register(
                projectRelativePath,
                methodLabel,
                shimMethod,
                filePath);
            return HotReloadMethodOutcome.Added(methodLabel, filePath, entry.lifecycleNote);
        }

        private static (MethodInfo ShimMethod, string ErrorMessage) FindShimMethod(
            Type shimType,
            string shimMethodName)
        {
            MethodInfo shimMethod = shimType.GetMethod(
                shimMethodName,
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
            if (shimMethod == null)
            {
                // Fall back to a broader lookup — DeclaredOnly can miss when the compiler emits
                // unexpected metadata flags, but still prefer public static.
                shimMethod = shimType.GetMethod(
                    shimMethodName,
                    BindingFlags.Public | BindingFlags.Static);
            }

            if (shimMethod == null)
            {
                return (null, "Shim method not found: " + shimType.Name + "." + shimMethodName);
            }

            return (shimMethod, null);
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
        /// delegate) once, before any patch is applied, so no delegation shim or added-method
        /// accessor rewrite can run with unbound accessor delegates. Returns bind failures keyed
        /// by shim type name; every delegation entry and added-method entry in a failed type
        /// becomes Failed instead of being patched or registered.
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

        internal static bool NeedsAddedFieldStoreReference(TransformWorkerOutputDto output)
        {
            Debug.Assert(output != null, "output must not be null.");
            return output.hasAddedFieldRewrites;
        }

        /// <summary>
        /// Appends Harmony and/or the added-field store assembly when the worker output needs them.
        /// Visible to tests so injection can be asserted without running CompilationPipeline.
        /// </summary>
        internal static void AppendOptionalShimAssemblyReferences(
            List<string> references,
            bool includeHarmonyReference,
            bool includeAddedFieldStoreReference)
        {
            Debug.Assert(references != null, "references must not be null.");

            if (includeHarmonyReference)
            {
                AppendIfMissingByFileName(references, typeof(Harmony).Assembly.Location);
            }

            if (includeAddedFieldStoreReference)
            {
                AppendIfMissingByFileName(
                    references,
                    typeof(HotReloadAddedFieldStore).Assembly.Location);
            }
        }

        // Why filename (not full path): ToolContracts lives under ScriptAssemblies and is
        // publicized, so the list may already hold a publicized copy while Location is the raw
        // DLL. Adding both is CS1703. Harmony is a plugin outside ScriptAssemblies, so this
        // collision does not arise there, but the same skip is still correct.
        private static void AppendIfMissingByFileName(List<string> references, string assemblyPath)
        {
            Debug.Assert(!string.IsNullOrEmpty(assemblyPath), "assemblyPath must not be empty.");
            string fileName = Path.GetFileName(assemblyPath);
            for (int index = 0; index < references.Count; index++)
            {
                if (string.Equals(
                    Path.GetFileName(references[index]),
                    fileName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            references.Add(assemblyPath);
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

        private static string FormatRemovedMembersWarning(
            TransformWorkerRemovedMemberDto[] removedMembers,
            TransformWorkerRemovedMethodSignatureDto[] removedMethodSignatures,
            IReadOnlyCollection<string> gatedReplacementMethodKeys)
        {
            if (removedMembers == null || removedMembers.Length == 0)
            {
                return null;
            }

            HashSet<string> gatedKeys = new HashSet<string>(
                gatedReplacementMethodKeys ?? Array.Empty<string>(),
                StringComparer.Ordinal);
            List<string> names = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (TransformWorkerRemovedMemberDto removed in removedMembers)
            {
                if (removed == null || string.IsNullOrEmpty(removed.name) || !seen.Add(removed.name))
                {
                    continue;
                }

                if (removed.kind == HotReloadConstants.RemovedMemberKindMethod
                    && ShouldSuppressGatedRemovedMethodName(
                        removed.name,
                        removedMethodSignatures,
                        gatedKeys))
                {
                    continue;
                }

                names.Add(removed.name);
            }

            if (names.Count == 0)
            {
                return null;
            }

            return string.Format(
                HotReloadConstants.RemovedMembersWarningFormat,
                string.Join(", ", names));
        }

        // Why signature keys, not simple names: a gated replacement and a real deletion can
        // share a method name across types in the same file. Name-only suppression would
        // drop the deletion warning (fail-open).
        private static bool ShouldSuppressGatedRemovedMethodName(
            string methodName,
            TransformWorkerRemovedMethodSignatureDto[] removedMethodSignatures,
            HashSet<string> gatedReplacementMethodKeys)
        {
            if (removedMethodSignatures == null)
            {
                return false;
            }

            bool sawSignature = false;
            foreach (TransformWorkerRemovedMethodSignatureDto signature in removedMethodSignatures)
            {
                if (signature == null || signature.methodName != methodName)
                {
                    continue;
                }

                sawSignature = true;
                string signatureKey = BuildMethodKeyParts(
                    signature.typeMetadataName,
                    signature.methodName,
                    signature.parameterTypeFullNames,
                    signature.genericArity);
                if (!gatedReplacementMethodKeys.Contains(signatureKey))
                {
                    return false;
                }
            }

            return sawSignature;
        }

        // Keep in sync with TransformWorkerProgram.BuildMethodKey (out-of-process worker side)
        // and HotReloadCallSiteScanner.CreateHit.
        // Why arity suffix: Caller(int) and Caller<T>(int) must not share a wire key.
        // Arity 0 keeps the bare name so existing non-generic keys stay stable.
        private static string BuildMethodKey(TransformWorkerEntryDto entry)
        {
            return BuildMethodKeyParts(
                entry.typeMetadataName,
                entry.methodName,
                entry.parameterTypeFullNames,
                entry.genericArity);
        }

        private static string BuildMethodKeyParts(
            string typeMetadataName,
            string methodName,
            string[] parameterTypeFullNames,
            int genericArity)
        {
            string nameWithArity = methodName;
            if (genericArity > 0)
            {
                nameWithArity = methodName + "`" + genericArity.ToString();
            }

            return typeMetadataName + "::" + nameWithArity + "("
                + string.Join(",", parameterTypeFullNames ?? Array.Empty<string>()) + ")";
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
                BuildFailedMethodOutcomes(attribution, assemblyResolvePath, workerOutput.skipped);
            IsolationExclusions exclusions = BuildIsolationExclusions(
                attribution.FailedEntries,
                workerOutput.entries);
            List<HotReloadMethodOutcome> skippedCallerOutcomes = BuildSkippedCallerOutcomes(
                exclusions.CallerEntries,
                assemblyResolvePath,
                HotReloadConstants.IsolatedAddedMethodCallerSkipReason);

            IsolationRetryRunResult retry = await RunIsolationRetryAsync(
                workerInput,
                exclusions,
                failedMethodOutcomes,
                skippedCallerOutcomes,
                compilationAssembly,
                targetDllPath,
                defines,
                workerOutput.skipped,
                assemblyResolvePath,
                ct).ConfigureAwait(false);
            return retry.Isolation;
        }

        private static async Task<IsolationRetryRunResult> RunIsolationRetryAsync(
            TransformWorkerInputDto workerInput,
            IsolationExclusions exclusions,
            List<HotReloadMethodOutcome> failedMethodOutcomes,
            List<HotReloadMethodOutcome> skippedCallerOutcomes,
            UnityCompilationAssembly compilationAssembly,
            string targetDllPath,
            string[] defines,
            TransformWorkerSkippedDto[] firstPassSkipped,
            string assemblyResolvePath,
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
                return IsolationRetryRunResult.Failed(
                    "Retry worker failed: " + retryWorkerResult.ErrorMessage);
            }

            TransformWorkerOutputDto retryOutput = retryWorkerResult.Output;
            // Why drop first-pass (Method, Reason) pairs: consuming them again would duplicate
            // every per-file skip. Retry-only pairs are new — typically transitive callers of
            // excluded added methods — and must surface or the edit is applied nowhere.
            skippedCallerOutcomes.AddRange(
                CollectRetryOnlySkippedOutcomes(
                    firstPassSkipped,
                    retryOutput.skipped,
                    assemblyResolvePath));
            if (string.IsNullOrEmpty(retryOutput.shimSource) || retryOutput.entries.Length == 0)
            {
                return IsolationRetryRunResult.Succeeded(
                    new HotReloadShimIsolationResult(
                        failedMethodOutcomes,
                        skippedCallerOutcomes,
                        Array.Empty<TransformWorkerEntryDto>(),
                        null,
                        retryOutput.addedFieldNames));
            }

            await MainThreadSwitcher.SwitchToMainThread(ct);
            bool includeHarmonyReference = NeedsHarmonyReference(retryOutput);
            bool includeAddedFieldStoreReference = NeedsAddedFieldStoreReference(retryOutput);
            ShimReferencePathsResult shimReferencePaths = TryBuildShimReferencePaths(
                compilationAssembly,
                targetDllPath,
                includeHarmonyReference,
                includeAddedFieldStoreReference);
            if (shimReferencePaths.ErrorMessage != null)
            {
                // First-pass publicize already succeeded, so a miss here is rare; abandon
                // isolation the same way as a retry compile failure.
                return IsolationRetryRunResult.Failed(
                    "Retry could not build shim references: " + shimReferencePaths.ErrorMessage);
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
                return IsolationRetryRunResult.Failed(
                    "Retry shim compile failed: " + retryCompileResult.ErrorMessage);
            }

            return IsolationRetryRunResult.Succeeded(
                new HotReloadShimIsolationResult(
                    failedMethodOutcomes,
                    skippedCallerOutcomes,
                    retryOutput.entries,
                    retryCompileResult,
                    retryOutput.addedFieldNames));
        }

        /// <summary>
        /// Converts retry-worker skips that are not already in the first-pass skipped list into
        /// outcomes. Match is (Method, Reason) Ordinal equality so a method skipped for a new
        /// reason on retry still surfaces.
        /// </summary>
        private static List<HotReloadMethodOutcome> CollectRetryOnlySkippedOutcomes(
            TransformWorkerSkippedDto[] firstPassSkipped,
            TransformWorkerSkippedDto[] retrySkipped,
            string assemblyResolvePath)
        {
            List<HotReloadMethodOutcome> retryOnly = new List<HotReloadMethodOutcome>();
            if (retrySkipped == null)
            {
                return retryOnly;
            }

            TransformWorkerSkippedDto[] baseline =
                firstPassSkipped ?? Array.Empty<TransformWorkerSkippedDto>();
            foreach (TransformWorkerSkippedDto retryRow in retrySkipped)
            {
                if (FirstPassContainsSkippedPair(baseline, retryRow))
                {
                    continue;
                }

                retryOnly.Add(
                    HotReloadMethodOutcome.Skipped(
                        retryRow.method ?? "(unknown)",
                        retryRow.reason ?? string.Empty,
                        assemblyResolvePath));
            }

            return retryOnly;
        }

        private static bool FirstPassContainsSkippedPair(
            TransformWorkerSkippedDto[] firstPassSkipped,
            TransformWorkerSkippedDto retryRow)
        {
            string retryMethod = retryRow.method ?? string.Empty;
            string retryReason = retryRow.reason ?? string.Empty;
            foreach (TransformWorkerSkippedDto firstPassRow in firstPassSkipped)
            {
                string firstMethod = firstPassRow.method ?? string.Empty;
                string firstReason = firstPassRow.reason ?? string.Empty;
                if (string.Equals(firstMethod, retryMethod, StringComparison.Ordinal)
                    && string.Equals(firstReason, retryReason, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<HotReloadMethodOutcome> BuildFailedMethodOutcomes(
            ShimCompileErrorAttribution attribution,
            string assemblyResolvePath,
            TransformWorkerSkippedDto[] skipped)
        {
            List<HotReloadMethodOutcome> failedMethodOutcomes = new List<HotReloadMethodOutcome>();
            foreach (TransformWorkerEntryDto failedEntry in attribution.FailedEntries)
            {
                string methodLabel = HotReloadPatcher.FormatMethodKeyParts(
                    failedEntry.typeMetadataName,
                    failedEntry.methodName,
                    failedEntry.parameterTypeFullNames ?? Array.Empty<string>(),
                    failedEntry.genericArity);
                List<string> entryErrorMessages = attribution.ErrorMessagesByEntry[failedEntry];
                string composedMessage = HotReloadShimCompiler.ComposeShimCompileFailureMessage(entryErrorMessages);
                failedMethodOutcomes.Add(
                    HotReloadMethodOutcome.Failed(
                        methodLabel,
                        HotReloadSkippedMemberCompileNote.AppendNotes(
                            composedMessage,
                            entryErrorMessages,
                            skipped),
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

            List<TransformWorkerEntryDto> callers = CollectCallerEntriesOfAddedMethods(
                failedAddedMethodKeys,
                failedEntryKeys,
                allEntries);
            foreach (TransformWorkerEntryDto entry in callers)
            {
                excludedCallerEntries.Add(entry);
                string callerKey = BuildMethodKey(entry);
                if (entry.patchKind == HotReloadConstants.PatchKindAddedMethod)
                {
                    excludedAddedMethodKeys.Add(callerKey);
                }
                else
                {
                    excludedKeys.Add(callerKey);
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

        private static List<TransformWorkerEntryDto> CollectCallerEntriesOfAddedMethods(
            HashSet<string> addedMethodKeys,
            HashSet<string> alreadyExcludedEntryKeys,
            TransformWorkerEntryDto[] allEntries)
        {
            List<TransformWorkerEntryDto> callerEntries = new List<TransformWorkerEntryDto>();
            if (addedMethodKeys.Count == 0 || allEntries == null)
            {
                return callerEntries;
            }

            foreach (TransformWorkerEntryDto entry in allEntries)
            {
                if (entry.calledAddedMethodKeys == null)
                {
                    continue;
                }

                string callerKey = BuildMethodKey(entry);
                if (alreadyExcludedEntryKeys.Contains(callerKey))
                {
                    continue;
                }

                bool callsAdded = false;
                foreach (string calledKey in entry.calledAddedMethodKeys)
                {
                    if (addedMethodKeys.Contains(calledKey))
                    {
                        callsAdded = true;
                        break;
                    }
                }

                if (!callsAdded)
                {
                    continue;
                }

                callerEntries.Add(entry);
            }

            return callerEntries;
        }

        private static List<HotReloadMethodOutcome> BuildSkippedCallerOutcomes(
            IReadOnlyList<TransformWorkerEntryDto> callerEntries,
            string assemblyResolvePath,
            string skipReason)
        {
            List<HotReloadMethodOutcome> skippedCallerOutcomes = new List<HotReloadMethodOutcome>();
            foreach (TransformWorkerEntryDto caller in callerEntries)
            {
                string methodLabel = HotReloadPatcher.FormatMethodKeyParts(
                    caller.typeMetadataName,
                    caller.methodName,
                    caller.parameterTypeFullNames ?? Array.Empty<string>(),
                    caller.genericArity);
                skippedCallerOutcomes.Add(
                    HotReloadMethodOutcome.Skipped(
                        methodLabel,
                        skipReason,
                        assemblyResolvePath));
            }

            return skippedCallerOutcomes;
        }

        /// <summary>
        /// First shim compile plus optional compile-failure isolation. Signature-change gate
        /// retries never call this — they already consumed the one worker retry.
        /// </summary>
        private static async Task<ShimFirstCompileResult> CompileShimFirstPassAsync(
            TransformWorkerInputDto workerInput,
            TransformWorkerOutputDto workerOutput,
            UnityCompilationAssembly compilationAssembly,
            string targetDllPath,
            string[] defines,
            string assemblyResolvePath,
            CancellationToken ct)
        {
            // BuildShimReferencePaths reads Application.dataPath / platform; stay on main thread.
            await MainThreadSwitcher.SwitchToMainThread(ct);
            bool includeHarmonyReference = NeedsHarmonyReference(workerOutput);
            bool includeAddedFieldStoreReference = NeedsAddedFieldStoreReference(workerOutput);
            ShimReferencePathsResult shimReferencePaths = TryBuildShimReferencePaths(
                compilationAssembly,
                targetDllPath,
                includeHarmonyReference,
                includeAddedFieldStoreReference);
            if (shimReferencePaths.ErrorMessage != null)
            {
                return ShimFirstCompileResult.Failed(
                    HotReloadMethodOutcome.Failed(
                        "(file)",
                        shimReferencePaths.ErrorMessage,
                        assemblyResolvePath));
            }

            List<string> shimReferences = shimReferencePaths.References;
            HotReloadShimCompileResult compileResult = await HotReloadShimCompiler.CompileAndLoadAsync(
                workerOutput.shimSource,
                shimReferences,
                defines,
                workerInput.projectRelativePath,
                ct).ConfigureAwait(false);

            TransformWorkerEntryDto[] entriesToPatch = workerOutput.entries;
            if (compileResult.Success)
            {
                return ShimFirstCompileResult.Succeeded(entriesToPatch, compileResult);
            }

            // Why isolate only here: a signature-change gate retry already used
            // RunIsolationRetryAsync (worker run #2). Calling isolation after that would be a
            // third worker run. Gate retry compile failures return Failed from the gate and
            // never reach this first-compile path.
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
                        soleEntry.genericArity);
                }

                List<string> fallbackErrorMessages = new List<string>(compileResult.Errors.Count);
                for (int errorIndex = 0; errorIndex < compileResult.Errors.Count; errorIndex++)
                {
                    fallbackErrorMessages.Add(compileResult.Errors[errorIndex].Message);
                }

                return ShimFirstCompileResult.Failed(
                    HotReloadMethodOutcome.Failed(
                        failureMethodLabel,
                        HotReloadSkippedMemberCompileNote.AppendNotes(
                            compileResult.ErrorMessage,
                            fallbackErrorMessages,
                            workerOutput.skipped),
                        assemblyResolvePath));
            }

            List<HotReloadMethodOutcome> isolationOutcomes = new List<HotReloadMethodOutcome>();
            isolationOutcomes.AddRange(isolation.FailedMethodOutcomes);
            isolationOutcomes.AddRange(isolation.SkippedCallerOutcomes);
            if (isolation.RetryEntries.Length == 0)
            {
                return ShimFirstCompileResult.SucceededEmpty(isolationOutcomes, isolation.AddedFieldNames);
            }

            return ShimFirstCompileResult.Succeeded(
                isolation.RetryEntries,
                isolation.RetryCompileResult,
                isolationOutcomes,
                isolation.AddedFieldNames);
        }

        /// <summary>
        /// Scans compiled call sites after worker #1 and before the first shim compile. No
        /// trigger means the scanner is not called.
        /// </summary>
        private static async Task<SignatureChangeGateResult> TryApplySignatureChangeGateAsync(
            string projectRoot,
            string assemblyName,
            TransformWorkerInputDto workerInput,
            TransformWorkerOutputDto workerOutput,
            UnityCompilationAssembly compilationAssembly,
            string targetDllPath,
            string[] defines,
            string assemblyResolvePath,
            string projectRelativePath,
            CancellationToken ct)
        {
            TransformWorkerEntryDto[] entries = workerOutput.entries ?? Array.Empty<TransformWorkerEntryDto>();
            TransformWorkerRemovedMethodSignatureDto[] removedSignatures =
                workerOutput.removedMethodSignatures
                ?? Array.Empty<TransformWorkerRemovedMethodSignatureDto>();
            List<TransformWorkerEntryDto> replacementEntries = CollectReplacementEntries(entries);
            if (replacementEntries.Count == 0 && removedSignatures.Length == 0)
            {
                return SignatureChangeGateResult.NoWork();
            }

            HotReloadCallSiteScanner.CompiledMethodIdentity[] targets = CollectScanTargets(
                assemblyName,
                replacementEntries,
                removedSignatures);
            List<HotReloadCallSiteScanner.CallSiteHit> hits =
                HotReloadCallSiteScanner.FindCallSites(projectRoot, targets);
            HashSet<string> coveredKeys = CollectCoveredMethodKeys(entries, targets);
            Dictionary<string, List<string>> uncoveredCallersByTarget =
                CollectUncoveredCallersByTarget(hits, coveredKeys);

            List<string> staleWarnings = CollectStaleSignatureWarnings(
                removedSignatures,
                uncoveredCallersByTarget);
            List<TransformWorkerEntryDto> gatedReplacements = CollectGatedReplacementEntries(
                replacementEntries,
                uncoveredCallersByTarget);
            if (gatedReplacements.Count == 0)
            {
                return SignatureChangeGateResult.WarningsOnly(
                    staleWarnings,
                    hits,
                    CollectScanTargetKeys(targets));
            }

            IsolationExclusions exclusions = BuildIsolationExclusions(gatedReplacements, entries);
            HashSet<string> editedFileMethodKeys = CollectEditedFileMethodKeys(
                entries,
                workerOutput.unchangedMethods ?? Array.Empty<TransformWorkerUnchangedMethodDto>());
            List<HotReloadMethodOutcome> skippedOutcomes = BuildGatedReplacementSkipOutcomes(
                gatedReplacements,
                uncoveredCallersByTarget,
                editedFileMethodKeys,
                assemblyResolvePath,
                projectRelativePath);
            skippedOutcomes.AddRange(
                BuildSkippedCallerOutcomes(
                    exclusions.CallerEntries,
                    assemblyResolvePath,
                    HotReloadConstants.SignatureChangedGatedCallerSkipReason));

            IsolationRetryRunResult retry = await RunIsolationRetryAsync(
                workerInput,
                exclusions,
                new List<HotReloadMethodOutcome>(),
                new List<HotReloadMethodOutcome>(),
                compilationAssembly,
                targetDllPath,
                defines,
                workerOutput.skipped,
                assemblyResolvePath,
                ct).ConfigureAwait(false);
            List<string> gatedReplacementMethodKeys =
                CollectGatedReplacementMethodKeys(gatedReplacements);
            if (retry.Isolation == null)
            {
                return SignatureChangeGateResult.Failed(
                    retry.FailureMessage,
                    gatedReplacementMethodKeys);
            }

            // Why merge here: gate consumption adds SkippedOutcomes only. Retry-only skips live
            // on Isolation.SkippedCallerOutcomes and would drop again without this join.
            skippedOutcomes.AddRange(retry.Isolation.SkippedCallerOutcomes);
            return SignatureChangeGateResult.Retried(
                retry.Isolation,
                skippedOutcomes,
                staleWarnings,
                hits,
                CollectScanTargetKeys(targets),
                gatedReplacementMethodKeys);
        }

        private static List<TransformWorkerEntryDto> CollectReplacementEntries(
            TransformWorkerEntryDto[] entries)
        {
            List<TransformWorkerEntryDto> replacements = new List<TransformWorkerEntryDto>();
            foreach (TransformWorkerEntryDto entry in entries)
            {
                if (entry.replacesCompiledMethod)
                {
                    replacements.Add(entry);
                }
            }

            return replacements;
        }

        private static HotReloadCallSiteScanner.CompiledMethodIdentity[] CollectScanTargets(
            string assemblyName,
            IReadOnlyList<TransformWorkerEntryDto> replacementEntries,
            TransformWorkerRemovedMethodSignatureDto[] removedSignatures)
        {
            List<HotReloadCallSiteScanner.CompiledMethodIdentity> targets =
                new List<HotReloadCallSiteScanner.CompiledMethodIdentity>();
            HashSet<string> seenKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (TransformWorkerEntryDto entry in replacementEntries)
            {
                TryAddScanTarget(
                    targets,
                    seenKeys,
                    assemblyName,
                    entry.typeMetadataName,
                    entry.methodName,
                    entry.parameterTypeFullNames,
                    entry.genericArity);
            }

            foreach (TransformWorkerRemovedMethodSignatureDto signature in removedSignatures)
            {
                TryAddScanTarget(
                    targets,
                    seenKeys,
                    assemblyName,
                    signature.typeMetadataName,
                    signature.methodName,
                    signature.parameterTypeFullNames,
                    signature.genericArity);
            }

            return targets.ToArray();
        }

        private static void TryAddScanTarget(
            List<HotReloadCallSiteScanner.CompiledMethodIdentity> targets,
            HashSet<string> seenKeys,
            string assemblyName,
            string typeMetadataName,
            string methodName,
            string[] parameterTypeFullNames,
            int genericArity)
        {
            string methodKey = BuildMethodKeyParts(
                typeMetadataName,
                methodName,
                parameterTypeFullNames,
                genericArity);
            if (!seenKeys.Add(methodKey))
            {
                return;
            }

            targets.Add(
                new HotReloadCallSiteScanner.CompiledMethodIdentity(
                    assemblyName,
                    typeMetadataName,
                    methodName,
                    parameterTypeFullNames ?? Array.Empty<string>(),
                    genericArity));
        }

        private static HashSet<string> CollectCoveredMethodKeys(
            TransformWorkerEntryDto[] entries,
            HotReloadCallSiteScanner.CompiledMethodIdentity[] targets)
        {
            HashSet<string> coveredKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (TransformWorkerEntryDto entry in entries)
            {
                coveredKeys.Add(BuildMethodKey(entry));
            }

            foreach (HotReloadCallSiteScanner.CompiledMethodIdentity target in targets)
            {
                // Why include removed-signature targets: a deleted helper that called the
                // replaced method is already stale (removed-members warning). Treating that
                // corpse as uncovered would gate a same-file helper-delete + return-type
                // change, which is still a consistent old world. Fail-closed only for live
                // compiled callers that will keep invoking the old method.
                coveredKeys.Add(
                    BuildMethodKeyParts(
                        target.TypeMetadataName,
                        target.MethodName,
                        target.ParameterTypeFullNames,
                        target.GenericArity));
            }

            return coveredKeys;
        }

        private static Dictionary<string, List<string>> CollectUncoveredCallersByTarget(
            IReadOnlyList<HotReloadCallSiteScanner.CallSiteHit> hits,
            HashSet<string> coveredKeys)
        {
            Dictionary<string, List<string>> uncoveredCallersByTarget =
                new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (HotReloadCallSiteScanner.CallSiteHit hit in hits)
            {
                if (coveredKeys.Contains(hit.CallerMethodKey))
                {
                    continue;
                }

                if (!uncoveredCallersByTarget.TryGetValue(hit.TargetMethodKey, out List<string> callers))
                {
                    callers = new List<string>();
                    uncoveredCallersByTarget.Add(hit.TargetMethodKey, callers);
                }

                if (!callers.Contains(hit.CallerMethodKey))
                {
                    callers.Add(hit.CallerMethodKey);
                }
            }

            return uncoveredCallersByTarget;
        }

        private static List<string> CollectStaleSignatureWarnings(
            TransformWorkerRemovedMethodSignatureDto[] removedSignatures,
            Dictionary<string, List<string>> uncoveredCallersByTarget)
        {
            List<string> warnings = new List<string>();
            foreach (TransformWorkerRemovedMethodSignatureDto signature in removedSignatures)
            {
                string methodKey = BuildMethodKeyParts(
                    signature.typeMetadataName,
                    signature.methodName,
                    signature.parameterTypeFullNames,
                    signature.genericArity);
                if (!uncoveredCallersByTarget.TryGetValue(methodKey, out List<string> callers)
                    || callers.Count == 0)
                {
                    continue;
                }

                warnings.Add(
                    string.Format(
                        HotReloadConstants.StaleSignatureCallersWarningFormat,
                        methodKey,
                        string.Join(", ", callers)));
            }

            return warnings;
        }

        /// <summary>
        /// True when every uncovered caller key is an apply entry or unchanged method in the
        /// edited file. A same-type caller that the worker did not see (other partial file,
        /// ctor, or another assembly) must return false so the compile-only wording is used.
        /// </summary>
        internal static bool AreUncoveredCallersInEditedFile(
            IReadOnlyList<string> uncoveredCallerKeys,
            TransformWorkerEntryDto[] entries,
            TransformWorkerUnchangedMethodDto[] unchangedMethods)
        {
            Debug.Assert(uncoveredCallerKeys != null, "uncoveredCallerKeys must not be null.");
            Debug.Assert(entries != null, "entries must not be null.");
            Debug.Assert(unchangedMethods != null, "unchangedMethods must not be null.");

            return AreAllUncoveredCallersInEditedFile(
                uncoveredCallerKeys,
                CollectEditedFileMethodKeys(entries, unchangedMethods));
        }

        /// <summary>
        /// Rechecks scan hits against the final apply set. Returns replacement keys that would
        /// still have uncovered compiled callers after isolation or a gate retry shrank entries.
        /// </summary>
        internal static List<string> FindSignatureChangeCoverageLosses(
            TransformWorkerEntryDto[] entriesToPatch,
            IReadOnlyList<HotReloadCallSiteScanner.CallSiteHit> hits,
            IReadOnlyList<string> scanTargetKeys)
        {
            Debug.Assert(entriesToPatch != null, "entriesToPatch must not be null.");
            Debug.Assert(hits != null, "hits must not be null.");
            Debug.Assert(scanTargetKeys != null, "scanTargetKeys must not be null.");

            HashSet<string> coveredKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (TransformWorkerEntryDto entry in entriesToPatch)
            {
                coveredKeys.Add(BuildMethodKey(entry));
            }

            foreach (string targetKey in scanTargetKeys)
            {
                coveredKeys.Add(targetKey);
            }

            Dictionary<string, List<string>> uncoveredCallersByTarget =
                CollectUncoveredCallersByTarget(hits, coveredKeys);
            List<string> lostReplacementKeys = new List<string>();
            foreach (TransformWorkerEntryDto entry in entriesToPatch)
            {
                if (!entry.replacesCompiledMethod)
                {
                    continue;
                }

                string methodKey = BuildMethodKey(entry);
                if (uncoveredCallersByTarget.TryGetValue(methodKey, out List<string> callers)
                    && callers.Count > 0)
                {
                    lostReplacementKeys.Add(methodKey);
                }
            }

            return lostReplacementKeys;
        }

        private static List<string> CollectScanTargetKeys(
            HotReloadCallSiteScanner.CompiledMethodIdentity[] targets)
        {
            List<string> keys = new List<string>(targets.Length);
            foreach (HotReloadCallSiteScanner.CompiledMethodIdentity target in targets)
            {
                keys.Add(
                    BuildMethodKeyParts(
                        target.TypeMetadataName,
                        target.MethodName,
                        target.ParameterTypeFullNames,
                        target.GenericArity));
            }

            return keys;
        }

        private static List<TransformWorkerEntryDto> CollectGatedReplacementEntries(
            IReadOnlyList<TransformWorkerEntryDto> replacementEntries,
            Dictionary<string, List<string>> uncoveredCallersByTarget)
        {
            List<TransformWorkerEntryDto> gated = new List<TransformWorkerEntryDto>();
            foreach (TransformWorkerEntryDto entry in replacementEntries)
            {
                string methodKey = BuildMethodKey(entry);
                if (uncoveredCallersByTarget.TryGetValue(methodKey, out List<string> callers)
                    && callers.Count > 0)
                {
                    gated.Add(entry);
                }
            }

            return gated;
        }

        private static List<string> CollectGatedReplacementMethodKeys(
            IReadOnlyList<TransformWorkerEntryDto> gatedReplacements)
        {
            List<string> keys = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (TransformWorkerEntryDto entry in gatedReplacements)
            {
                string methodKey = BuildMethodKey(entry);
                if (!seen.Add(methodKey))
                {
                    continue;
                }

                keys.Add(methodKey);
            }

            return keys;
        }

        private static HashSet<string> CollectEditedFileMethodKeys(
            TransformWorkerEntryDto[] entries,
            TransformWorkerUnchangedMethodDto[] unchangedMethods)
        {
            HashSet<string> methodKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (TransformWorkerEntryDto entry in entries)
            {
                methodKeys.Add(BuildMethodKey(entry));
            }

            foreach (TransformWorkerUnchangedMethodDto unchanged in unchangedMethods)
            {
                methodKeys.Add(
                    BuildMethodKeyParts(
                        unchanged.typeMetadataName,
                        unchanged.methodName,
                        unchanged.parameterTypeFullNames,
                        unchanged.genericArity));
            }

            return methodKeys;
        }

        private static bool AreAllUncoveredCallersInEditedFile(
            IReadOnlyList<string> uncoveredCallerKeys,
            HashSet<string> editedFileMethodKeys)
        {
            foreach (string callerKey in uncoveredCallerKeys)
            {
                if (!editedFileMethodKeys.Contains(callerKey))
                {
                    return false;
                }
            }

            return true;
        }

        // Why FormatMethodKeyParts, not BuildMethodKey: registry MethodKey uses the display
        // label ('+' nested separators, '.' before the name). The wire key keeps '/' and '::'
        // and never matches Describe().
        internal static string FormatGatedReplacementRegistryKey(TransformWorkerEntryDto entry)
        {
            Debug.Assert(entry != null, "entry must not be null.");
            return HotReloadPatcher.FormatMethodKeyParts(
                entry.typeMetadataName,
                entry.methodName,
                entry.parameterTypeFullNames ?? Array.Empty<string>(),
                entry.genericArity);
        }

        private static List<HotReloadMethodOutcome> BuildGatedReplacementSkipOutcomes(
            IReadOnlyList<TransformWorkerEntryDto> gatedReplacements,
            Dictionary<string, List<string>> uncoveredCallersByTarget,
            HashSet<string> editedFileMethodKeys,
            string assemblyResolvePath,
            string projectRelativePath)
        {
            List<HotReloadMethodOutcome> outcomes = new List<HotReloadMethodOutcome>();
            foreach (TransformWorkerEntryDto entry in gatedReplacements)
            {
                string methodLabel = FormatGatedReplacementRegistryKey(entry);
                string methodKey = BuildMethodKey(entry);
                string reasonFormat = HotReloadConstants.SignatureChangedGateSkipReasonFormat;
                // Why live registry, not a run-start snapshot: BeginFileGeneration runs after
                // this gate, so the previous apply's added members are still listed here.
                if (HotReloadAddedMemberRegistry.IsActiveMember(projectRelativePath, methodLabel))
                {
                    reasonFormat = HotReloadConstants.SignatureChangedGateSkipReasonAlreadyActiveFormat;
                }
                else if (uncoveredCallersByTarget.TryGetValue(methodKey, out List<string> uncoveredCallers)
                    && AreAllUncoveredCallersInEditedFile(uncoveredCallers, editedFileMethodKeys))
                {
                    reasonFormat = HotReloadConstants.SignatureChangedGateSkipReasonSameFileCallersFormat;
                }

                outcomes.Add(
                    HotReloadMethodOutcome.Skipped(
                        methodLabel,
                        string.Format(reasonFormat, methodLabel),
                        assemblyResolvePath));
            }

            return outcomes;
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

        private sealed class IsolationRetryRunResult
        {
            public HotReloadShimIsolationResult Isolation { get; }
            public string FailureMessage { get; }

            private IsolationRetryRunResult(HotReloadShimIsolationResult isolation, string failureMessage)
            {
                Isolation = isolation;
                FailureMessage = failureMessage;
            }

            public static IsolationRetryRunResult Succeeded(HotReloadShimIsolationResult isolation)
            {
                return new IsolationRetryRunResult(isolation, null);
            }

            public static IsolationRetryRunResult Failed(string failureMessage)
            {
                return new IsolationRetryRunResult(null, failureMessage);
            }
        }

        private sealed class SignatureChangeGateResult
        {
            public bool FileFailed { get; }
            public string FailureMessage { get; }
            public bool UsedWorkerRetry { get; }
            public bool DidScan { get; }
            public HotReloadShimIsolationResult Isolation { get; }
            public List<HotReloadMethodOutcome> SkippedOutcomes { get; }
            public List<string> Warnings { get; }
            public List<HotReloadCallSiteScanner.CallSiteHit> Hits { get; }
            public List<string> ScanTargetKeys { get; }
            public List<string> GatedReplacementMethodKeys { get; }

            private SignatureChangeGateResult(
                bool fileFailed,
                string failureMessage,
                bool usedWorkerRetry,
                bool didScan,
                HotReloadShimIsolationResult isolation,
                List<HotReloadMethodOutcome> skippedOutcomes,
                List<string> warnings,
                List<HotReloadCallSiteScanner.CallSiteHit> hits,
                List<string> scanTargetKeys,
                List<string> gatedReplacementMethodKeys)
            {
                FileFailed = fileFailed;
                FailureMessage = failureMessage;
                UsedWorkerRetry = usedWorkerRetry;
                DidScan = didScan;
                Isolation = isolation;
                SkippedOutcomes = skippedOutcomes ?? new List<HotReloadMethodOutcome>();
                Warnings = warnings ?? new List<string>();
                Hits = hits ?? new List<HotReloadCallSiteScanner.CallSiteHit>();
                ScanTargetKeys = scanTargetKeys ?? new List<string>();
                GatedReplacementMethodKeys = gatedReplacementMethodKeys ?? new List<string>();
            }

            public static SignatureChangeGateResult NoWork()
            {
                return new SignatureChangeGateResult(
                    false, null, false, false, null, null, null, null, null, null);
            }

            public static SignatureChangeGateResult WarningsOnly(
                List<string> warnings,
                List<HotReloadCallSiteScanner.CallSiteHit> hits,
                List<string> scanTargetKeys)
            {
                return new SignatureChangeGateResult(
                    false, null, false, true, null, null, warnings, hits, scanTargetKeys, null);
            }

            public static SignatureChangeGateResult Failed(
                string failureMessage,
                List<string> gatedReplacementMethodKeys)
            {
                return new SignatureChangeGateResult(
                    true,
                    failureMessage,
                    false,
                    false,
                    null,
                    null,
                    null,
                    null,
                    null,
                    gatedReplacementMethodKeys);
            }

            public static SignatureChangeGateResult Retried(
                HotReloadShimIsolationResult isolation,
                List<HotReloadMethodOutcome> skippedOutcomes,
                List<string> warnings,
                List<HotReloadCallSiteScanner.CallSiteHit> hits,
                List<string> scanTargetKeys,
                List<string> gatedReplacementMethodKeys)
            {
                return new SignatureChangeGateResult(
                    false,
                    null,
                    true,
                    true,
                    isolation,
                    skippedOutcomes,
                    warnings,
                    hits,
                    scanTargetKeys,
                    gatedReplacementMethodKeys);
            }
        }

        private sealed class ShimFirstCompileResult
        {
            public bool FileFailed { get; }
            public List<HotReloadMethodOutcome> Outcomes { get; }
            public TransformWorkerEntryDto[] EntriesToPatch { get; }
            public HotReloadShimCompileResult CompileResult { get; }
            public string[] AddedFieldNames { get; }

            private ShimFirstCompileResult(
                bool fileFailed,
                List<HotReloadMethodOutcome> outcomes,
                TransformWorkerEntryDto[] entriesToPatch,
                HotReloadShimCompileResult compileResult,
                string[] addedFieldNames = null)
            {
                FileFailed = fileFailed;
                Outcomes = outcomes ?? new List<HotReloadMethodOutcome>();
                EntriesToPatch = entriesToPatch ?? Array.Empty<TransformWorkerEntryDto>();
                CompileResult = compileResult;
                AddedFieldNames = addedFieldNames;
            }

            public static ShimFirstCompileResult Failed(HotReloadMethodOutcome outcome)
            {
                return new ShimFirstCompileResult(
                    true,
                    new List<HotReloadMethodOutcome> { outcome },
                    Array.Empty<TransformWorkerEntryDto>(),
                    null);
            }

            public static ShimFirstCompileResult SucceededEmpty(
                List<HotReloadMethodOutcome> outcomes,
                string[] addedFieldNames = null)
            {
                return new ShimFirstCompileResult(
                    false,
                    outcomes,
                    Array.Empty<TransformWorkerEntryDto>(),
                    null,
                    addedFieldNames);
            }

            public static ShimFirstCompileResult Succeeded(
                TransformWorkerEntryDto[] entriesToPatch,
                HotReloadShimCompileResult compileResult,
                List<HotReloadMethodOutcome> outcomes = null,
                string[] addedFieldNames = null)
            {
                return new ShimFirstCompileResult(
                    false,
                    outcomes,
                    entriesToPatch,
                    compileResult,
                    addedFieldNames);
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
            public string[] AddedFieldNames { get; }

            public HotReloadShimIsolationResult(
                List<HotReloadMethodOutcome> failedMethodOutcomes,
                List<HotReloadMethodOutcome> skippedCallerOutcomes,
                TransformWorkerEntryDto[] retryEntries,
                HotReloadShimCompileResult retryCompileResult,
                string[] addedFieldNames = null)
            {
                Debug.Assert(skippedCallerOutcomes != null, "skippedCallerOutcomes must not be null.");
                FailedMethodOutcomes = failedMethodOutcomes;
                SkippedCallerOutcomes = skippedCallerOutcomes;
                RetryEntries = retryEntries;
                RetryCompileResult = retryCompileResult;
                AddedFieldNames = addedFieldNames ?? Array.Empty<string>();
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
            bool includeHarmonyReference,
            bool includeAddedFieldStoreReference)
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
                        includeHarmonyReference,
                        includeAddedFieldStoreReference),
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
        /// The added-field store assembly is added when the worker rewrote added-field accesses.
        /// </summary>
        private static List<string> BuildShimReferencePaths(
            UnityCompilationAssembly compilationAssembly,
            string targetDllPath,
            bool includeHarmonyReference,
            bool includeAddedFieldStoreReference)
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

            AppendOptionalShimAssemblyReferences(
                references,
                includeHarmonyReference,
                includeAddedFieldStoreReference);

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

        // Why a list hook: one contentPathOverride cannot feed two edited copies, and
        // AddRange+Sort across files is otherwise untestable.
        private static string ResolveWorkerSourcePath(
            string filePath,
            string contentPathOverride,
            IReadOnlyList<string> contentPathOverrides,
            int index)
        {
            if (contentPathOverrides != null
                && index < contentPathOverrides.Count
                && !string.IsNullOrEmpty(contentPathOverrides[index]))
            {
                return contentPathOverrides[index];
            }

            if (string.IsNullOrEmpty(contentPathOverride))
            {
                return filePath;
            }

            return contentPathOverride;
        }

        // What: decide whether an unchanged source should short-circuit, re-apply with a
        // non-baseline warning, or fall through as a normal changed/unknown source.
        // Why Clear on the miss and non-baseline paths: a later Failed run or revert can leave
        // the hash pointing at a different live patch set; the next reload must not inherit
        // that stale hash. Non-baseline matches still Clear; Stage/Record writes the same
        // hash+flag back so the next identical reload warns again.
        private static HotReloadUnchangedSourceDecision TryShortCircuitUnchangedAppliedSource(
            string workerSourcePath,
            string projectRelativePath,
            string assemblyResolvePath,
            List<HotReloadMethodOutcome> outcomes)
        {
            Debug.Assert(!string.IsNullOrEmpty(workerSourcePath), "workerSourcePath must not be empty.");
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");
            Debug.Assert(outcomes != null, "outcomes must not be null.");

            // Why Exists (not ReadAllBytes first): a missing file after a successful apply
            // used to surface as a file-level Failed from the worker. Reading unconditionally
            // would throw and abort the whole RunAsync. Why not Clear: this path does not
            // mutate patches, so the ledger still describes the live patch set.
            string fullWorkerSourcePath = Path.GetFullPath(workerSourcePath);
            if (!File.Exists(fullWorkerSourcePath))
            {
                return HotReloadUnchangedSourceDecision.NotUnchanged;
            }

            byte[] probeBytes = File.ReadAllBytes(fullWorkerSourcePath);
            string probeHash = HotReloadAppliedSourceLedger.ComputeContentHash(probeBytes);
            HashSet<string> activeLabels = CollectActiveLabelsForFile(projectRelativePath);
            (string Hash, bool IsFullyApplied)? recorded = HotReloadAppliedSourceLedger.TryGet(projectRelativePath);
            if (recorded == null
                || !string.Equals(probeHash, recorded.Value.Hash, StringComparison.Ordinal)
                || (recorded.Value.IsFullyApplied && activeLabels.Count == 0))
            {
                HotReloadAppliedSourceLedger.Clear(projectRelativePath);
                return HotReloadUnchangedSourceDecision.NotUnchanged;
            }

            if (recorded.Value.IsFullyApplied)
            {
                List<string> sortedLabels = new List<string>(activeLabels);
                sortedLabels.Sort(StringComparer.Ordinal);
                for (int index = 0; index < sortedLabels.Count; index++)
                {
                    outcomes.Add(
                        HotReloadMethodOutcome.AlreadyActive(sortedLabels[index], assemblyResolvePath));
                }

                return HotReloadUnchangedSourceDecision.ShortCircuited;
            }

            HotReloadAppliedSourceLedger.Clear(projectRelativePath);
            return HotReloadUnchangedSourceDecision.ReapplyNonBaseline;
        }

        // Why worker hash (not the orchestrator probe): the worker re-reads the file in another
        // process, so the bytes it compiled can differ from the probe if the file changed mid-run.
        // Why last occurrence wins: duplicate paths in one run apply twice; only the last
        // qualifying hash is recorded so the next run short-circuits against what actually landed.
        private static void StageAppliedSourceHash(
            Dictionary<string, (string Hash, bool IsFullyApplied)> appliedSourceHashByPath,
            string projectRelativePath,
            string sourceContentSha256,
            IReadOnlyList<HotReloadMethodOutcome> outcomes)
        {
            Debug.Assert(appliedSourceHashByPath != null, "appliedSourceHashByPath must not be null.");
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");
            Debug.Assert(outcomes != null, "outcomes must not be null.");

            (string Hash, bool IsFullyApplied)? record = DecideAppliedSourceRecord(
                sourceContentSha256,
                outcomes);
            if (record == null)
            {
                appliedSourceHashByPath.Remove(projectRelativePath);
                return;
            }

            appliedSourceHashByPath[projectRelativePath] = record.Value;
        }

        // Why not record "everything that is not fully applied": deleting an added method and
        // converging to compiled IL yields empty outcomes on the empty-entries path. Recording
        // that as non-baseline would make the next identical reload claim a prior Skipped/Failed
        // that never happened.
        private static (string Hash, bool IsFullyApplied)? DecideAppliedSourceRecord(
            string sourceContentSha256,
            IReadOnlyList<HotReloadMethodOutcome> outcomes)
        {
            if (string.IsNullOrEmpty(sourceContentSha256) || outcomes.Count == 0)
            {
                return null;
            }

            bool hasSkippedOrFailed = false;
            bool allPatchedOrAdded = true;
            for (int index = 0; index < outcomes.Count; index++)
            {
                HotReloadMethodOutcomeKind kind = outcomes[index].Kind;
                if (kind == HotReloadMethodOutcomeKind.Patched
                    || kind == HotReloadMethodOutcomeKind.Added)
                {
                    continue;
                }

                allPatchedOrAdded = false;
                if (kind == HotReloadMethodOutcomeKind.Skipped
                    || kind == HotReloadMethodOutcomeKind.Failed)
                {
                    hasSkippedOrFailed = true;
                }
            }

            if (allPatchedOrAdded)
            {
                return (sourceContentSha256, true);
            }

            if (hasSkippedOrFailed)
            {
                return (sourceContentSha256, false);
            }

            return null;
        }

        private static HashSet<string> CollectActiveLabelsForFile(string projectRelativePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");
            HashSet<string> labels = new HashSet<string>(StringComparer.Ordinal);
            IReadOnlyList<string> addedKeys =
                HotReloadAddedMemberRegistry.ListActiveMethodKeys(projectRelativePath);
            for (int index = 0; index < addedKeys.Count; index++)
            {
                labels.Add(addedKeys[index]);
            }

            IReadOnlyList<string> patchedKeys = HotReloadPatcher.ListActiveMethodKeys(projectRelativePath);
            for (int index = 0; index < patchedKeys.Count; index++)
            {
                labels.Add(patchedKeys[index]);
            }

            return labels;
        }

        // Why first-pass added entries: a return-type replacement is both an added entry and
        // a removed signature with the same label, so subtracting removals would swallow the
        // warning. Convergence is quiet because dropping the declaration also drops the entry.
        private static HashSet<string> CollectAddedEntryLabels(TransformWorkerOutputDto workerOutput)
        {
            HashSet<string> labels = new HashSet<string>(StringComparer.Ordinal);
            if (workerOutput == null || workerOutput.entries == null)
            {
                return labels;
            }

            foreach (TransformWorkerEntryDto entry in workerOutput.entries)
            {
                if (entry == null || entry.patchKind != HotReloadConstants.PatchKindAddedMethod)
                {
                    continue;
                }

                labels.Add(
                    HotReloadPatcher.FormatMethodKeyParts(
                        entry.typeMetadataName,
                        entry.methodName,
                        entry.parameterTypeFullNames ?? Array.Empty<string>(),
                        entry.genericArity));
            }

            return labels;
        }

        // Why union Skipped labels: a still-declared added method can leave the first-pass
        // entries when the worker skips it (virtual, generic, interface). Why not Failed:
        // a Failed added method is always a first-pass added entry.
        private static HashSet<string> CollectStillDeclaredAddedLabels(
            TransformWorkerOutputDto workerOutput,
            IReadOnlyList<HotReloadMethodOutcome> outcomes)
        {
            HashSet<string> labels = CollectAddedEntryLabels(workerOutput);
            if (outcomes == null)
            {
                return labels;
            }

            foreach (HotReloadMethodOutcome outcome in outcomes)
            {
                if (outcome == null
                    || outcome.Kind != HotReloadMethodOutcomeKind.Skipped
                    || string.IsNullOrEmpty(outcome.Method))
                {
                    continue;
                }

                labels.Add(outcome.Method);
            }

            return labels;
        }

        private static bool IsUnexpectedDeactivation(
            string label,
            HashSet<string> currentLabels,
            HashSet<string> stillDeclaredAddedLabels)
        {
            return !currentLabels.Contains(label) && stillDeclaredAddedLabels.Contains(label);
        }

        private static void AppendDeactivatedPatchesWarning(
            List<string> warnings,
            HashSet<string> snapshotLabels,
            string projectRelativePath,
            TransformWorkerOutputDto workerOutput,
            IReadOnlyList<HotReloadMethodOutcome> outcomes)
        {
            Debug.Assert(warnings != null, "warnings must not be null.");
            Debug.Assert(snapshotLabels != null, "snapshotLabels must not be null.");
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");

            HashSet<string> currentLabels = CollectActiveLabelsForFile(projectRelativePath);
            HashSet<string> stillDeclaredAdded = CollectStillDeclaredAddedLabels(workerOutput, outcomes);
            List<string> deactivated = new List<string>();
            foreach (string label in snapshotLabels)
            {
                if (!IsUnexpectedDeactivation(label, currentLabels, stillDeclaredAdded))
                {
                    continue;
                }

                deactivated.Add(label);
            }

            if (deactivated.Count == 0)
            {
                return;
            }

            deactivated.Sort(string.CompareOrdinal);
            warnings.Add(
                string.Format(
                    HotReloadConstants.DeactivatedPatchesWarningFormat,
                    string.Join(", ", deactivated)));
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
            public string[] AddedFieldNames { get; }
            public string SourceContentSha256 { get; }

            public HotReloadFileProcessResult(
                List<HotReloadMethodOutcome> outcomes,
                List<string> warnings,
                int patchedCount,
                List<string> suppressedPausePointIds = null,
                List<string> inlineRiskMethodLabels = null,
                int unchangedMethodCount = 0,
                List<string> retargetedPausePointIds = null,
                string[] addedFieldNames = null,
                string sourceContentSha256 = null)
            {
                Outcomes = outcomes;
                Warnings = warnings;
                PatchedCount = patchedCount;
                SuppressedPausePointIds = suppressedPausePointIds ?? new List<string>();
                InlineRiskMethodLabels = inlineRiskMethodLabels ?? new List<string>();
                UnchangedMethodCount = unchangedMethodCount;
                RetargetedPausePointIds = retargetedPausePointIds ?? new List<string>();
                AddedFieldNames = addedFieldNames ?? Array.Empty<string>();
                SourceContentSha256 = sourceContentSha256;
            }
        }
    }
}
