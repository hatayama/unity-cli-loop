using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Words the missing-assembly reason for an ordinary project and for a Multiplayer Play Mode
    /// Virtual Player.
    /// </summary>
    internal static class HotReloadVirtualPlayerProject
    {
        // Why the text and the kinds come from one decision: a reason that names a Virtual Player
        // has to arrive with VirtualPlayer set, or the next step would tell the player to compile
        // its own project, which has no assemblies to compile.
        internal static HotReloadFailureDescription DescribeMissingCompiledAssembly(string projectRoot, string dllPath)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty.");
            Debug.Assert(!string.IsNullOrEmpty(dllPath), "dllPath must not be null or empty.");

            if (!CompiledAssemblyLayout.Resolve(projectRoot).IsVirtualPlayer)
            {
                return HotReloadFailureDescription.CompiledAssemblyMissing(
                    "Compiled assembly not found at '" + dllPath + "'. Compile the project first.",
                    isVirtualPlayer: false);
            }

            // Why a different reason: a Virtual Player has no compiled assemblies under its own
            // root, so "compile first" gives the same answer however many times it is followed.
            return HotReloadFailureDescription.CompiledAssemblyMissing(
                "Compiled assembly not found at '" + dllPath + "'. This Editor is a Multiplayer Play Mode "
                + "Virtual Player: it loads the script assemblies of the main Editor's project, so hot reload "
                + "cannot patch it yet. The edit reaches this player through a compile; a patch applied to the "
                + "main Editor does not reach it.",
                isVirtualPlayer: true);
        }
    }
}
