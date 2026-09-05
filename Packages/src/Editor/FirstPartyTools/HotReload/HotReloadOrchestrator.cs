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
                    plannerInput);
            }

            IReadOnlyList<HotReloadFileGroupPlan> plans = HotReloadFileGroupPlanner.Plan(plannerInput);
            foreach (HotReloadFileGroupPlan plan in plans)
            {
                ct.ThrowIfCancellationRequested();
                await ProcessPlannedGroupAsync(plan, groupFiles, resultSlots, correlationId, ct)
                    .ConfigureAwait(false);
            }

            for (int index = 0; index < files.Count; index++)
            {
                run.Add(resultPaths[index], resultSlots[index]);
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
            List<(int InputIndex, string AssemblyName, string ProjectRelativePath)> plannerInput)
        {
            string workerSourcePath = ResolveWorkerSourcePath(
                filePath,
                contentPathOverride,
                contentPathOverrideByFile);
            HotReloadFileSinks sinks = new HotReloadFileSinks(
                run.SiblingDerivedWarnings,
                run.OneShotCallerNoteCandidates);

            (HotReloadFileProcessResult earlyResolve,
                string projectRelativePath,
                string assemblyName,
                UnityCompilationAssembly compilationAssembly,
                string targetDllPath,
                string projectRoot) = HotReloadPatchTargetSupport.ResolvePatchTarget(
                filePath,
                workerSourcePath,
                sinks.Outcomes,
                sinks.Warnings,
                correlationId);
            if (earlyResolve != null)
            {
                resultSlots[index] = earlyResolve;
                resultPaths[index] = HotReloadPatchTargetSupport.ToProjectRelativeScriptPath(filePath);
                return;
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

        private static async Task ProcessPlannedGroupAsync(
            HotReloadFileGroupPlan plan,
            HotReloadGroupFile[] groupFiles,
            HotReloadFileProcessResult[] resultSlots,
            string correlationId,
            CancellationToken ct)
        {
            IReadOnlyList<int> inputIndexes = plan.InputIndexes;
            List<HotReloadGroupFile> filesOfGroup = new List<HotReloadGroupFile>(inputIndexes.Count);
            foreach (int inputIndex in inputIndexes)
            {
                filesOfGroup.Add(groupFiles[inputIndex]);
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
                groupResults.Count == inputIndexes.Count,
                "A group must report one result per edited file.");
            for (int position = 0; position < inputIndexes.Count; position++)
            {
                resultSlots[inputIndexes[position]] = groupResults[position];
            }
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
