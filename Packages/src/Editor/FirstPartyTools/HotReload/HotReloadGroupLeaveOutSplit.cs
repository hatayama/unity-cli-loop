using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Splits a group into the files a default selection leaves out and the files that go on to
    /// the rerun, and puts the results of both back in the group's order.
    /// </summary>
    /// <remarks>
    /// Why the order matters: the run matches each result to its file by position, so a group
    /// must return one result per file in the order it received them.
    /// </remarks>
    internal sealed class HotReloadGroupLeaveOutSplit
    {
        private readonly IReadOnlyList<HotReloadGroupFile> _files;
        private readonly HashSet<string> _leftOutPaths;

        internal HotReloadGroupLeaveOutSplit(
            IReadOnlyList<HotReloadGroupFile> files,
            IReadOnlyCollection<string> leftOutPaths)
        {
            if (files == null || leftOutPaths == null)
            {
                throw new ArgumentNullException(files == null ? nameof(files) : nameof(leftOutPaths));
            }

            _files = files;
            _leftOutPaths = new HashSet<string>(leftOutPaths, StringComparer.Ordinal);
            List<HotReloadGroupFile> leftOutFiles = new List<HotReloadGroupFile>();
            List<HotReloadGroupFile> remainingFiles = new List<HotReloadGroupFile>();
            foreach (HotReloadGroupFile file in files)
            {
                if (IsLeftOut(file))
                {
                    leftOutFiles.Add(file);
                }
                else
                {
                    remainingFiles.Add(file);
                }
            }

            // Why throw: a left-out path the group does not hold would splice fewer results than
            // the run expects, and a group left without files has nothing for the rerun to report.
            if (leftOutFiles.Count != _leftOutPaths.Count || remainingFiles.Count == 0)
            {
                throw new InvalidOperationException(
                    "A leave-out must name files of the group and keep at least one of them.");
            }

            LeftOutFiles = leftOutFiles;
            RemainingFiles = remainingFiles;
        }

        internal IReadOnlyList<HotReloadGroupFile> LeftOutFiles { get; }

        internal IReadOnlyList<HotReloadGroupFile> RemainingFiles { get; }

        internal List<HotReloadFileProcessResult> Splice(
            IReadOnlyList<HotReloadFileProcessResult> leftOutResults,
            IReadOnlyList<HotReloadFileProcessResult> remainingResults)
        {
            if (leftOutResults.Count != LeftOutFiles.Count || remainingResults.Count != RemainingFiles.Count)
            {
                throw new InvalidOperationException(
                    "Each file of the group must have exactly one result before the results are spliced.");
            }

            List<HotReloadFileProcessResult> results = new List<HotReloadFileProcessResult>(_files.Count);
            int leftOutIndex = 0;
            int remainingIndex = 0;
            foreach (HotReloadGroupFile file in _files)
            {
                if (IsLeftOut(file))
                {
                    results.Add(leftOutResults[leftOutIndex]);
                    leftOutIndex++;
                    continue;
                }

                results.Add(remainingResults[remainingIndex]);
                remainingIndex++;
            }

            return results;
        }

        private bool IsLeftOut(HotReloadGroupFile file)
        {
            return _leftOutPaths.Contains(file.ProjectRelativePath);
        }
    }
}
