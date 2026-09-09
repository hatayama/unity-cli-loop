using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// End-to-end hot-reload pipeline: resolve every file's assembly, group the files of one
    /// assembly, run each group, and merge the per-file results in input order.
    /// </summary>
    internal sealed class HotReloadOrchestrator : IHotReloadOrchestrator
    {
        private readonly HotReloadGroupProcessor _groupProcessor;
        private readonly HotReloadInputFileResolver _inputFileResolver;
        private readonly HotReloadDeferredInputClassifier _deferredInputClassifier;
        private readonly HotReloadSiblingRebindReporter _siblingRebindReporter;
        private readonly IHotReloadPackageRootCapture _packageRootCapture;
        private readonly HotReloadDomain _domain;
        private readonly HotReloadPatcher _patcher;

        internal HotReloadOrchestrator(
            HotReloadDomain domain,
            HotReloadPatcher patcher,
            HotReloadGroupProcessor groupProcessor,
            HotReloadInputFileResolver inputFileResolver,
            HotReloadDeferredInputClassifier deferredInputClassifier,
            HotReloadSiblingRebindReporter siblingRebindReporter,
            IHotReloadPackageRootCapture packageRootCapture)
        {
            Debug.Assert(domain != null, "domain must not be null.");
            Debug.Assert(patcher != null, "patcher must not be null.");
            Debug.Assert(groupProcessor != null, "groupProcessor must not be null.");
            Debug.Assert(inputFileResolver != null, "inputFileResolver must not be null.");
            Debug.Assert(deferredInputClassifier != null, "deferredInputClassifier must not be null.");
            Debug.Assert(siblingRebindReporter != null, "siblingRebindReporter must not be null.");
            Debug.Assert(packageRootCapture != null, "packageRootCapture must not be null.");
            _domain = domain;
            _patcher = patcher;
            _groupProcessor = groupProcessor;
            _inputFileResolver = inputFileResolver;
            _deferredInputClassifier = deferredInputClassifier;
            _siblingRebindReporter = siblingRebindReporter;
            _packageRootCapture = packageRootCapture;
        }

        /// <summary>
        /// Runs hot reload for each path in <paramref name="files"/>.
        /// <paramref name="contentPathOverride"/> is test-only: when set, the worker reads that
        /// path while assembly resolution still uses <paramref name="files"/> (so edited copies
        /// can live under <c>Library/UloopHotReload/TestSources/</c> without provoking AssetDatabase).
        /// <paramref name="contentPathOverrideByFile"/> is the per-file form of that hook, keyed by
        /// the entry in <paramref name="files"/>; it wins over the single override.
        /// </summary>
        public async Task<HotReloadOrchestratorResult> RunAsync(
            IReadOnlyList<string> files,
            string contentPathOverride,
            CancellationToken ct,
            IReadOnlyDictionary<string, string> contentPathOverrideByFile = null)
        {
            Debug.Assert(files != null, "files must not be null.");
            Debug.Assert(files.Count > 0, "files must not be empty.");

            string correlationId = VibeLogger.GenerateCorrelationId();

            // CompilationPipeline / Application.dataPath require the Unity main thread, and the
            // groups cannot be planned before every file knows which assembly it compiles into.
            await MainThreadSwitcher.SwitchToMainThread(ct);
            // Why after the switch: PackageInfo is main-thread only, and script paths are
            // normalized against these roots later on the background threads this run switches to.
            _packageRootCapture.CaptureCurrent();
            // Why after the switch: the accumulator has to read the Auto Refresh hold flag out of
            // SessionState, which is a main-thread API.
            HotReloadRunAccumulator run =
                new HotReloadRunAccumulator(_domain, _patcher, HotReloadAutoRefreshHold.IsHeld);
            HotReloadInputResolutionSlot[] slots = new HotReloadInputResolutionSlot[files.Count];
            for (int index = 0; index < slots.Length; index++)
            {
                slots[index] = new HotReloadInputResolutionSlot();
            }

            List<(int InputIndex, string AssemblyName, string ProjectRelativePath)> plannerInput =
                new List<(int InputIndex, string AssemblyName, string ProjectRelativePath)>();
            for (int index = 0; index < files.Count; index++)
            {
                ct.ThrowIfCancellationRequested();
                _inputFileResolver.ResolveInputFile(
                    files[index],
                    index,
                    contentPathOverride,
                    contentPathOverrideByFile,
                    correlationId,
                    run,
                    slots[index],
                    plannerInput);
            }

            IReadOnlyList<HotReloadFileGroupPlan> plans = HotReloadFileGroupPlanner.Plan(plannerInput);
            HashSet<string> pathsInRun = new HashSet<string>(
                HotReloadSourcePathNormalizer.ProjectRelativePathComparer());
            for (int pathIndex = 0; pathIndex < slots.Length; pathIndex++)
            {
                if (!string.IsNullOrEmpty(slots[pathIndex].ResultPath))
                {
                    pathsInRun.Add(slots[pathIndex].ResultPath);
                }
            }

            IReadOnlyList<HotReloadDeferredInputPlan> classifiedPlans =
                _deferredInputClassifier.ClassifyAllDeferredPlans(plans, slots);

            List<(string Path, HotReloadFileProcessResult Result)> extraResults =
                new List<(string Path, HotReloadFileProcessResult Result)>();
            for (int planIndex = 0; planIndex < plans.Count; planIndex++)
            {
                ct.ThrowIfCancellationRequested();
                HotReloadFileGroupPlan plan = plans[planIndex];
                if (classifiedPlans[planIndex].IsAllDeferred)
                {
                    continue;
                }

                bool isLastChangedGroup = _siblingRebindReporter.IsLastChangedPlanForAssembly(
                    plans,
                    classifiedPlans,
                    planIndex);
                List<int> inputIndexes = new List<int>(plan.InputIndexes);
                if (isLastChangedGroup)
                {
                    _deferredInputClassifier.AppendUniqueDeferredInputIndexes(
                        plans,
                        classifiedPlans,
                        planIndex,
                        slots,
                        inputIndexes);
                }

                await ProcessPlannedGroupAsync(
                        inputIndexes,
                        slots,
                        correlationId,
                        ct,
                        pathsInRun,
                        contentPathOverrideByFile,
                        isLastChangedGroup,
                        extraResults,
                        run)
                    .ConfigureAwait(false);
            }

            // Why after every changed plan: an earlier all-deferred plan must not fill its
            // slots before the last changed group can absorb its unique callers.
            for (int planIndex = 0; planIndex < plans.Count; planIndex++)
            {
                if (classifiedPlans[planIndex].IsAllDeferred)
                {
                    _deferredInputClassifier.ApplyDeferredAlreadyActive(plans[planIndex], slots);
                }
            }

            for (int index = 0; index < files.Count; index++)
            {
                run.Add(slots[index].ResultPath, slots[index].Result);
            }

            for (int extraIndex = 0; extraIndex < extraResults.Count; extraIndex++)
            {
                run.AddReappliedSibling(extraResults[extraIndex].Path, extraResults[extraIndex].Result);
            }

            run.RecordAppliedSourceHashes();

            await MainThreadSwitcher.SwitchToMainThread(ct);
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            run.ApplyOneShotCallerNotes(projectRoot);

            await MainThreadSwitcher.SwitchToMainThread(ct);
            return run.BuildResult(correlationId);
        }

        private async Task ProcessPlannedGroupAsync(
            IReadOnlyList<int> inputIndexes,
            HotReloadInputResolutionSlot[] slots,
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
                filesOfGroup.Add(slots[inputIndex].GroupFile);
            }

            int inputCount = inputIndexes.Count;
            if (isLastGroupOfAssembly)
            {
                _siblingRebindReporter.AppendActiveSiblingsToGroup(
                    filesOfGroup,
                    pathsInRun,
                    contentPathOverrideByFile,
                    run,
                    _inputFileResolver);
            }

            // Why ConfigureAwait(false): UnityCliLoopTool forbids capturing Unity's
            // SynchronizationContext across awaits — while Play Mode is paused that context
            // does not run continuations, so a true resume would hang the tool forever.
            // ProcessGroupAsync switches back via MainThreadSwitcher (EditorApplication.update
            // queue) before any main-thread-only editor API or Harmony patch.
            IReadOnlyList<HotReloadFileProcessResult> groupResults =
                await _groupProcessor.ProcessGroupAsync(filesOfGroup, correlationId, ct)
                    .ConfigureAwait(false);
            Debug.Assert(
                groupResults.Count == filesOfGroup.Count,
                "A group must report one result per file, including re-applied siblings.");
            for (int position = 0; position < inputIndexes.Count; position++)
            {
                slots[inputIndexes[position]].Result = groupResults[position];
            }

            for (int position = inputCount; position < filesOfGroup.Count; position++)
            {
                extraResults.Add((filesOfGroup[position].ProjectRelativePath, groupResults[position]));
            }

            if (isLastGroupOfAssembly)
            {
                _siblingRebindReporter.AddSiblingRebindResultWarnings(
                    filesOfGroup,
                    inputCount,
                    groupResults);
            }
        }
    }
}
