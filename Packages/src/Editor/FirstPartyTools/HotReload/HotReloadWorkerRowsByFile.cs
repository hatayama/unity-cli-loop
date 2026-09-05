using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Splits the output of one group worker run into the rows of each edited file, so per-file
    /// notices, gates and apply steps read only what their own file produced.
    /// </summary>
    internal sealed class HotReloadWorkerRowsByFile
    {
        private readonly Dictionary<string, List<TransformWorkerEntryDto>> entriesByFile;
        private readonly Dictionary<string, List<TransformWorkerSkippedDto>> skippedByFile;
        private readonly Dictionary<string, List<TransformWorkerUnchangedMethodDto>> unchangedByFile;
        private readonly Dictionary<string, TransformWorkerFileOutputDto> fileOutputsByFile;

        private HotReloadWorkerRowsByFile(
            Dictionary<string, List<TransformWorkerEntryDto>> entriesByFile,
            Dictionary<string, List<TransformWorkerSkippedDto>> skippedByFile,
            Dictionary<string, List<TransformWorkerUnchangedMethodDto>> unchangedByFile,
            Dictionary<string, TransformWorkerFileOutputDto> fileOutputsByFile)
        {
            this.entriesByFile = entriesByFile;
            this.skippedByFile = skippedByFile;
            this.unchangedByFile = unchangedByFile;
            this.fileOutputsByFile = fileOutputsByFile;
        }

        public static HotReloadWorkerRowsByFile Build(
            TransformWorkerOutputDto output,
            IReadOnlyCollection<string> groupPaths)
        {
            Debug.Assert(output != null, "output must not be null.");
            Debug.Assert(groupPaths != null && groupPaths.Count > 0, "A group must hold a file.");

            Dictionary<string, List<TransformWorkerEntryDto>> entriesByFile =
                new Dictionary<string, List<TransformWorkerEntryDto>>(StringComparer.Ordinal);
            Dictionary<string, List<TransformWorkerSkippedDto>> skippedByFile =
                new Dictionary<string, List<TransformWorkerSkippedDto>>(StringComparer.Ordinal);
            Dictionary<string, List<TransformWorkerUnchangedMethodDto>> unchangedByFile =
                new Dictionary<string, List<TransformWorkerUnchangedMethodDto>>(StringComparer.Ordinal);
            Dictionary<string, TransformWorkerFileOutputDto> fileOutputsByFile =
                new Dictionary<string, TransformWorkerFileOutputDto>(StringComparer.Ordinal);
            foreach (string groupPath in groupPaths)
            {
                entriesByFile[groupPath] = new List<TransformWorkerEntryDto>();
                skippedByFile[groupPath] = new List<TransformWorkerSkippedDto>();
                unchangedByFile[groupPath] = new List<TransformWorkerUnchangedMethodDto>();
            }

            HashSet<string> groupPathSet = new HashSet<string>(groupPaths, StringComparer.Ordinal);
            foreach (TransformWorkerEntryDto entry in output.entries)
            {
                AssertRowBelongsToGroup(groupPathSet, entry.sourceProjectRelativePath, "entry");
                entriesByFile[entry.sourceProjectRelativePath].Add(entry);
            }

            foreach (TransformWorkerSkippedDto skipped in output.skipped)
            {
                AssertRowBelongsToGroup(groupPathSet, skipped.sourceProjectRelativePath, "skipped");
                skippedByFile[skipped.sourceProjectRelativePath].Add(skipped);
            }

            foreach (TransformWorkerUnchangedMethodDto unchanged in output.unchangedMethods)
            {
                AssertRowBelongsToGroup(
                    groupPathSet,
                    unchanged.sourceProjectRelativePath,
                    "unchanged-method");
                unchangedByFile[unchanged.sourceProjectRelativePath].Add(unchanged);
            }

            foreach (TransformWorkerFileOutputDto fileOutput in output.files)
            {
                AssertRowBelongsToGroup(groupPathSet, fileOutput.projectRelativePath, "per-file");
                fileOutputsByFile[fileOutput.projectRelativePath] = fileOutput;
            }

            return new HotReloadWorkerRowsByFile(
                entriesByFile,
                skippedByFile,
                unchangedByFile,
                fileOutputsByFile);
        }

        // Groups an entry list that did not come from the group output itself - the isolation
        // retry produces its own entries - so the apply stage can walk one file at a time.
        public static Dictionary<string, List<TransformWorkerEntryDto>> GroupEntriesBySourceFile(
            IReadOnlyList<TransformWorkerEntryDto> entries,
            IReadOnlyCollection<string> groupPaths)
        {
            Debug.Assert(entries != null, "entries must not be null.");
            Debug.Assert(groupPaths != null && groupPaths.Count > 0, "A group must hold a file.");

            Dictionary<string, List<TransformWorkerEntryDto>> entriesByFile =
                new Dictionary<string, List<TransformWorkerEntryDto>>(StringComparer.Ordinal);
            foreach (string groupPath in groupPaths)
            {
                entriesByFile[groupPath] = new List<TransformWorkerEntryDto>();
            }

            HashSet<string> groupPathSet = new HashSet<string>(groupPaths, StringComparer.Ordinal);
            foreach (TransformWorkerEntryDto entry in entries)
            {
                AssertRowBelongsToGroup(groupPathSet, entry.sourceProjectRelativePath, "entry");
                entriesByFile[entry.sourceProjectRelativePath].Add(entry);
            }

            return entriesByFile;
        }

        public IReadOnlyList<TransformWorkerEntryDto> EntriesFor(string projectRelativePath)
        {
            Debug.Assert(
                entriesByFile.ContainsKey(projectRelativePath),
                "Entries can only be read for a file of the group.");
            return entriesByFile[projectRelativePath];
        }

        public IReadOnlyList<TransformWorkerSkippedDto> SkippedFor(string projectRelativePath)
        {
            Debug.Assert(
                skippedByFile.ContainsKey(projectRelativePath),
                "Skipped rows can only be read for a file of the group.");
            return skippedByFile[projectRelativePath];
        }

        public IReadOnlyList<TransformWorkerUnchangedMethodDto> UnchangedFor(string projectRelativePath)
        {
            Debug.Assert(
                unchangedByFile.ContainsKey(projectRelativePath),
                "Unchanged methods can only be read for a file of the group.");
            return unchangedByFile[projectRelativePath];
        }

        public TransformWorkerFileOutputDto FileOutputFor(string projectRelativePath)
        {
            Debug.Assert(
                fileOutputsByFile.ContainsKey(projectRelativePath),
                "Every file of the group must have a per-file worker output.");
            return fileOutputsByFile[projectRelativePath];
        }

        // Why assert (not tolerate): a row naming a file outside the group means the worker and
        // the group disagree about what was sent, and silently dropping it would hide the edit.
        private static void AssertRowBelongsToGroup(
            HashSet<string> groupPathSet,
            string sourceProjectRelativePath,
            string rowKind)
        {
            Debug.Assert(
                sourceProjectRelativePath != null
                && groupPathSet.Contains(sourceProjectRelativePath),
                "A worker " + rowKind + " row must name a file of the group.");
        }
    }
}
