using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Maps shim compile diagnostics onto worker entries by original-source line range.
    /// </summary>
    internal static class HotReloadShimErrorAttribution
    {
        /// <summary>
        /// Maps each shim compile error to the entry whose original-source [sourceStartLine,
        /// sourceEndLine] contains its #line-mapped location in the same user file. Returns null
        /// if any error is unattributable (wrong/empty file, scaffold path, or outside every
        /// entry range) — method isolation cannot fix those.
        /// </summary>
        internal static ShimCompileErrorAttribution AttributeErrorsToEntries(
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

        internal sealed class ShimCompileErrorAttribution
        {
            public IReadOnlyDictionary<TransformWorkerEntryDto, List<string>> ErrorMessagesByEntry { get; }
            public IReadOnlyList<TransformWorkerEntryDto> FailedEntries { get; }

            public ShimCompileErrorAttribution(Dictionary<TransformWorkerEntryDto, List<string>> errorMessagesByEntry)
            {
                ErrorMessagesByEntry = errorMessagesByEntry;
                FailedEntries = new List<TransformWorkerEntryDto>(errorMessagesByEntry.Keys);
            }
        }
    }
}
