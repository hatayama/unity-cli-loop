using System;
using System.Collections.Generic;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Resolves the file identity a worker row carries into the path outcomes report for it.
    /// </summary>
    /// <remarks>
    /// Why a resolver (not one path per stage): a group run returns rows of several files, so a
    /// stage that stamps outcomes with one path would report every skip and failure under
    /// whichever file happened to be handed to it.
    /// </remarks>
    internal sealed class HotReloadGroupFilePaths
    {
        private readonly Dictionary<string, string> assemblyResolvePathsByFile;
        private readonly string firstAssemblyResolvePath;

        internal HotReloadGroupFilePaths(
            IReadOnlyList<(string ProjectRelativePath, string AssemblyResolvePath)> files)
        {
            Debug.Assert(files != null && files.Count > 0, "A group must hold a file.");

            assemblyResolvePathsByFile = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach ((string ProjectRelativePath, string AssemblyResolvePath) file in files)
            {
                Debug.Assert(
                    !string.IsNullOrEmpty(file.ProjectRelativePath),
                    "projectRelativePath must not be empty.");
                Debug.Assert(
                    !string.IsNullOrEmpty(file.AssemblyResolvePath),
                    "assemblyResolvePath must not be empty.");
                assemblyResolvePathsByFile[file.ProjectRelativePath] = file.AssemblyResolvePath;
            }

            firstAssemblyResolvePath = files[0].AssemblyResolvePath;
        }

        internal static HotReloadGroupFilePaths ForSingleFile(
            string projectRelativePath,
            string assemblyResolvePath)
        {
            return new HotReloadGroupFilePaths(
                new List<(string ProjectRelativePath, string AssemblyResolvePath)>
                {
                    (projectRelativePath, assemblyResolvePath)
                });
        }

        internal string ResolveAssemblyResolvePath(string sourceProjectRelativePath)
        {
            if (sourceProjectRelativePath != null
                && assemblyResolvePathsByFile.TryGetValue(
                    sourceProjectRelativePath,
                    out string assemblyResolvePath))
            {
                return assemblyResolvePath;
            }

            // Why fall back to the first file of the group: an outcome without a file path loses
            // the only location the reader has. Every row of a group run names a file of that
            // group, so reaching here means the row set and the group disagree.
            Debug.Assert(false, "A worker row must name a file of the group: " + sourceProjectRelativePath);
            return firstAssemblyResolvePath;
        }
    }
}
