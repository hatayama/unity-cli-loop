using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Serializes source batches for the shared worker and one-shot Roslyn processes.
    /// </summary>
    internal static class RoslynCompilerRequestFileWriter
    {
        internal static void WriteCompilerResponseFile(
            string responseFilePath,
            string sourcePath,
            string dllPath,
            IReadOnlyCollection<string> references,
            IReadOnlyCollection<string> defineSymbols,
            bool allowUnsafeCode,
            bool emitDebugCode)
        {
            List<string> lines = CreateCompilerOptions(dllPath, allowUnsafeCode, emitDebugCode);
            // csc resolves relative #line document paths from the source directory and otherwise
            // writes work-directory absolute URLs into PDB files. Map that directory to the project
            // root so PDB documents identify project files even if external code changes the CWD.
            // UnityCliLoopPathResolver owns the project-root value as the single source of truth.
            string sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(sourcePath));
            lines.Add(QuoteArgument("-pathmap:", sourceDirectory + "=" + UnityCliLoopPathResolver.GetProjectRoot()));
            AddDefines(lines, defineSymbols);
            AddReferences(lines, references);
            lines.Add(QuotePath(sourcePath));
            File.WriteAllLines(responseFilePath, lines);
        }

        internal static void WriteMultipleSourcesCompilerResponseFile(
            string responseFilePath,
            IReadOnlyList<string> sourcePaths,
            string dllPath,
            IReadOnlyCollection<string> references,
            IReadOnlyCollection<string> defineSymbols,
            bool allowUnsafeCode,
            bool emitDebugCode)
        {
            ValidateSourcePaths(sourcePaths);
            WriteCompilerResponseFile(responseFilePath, sourcePaths[0], dllPath, references, defineSymbols, allowUnsafeCode, emitDebugCode);
            List<string> lines = new List<string>(File.ReadAllLines(responseFilePath));
            for (int index = 1; index < sourcePaths.Count; index++)
            {
                lines.Add(QuotePath(sourcePaths[index]));
            }

            File.WriteAllLines(responseFilePath, lines);
        }

        internal static void WriteWorkerRequestFile(
            string requestFilePath,
            string sourcePath,
            string dllPath,
            IReadOnlyCollection<string> references,
            IReadOnlyCollection<string> defineSymbols,
            bool allowUnsafeCode,
            bool emitDebugCode)
        {
            List<string> lines = new List<string>
            {
                Path.GetFullPath(sourcePath),
                Path.GetFullPath(dllPath),
                allowUnsafeCode ? "unsafe:1" : "unsafe:0",
                emitDebugCode ? "debugCode:1" : "debugCode:0"
            };
            AddWorkerDefines(lines, defineSymbols);
            AddWorkerReferences(lines, references);
            File.WriteAllLines(Path.GetFullPath(requestFilePath), lines);
        }

        internal static void WriteMultipleSourcesWorkerRequestFile(
            string requestFilePath,
            IReadOnlyList<string> sourcePaths,
            string dllPath,
            IReadOnlyCollection<string> references,
            IReadOnlyCollection<string> defineSymbols,
            bool allowUnsafeCode,
            bool emitDebugCode)
        {
            ValidateSourcePaths(sourcePaths);
            WriteWorkerRequestFile(requestFilePath, sourcePaths[0], dllPath, references, defineSymbols, allowUnsafeCode, emitDebugCode);
            List<string> lines = new List<string>(File.ReadAllLines(requestFilePath));
            for (int index = 1; index < sourcePaths.Count; index++)
            {
                string fullPath = Path.GetFullPath(sourcePaths[index]);
                lines.Add("source-base64:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(fullPath)));
            }

            File.WriteAllLines(requestFilePath, lines);
        }

        internal static void ValidateSourcePaths(IReadOnlyList<string> sourcePaths)
        {
            if (sourcePaths == null || sourcePaths.Count == 0)
            {
                throw new ArgumentException("Source paths must not be empty.", nameof(sourcePaths));
            }

            StringComparer pathComparer = CreatePathComparer();
            HashSet<string> paths = new HashSet<string>(pathComparer);
            string sourceDirectory = null;
            foreach (string sourcePath in sourcePaths)
            {
                if (string.IsNullOrWhiteSpace(sourcePath))
                {
                    throw new ArgumentException("Source paths must be non-empty and unique.", nameof(sourcePaths));
                }

                string fullPath = Path.GetFullPath(sourcePath);
                if (!paths.Add(fullPath))
                {
                    throw new ArgumentException("Source paths must be non-empty and unique.", nameof(sourcePaths));
                }

                string currentDirectory = Path.GetDirectoryName(fullPath);
                if (sourceDirectory == null)
                {
                    sourceDirectory = currentDirectory;
                    continue;
                }

                // This request format emits a single source-directory mapping.
                if (!pathComparer.Equals(sourceDirectory, currentDirectory))
                {
                    throw new ArgumentException(
                        "Source paths must share one normalized parent directory.",
                        nameof(sourcePaths));
                }
            }
        }

        internal static string CreateRequestFilePath(string sourcePath, string extension, bool isMultipleSources)
        {
            return isMultipleSources
                ? Path.ChangeExtension(sourcePath, Guid.NewGuid().ToString("N") + extension)
                : Path.ChangeExtension(sourcePath, extension);
        }

        private static List<string> CreateCompilerOptions(string dllPath, bool allowUnsafeCode, bool emitDebugCode)
        {
            return new List<string>
            {
                "-nologo",
                // AI consumers need machine-readable English diagnostics encoded as UTF-8.
                "-preferreduilang:en-US",
                "-utf8output",
                "-nostdlib+",
                "-target:library",
                // Hot-reload shims need PDB locals for pause-point capture; dynamic code keeps optimization enabled.
                emitDebugCode ? "-optimize-" : "-optimize+",
                // One-shot compilation needs a portable PDB to map Assembly.Load exceptions to user-snippet.cs lines.
                "-debug:portable",
                allowUnsafeCode ? "-unsafe+" : "-unsafe-",
                QuoteArgument("-out:", dllPath)
            };
        }

        private static StringComparer CreatePathComparer()
        {
            return Path.DirectorySeparatorChar == '\\'
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
        }

        private static void AddDefines(List<string> lines, IReadOnlyCollection<string> defineSymbols)
        {
            string serializedDefines = SerializeDefineSymbols(defineSymbols);
            if (!string.IsNullOrEmpty(serializedDefines))
            {
                lines.Add("-define:" + serializedDefines);
            }
        }

        private static void AddReferences(List<string> lines, IReadOnlyCollection<string> references)
        {
            foreach (string reference in references)
            {
                lines.Add(QuoteArgument("-r:", reference));
            }
        }

        private static void AddWorkerDefines(List<string> lines, IReadOnlyCollection<string> defineSymbols)
        {
            string serializedDefines = SerializeDefineSymbols(defineSymbols);
            if (!string.IsNullOrEmpty(serializedDefines))
            {
                lines.Add("define:" + serializedDefines);
            }
        }

        private static void AddWorkerReferences(List<string> lines, IReadOnlyCollection<string> references)
        {
            foreach (string reference in references)
            {
                lines.Add("ref:" + Path.GetFullPath(reference));
            }
        }

        private static string SerializeDefineSymbols(IReadOnlyCollection<string> defineSymbols)
        {
            if (defineSymbols == null || defineSymbols.Count == 0)
            {
                return string.Empty;
            }

            List<string> filteredDefines = new List<string>(defineSymbols.Count);
            foreach (string defineSymbol in defineSymbols)
            {
                if (!string.IsNullOrWhiteSpace(defineSymbol))
                {
                    filteredDefines.Add(defineSymbol);
                }
            }

            return filteredDefines.Count == 0 ? string.Empty : string.Join(";", filteredDefines);
        }

        private static string QuoteArgument(string prefix, string value)
        {
            return prefix + QuotePath(value);
        }

        private static string QuotePath(string path)
        {
            return "\"" + path + "\"";
        }
    }
}
