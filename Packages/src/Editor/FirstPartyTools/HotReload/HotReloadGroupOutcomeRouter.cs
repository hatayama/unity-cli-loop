using System;
using System.Collections.Generic;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Delivers the outcomes a group-level stage produced to the sinks of the file each outcome
    /// belongs to, and reports a group-wide failure once per file.
    /// </summary>
    /// <remarks>
    /// Why by file path: the gate, the shim compile and the isolation retry all see the whole
    /// group, so they return one flat list. The reported unit stays the single file, and the file
    /// each outcome names is the path it already carries.
    /// </remarks>
    internal static class HotReloadGroupOutcomeRouter
    {
        internal static void AppendByFilePath(
            IReadOnlyList<HotReloadGroupFile> files,
            IReadOnlyList<HotReloadMethodOutcome> outcomes)
        {
            Debug.Assert(files != null && files.Count > 0, "A group must hold a file.");
            if (outcomes == null || outcomes.Count == 0)
            {
                return;
            }

            Dictionary<string, HotReloadGroupFile> filesByAssemblyResolvePath =
                new Dictionary<string, HotReloadGroupFile>(StringComparer.Ordinal);
            foreach (HotReloadGroupFile file in files)
            {
                filesByAssemblyResolvePath[file.AssemblyResolvePath] = file;
            }

            foreach (HotReloadMethodOutcome outcome in outcomes)
            {
                if (outcome.FilePath != null
                    && filesByAssemblyResolvePath.TryGetValue(
                        outcome.FilePath,
                        out HotReloadGroupFile owningFile))
                {
                    owningFile.Sinks.Outcomes.Add(outcome);
                    continue;
                }

                // Why the first file rather than dropping the row: an outcome the reader never
                // sees is worse than one filed under a sibling of the same group, and reaching
                // here means a stage stamped a path the group does not contain.
                Debug.Assert(false, "A group outcome must name a file of the group: " + outcome.FilePath);
                files[0].Sinks.Outcomes.Add(outcome);
            }
        }

        // Why one row per file: a stage that ran for the whole group cannot say which file broke,
        // and a run response lists results per file, so a single row would leave the other files
        // of the group looking untouched.
        internal static void AppendGroupFailure(
            IReadOnlyList<HotReloadGroupFile> files,
            string methodLabel,
            string reason)
        {
            Debug.Assert(files != null && files.Count > 0, "A group must hold a file.");

            foreach (HotReloadGroupFile file in files)
            {
                file.Sinks.Outcomes.Add(
                    HotReloadMethodOutcome.Failed(methodLabel, reason, file.AssemblyResolvePath));
            }
        }
    }
}
