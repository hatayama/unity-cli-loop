using System;
using System.IO;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Recognizes a Multiplayer Play Mode Virtual Player by its project root, and words the
    /// missing-assembly reason for it.
    /// </summary>
    internal static class HotReloadVirtualPlayerProject
    {
        private const string LibraryDirectoryName = "Library";
        private const string VirtualPlayersDirectoryName = "VP";

        // A Virtual Player's project root is <main project>/Library/VP/<player directory>.
        internal static bool IsVirtualPlayerProjectRoot(string projectRoot)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty.");

            // Why trim first: with a trailing separator, Path.GetDirectoryName returns the same
            // directory, so every parent lookup below would land one level too low.
            string trimmedRoot = projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            // A root made only of separators has no parent, and Path.GetDirectoryName rejects the empty string.
            if (trimmedRoot.Length == 0)
            {
                return false;
            }

            string virtualPlayersDirectory = Path.GetDirectoryName(trimmedRoot);
            if (string.IsNullOrEmpty(virtualPlayersDirectory))
            {
                return false;
            }

            if (!string.Equals(
                    Path.GetFileName(virtualPlayersDirectory),
                    VirtualPlayersDirectoryName,
                    StringComparison.Ordinal))
            {
                return false;
            }

            string libraryDirectory = Path.GetDirectoryName(virtualPlayersDirectory);
            if (string.IsNullOrEmpty(libraryDirectory))
            {
                return false;
            }

            return string.Equals(Path.GetFileName(libraryDirectory), LibraryDirectoryName, StringComparison.Ordinal);
        }

        // Why the text and the kinds come from one decision: a reason that names a Virtual Player
        // has to arrive with VirtualPlayer set, or the next step would tell the player to compile
        // its own project, which has no assemblies to compile.
        internal static HotReloadFailureDescription DescribeMissingCompiledAssembly(string projectRoot, string dllPath)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty.");
            Debug.Assert(!string.IsNullOrEmpty(dllPath), "dllPath must not be null or empty.");

            if (!IsVirtualPlayerProjectRoot(projectRoot))
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
