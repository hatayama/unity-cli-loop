using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds the apply Warning when a hot-reloaded file's line count differs from last compiled source.
    /// </summary>
    internal static class HotReloadUnpatchedMethodLineShiftWarningBuilder
    {
        // Why file+line-count only: pause-point --line for methods not patched in this run still
        // resolves against the compiled snapshot; one sentence per file is enough.
        // Why empty when counts match: in-body edits do not shift later methods' compiled lines.
        public static string Build(string file, string editedSource, string compiledSource)
        {
            if (string.IsNullOrEmpty(file) || editedSource == null || string.IsNullOrEmpty(compiledSource))
            {
                return string.Empty;
            }

            int editedLineCount = CountLines(editedSource);
            int compiledLineCount = CountLines(compiledSource);
            if (editedLineCount == compiledLineCount)
            {
                return string.Empty;
            }

            string normalizedFile = HotReloadSourcePathNormalizer.ToForwardSlashes(file);
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}: line numbers have shifted vs the last compiled source (edited {1} lines vs compiled {2}). enable-pause-point --line on methods NOT patched in this run still resolves against the last compiled source; patched methods resolve against the edited file.",
                normalizedFile,
                editedLineCount,
                compiledLineCount);
        }

        // Why unique file: the apply lists one Methods row per member; one shift sentence per file
        // is enough for agents to recompute --line against compiled source.
        public static void Append(
            List<string> warnings,
            IReadOnlyList<HotReloadMethodOutcome> methods,
            Func<string, string> readEditedSource,
            Func<string, string> readCompiledSource)
        {
            Debug.Assert(warnings != null, "warnings must not be null.");
            Debug.Assert(methods != null, "methods must not be null.");
            Debug.Assert(readEditedSource != null, "readEditedSource must not be null.");
            Debug.Assert(readCompiledSource != null, "readCompiledSource must not be null.");

            HashSet<string> seenFiles = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < methods.Count; index++)
            {
                string filePath = methods[index].FilePath;
                if (string.IsNullOrEmpty(filePath))
                {
                    continue;
                }

                string normalizedFile = HotReloadSourcePathNormalizer.ToForwardSlashes(filePath);
                if (!seenFiles.Add(normalizedFile))
                {
                    continue;
                }

                string warning = Build(
                    normalizedFile,
                    readEditedSource(filePath),
                    readCompiledSource(filePath));
                if (warning.Length == 0)
                {
                    continue;
                }

                warnings.Add(warning);
            }
        }

        internal static string ReadEditedSourceFromDisk(string projectRelativePath)
        {
            if (string.IsNullOrEmpty(projectRelativePath))
            {
                return null;
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string absolutePath = Path.Combine(
                projectRoot,
                projectRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(absolutePath))
            {
                return null;
            }

            return File.ReadAllText(absolutePath);
        }

        internal static string ReadCompiledSnapshot(string projectRelativePath)
        {
            if (string.IsNullOrEmpty(projectRelativePath))
            {
                return null;
            }

            Func<string, string> loader = HotReloadPausePointCoordination.GetVerifiedSnapshotSourceForFile;
            if (loader == null)
            {
                return null;
            }

            return loader(HotReloadSourcePathNormalizer.ToForwardSlashes(projectRelativePath));
        }

        // Why the same split as pause-point compiled-line reads: --line is 1-based against that split,
        // so this warning's N vs M must use that counting, not a different newline convention.
        private static int CountLines(string sourceText)
        {
            if (string.IsNullOrEmpty(sourceText))
            {
                return 0;
            }

            return sourceText.Replace("\r\n", "\n").Split('\n').Length;
        }
    }
}
