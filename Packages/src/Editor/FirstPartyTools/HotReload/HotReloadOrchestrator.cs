using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

using UnityCompilationAssembly = UnityEditor.Compilation.Assembly;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// End-to-end hot-reload pipeline: resolve every file's assembly, group the files of one
    /// assembly, run each group, and merge the per-file results in input order.
    /// </summary>
    internal static class HotReloadOrchestrator
    {
        /// <summary>
        /// Runs hot reload for each path in <paramref name="files"/>.
        /// <paramref name="contentPathOverride"/> is test-only: when set, the worker reads that
        /// path while assembly resolution still uses <paramref name="files"/> (so edited copies
        /// can live under <c>Library/UloopHotReload/TestSources/</c> without provoking AssetDatabase).
        /// <paramref name="contentPathOverrideByFile"/> is the per-file form of that hook, keyed by
        /// the entry in <paramref name="files"/>; it wins over the single override.
        /// </summary>
        public static async Task<HotReloadOrchestratorResult> RunAsync(
            IReadOnlyList<string> files,
            string contentPathOverride,
            CancellationToken ct,
            IReadOnlyDictionary<string, string> contentPathOverrideByFile = null)
        {
            Debug.Assert(files != null, "files must not be null.");
            Debug.Assert(files.Count > 0, "files must not be empty.");

            string correlationId = VibeLogger.GenerateCorrelationId();
            HotReloadRunAccumulator run = new HotReloadRunAccumulator();

            // CompilationPipeline / Application.dataPath require the Unity main thread, and the
            // groups cannot be planned before every file knows which assembly it compiles into.
            await MainThreadSwitcher.SwitchToMainThread(ct);
            HotReloadFileProcessResult[] resultSlots = new HotReloadFileProcessResult[files.Count];
            string[] resultPaths = new string[files.Count];
            HotReloadGroupFile[] groupFiles = new HotReloadGroupFile[files.Count];
            List<HotReloadMethodOutcome>[] deferredAlreadyActive = new List<HotReloadMethodOutcome>[files.Count];
            List<(int InputIndex, string AssemblyName, string ProjectRelativePath)> plannerInput =
                new List<(int InputIndex, string AssemblyName, string ProjectRelativePath)>();
            for (int index = 0; index < files.Count; index++)
            {
                ct.ThrowIfCancellationRequested();
                ResolveInputFile(
                    files[index],
                    index,
                    contentPathOverride,
                    contentPathOverrideByFile,
                    correlationId,
                    run,
                    resultSlots,
                    resultPaths,
                    groupFiles,
                    plannerInput,
                    deferredAlreadyActive);
            }

            IReadOnlyList<HotReloadFileGroupPlan> plans = HotReloadFileGroupPlanner.Plan(plannerInput);
            HashSet<string> pathsInRun = new HashSet<string>(
                HotReloadSourcePathNormalizer.ProjectRelativePathComparer());
            for (int pathIndex = 0; pathIndex < resultPaths.Length; pathIndex++)
            {
                if (!string.IsNullOrEmpty(resultPaths[pathIndex]))
                {
                    pathsInRun.Add(resultPaths[pathIndex]);
                }
            }

            bool[] allDeferred = ClassifyAllDeferredPlans(plans, deferredAlreadyActive);

            List<(string Path, HotReloadFileProcessResult Result)> extraResults =
                new List<(string Path, HotReloadFileProcessResult Result)>();
            for (int planIndex = 0; planIndex < plans.Count; planIndex++)
            {
                ct.ThrowIfCancellationRequested();
                HotReloadFileGroupPlan plan = plans[planIndex];
                if (allDeferred[planIndex])
                {
                    ApplyDeferredAlreadyActive(plan, groupFiles, resultSlots, deferredAlreadyActive);
                    continue;
                }

                bool isLastChangedGroup = IsLastChangedPlanForAssembly(plans, allDeferred, planIndex);
                List<int> inputIndexes = new List<int>(plan.InputIndexes);
                if (isLastChangedGroup)
                {
                    AppendUniqueLaterDeferredInputIndexes(
                        plans,
                        allDeferred,
                        planIndex,
                        groupFiles,
                        inputIndexes);
                }

                await ProcessPlannedGroupAsync(
                        inputIndexes,
                        groupFiles,
                        resultSlots,
                        correlationId,
                        ct,
                        pathsInRun,
                        contentPathOverrideByFile,
                        isLastChangedGroup,
                        extraResults,
                        run)
                    .ConfigureAwait(false);
            }

            for (int index = 0; index < files.Count; index++)
            {
                run.Add(resultPaths[index], resultSlots[index]);
            }

            for (int extraIndex = 0; extraIndex < extraResults.Count; extraIndex++)
            {
                run.Add(extraResults[extraIndex].Path, extraResults[extraIndex].Result);
            }

            run.RecordAppliedSourceHashes();

            await MainThreadSwitcher.SwitchToMainThread(ct);
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            run.ApplyOneShotCallerNotes(projectRoot);

            await MainThreadSwitcher.SwitchToMainThread(ct);
            return run.BuildResult(correlationId);
        }

        // Resolves one input path's patch target and either records its early result or enrolls
        // it in the group plan.
        private static void ResolveInputFile(
            string filePath,
            int index,
            string contentPathOverride,
            IReadOnlyDictionary<string, string> contentPathOverrideByFile,
            string correlationId,
            HotReloadRunAccumulator run,
            HotReloadFileProcessResult[] resultSlots,
            string[] resultPaths,
            HotReloadGroupFile[] groupFiles,
            List<(int InputIndex, string AssemblyName, string ProjectRelativePath)> plannerInput,
            List<HotReloadMethodOutcome>[] deferredAlreadyActive)
        {
            string workerSourcePath = ResolveWorkerSourcePath(
                filePath,
                contentPathOverride,
                contentPathOverrideByFile);
            HotReloadFileSinks sinks = new HotReloadFileSinks(
                run.SiblingDerivedWarnings,
                run.OneShotCallerNoteCandidates);
            List<HotReloadMethodOutcome> alreadyActiveOutcomes = new List<HotReloadMethodOutcome>();

            (HotReloadFileProcessResult earlyResolve,
                string projectRelativePath,
                string assemblyName,
                UnityCompilationAssembly compilationAssembly,
                string targetDllPath,
                string projectRoot,
                HotReloadUnchangedSourceDecision unchangedDecision) = HotReloadPatchTargetSupport.ResolvePatchTarget(
                filePath,
                workerSourcePath,
                sinks.Outcomes,
                sinks.Warnings,
                correlationId,
                alreadyActiveOutcomes);
            if (earlyResolve != null)
            {
                resultSlots[index] = earlyResolve;
                resultPaths[index] = HotReloadPatchTargetSupport.ToProjectRelativeScriptPath(filePath);
                return;
            }

            if (unchangedDecision == HotReloadUnchangedSourceDecision.ShortCircuited)
            {
                deferredAlreadyActive[index] = alreadyActiveOutcomes;
            }

            groupFiles[index] = new HotReloadGroupFile(
                filePath,
                workerSourcePath,
                projectRelativePath,
                assemblyName,
                compilationAssembly,
                targetDllPath,
                projectRoot,
                sinks);
            resultPaths[index] = projectRelativePath;
            plannerInput.Add((index, assemblyName, projectRelativePath));
        }

        private static bool[] ClassifyAllDeferredPlans(
            IReadOnlyList<HotReloadFileGroupPlan> plans,
            List<HotReloadMethodOutcome>[] deferredAlreadyActive)
        {
            Debug.Assert(plans != null, "plans must not be null.");
            Debug.Assert(deferredAlreadyActive != null, "deferredAlreadyActive must not be null.");

            bool[] allDeferred = new bool[plans.Count];
            for (int planIndex = 0; planIndex < plans.Count; planIndex++)
            {
                allDeferred[planIndex] = AreAllInputsDeferredAlreadyActive(
                    plans[planIndex],
                    deferredAlreadyActive);
            }

            return allDeferred;
        }

        private static bool AreAllInputsDeferredAlreadyActive(
            HotReloadFileGroupPlan plan,
            List<HotReloadMethodOutcome>[] deferredAlreadyActive)
        {
            foreach (int inputIndex in plan.InputIndexes)
            {
                if (deferredAlreadyActive[inputIndex] == null)
                {
                    return false;
                }
            }

            return true;
        }

        // Why absorbed here: a later all-deferred plan can hold an unchanged caller that is
        // already in pathsInRun, so sibling auto-include cannot add it, and running that plan
        // alone would emit a shim that omits this group's changed host.
        private static void AppendUniqueLaterDeferredInputIndexes(
            IReadOnlyList<HotReloadFileGroupPlan> plans,
            bool[] allDeferred,
            int currentPlanIndex,
            HotReloadGroupFile[] groupFiles,
            List<int> inputIndexes)
        {
            Debug.Assert(plans != null, "plans must not be null.");
            Debug.Assert(allDeferred != null, "allDeferred must not be null.");
            Debug.Assert(groupFiles != null, "groupFiles must not be null.");
            Debug.Assert(inputIndexes != null, "inputIndexes must not be null.");

            string assemblyName = plans[currentPlanIndex].AssemblyName;
            HashSet<string> pathsInGroup = new HashSet<string>(
                HotReloadSourcePathNormalizer.ProjectRelativePathComparer());
            for (int position = 0; position < inputIndexes.Count; position++)
            {
                pathsInGroup.Add(groupFiles[inputIndexes[position]].ProjectRelativePath);
            }

            for (int planIndex = currentPlanIndex + 1; planIndex < plans.Count; planIndex++)
            {
                if (!allDeferred[planIndex]
                    || !string.Equals(
                        plans[planIndex].AssemblyName,
                        assemblyName,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                IReadOnlyList<int> laterIndexes = plans[planIndex].InputIndexes;
                for (int laterPosition = 0; laterPosition < laterIndexes.Count; laterPosition++)
                {
                    int inputIndex = laterIndexes[laterPosition];
                    string path = groupFiles[inputIndex].ProjectRelativePath;
                    if (pathsInGroup.Add(path))
                    {
                        inputIndexes.Add(inputIndex);
                    }
                }
            }
        }

        private static void ApplyDeferredAlreadyActive(
            HotReloadFileGroupPlan plan,
            HotReloadGroupFile[] groupFiles,
            HotReloadFileProcessResult[] resultSlots,
            List<HotReloadMethodOutcome>[] deferredAlreadyActive)
        {
            foreach (int inputIndex in plan.InputIndexes)
            {
                if (resultSlots[inputIndex] != null)
                {
                    continue;
                }

                Debug.Assert(
                    deferredAlreadyActive[inputIndex] != null,
                    "An unfilled deferred slot must have AlreadyActive rows.");
                groupFiles[inputIndex].Sinks.Outcomes.AddRange(deferredAlreadyActive[inputIndex]);
                resultSlots[inputIndex] = new HotReloadFileProcessResult(
                    groupFiles[inputIndex].Sinks.Outcomes,
                    groupFiles[inputIndex].Sinks.Warnings,
                    0);
            }
        }

        private static async Task ProcessPlannedGroupAsync(
            IReadOnlyList<int> inputIndexes,
            HotReloadGroupFile[] groupFiles,
            HotReloadFileProcessResult[] resultSlots,
            string correlationId,
            CancellationToken ct,
            HashSet<string> pathsInRun,
            IReadOnlyDictionary<string, string> contentPathOverrideByFile,
            bool isLastGroupOfAssembly,
            List<(string Path, HotReloadFileProcessResult Result)> extraResults,
            HotReloadRunAccumulator run)
        {
            Debug.Assert(inputIndexes != null && inputIndexes.Count > 0, "A group must hold a file.");
            List<HotReloadGroupFile> filesOfGroup = new List<HotReloadGroupFile>(inputIndexes.Count);
            foreach (int inputIndex in inputIndexes)
            {
                filesOfGroup.Add(groupFiles[inputIndex]);
            }

            int inputCount = inputIndexes.Count;
            if (isLastGroupOfAssembly)
            {
                AppendActiveSiblingsToGroup(filesOfGroup, pathsInRun, contentPathOverrideByFile, run);
            }

            // Why ConfigureAwait(false): UnityCliLoopTool forbids capturing Unity's
            // SynchronizationContext across awaits — while Play Mode is paused that context
            // does not run continuations, so a true resume would hang the tool forever.
            // ProcessGroupAsync switches back via MainThreadSwitcher (EditorApplication.update
            // queue) before any main-thread-only editor API or Harmony patch.
            IReadOnlyList<HotReloadFileProcessResult> groupResults =
                await HotReloadGroupProcessor.ProcessGroupAsync(filesOfGroup, correlationId, ct)
                    .ConfigureAwait(false);
            Debug.Assert(
                groupResults.Count == filesOfGroup.Count,
                "A group must report one result per file, including re-applied siblings.");
            for (int position = 0; position < inputIndexes.Count; position++)
            {
                resultSlots[inputIndexes[position]] = groupResults[position];
            }

            for (int position = inputCount; position < filesOfGroup.Count; position++)
            {
                extraResults.Add((filesOfGroup[position].ProjectRelativePath, groupResults[position]));
            }

            if (isLastGroupOfAssembly)
            {
                AddSiblingRebindResultWarnings(filesOfGroup, inputCount, groupResults);
            }
        }

        private static void AppendActiveSiblingsToGroup(
            List<HotReloadGroupFile> filesOfGroup,
            HashSet<string> pathsInRun,
            IReadOnlyDictionary<string, string> contentPathOverrideByFile,
            HotReloadRunAccumulator run)
        {
            HotReloadGroupFile firstFile = filesOfGroup[0];
            HotReloadActiveSiblingRebindPlan rebind = HotReloadActiveSiblingRebindPlanner.Plan(
                firstFile.AssemblyName,
                firstFile.CompilationAssembly.sourceFiles,
                pathsInRun,
                path => ResolveSiblingWorkerSourcePath(
                    path,
                    firstFile.ProjectRoot,
                    contentPathOverrideByFile));
            IReadOnlyList<(string ProjectRelativePath, string WorkerSourcePath)> filesToInclude =
                rebind.FilesToInclude;
            for (int index = 0; index < filesToInclude.Count; index++)
            {
                filesOfGroup.Add(
                    HotReloadGroupFile.ForActiveSibling(
                        firstFile,
                        filesToInclude[index].ProjectRelativePath,
                        filesToInclude[index].WorkerSourcePath,
                        new HotReloadFileSinks(run.SiblingDerivedWarnings, run.OneShotCallerNoteCandidates)));
            }

            AddChangedSinceApplyWarnings(firstFile, rebind);
        }

        private static void AddChangedSinceApplyWarnings(
            HotReloadGroupFile firstFile,
            HotReloadActiveSiblingRebindPlan rebind)
        {
            IReadOnlyList<string> changedSinceApplyPaths = rebind.ChangedSinceApplyPaths;
            for (int index = 0; index < changedSinceApplyPaths.Count; index++)
            {
                firstFile.Sinks.Warnings.Add(
                    string.Format(
                        HotReloadConstants.ActiveSiblingChangedSinceApplyWarningFormat,
                        changedSinceApplyPaths[index]));
            }
        }

        private static void AddSiblingRebindResultWarnings(
            List<HotReloadGroupFile> filesOfGroup,
            int inputCount,
            IReadOnlyList<HotReloadFileProcessResult> groupResults)
        {
            Debug.Assert(filesOfGroup != null, "filesOfGroup must not be null.");
            Debug.Assert(groupResults != null, "groupResults must not be null.");
            Debug.Assert(
                groupResults.Count == filesOfGroup.Count,
                "Rebind warnings need one result per group file.");
            Debug.Assert(groupResults.Count > 0, "A processed group must have a result.");

            List<string> reappliedPaths = new List<string>();
            for (int position = inputCount; position < filesOfGroup.Count; position++)
            {
                string path = filesOfGroup[position].ProjectRelativePath;
                if (ShouldDescribeSiblingAsReapplied(groupResults[position]))
                {
                    reappliedPaths.Add(path);
                }
                else
                {
                    groupResults[0].Warnings.Add(
                        string.Format(
                            HotReloadConstants.ActiveSiblingRebindFailedWarningFormat,
                            path));
                }
            }

            if (reappliedPaths.Count > 0)
            {
                groupResults[0].Warnings.Add(
                    string.Format(
                        HotReloadConstants.ActiveSiblingsRebindWarningFormat,
                        reappliedPaths.Count,
                        filesOfGroup[0].AssemblyName,
                        string.Join(", ", reappliedPaths)));
            }
        }

        // Why not "no Failed row": isolation leaves a sibling as Skipped when its added-method
        // callee failed to compile, and claiming that file was re-applied would be false.
        private static bool ShouldDescribeSiblingAsReapplied(HotReloadFileProcessResult result)
        {
            Debug.Assert(result != null, "result must not be null.");
            bool sawApplied = false;
            foreach (HotReloadMethodOutcome outcome in result.Outcomes)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Failed)
                {
                    return false;
                }

                if (outcome.Kind == HotReloadMethodOutcomeKind.Patched
                    || outcome.Kind == HotReloadMethodOutcomeKind.Added)
                {
                    sawApplied = true;
                }
            }

            return sawApplied;
        }

        // Why changed groups only: a trailing all-deferred plan is not the shim that carries
        // this run's host edits, so auto-include and absorbed callers belong on the last
        // changed group.
        private static bool IsLastChangedPlanForAssembly(
            IReadOnlyList<HotReloadFileGroupPlan> plans,
            bool[] allDeferred,
            int planIndex)
        {
            Debug.Assert(plans != null, "plans must not be null.");
            Debug.Assert(allDeferred != null, "allDeferred must not be null.");
            Debug.Assert(allDeferred.Length == plans.Count, "allDeferred must match plans.");
            Debug.Assert(planIndex >= 0 && planIndex < plans.Count, "planIndex must be in range.");
            Debug.Assert(!allDeferred[planIndex], "Last-changed lookup is only for a changed group.");

            string assemblyName = plans[planIndex].AssemblyName;
            for (int index = planIndex + 1; index < plans.Count; index++)
            {
                if (allDeferred[index])
                {
                    continue;
                }

                if (string.Equals(plans[index].AssemblyName, assemblyName, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static string ResolveSiblingWorkerSourcePath(
            string projectRelativePath,
            string projectRoot,
            IReadOnlyDictionary<string, string> overrideByFile)
        {
            if (overrideByFile != null)
            {
                StringComparer comparer = HotReloadSourcePathNormalizer.ProjectRelativePathComparer();
                foreach (KeyValuePair<string, string> pair in overrideByFile)
                {
                    string keyRelative = HotReloadPatchTargetSupport.ToProjectRelativeScriptPath(pair.Key);
                    if (comparer.Equals(keyRelative, projectRelativePath))
                    {
                        return Path.GetFullPath(pair.Value);
                    }
                }
            }

            return Path.GetFullPath(
                Path.Combine(
                    projectRoot,
                    projectRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        // Why keyed by path (not by index): one contentPathOverride cannot feed two edited
        // copies, and a positional list would silently misalign once files are grouped or
        // reordered before the worker runs.
        private static string ResolveWorkerSourcePath(
            string filePath,
            string contentPathOverride,
            IReadOnlyDictionary<string, string> contentPathOverrideByFile)
        {
            if (contentPathOverrideByFile != null
                && contentPathOverrideByFile.TryGetValue(filePath, out string perFileOverride)
                && !string.IsNullOrEmpty(perFileOverride))
            {
                return perFileOverride;
            }

            if (string.IsNullOrEmpty(contentPathOverride))
            {
                return filePath;
            }

            return contentPathOverride;
        }
    }
}
