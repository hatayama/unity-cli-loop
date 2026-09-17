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
            string unimportedAsmdefProjectRelativePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(assemblyName), "assemblyName must not be empty.");
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");

            if (compilationAssembly == null)
            {
                if (!string.IsNullOrEmpty(unimportedAsmdefProjectRelativePath))
                {
                    return string.Format(
                        HotReloadConstants.UnimportedAsmdefCompilationAssemblyNotFoundReasonFormat,
                        assemblyName,
                        projectRelativePath,
                        unimportedAsmdefProjectRelativePath);
                }

                // Why after the .asmdef branch: Unity maps a script under a not-yet-imported
                // .asmdef onto a predefined assembly too, so deciding on the name first would
                // hide the .asmdef the reader actually has to import.
                if (IsPredefinedAssemblyName(assemblyName))
                {
                    return string.Format(
                        HotReloadConstants.PredefinedAssemblyNotCompiledReasonFormat,
                        assemblyName);
                }

                return string.Format(
                    HotReloadConstants.CompilationAssemblyNotFoundReasonFormat,
                    assemblyName);
            }

            return null;
        }

        /// <summary>
        /// Whether Unity creates this assembly itself rather than an .asmdef declaring it.
        /// </summary>
        internal static bool IsPredefinedAssemblyName(string assemblyName)
        {
            return string.Equals(assemblyName, "Assembly-CSharp", StringComparison.Ordinal)
                || string.Equals(assemblyName, "Assembly-CSharp-Editor", StringComparison.Ordinal)
                || string.Equals(assemblyName, "Assembly-CSharp-firstpass", StringComparison.Ordinal)
                || string.Equals(assemblyName, "Assembly-CSharp-Editor-firstpass", StringComparison.Ordinal);
        }

        // Why disk scan instead of CompilationPipeline: GetAssemblyDefinitionFilePathFromScriptPath
        // returns null for a not-yet-imported .asmdef, so only the on-disk ancestor file can
        // distinguish that case from a true predefined-assembly script. Why the path rather than
        // a flag: the reason text names the file the reader has to import, and the resolved
        // assembly name alone reads as a contradiction with "sits under a .asmdef".
        internal static string FindAncestorAsmdefProjectRelativePath(string scriptPath)
        {
            Debug.Assert(!string.IsNullOrEmpty(scriptPath), "scriptPath must not be empty.");

            string directory = Path.GetDirectoryName(Path.GetFullPath(scriptPath));
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            StringComparison comparison = Application.platform == RuntimePlatform.WindowsEditor
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            while (!string.IsNullOrEmpty(directory))
            {
                if (Directory.Exists(directory))
                {
                    string[] asmdefFiles =
                        Directory.GetFiles(directory, "*.asmdef", SearchOption.TopDirectoryOnly);
                    if (asmdefFiles.Length > 0)
                    {
                        return Path.GetRelativePath(projectRoot, asmdefFiles[0]).Replace('\\', '/');
                    }
                }

                if (string.Equals(Path.GetFullPath(directory), projectRoot, comparison))
                {
                    return null;
                }

                directory = Path.GetDirectoryName(directory);
            }

            return null;
        }

        internal static bool ContainsProjectRelativeSourceFile(string[] sourceFiles, string projectRelativePath)
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
