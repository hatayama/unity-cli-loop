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
            IReadOnlyList<HotReloadSiblingInclusion> filesToInclude = rebind.FilesToInclude;
            for (int index = 0; index < filesToInclude.Count; index++)
            {
                HotReloadSiblingInclusion inclusion = filesToInclude[index];
                run.NoteSiblingInclusion(inclusion.ProjectRelativePath, inclusion.Reason);
                filesOfGroup.Add(
                    HotReloadGroupFile.ForActiveSibling(
                        firstFile,
                        inclusion.ProjectRelativePath,
                        inclusion.WorkerSourcePath,
                        new HotReloadFileSinks(run.SiblingDerivedWarnings, run.OneShotCallerNoteCandidates),
                        inclusion.Evidence,
                        run.SiblingBaselineNotices));
            }

            AddChangedSinceApplyWarnings(firstFile, rebind);
        }

        internal void AddSiblingRebindResultWarnings(
            List<HotReloadGroupFile> filesOfGroup,
            int inputCount,
            IReadOnlyList<HotReloadFileProcessResult> groupResults,
            HotReloadRunAccumulator run)
        {
            Debug.Assert(filesOfGroup != null, "filesOfGroup must not be null.");
            Debug.Assert(groupResults != null, "groupResults must not be null.");
            Debug.Assert(run != null, "run must not be null.");
            Debug.Assert(
                groupResults.Count == filesOfGroup.Count,
                "Rebind warnings need one result per group file.");
            Debug.Assert(groupResults.Count > 0, "A processed group must have a result.");

            List<string> warnings = groupResults[0].Warnings;
            List<string> reappliedPaths = new List<string>();
            List<string> retriedPaths = new List<string>();
            List<string> companionPaths = new List<string>();
            for (int position = inputCount; position < filesOfGroup.Count; position++)
            {
                string path = filesOfGroup[position].ProjectRelativePath;
                HotReloadFileProcessResult result = groupResults[position];
                switch (run.SiblingInclusionReasonOf(path))
                {
                    case HotReloadSiblingInclusionReason.Companion:
                        AddCompanionResult(path, result, companionPaths);
                        break;
                    case HotReloadSiblingInclusionReason.RetryAfterSkip:
                        AddRetryResult(path, result, retriedPaths, warnings);
                        break;
                    default:
                        AddActiveChangesResult(path, result, reappliedPaths, warnings);
                        break;
                }
            }

            string assemblyName = filesOfGroup[0].AssemblyName;
            AddSummary(warnings, HotReloadConstants.ActiveSiblingsRebindWarningFormat, assemblyName, reappliedPaths);
            AddSummary(warnings, HotReloadConstants.RetriedSiblingsWarningFormat, assemblyName, retriedPaths);
            AddSummary(warnings, HotReloadConstants.CompanionSiblingsWarningFormat, assemblyName, companionPaths);
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

        private static void AddActiveChangesResult(
            string path,
            HotReloadFileProcessResult result,
            List<string> reappliedPaths,
            List<string> warnings)
        {
            string warningFormat = HotReloadSiblingRebindWarningSelector.SelectUnappliedWarningFormat(result);
            if (warningFormat == null)
            {
                reappliedPaths.Add(path);
                return;
            }

            warnings.Add(string.Format(warningFormat, path));
        }

        private static void AddRetryResult(
            string path,
            HotReloadFileProcessResult result,
            List<string> retriedPaths,
            List<string> warnings)
        {
            if (HotReloadSiblingRebindWarningSelector.AppliedAnyChange(result))
            {
                retriedPaths.Add(path);
                return;
            }

            warnings.Add(string.Format(HotReloadConstants.RetriedSiblingNotAppliedWarningFormat, path));
        }

        // Why a companion with rows is left to its rows: it had nothing to apply, so a row means
        // the whole group was refused, which the passed files' rows already explain.
        private static void AddCompanionResult(
            string path,
            HotReloadFileProcessResult result,
            List<string> companionPaths)
        {
            if (result.Outcomes.Count == 0)
            {
                companionPaths.Add(path);
            }
        }

        private static void AddSummary(
            List<string> warnings,
            string format,
            string assemblyName,
            List<string> paths)
        {
            if (paths.Count == 0)
            {
                return;
            }

            warnings.Add(string.Format(format, paths.Count, assemblyName, string.Join(", ", paths)));
        }

        private void AddChangedSinceApplyWarnings(
            HotReloadGroupFile firstFile,
            HotReloadActiveSiblingRebindPlan rebind)
        {
            AddChangedWarnings(firstFile, HotReloadConstants.ActiveSiblingChangedSinceApplyWarningFormat, rebind.ChangedSinceApplyPaths);
            AddChangedWarnings(firstFile, HotReloadConstants.RetrySiblingChangedSinceSkipWarningFormat, rebind.ChangedSinceSkipPaths);
            AddChangedWarnings(firstFile, HotReloadConstants.CompanionSiblingChangedWarningFormat, rebind.ChangedCompanionPaths);
        }

        private static void AddChangedWarnings(
            HotReloadGroupFile firstFile,
            string format,
            IReadOnlyList<string> changedPaths)
        {
            for (int index = 0; index < changedPaths.Count; index++)
            {
                firstFile.Sinks.Warnings.Add(string.Format(format, changedPaths[index]));
            }
        }
    }
}
