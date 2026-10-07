using System;
using System.IO;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The id of a pause point set at a source location, <c>&lt;asset path&gt;:&lt;line&gt;</c>: built
    /// once when the marker is enabled, and rebuilt from whatever path form a later query names
    /// the file by, so every form reaches the same marker.
    /// </summary>
    internal static class SourcePausePointId
    {
        internal static string Build(string assetPath, int line)
        {
            Debug.Assert(!string.IsNullOrEmpty(assetPath), "assetPath must not be empty.");
            return assetPath + ":" + line;
        }

        /// <summary>
        /// Returns the id a query names its marker by. An id already registered, such as a named
        /// marker, comes back as given. Otherwise an id in the source form has its path rewritten
        /// to the asset path; any other id, and a source id whose path is already the asset path,
        /// comes back unchanged. Must be called on the Unity main thread.
        /// </summary>
        internal static string ToMarkerId(string id, string projectRoot, Func<string, bool> isRegistered)
        {
            Debug.Assert(!string.IsNullOrEmpty(id), "id must not be empty.");
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be empty.");
            Debug.Assert(isRegistered != null, "isRegistered must not be null.");

            // Why first: a named marker may itself read like <path>:<line>, and the rewrite would
            // send its query to an id nobody enabled.
            if (isRegistered(id))
            {
                return id;
            }

            int separator = id.LastIndexOf(':');
            if (separator <= 0 || !int.TryParse(id.Substring(separator + 1), out int line))
            {
                return id;
            }

            string path = id.Substring(0, separator);
            // Why skip the Package Manager for an Assets path: await polls status every second, and
            // such a path is already its asset path.
            if (RewriteLeavesUnchanged(path))
            {
                return id;
            }

            string assetPath = ScriptPathNormalizer.ToAssetPath(path, projectRoot, ScriptPackageRoots.ReadCurrent());
            return assetPath == path ? id : Build(assetPath, line);
        }

        // Why Packages/ is not skipped: a package's folder path also starts with it, and only the
        // package roots tell the two apart. Why . and .. segments are not skipped: enable folds them
        // away, while the CLI sends them as typed.
        private static bool RewriteLeavesUnchanged(string path)
        {
            return !Path.IsPathRooted(path)
                && path.IndexOf('\\') < 0
                && !path.StartsWith("Packages/", StringComparison.Ordinal)
                && !HasDotSegment(path);
        }

        private static bool HasDotSegment(string path)
        {
            foreach (string segment in path.Split('/'))
            {
                if (segment == "." || segment == "..")
                {
                    return true;
                }
            }

            return false;
        }
    }
}
