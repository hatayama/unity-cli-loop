using System;
using System.Collections.Generic;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Maps a script path between the file on disk and the project-relative form that
    /// CompilationPipeline.GetAssemblyNameFromScriptPath accepts, in either direction.
    /// </summary>
    internal static class HotReloadScriptPathNormalizer
    {
        internal static string ToProjectRelative(
            string fullPath,
            string projectRoot,
            IReadOnlyList<HotReloadPackageRoot> packageRoots,
            StringComparison comparison)
        {
            Debug.Assert(!string.IsNullOrEmpty(fullPath), "fullPath must not be empty.");
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be empty.");
            Debug.Assert(packageRoots != null, "packageRoots must not be null.");

            string normalized = fullPath.Replace('\\', '/');
            // Package folders are matched before the project root: an embedded package lives under
            // the project root, and stripping the root would yield the physical Packages/<folder>
            // path, which resolves to the wrong assembly.
            string packageRelative = TryMapToPackagePath(normalized, packageRoots, comparison);
            if (packageRelative != null)
            {
                return packageRelative;
            }

            string root = WithTrailingSlash(projectRoot.Replace('\\', '/'));
            // Assets has no virtual mapping of its own, so it stays absolute and needs the root removed.
            if (normalized.StartsWith(root, comparison))
            {
                return normalized.Substring(root.Length);
            }

            // Outside the project: returned as is so GetAssemblyNameFromScriptPath yields an empty
            // name and the caller reports the existing "not part of any compiled assembly" failure.
            return normalized;
        }

        private static string TryMapToPackagePath(
            string normalized,
            IReadOnlyList<HotReloadPackageRoot> packageRoots,
            StringComparison comparison)
        {
            foreach (HotReloadPackageRoot packageRoot in packageRoots)
            {
                string resolvedRoot = WithTrailingSlash(packageRoot.ResolvedPath.Replace('\\', '/'));
                if (!normalized.StartsWith(resolvedRoot, comparison))
                {
                    continue;
                }

                string assetRoot = WithTrailingSlash(packageRoot.AssetPath.Replace('\\', '/'));
                return assetRoot + normalized.Substring(resolvedRoot.Length);
            }

            return null;
        }

        /// <summary>
        /// Turns a project-relative asset path into the path of the file behind it: the
        /// project-relative physical path, or the absolute one when the package lives outside
        /// the project. A path under no package root comes back with forward slashes only.
        /// </summary>
        internal static string ToPhysicalProjectRelative(
            string assetRelativePath,
            string projectRoot,
            IReadOnlyList<HotReloadPackageRoot> packageRoots,
            StringComparison comparison)
        {
            Debug.Assert(!string.IsNullOrEmpty(assetRelativePath), "assetRelativePath must not be empty.");
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be empty.");
            Debug.Assert(packageRoots != null, "packageRoots must not be null.");

            string normalized = assetRelativePath.Replace('\\', '/');
            foreach (HotReloadPackageRoot packageRoot in packageRoots)
            {
                // Matched with the trailing slash so Packages/io.example.pkg does not claim a file of
                // Packages/io.example.pkg.extra.
                string assetRoot = WithTrailingSlash(packageRoot.AssetPath.Replace('\\', '/'));
                if (!normalized.StartsWith(assetRoot, comparison))
                {
                    continue;
                }

                string physical = WithTrailingSlash(packageRoot.ResolvedPath.Replace('\\', '/'))
                    + normalized.Substring(assetRoot.Length);
                string root = WithTrailingSlash(projectRoot.Replace('\\', '/'));
                // A package outside the project has no project-relative path, so the absolute one stands.
                return physical.StartsWith(root, comparison) ? physical.Substring(root.Length) : physical;
            }

            // Assets has no virtual root, so its asset path already names the file itself.
            return normalized;
        }

        private static string WithTrailingSlash(string path)
        {
            return path.EndsWith("/", StringComparison.Ordinal) ? path : path + "/";
        }
    }
}
