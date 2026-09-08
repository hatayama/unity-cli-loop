using System.Collections.Generic;
using System.Reflection;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The single entry point for every write to a file's hot reload generation state:
    /// starting, registering, removing, and resetting.
    /// </summary>
    /// <remarks>
    /// Why: per-file generation state lives in two registries that must move in lockstep, and
    /// only writes can take them out of step. Reads stay on the registries; routing every write
    /// through one place keeps the two sides consistent, and gives the per-assembly grouping of
    /// the next stage one place to widen from file to group.
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
        /// Starts a new added-member generation for one file, leaving its shim generation alone.
        /// </summary>
        /// <remarks>
        /// Why not BeginFileGeneration: a file that contributed no body to the shim assembly has
        /// no bytes to register a shim generation with. Only the added-member side has stale rows
        /// to drop, so this is the one write that must not move the two registries together.
        /// </remarks>
        internal static void BeginAddedMemberOnlyGeneration(string projectRelativePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");

            HotReloadAddedMemberRegistry.BeginFileGeneration(projectRelativePath);
        }

        /// <summary>
        /// Records an added method's shim in the file's current added-member generation.
        /// </summary>
        internal static void RegisterAddedMethod(
            string projectRelativePath,
            string methodKey,
            MethodInfo shimMethod,
            string filePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");

            HotReloadAddedMemberRegistry.Register(
                projectRelativePath,
                methodKey,
                shimMethod,
                filePath);
        }

        /// <summary>
        /// Records a patched method's shim in the file's current shim generation.
        /// </summary>
        internal static void RegisterShimMethod(
            string projectRelativePath,
            MethodBase originalMethod,
            HotReloadShimRegistry.MethodEntry entry)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");

            HotReloadShimRegistry.RegisterMethod(projectRelativePath, originalMethod, entry);
        }

        /// <summary>
        /// Removes one method's shim from every file generation, keeping the generations.
        /// </summary>
        internal static void RemoveShimMethod(MethodBase originalMethod)
        {
            Debug.Assert(originalMethod != null, "originalMethod must not be null.");

            HotReloadShimRegistry.RemoveMethod(originalMethod);
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

        internal static IReadOnlyList<string> ListPathsWithActiveAddedMembers()
        {
            return HotReloadAddedMemberRegistry.ListPathsWithActiveMembers();
        }
    }
}
