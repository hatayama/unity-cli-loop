using System;
using System.Collections.Generic;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Decides which edited files of a group a failed shim compile takes down, what those files
    /// report, and which method keys the isolation retry must leave out.
    /// </summary>
    /// <remarks>
    /// Why whole files (not single methods): the group compiles one shim assembly, so a method
    /// whose body does not compile is dropped together with everything else its file contributed.
    /// Applying a file's remaining methods without the failed one would leave that file half
    /// applied, which is the atomicity rule a single-file run already followed.
    /// Why the two exclusion branches: dropping whole files is what lets the other files of the
    /// group still be applied. When no file survives there is nothing to save, so the narrower
    /// method-and-caller exclusion is kept — it is what makes the retry report a caller of a
    /// broken added method as such instead of as one more file-atomic skip.
    /// </remarks>
    internal sealed class HotReloadFileAtomicIsolationPlan
    {
        private readonly HashSet<string> failedFiles;

        private HotReloadFileAtomicIsolationPlan(
            HashSet<string> failedFiles,
            bool allFilesFailed,
            string[] excludedMethodKeys,
            string[] excludedAddedMethodKeys,
            IReadOnlyList<TransformWorkerEntryDto> callerEntries,
            Dictionary<string, List<HotReloadMethodOutcome>> failedOutcomesByFile,
            Dictionary<string, List<HotReloadMethodOutcome>> atomicSkipOutcomesByFile)
        {
            this.failedFiles = failedFiles;
            AllFilesFailed = allFilesFailed;
            ExcludedMethodKeys = excludedMethodKeys;
            ExcludedAddedMethodKeys = excludedAddedMethodKeys;
            CallerEntries = callerEntries;
            FailedOutcomesByFile = failedOutcomesByFile;
            AtomicSkipOutcomesByFile = atomicSkipOutcomesByFile;
        }

        // The project-relative paths of the files a compile error was attributed to.
        internal IReadOnlyCollection<string> FailedFiles => failedFiles;

        internal bool AllFilesFailed { get; }

        internal bool IsFailedFile(string projectRelativePath)
        {
            return failedFiles.Contains(projectRelativePath);
        }

        internal string[] ExcludedMethodKeys { get; }

        internal string[] ExcludedAddedMethodKeys { get; }

        // Entries that call a failed added method and are therefore excluded by name. Empty
        // unless every file failed, because a file-wide exclusion already covers its callers.
        internal IReadOnlyList<TransformWorkerEntryDto> CallerEntries { get; }

        // Failed outcomes of the attributed methods, keyed by their file.
        internal IReadOnlyDictionary<string, List<HotReloadMethodOutcome>> FailedOutcomesByFile { get; }

        // Skipped outcomes of the remaining entries of a failed file, keyed by that file. Empty
        // when every file failed: there the retry still runs, and only the entries it re-emitted
        // are file-atomic skips — the ones it dropped report the reason the retry gave them.
        internal IReadOnlyDictionary<string, List<HotReloadMethodOutcome>> AtomicSkipOutcomesByFile { get; }

        internal static HotReloadFileAtomicIsolationPlan Build(
            TransformWorkerEntryDto[] entries,
            HotReloadShimErrorAttribution.ShimCompileErrorAttribution attribution,
            TransformWorkerSkippedDto[] skipped,
            HotReloadGroupFilePaths groupFilePaths,
            IReadOnlyCollection<string> groupPaths)
        {
            Debug.Assert(entries != null, "entries must not be null.");
            Debug.Assert(attribution != null, "attribution must not be null.");
            Debug.Assert(groupFilePaths != null, "groupFilePaths must not be null.");
            Debug.Assert(groupPaths != null && groupPaths.Count > 0, "A group must hold a file.");

            HashSet<string> failedFiles = CollectFailedFiles(attribution.FailedEntries);
            bool allFilesFailed = failedFiles.Count >= groupPaths.Count;
            Dictionary<string, List<HotReloadMethodOutcome>> failedOutcomesByFile = BuildFailedOutcomesByFile(
                attribution,
                skipped,
                groupFilePaths);
            if (allFilesFailed)
            {
                HotReloadShimIsolation.IsolationExclusions exclusions =
                    HotReloadShimIsolation.BuildIsolationExclusions(attribution.FailedEntries, entries);
                return new HotReloadFileAtomicIsolationPlan(
                    failedFiles,
                    true,
                    exclusions.ExcludedMethodKeys,
                    exclusions.ExcludedAddedMethodKeys,
                    exclusions.CallerEntries,
                    failedOutcomesByFile,
                    new Dictionary<string, List<HotReloadMethodOutcome>>(StringComparer.Ordinal));
            }

            HashSet<TransformWorkerEntryDto> failedEntries =
                new HashSet<TransformWorkerEntryDto>(attribution.FailedEntries);
            HashSet<string> excludedMethodKeys = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> excludedAddedMethodKeys = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, List<HotReloadMethodOutcome>> atomicSkipOutcomesByFile =
                new Dictionary<string, List<HotReloadMethodOutcome>>(StringComparer.Ordinal);
            foreach (TransformWorkerEntryDto entry in entries)
            {
                string sourceFile = entry.sourceProjectRelativePath;
                if (!failedFiles.Contains(sourceFile))
                {
                    continue;
                }

                string methodKey = HotReloadMethodKeys.BuildMethodKey(entry);
                if (entry.patchKind == HotReloadConstants.PatchKindAddedMethod)
                {
                    // Why a separate set: dropping a healthy added shim through excludedMethodKeys
                    // leaves its callers with CS0103. The added-method set removes the declaration
                    // as well, so the worker skips those callers instead of emitting broken bodies.
                    excludedAddedMethodKeys.Add(methodKey);
                }
                else
                {
                    excludedMethodKeys.Add(methodKey);
                }

                if (failedEntries.Contains(entry))
                {
                    continue;
                }

                AppendOutcome(
                    atomicSkipOutcomesByFile,
                    sourceFile,
                    HotReloadMethodOutcome.Skipped(
                        HotReloadMethodKeys.FormatMethodLabelParts(
                            entry.typeMetadataName,
                            entry.methodName,
                            entry.parameterTypeFullNames ?? Array.Empty<string>(),
                            entry.genericArity),
                        HotReloadConstants.AtomicFileSkipReason,
                        groupFilePaths.ResolveAssemblyResolvePath(sourceFile)));
            }

            return new HotReloadFileAtomicIsolationPlan(
                failedFiles,
                false,
                ToArray(excludedMethodKeys),
                ToArray(excludedAddedMethodKeys),
                Array.Empty<TransformWorkerEntryDto>(),
                failedOutcomesByFile,
                atomicSkipOutcomesByFile);
        }

        // Flattens per-file outcomes in the order of the group's files, for the stages that route
        // outcomes by the file path each outcome already carries.
        internal static List<HotReloadMethodOutcome> CollectOutcomes(
            IReadOnlyDictionary<string, List<HotReloadMethodOutcome>> outcomesByFile,
            IReadOnlyList<string> groupPaths)
        {
            Debug.Assert(outcomesByFile != null, "outcomesByFile must not be null.");
            Debug.Assert(groupPaths != null, "groupPaths must not be null.");

            List<HotReloadMethodOutcome> outcomes = new List<HotReloadMethodOutcome>();
            foreach (string groupPath in groupPaths)
            {
                if (outcomesByFile.TryGetValue(groupPath, out List<HotReloadMethodOutcome> fileOutcomes))
                {
                    outcomes.AddRange(fileOutcomes);
                }
            }

            return outcomes;
        }

        private static Dictionary<string, List<HotReloadMethodOutcome>> BuildFailedOutcomesByFile(
            HotReloadShimErrorAttribution.ShimCompileErrorAttribution attribution,
            TransformWorkerSkippedDto[] skipped,
            HotReloadGroupFilePaths groupFilePaths)
        {
            Dictionary<string, List<HotReloadMethodOutcome>> failedOutcomesByFile =
                new Dictionary<string, List<HotReloadMethodOutcome>>(StringComparer.Ordinal);
            foreach (TransformWorkerEntryDto failedEntry in attribution.FailedEntries)
            {
                List<string> entryErrorMessages = attribution.ErrorMessagesByEntry[failedEntry];
                AppendOutcome(
                    failedOutcomesByFile,
                    failedEntry.sourceProjectRelativePath,
                    HotReloadMethodOutcome.Failed(
                        HotReloadMethodKeys.FormatMethodLabelParts(
                            failedEntry.typeMetadataName,
                            failedEntry.methodName,
                            failedEntry.parameterTypeFullNames ?? Array.Empty<string>(),
                            failedEntry.genericArity),
                        HotReloadSkippedMemberCompileNote.AppendNotes(
                            HotReloadShimCompiler.ComposeShimCompileFailureMessage(entryErrorMessages),
                            entryErrorMessages,
                            skipped),
                        groupFilePaths.ResolveAssemblyResolvePath(failedEntry.sourceProjectRelativePath)));
            }

            return failedOutcomesByFile;
        }

        private static HashSet<string> CollectFailedFiles(
            IReadOnlyList<TransformWorkerEntryDto> failedEntries)
        {
            HashSet<string> failedFiles = new HashSet<string>(StringComparer.Ordinal);
            foreach (TransformWorkerEntryDto failedEntry in failedEntries)
            {
                Debug.Assert(
                    !string.IsNullOrEmpty(failedEntry.sourceProjectRelativePath),
                    "An attributed entry must name the source file it came from.");
                failedFiles.Add(failedEntry.sourceProjectRelativePath);
            }

            return failedFiles;
        }

        private static void AppendOutcome(
            Dictionary<string, List<HotReloadMethodOutcome>> outcomesByFile,
            string sourceFile,
            HotReloadMethodOutcome outcome)
        {
            if (!outcomesByFile.TryGetValue(sourceFile, out List<HotReloadMethodOutcome> outcomes))
            {
                outcomes = new List<HotReloadMethodOutcome>();
                outcomesByFile[sourceFile] = outcomes;
            }

            outcomes.Add(outcome);
        }

        private static string[] ToArray(HashSet<string> keys)
        {
            string[] array = new string[keys.Count];
            keys.CopyTo(array);
            return array;
        }
    }
}
