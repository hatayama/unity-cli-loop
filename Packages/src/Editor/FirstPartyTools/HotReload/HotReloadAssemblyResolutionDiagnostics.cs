using System;
using System.IO;

using UnityEngine;

using UnityCompilationAssembly = UnityEditor.Compilation.Assembly;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds Failed reasons when hot-reload cannot target the resolved compilation assembly.
    /// </summary>
    internal static class HotReloadAssemblyResolutionDiagnostics
    {
        internal static string TryGetAssemblyResolutionFailureReason(
            string assemblyName,
            UnityCompilationAssembly compilationAssembly,
            string projectRelativePath,
            bool hasUnimportedAsmdefOnDisk)
        {
            Debug.Assert(!string.IsNullOrEmpty(assemblyName), "assemblyName must not be empty.");
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");

            if (compilationAssembly == null)
            {
                if (hasUnimportedAsmdefOnDisk)
                {
                    return string.Format(
                        HotReloadConstants.UnimportedAsmdefCompilationAssemblyNotFoundReasonFormat,
                        assemblyName,
                        projectRelativePath);
                }

                return string.Format(
                    HotReloadConstants.CompilationAssemblyNotFoundReasonFormat,
                    assemblyName);
            }

            if (!ContainsProjectRelativeSourceFile(compilationAssembly.sourceFiles, projectRelativePath))
            {
                return string.Format(
                    HotReloadConstants.SourceFileNotInCompiledAssemblyReasonFormat,
                    projectRelativePath,
                    compilationAssembly.name);
            }

            return null;
        }

        // Why disk scan instead of CompilationPipeline: GetAssemblyDefinitionFilePathFromScriptPath
        // returns null for a not-yet-imported .asmdef, so only the on-disk ancestor file can
        // distinguish that case from a true predefined-assembly script.
        internal static bool AncestorDirectoryContainsAsmdef(string scriptPath)
        {
            Debug.Assert(!string.IsNullOrEmpty(scriptPath), "scriptPath must not be empty.");

            string directory = Path.GetDirectoryName(Path.GetFullPath(scriptPath));
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            StringComparison comparison = Application.platform == RuntimePlatform.WindowsEditor
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            while (!string.IsNullOrEmpty(directory))
            {
                if (Directory.Exists(directory)
                    && Directory.GetFiles(directory, "*.asmdef", SearchOption.TopDirectoryOnly).Length > 0)
                {
                    return true;
                }

                if (string.Equals(Path.GetFullPath(directory), projectRoot, comparison))
                {
                    return false;
                }

                directory = Path.GetDirectoryName(directory);
            }

            return false;
        }

        private static bool ContainsProjectRelativeSourceFile(string[] sourceFiles, string projectRelativePath)
        {
            if (sourceFiles == null || sourceFiles.Length == 0)
            {
                return false;
            }

            string normalizedTarget = projectRelativePath.Replace('\\', '/');
            StringComparison comparison = Application.platform == RuntimePlatform.WindowsEditor
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            for (int index = 0; index < sourceFiles.Length; index++)
            {
                string sourceFile = sourceFiles[index];
                if (string.IsNullOrEmpty(sourceFile))
                {
                    continue;
                }

                if (string.Equals(sourceFile.Replace('\\', '/'), normalizedTarget, comparison))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
