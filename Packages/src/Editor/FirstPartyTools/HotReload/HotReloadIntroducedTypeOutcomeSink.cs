using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Routes type outcomes and type notices to the per-file buffers of the file they belong to.
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

        /// <summary>
        /// Appends the notices to the warnings of the file that declares them, each prefixed with
        /// that file so a run over several files says which one the notice is about.
        /// </summary>
        internal static void AppendNotices(
            IReadOnlyList<HotReloadGroupFile> files,
            IReadOnlyList<HotReloadIntroducedTypeNotice> notices)
        {
            if (notices == null || notices.Count == 0)
            {
                return;
            }

            foreach (HotReloadIntroducedTypeNotice notice in notices)
            {
                HotReloadGroupFile carrier = FindCarrier(files, notice.OwnerProjectRelativePath);
                carrier.Sinks.Warnings.Add(
                    string.IsNullOrEmpty(notice.OwnerProjectRelativePath)
                        ? notice.Text
                        : notice.OwnerProjectRelativePath + ": " + notice.Text);
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
