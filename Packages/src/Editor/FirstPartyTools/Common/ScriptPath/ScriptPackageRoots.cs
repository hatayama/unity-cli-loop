using System;
using System.Collections.Generic;
using System.IO;

using UnityEngine;

using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Asks the Package Manager where the registered packages live. Both members read Package
    /// Manager state, so both must be called on the Unity main thread.
    /// </summary>
    internal static class ScriptPackageRoots
    {
        /// <summary>
        /// Lists every registered package that has both a folder on disk and a virtual path.
        /// </summary>
        internal static IReadOnlyList<ScriptPackageRoot> ReadCurrent()
        {
            PackageManagerPackageInfo[] packages = PackageManagerPackageInfo.GetAllRegisteredPackages();
            List<ScriptPackageRoot> roots = new List<ScriptPackageRoot>(packages.Length);
            foreach (PackageManagerPackageInfo package in packages)
            {
                if (string.IsNullOrEmpty(package.resolvedPath) || string.IsNullOrEmpty(package.assetPath))
                {
                    continue;
                }

                roots.Add(new ScriptPackageRoot(package.resolvedPath, package.assetPath));
            }

            return roots;
        }

        /// <summary>
        /// Returns the path of the file behind a script's asset path: project-relative when the
        /// file is in the project, absolute when its package lives outside it, and the input itself
        /// when no package owns the path, as for Assets. This is the form a portable PDB records
        /// for the document, and Path.Combine(projectRoot, result) reaches the file on disk.
        /// </summary>
        internal static string ToPhysicalPath(string projectRoot, string slashNormalizedAssetPath)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be empty.");
            Debug.Assert(!string.IsNullOrEmpty(slashNormalizedAssetPath), "slashNormalizedAssetPath must not be empty.");

            PackageManagerPackageInfo package = PackageManagerPackageInfo.FindForAssetPath(slashNormalizedAssetPath);
            if (package == null || string.IsNullOrEmpty(package.resolvedPath) || string.IsNullOrEmpty(package.assetPath))
            {
                return slashNormalizedAssetPath;
            }

            ScriptPackageRoot root = new ScriptPackageRoot(package.resolvedPath, package.assetPath);
            StringComparison comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return ScriptPathNormalizer.ToPhysicalProjectRelative(
                slashNormalizedAssetPath,
                projectRoot,
                new[] { root },
                comparison);
        }
    }
}
