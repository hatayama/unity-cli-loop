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
        internal static HotReloadFailureDescription DescribeMissingCompiledAssembly(
            CompiledAssemblyLayout layout,
            string dllPath)
        {
            Debug.Assert(layout != null, "layout must not be null.");
            Debug.Assert(!string.IsNullOrEmpty(dllPath), "dllPath must not be null or empty.");

            if (!layout.IsVirtualPlayer)
            {
                return HotReloadFailureDescription.CompiledAssemblyMissing(
                    "Compiled assembly not found at '" + dllPath + "'. Compile the project first.",
                    isVirtualPlayer: false);
            }

            // Why a different reason: a Virtual Player cannot compile its own assemblies, so only a
            // compile of the main Editor's project puts the missing assembly where it reads from.
            return HotReloadFailureDescription.CompiledAssemblyMissing(
                "Compiled assembly not found at '" + dllPath + "'. This Editor is a Multiplayer Play Mode "
                + "Virtual Player: it loads the script assemblies of the main Editor's project at '"
                + layout.MainProjectRoot + "', and that project has not compiled this assembly yet. Compile the "
                + "main Editor's project first; a patch applied to the main Editor does not reach this player.",
                isVirtualPlayer: true);
        }
    }
}
