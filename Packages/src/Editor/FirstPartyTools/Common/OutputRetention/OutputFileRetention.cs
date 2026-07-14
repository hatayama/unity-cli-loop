using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Keeps output directories from growing without bound by deleting oldest matching files.
    /// </summary>
    public static class OutputFileRetention
    {
        public const int MAX_FILES_PER_DIRECTORY = 20;

        /// <summary>
        /// Deletes matching files older than the newest MAX_FILES_PER_DIRECTORY entries.
        /// Call after a successful write so the just-saved file is counted toward the limit.
        /// </summary>
        /// <param name="directory">Directory that already contains the newly written file.</param>
        /// <param name="searchPattern">Glob used to count and delete (for example "*.png").</param>
        public static void DeleteOldestBeyondLimit(string directory, string searchPattern)
        {
            Debug.Assert(!string.IsNullOrEmpty(directory), "directory must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(searchPattern), "searchPattern must not be null or empty");

            if (!Directory.Exists(directory))
            {
                return;
            }

            FileInfo[] matchingFiles = new DirectoryInfo(directory).GetFiles(searchPattern);
            if (matchingFiles.Length <= MAX_FILES_PER_DIRECTORY)
            {
                return;
            }

            // Newest first; file name is the tie-breaker so equal timestamps stay deterministic.
            List<FileInfo> orderedNewestFirst = matchingFiles
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ThenByDescending(file => file.Name, StringComparer.Ordinal)
                .ToList();

            for (int i = MAX_FILES_PER_DIRECTORY; i < orderedNewestFirst.Count; i++)
            {
                orderedNewestFirst[i].Delete();
            }
        }
    }
}
