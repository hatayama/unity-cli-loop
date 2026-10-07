using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Lists that a group processor derives from the files of one group.
    /// </summary>
    internal static class HotReloadGroupFileLists
    {
        // Why a file that declares a new type never counts as enum-only: its artifact is prepared
        // against the whole group, and leaving its owner out would drop it from the commit.
        internal static List<HotReloadLeaveOutFile> DescribeLeaveOutFiles(IReadOnlyList<HotReloadGroupFile> files)
        {
            List<HotReloadLeaveOutFile> leaveOutFiles = new List<HotReloadLeaveOutFile>(files.Count);
            foreach (HotReloadGroupFile file in files)
            {
                leaveOutFiles.Add(new HotReloadLeaveOutFile(
                    file.ProjectRelativePath,
                    file.IsDefaultSelected,
                    file.DeclaresIntroducedType || file.DeclaresRefusedIntroducedType));
            }

            return leaveOutFiles;
        }

        internal static List<string> CollectProjectRelativePaths(IReadOnlyList<HotReloadGroupFile> files)
        {
            List<string> projectRelativePaths = new List<string>(files.Count);
            foreach (HotReloadGroupFile file in files)
            {
                projectRelativePaths.Add(file.ProjectRelativePath);
            }

            return projectRelativePaths;
        }
    }
}
