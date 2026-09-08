using System;
using System.Collections.Generic;

using UnityEngine;

using UpmPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// One package's physical folder and the virtual path Unity exposes it under.
    /// </summary>
    internal sealed class HotReloadPackageRoot
    {
        internal HotReloadPackageRoot(string resolvedPath, string assetPath)
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

    /// <summary>
    /// Captures the package folder mapping on the Unity main thread so path normalization can run
    /// on the background threads the hot-reload run switches to.
    /// </summary>
    internal static class HotReloadPackageRootProvider
    {
        // Why a replaceable seam: the real mapping is Editor process state, while tests must be able
        // to prove the normalization against package roots that this project does not contain.
        internal static Func<IReadOnlyList<HotReloadPackageRoot>> CaptureForTesting = Capture;

        private static IReadOnlyList<HotReloadPackageRoot> _current;

        /// <summary>Refreshes the mapping. Must be called from the Unity main thread.</summary>
        internal static void CaptureCurrent()
        {
            _current = CaptureForTesting();
        }

        internal static IReadOnlyList<HotReloadPackageRoot> Current
        {
            get
            {
                // Fail fast rather than capturing lazily: PackageInfo is main-thread only, so a
                // lazy capture would succeed or throw depending on which thread first asked, and a
                // caller that never passed through the run entry point would be silently tolerated.
                if (_current == null)
                {
                    throw new InvalidOperationException(
                        "Hot-reload package roots were never captured. Call "
                        + nameof(HotReloadPackageRootProvider) + "." + nameof(CaptureCurrent)
                        + " on the Unity main thread before normalizing script paths.");
                }

                return _current;
            }
        }

        private static IReadOnlyList<HotReloadPackageRoot> Capture()
        {
            UpmPackageInfo[] packages = UpmPackageInfo.GetAllRegisteredPackages();
            List<HotReloadPackageRoot> roots = new List<HotReloadPackageRoot>(packages.Length);
            foreach (UpmPackageInfo package in packages)
            {
                if (string.IsNullOrEmpty(package.resolvedPath) || string.IsNullOrEmpty(package.assetPath))
                {
                    continue;
                }

                roots.Add(new HotReloadPackageRoot(package.resolvedPath, package.assetPath));
            }

            return roots;
        }
    }
}
