using System;
using System.Collections.Generic;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Adds the active siblings of an assembly to the last changed group and reports how each
    /// of them came back.
    /// </summary>
    internal sealed class HotReloadSiblingRebindReporter
    {
        private readonly HotReloadDomain _domain;

        internal HotReloadSiblingRebindReporter(HotReloadDomain domain)
        {
            Debug.Assert(domain != null, "domain must not be null.");
            _domain = domain;
        }

        // Why the resolver arrives per call: the reporter holds no collaborator of its own, and
        // the orchestrator already owns the one resolver this run uses.
        internal void AppendActiveSiblingsToGroup(
            List<HotReloadGroupFile> filesOfGroup,
            HashSet<string> pathsInRun,
            IReadOnlyDictionary<string, string> contentPathOverrideByFile,
            HotReloadRunAccumulator run,
            HotReloadInputFileResolver inputFileResolver)
        {
            HotReloadGroupFile firstFile = filesOfGroup[0];
            HotReloadActiveSiblingRebindPlan rebind = HotReloadActiveSiblingRebindPlanner.Plan(
                _domain,
                firstFile.AssemblyName,
                firstFile.CompilationAssembly.sourceFiles,
                pathsInRun,
                path => inputFileResolver.ResolveSiblingWorkerSourcePath(
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

        internal void AddSiblingRebindResultWarnings(
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
                else if (groupResults[position].Outcomes.Count == 0)
                {
                    // A run stopped before it applied anything wrote no row for this file, so
                    // the failed-rebind sentence would send the reader looking for rows that
                    // were never written.
                    groupResults[0].Warnings.Add(
                        string.Format(
                            HotReloadConstants.ActiveSiblingRebindSkippedWarningFormat,
                            path));
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

        // Why changed groups only: a trailing all-deferred plan is not the shim that carries
        // this run's host edits, so auto-include and absorbed callers belong on the last
        // changed group.
        internal bool IsLastChangedPlanForAssembly(
            IReadOnlyList<HotReloadFileGroupPlan> plans,
            IReadOnlyList<HotReloadDeferredInputPlan> classified,
            int planIndex)
        {
            Debug.Assert(plans != null, "plans must not be null.");
            Debug.Assert(classified != null, "classified must not be null.");
            Debug.Assert(classified.Count == plans.Count, "classified must match plans.");
            Debug.Assert(planIndex >= 0 && planIndex < plans.Count, "planIndex must be in range.");
            Debug.Assert(!classified[planIndex].IsAllDeferred, "Last-changed lookup is only for a changed group.");

            string assemblyName = plans[planIndex].AssemblyName;
            for (int index = planIndex + 1; index < plans.Count; index++)
            {
                if (classified[index].IsAllDeferred)
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

        private void AddChangedSinceApplyWarnings(
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

        // Why not "no Failed row": isolation leaves a sibling as Skipped when its added-method
        // callee failed to compile, and claiming that file was re-applied would be false.
        private bool ShouldDescribeSiblingAsReapplied(HotReloadFileProcessResult result)
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
    }
}
