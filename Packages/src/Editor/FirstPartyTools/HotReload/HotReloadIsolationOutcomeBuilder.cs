using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds the exclusion sets an isolation retry runs with, and turns the entries and skip rows
    /// it leaves behind into method outcomes.
    /// </summary>
    internal sealed class HotReloadIsolationOutcomeBuilder
    {
        /// <summary>
        /// Converts retry-worker skips that are not already in the first-pass skipped list into
        /// outcomes. Match is (Method, Reason) Ordinal equality so a method skipped for a new
        /// reason on retry still surfaces. The trigger decides whether the retry-only reasons are
        /// rewritten before they become outcomes.
        /// </summary>
        internal List<HotReloadMethodOutcome> CollectRetryOnlySkippedOutcomes(
            TransformWorkerSkippedDto[] firstPassSkipped,
            TransformWorkerSkippedDto[] retrySkipped,
            HotReloadGroupFilePaths groupFilePaths,
            IHotReloadIsolationTrigger trigger,
            IReadOnlyCollection<string> excludedAddedMethodKeys)
        {
            List<HotReloadMethodOutcome> retryOnly = new List<HotReloadMethodOutcome>();
            if (retrySkipped == null)
            {
                return retryOnly;
            }

            TransformWorkerSkippedDto[] baseline =
                firstPassSkipped ?? Array.Empty<TransformWorkerSkippedDto>();
            List<TransformWorkerSkippedDto> retryOnlyRows = new List<TransformWorkerSkippedDto>();
            foreach (TransformWorkerSkippedDto retryRow in retrySkipped)
            {
                if (FirstPassContainsSkippedPair(baseline, retryRow))
                {
                    continue;
                }

                retryOnlyRows.Add(retryRow);
            }

            trigger.RewriteRetryOnlySkippedReasons(retryOnlyRows, excludedAddedMethodKeys);

            foreach (TransformWorkerSkippedDto retryRow in retryOnlyRows)
            {
                retryOnly.Add(
                    HotReloadMethodOutcome.Skipped(
                        retryRow.method ?? "(unknown)",
                        retryRow.reason ?? string.Empty,
                        groupFilePaths.ResolveAssemblyResolvePath(retryRow.sourceProjectRelativePath)));
            }

            return retryOnly;
        }

        private bool FirstPassContainsSkippedPair(
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

        // The project-relative paths of every source the run transformed, for shim-diagnostic
        // line mapping.
        internal List<string> CollectSourceProjectRelativePaths(TransformWorkerInputDto workerInput)
        {
            List<string> projectRelativePaths = new List<string>(workerInput.sources.Length);
            foreach (TransformWorkerSourceDto source in workerInput.sources)
            {
                projectRelativePaths.Add(source.projectRelativePath);
            }

            return projectRelativePaths;
        }

        internal HotReloadShimIsolation.IsolationExclusions BuildIsolationExclusions(
            IReadOnlyList<TransformWorkerEntryDto> failedEntries,
            TransformWorkerEntryDto[] allEntries)
        {
            HashSet<string> excludedKeys = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> excludedAddedMethodKeys = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> failedAddedMethodKeys = new HashSet<string>(StringComparer.Ordinal);
            List<TransformWorkerEntryDto> excludedCallerEntries = new List<TransformWorkerEntryDto>();
            foreach (TransformWorkerEntryDto failedEntry in failedEntries)
            {
                string methodKey = HotReloadMethodKeys.BuildMethodKey(failedEntry);
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
                failedEntryKeys.Add(HotReloadMethodKeys.BuildMethodKey(failedEntry));
            }

            List<TransformWorkerEntryDto> callers = CollectCallerEntriesOfAddedMethods(
                failedAddedMethodKeys,
                failedEntryKeys,
                allEntries);
            foreach (TransformWorkerEntryDto entry in callers)
            {
                excludedCallerEntries.Add(entry);
                string callerKey = HotReloadMethodKeys.BuildMethodKey(entry);
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
            return new HotReloadShimIsolation.IsolationExclusions(
                excludedMethodKeys,
                excludedAddedKeys,
                excludedCallerEntries);
        }

        private List<TransformWorkerEntryDto> CollectCallerEntriesOfAddedMethods(
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

                string callerKey = HotReloadMethodKeys.BuildMethodKey(entry);
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

        internal List<HotReloadMethodOutcome> BuildSkippedCallerOutcomes(
            IReadOnlyList<TransformWorkerEntryDto> callerEntries,
            HotReloadGroupFilePaths groupFilePaths,
            string skipReason)
        {
            List<HotReloadMethodOutcome> skippedCallerOutcomes = new List<HotReloadMethodOutcome>();
            foreach (TransformWorkerEntryDto caller in callerEntries)
            {
                string methodLabel = HotReloadMethodKeys.FormatMethodLabelParts(
                    new HotReloadMetadataTypeName(caller.typeMetadataName),
                    caller.methodName,
                    caller.parameterTypeFullNames ?? Array.Empty<string>(),
                    caller.genericArity);
                skippedCallerOutcomes.Add(
                    HotReloadMethodOutcome.Skipped(
                        methodLabel,
                        skipReason,
                        groupFilePaths.ResolveAssemblyResolvePath(caller.sourceProjectRelativePath)));
            }

            return skippedCallerOutcomes;
        }
    }
}
