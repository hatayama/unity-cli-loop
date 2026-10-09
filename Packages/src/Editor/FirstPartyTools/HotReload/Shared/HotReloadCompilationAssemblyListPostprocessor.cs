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
        // Assets whose import, deletion or move changes what CompilationPipeline.GetAssemblies()
        // answers: sources, assembly definitions and references, and precompiled references.
        private static readonly string[] AssemblyListExtensions = { ".cs", ".asmdef", ".asmref", ".dll" };

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
            Debug.Assert(importedAssets != null, "importedAssets must not be null.");
            Debug.Assert(deletedAssets != null, "deletedAssets must not be null.");
            Debug.Assert(movedAssets != null, "movedAssets must not be null.");
            Debug.Assert(movedFromAssetPaths != null, "movedFromAssetPaths must not be null.");

            // Why keep the list after a reload: the new domain fills it after the reload, from the
            // list the compile produced, so the import that triggered that compile has nothing newer.
            if (didDomainReload)
            {
                return;
            }

            if (!ChangesTheAssemblyList(importedAssets)
                && !ChangesTheAssemblyList(deletedAssets)
                && !ChangesTheAssemblyList(movedAssets)
                && !ChangesTheAssemblyList(movedFromAssetPaths))
            {
                return;
            }

            HotReloadCompilationAssemblies.DropForScriptSetChange();
        }

        private static bool ChangesTheAssemblyList(string[] assetPaths)
        {
            foreach (string assetPath in assetPaths)
            {
                string extension = Path.GetExtension(assetPath);
                foreach (string candidate in AssemblyListExtensions)
                {
                    if (string.Equals(extension, candidate, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
