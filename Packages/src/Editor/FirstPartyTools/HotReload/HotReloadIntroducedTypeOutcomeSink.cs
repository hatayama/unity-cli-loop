using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Routes type outcomes to the per-file buffer of the file that declares them.
    /// </summary>
    /// <remarks>
    /// Why routed rather than kept per run: the run result is merged from the per-file results, so
    /// a row that reached no file's buffer would never reach the response. The file that carries a
    /// row and the file the row names are separate concerns — an unattributed row still has to
    /// travel on some file, and it keeps its own empty path while doing so.
    /// </remarks>
    internal static class HotReloadIntroducedTypeOutcomeSink
    {
        internal static void Append(
            IReadOnlyList<HotReloadGroupFile> files,
            IReadOnlyList<HotReloadIntroducedTypeOutcome> outcomes)
        {
            if (outcomes == null || outcomes.Count == 0)
            {
                return;
            }

            foreach (HotReloadIntroducedTypeOutcome outcome in outcomes)
            {
                FindCarrier(files, outcome.OwnerProjectRelativePath).Sinks.IntroducedTypes.Add(outcome);
            }
        }

        // The first file of the group carries what the group cannot attribute, which is also where
        // the group-level failure rows of the other stages travel.
        private static HotReloadGroupFile FindCarrier(
            IReadOnlyList<HotReloadGroupFile> files,
            string ownerProjectRelativePath)
        {
            foreach (HotReloadGroupFile file in files)
            {
                if (string.Equals(file.ProjectRelativePath, ownerProjectRelativePath, StringComparison.Ordinal))
                {
                    return file;
                }
            }

            return files[0];
        }
    }
}
