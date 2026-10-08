using System;
using System.IO;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Checks that the compilation pipeline puts an assembly where hot reload reads it from.
    /// </summary>
    internal static class HotReloadCompiledAssemblyPathCheck
    {
        // Why only a check: hot reload decides where an assembly lives from the root's shape, since
        // most places that need the path have only the assembly's name. Comparing here keeps that
        // decision from silently reading another DLL if Unity ever writes assemblies elsewhere.
        internal static HotReloadFailureDescription DescribeOutputPathMismatch(
            CompiledAssemblyLayout layout,
            string assemblyName,
            string outputPath)
        {
            Debug.Assert(layout != null, "layout must not be null.");
            Debug.Assert(!string.IsNullOrEmpty(assemblyName), "assemblyName must not be null or empty.");

            if (string.IsNullOrEmpty(outputPath))
            {
                return null;
            }

            // Path.Combine returns an absolute outputPath unchanged, so one expression covers both forms.
            string reported = Path.GetFullPath(Path.Combine(layout.ProjectRoot, outputPath));
            string expected = Path.GetFullPath(layout.DllPath(assemblyName));
            // Windows paths are case-insensitive, as ReferencePublicizer compares them.
            StringComparison comparison = Application.platform == RuntimePlatform.WindowsEditor
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (string.Equals(reported, expected, comparison))
            {
                return null;
            }

            return HotReloadFailureDescription.CompiledAssemblyMissing(
                "The compilation pipeline reports '" + assemblyName + "' at '" + reported
                + "', but hot reload reads compiled assemblies from '" + layout.CompiledAssembliesDirectory
                + "'. Compile the project and retry; if this persists, the project layout is one hot reload does not know.",
                isVirtualPlayer: layout.IsVirtualPlayer);
        }
    }
}
