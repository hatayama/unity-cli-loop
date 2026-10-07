using System.Diagnostics;
using System.IO;

using UnityEditor.Compilation;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Where the compiled assembly and portable PDB for a script live, or why they are missing.
    /// </summary>
    internal sealed class SourcePausePointCompiledAssemblyLocation
    {
        // All four are empty unless Found.
        internal string AssemblyName { get; }
        internal string AssemblyPath { get; }
        internal string SymbolsPath { get; }
        // The script's path in the form the PDB records it: for a package script, the file behind
        // its virtual path, project-relative or absolute when the package lives outside the project;
        // for an Assets script, the located path itself.
        internal string PhysicalPath { get; }
        internal SourcePausePointResolveFailureReason FailureReason { get; }
        internal string FailureMessage { get; }
        internal bool Found => FailureReason == SourcePausePointResolveFailureReason.None;

        private SourcePausePointCompiledAssemblyLocation(
            string assemblyName,
            string assemblyPath,
            string symbolsPath,
            string physicalPath,
            SourcePausePointResolveFailureReason failureReason,
            string failureMessage)
        {
            AssemblyName = assemblyName;
            AssemblyPath = assemblyPath;
            SymbolsPath = symbolsPath;
            PhysicalPath = physicalPath;
            FailureReason = failureReason;
            FailureMessage = failureMessage;
        }

        internal static SourcePausePointCompiledAssemblyLocation FoundAt(
            string assemblyName,
            string assemblyPath,
            string symbolsPath,
            string physicalPath)
        {
            Debug.Assert(
                !string.IsNullOrEmpty(assemblyName) && !string.IsNullOrEmpty(assemblyPath) && !string.IsNullOrEmpty(symbolsPath),
                "a found location must carry the assembly name and both paths.");
            Debug.Assert(!string.IsNullOrEmpty(physicalPath), "a found location must carry the script's physical path.");
            return new SourcePausePointCompiledAssemblyLocation(
                assemblyName,
                assemblyPath,
                symbolsPath,
                physicalPath,
                SourcePausePointResolveFailureReason.None,
                string.Empty);
        }

        internal static SourcePausePointCompiledAssemblyLocation NotFound(
            SourcePausePointResolveFailureReason failureReason,
            string failureMessage)
        {
            Debug.Assert(
                failureReason != SourcePausePointResolveFailureReason.None,
                "a missing location must carry a failure reason.");
            return new SourcePausePointCompiledAssemblyLocation(
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                failureReason,
                failureMessage);
        }
    }

    /// <summary>
    /// Finds the compiled assembly and portable PDB that Unity built a script into, so every
    /// reader of the compiled code derives the same paths.
    /// </summary>
    internal static class SourcePausePointCompiledAssemblyLocator
    {
        /// <summary>
        /// Locates a script named by its asset path (Assets/... or Packages/&lt;package-id&gt;/...),
        /// the only form CompilationPipeline maps to an assembly. Callers normalize any other form
        /// first; the folder path behind a package is reported as belonging to no assembly.
        /// Must be called on the Unity main thread.
        /// </summary>
        internal static SourcePausePointCompiledAssemblyLocation Locate(string projectRelativeFilePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRelativeFilePath), "projectRelativeFilePath must not be null or empty.");

            string normalizedInputPath = SourcePausePointPathNormalizer.ToForwardSlashes(projectRelativeFilePath);
            string rawAssemblyName = CompilationPipeline.GetAssemblyNameFromScriptPath(normalizedInputPath);
            if (string.IsNullOrEmpty(rawAssemblyName))
            {
                return SourcePausePointCompiledAssemblyLocation.NotFound(
                    SourcePausePointResolveFailureReason.ScriptNotInAnyAssembly,
                    $"'{projectRelativeFilePath}' does not belong to any compiled assembly.");
            }

            // CompilationPipeline.GetAssemblyNameFromScriptPath returns the TargetAssembly's file name,
            // which already carries a ".dll" suffix (see Unity's EditorBuildRules.TargetAssembly).
            string assemblyName = Path.GetFileNameWithoutExtension(rawAssemblyName);

            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            string dllPath = Path.Combine(
                projectRoot,
                SourcePausePointConstants.ScriptAssembliesRelativeDirectory,
                assemblyName + SourcePausePointConstants.CompiledAssemblyExtension);
            string pdbPath = Path.Combine(
                projectRoot,
                SourcePausePointConstants.ScriptAssembliesRelativeDirectory,
                assemblyName + SourcePausePointConstants.DebugSymbolsExtension);

            if (!File.Exists(dllPath))
            {
                return SourcePausePointCompiledAssemblyLocation.NotFound(
                    SourcePausePointResolveFailureReason.CompiledAssemblyNotFound,
                    $"Compiled assembly not found at '{dllPath}'. Compile the project first.");
            }

            if (!File.Exists(pdbPath))
            {
                return SourcePausePointCompiledAssemblyLocation.NotFound(
                    SourcePausePointResolveFailureReason.SymbolsUnavailable,
                    $"Debug symbols not found at '{pdbPath}'. Ensure the project uses Debug code optimization.");
            }

            // Asked only once the assembly is found, so a script that is not compiled never reaches
            // the Package Manager.
            string physicalPath = ScriptPackageRoots.ToPhysicalPath(projectRoot, normalizedInputPath);
            return SourcePausePointCompiledAssemblyLocation.FoundAt(assemblyName, dllPath, pdbPath, physicalPath);
        }
    }
}
