using System.Collections.Generic;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// One assembly the warm-up loads ahead of the first run: its compiled dll and PDB, and the
    /// compiled dlls of the assemblies that reference it.
    /// </summary>
    internal sealed class HotReloadWarmUpTarget
    {
        internal HotReloadWarmUpTarget(
            string assemblyName,
            string dllPath,
            string pdbPath,
            IReadOnlyList<string> referencingDllPaths)
        {
            Debug.Assert(!string.IsNullOrEmpty(assemblyName), "assemblyName must not be null or empty.");
            Debug.Assert(!string.IsNullOrEmpty(dllPath), "dllPath must not be null or empty.");
            Debug.Assert(!string.IsNullOrEmpty(pdbPath), "pdbPath must not be null or empty.");
            Debug.Assert(referencingDllPaths != null, "referencingDllPaths must not be null.");
            AssemblyName = assemblyName;
            DllPath = dllPath;
            PdbPath = pdbPath;
            ReferencingDllPaths = referencingDllPaths;
        }

        internal string AssemblyName { get; }

        internal string DllPath { get; }

        internal string PdbPath { get; }

        /// <summary>The compiled dlls of the other assemblies that reference this one; may be empty.</summary>
        internal IReadOnlyList<string> ReferencingDllPaths { get; }
    }
}
