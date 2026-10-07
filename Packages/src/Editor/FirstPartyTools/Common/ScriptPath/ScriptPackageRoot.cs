using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// One package's physical folder and the virtual path Unity exposes it under.
    /// </summary>
    internal sealed class ScriptPackageRoot
    {
        internal ScriptPackageRoot(string resolvedPath, string assetPath)
        {
            Debug.Assert(!string.IsNullOrEmpty(resolvedPath), "resolvedPath must not be empty.");
            Debug.Assert(!string.IsNullOrEmpty(assetPath), "assetPath must not be empty.");
            ResolvedPath = resolvedPath;
            AssetPath = assetPath;
        }

        /// <summary>Absolute folder the package's files actually live in.</summary>
        internal string ResolvedPath { get; }

        /// <summary>Virtual folder Unity's script APIs expect, in the form Packages/&lt;package-id&gt;.</summary>
        internal string AssetPath { get; }
    }
}
