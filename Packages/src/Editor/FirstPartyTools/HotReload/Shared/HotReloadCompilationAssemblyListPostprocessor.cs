using System;
using System.IO;

using UnityEditor;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Drops the compilation assembly list when an import changed the script set without a domain
    /// reload. Why not EditorApplication.projectChanged: Unity raises it for every import, including
    /// the one whose compile just reloaded the domain, and after the new domain's startup capture
    /// has filled the list; dropping it there made the first hot reload after every compile ask
    /// Unity for the list again (0.7-1.7 s on a project with several hundred assemblies).
    /// </summary>
    internal sealed class HotReloadCompilationAssemblyListPostprocessor : AssetPostprocessor
    {
        // Called by Unity through reflection after every import batch. Why this five-parameter
        // form and no four-parameter one: Unity calls the four-parameter form when both exist,
        // and only this one says whether the batch's compile already reloaded the domain.
        internal static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths,
            bool didDomainReload)
        {
        }
    }
}
