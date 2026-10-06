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

        internal static string DescribeMissingCompiledAssembly(string projectRoot, string dllPath)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty.");
            Debug.Assert(!string.IsNullOrEmpty(dllPath), "dllPath must not be null or empty.");

            if (!IsVirtualPlayerProjectRoot(projectRoot))
            {
                return "Compiled assembly not found at '" + dllPath + "'. Compile the project first.";
            }

            // Why a different reason: a Virtual Player has no compiled assemblies under its own
            // root, so "compile first" gives the same answer however many times it is followed.
            return "Compiled assembly not found at '" + dllPath + "'. This Editor is a Multiplayer Play Mode "
                + "Virtual Player: it loads the script assemblies of the main Editor's project, so hot reload "
                + "cannot patch it yet. The edit reaches this player through a compile; a patch applied to the "
                + "main Editor does not reach it.";
        }
    }
}
