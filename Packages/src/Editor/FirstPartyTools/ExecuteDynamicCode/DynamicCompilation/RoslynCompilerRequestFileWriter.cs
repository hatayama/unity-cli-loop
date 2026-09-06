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

            HashSet<string> paths = new HashSet<string>(StringComparer.Ordinal);
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
                if (!string.Equals(sourceDirectory, currentDirectory, StringComparison.Ordinal))
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
                "-preferreduilang:en-US",
                "-utf8output",
                "-nostdlib+",
                "-target:library",
                emitDebugCode ? "-optimize-" : "-optimize+",
                "-debug:portable",
                allowUnsafeCode ? "-unsafe+" : "-unsafe-",
                QuoteArgument("-out:", dllPath)
            };
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
