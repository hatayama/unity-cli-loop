using System.Collections.Generic;
using System.Reflection;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The single entry point for starting and resetting a file's hot reload generation state.
    /// </summary>
    /// <remarks>
    /// Why: per-file generation state lives in two registries that must start and reset together.
    /// Routing both through one place keeps them in lockstep, and gives the per-assembly grouping
    /// of the next stage one place to widen from file to group.
    /// Static because the registries it fronts are static; it adds no state of its own.
    /// </remarks>
    internal static class HotReloadFileGenerations
    {
        /// <summary>
        /// Starts a new generation for one file, replacing whatever the previous apply left.
        /// </summary>
        internal static void BeginFileGeneration(
            string projectRelativePath,
            byte[] assemblyBytes,
            byte[] pdbBytes,
            Assembly loadedAssembly)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");

            HotReloadShimRegistry.BeginFileGeneration(
                projectRelativePath,
                assemblyBytes,
                pdbBytes,
                loadedAssembly);
            HotReloadAddedMemberRegistry.BeginFileGeneration(projectRelativePath);
        }

        /// <summary>
        /// Drops the generation state of every file. Paired with a full revert.
        /// </summary>
        internal static void ClearAll()
        {
            HotReloadShimRegistry.Clear();
            HotReloadAddedMemberRegistry.Clear();
        }

        /// <summary>
        /// Lists the method keys of the added members currently active for one file.
        /// </summary>
        internal static IReadOnlyList<string> ListActiveAddedMethodKeys(string projectRelativePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");

            return HotReloadAddedMemberRegistry.ListActiveMethodKeys(projectRelativePath);
        }
    }
}
