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
        // Why "line count differs" not "line numbers have shifted": a trailing newline changes
        // the split count without moving statements. Why "patched methods with debug symbols":
        // PatchedMethodPdbUnavailable still falls through to the compiled line map.
        // Why conclusion first: the first sentence is the conclusion; usability rounds
        // showed readers stop at sentence one, so do not restore the explanation-first order.
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
                "{0}: line count differs from the last compiled source (edited {1} lines vs compiled {2}). This matters for 'enable-pause-point --line' targeting: methods NOT patched in this run still resolve against the last compiled source; patched methods with debug symbols resolve against the edited file. To pin the target, pass --method together with --line.",
                normalizedFile,
                editedLineCount,
                compiledLineCount);
        }

        // Why unique canonical file: outcome FilePath is the raw apply input, so absolute+relative
        // (or Windows casing) spellings of one file would otherwise emit duplicate warnings.
        public static void Append(
            List<string> warnings,
            IReadOnlyList<HotReloadMethodOutcome> methods,
            Func<string, string> readEditedSource,
            Func<string, string> readCompiledSource,
            IReadOnlyCollection<string> reappliedSiblingPaths)
        {
            Debug.Assert(warnings != null, "warnings must not be null.");
            Debug.Assert(methods != null, "methods must not be null.");
            Debug.Assert(readEditedSource != null, "readEditedSource must not be null.");
            Debug.Assert(readCompiledSource != null, "readCompiledSource must not be null.");
            Debug.Assert(reappliedSiblingPaths != null, "reappliedSiblingPaths must not be null.");

            StringComparer fileComparer = Application.platform == RuntimePlatform.WindowsEditor
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
            List<LineShiftFileBucket> buckets = CollectUniqueFileBuckets(
                methods,
                reappliedSiblingPaths,
                fileComparer);
            List<string> continuingFiles = new List<string>();
            for (int index = 0; index < buckets.Count; index++)
            {
                LineShiftFileBucket bucket = buckets[index];
                string warning = Build(
                    bucket.CanonicalFile,
                    readEditedSource(bucket.CanonicalFile),
                    readCompiledSource(bucket.CanonicalFile));
                if (warning.Length == 0)
                {
                    continue;
                }

                if (bucket.TouchedThisRun)
                {
                    warnings.Add(warning);
                    continue;
                }

                continuingFiles.Add(bucket.CanonicalFile);
            }

            AppendContinuingWarning(warnings, continuingFiles);
        }

        private sealed class LineShiftFileBucket
        {
            public string CanonicalFile;
            public bool TouchedThisRun;
        }

        private static List<LineShiftFileBucket> CollectUniqueFileBuckets(
            IReadOnlyList<HotReloadMethodOutcome> methods,
            IReadOnlyCollection<string> reappliedSiblingPaths,
            StringComparer fileComparer)
        {
            List<LineShiftFileBucket> buckets = new List<LineShiftFileBucket>();
            Dictionary<string, LineShiftFileBucket> byFile = new Dictionary<string, LineShiftFileBucket>(fileComparer);
            HashSet<string> siblingKeys = BuildReappliedSiblingKeys(reappliedSiblingPaths, fileComparer);
            for (int index = 0; index < methods.Count; index++)
            {
                string filePath = methods[index].FilePath;
                if (string.IsNullOrEmpty(filePath))
                {
                    continue;
                }

                string canonicalFile = HotReloadPatchTargetSupport.ToProjectRelativeScriptPath(filePath);
                if (!byFile.TryGetValue(canonicalFile, out LineShiftFileBucket bucket))
                {
                    bucket = new LineShiftFileBucket { CanonicalFile = canonicalFile };
                    byFile.Add(canonicalFile, bucket);
                    buckets.Add(bucket);
                }

                // Auto-reapplied siblings come back as Patched/Added, so Kind alone would
                // reprint the full warning every run. extraResults paths are the only signal.
                if (methods[index].Kind != HotReloadMethodOutcomeKind.AlreadyActive
                    && !siblingKeys.Contains(canonicalFile))
                {
                    bucket.TouchedThisRun = true;
                }
            }

            return buckets;
        }

        private static HashSet<string> BuildReappliedSiblingKeys(
            IReadOnlyCollection<string> reappliedSiblingPaths,
            StringComparer fileComparer)
        {
            HashSet<string> keys = new HashSet<string>(fileComparer);
            foreach (string path in reappliedSiblingPaths)
            {
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                keys.Add(HotReloadPatchTargetSupport.ToProjectRelativeScriptPath(path));
            }

            return keys;
        }

        private static void AppendContinuingWarning(List<string> warnings, List<string> continuingFiles)
        {
            if (continuingFiles.Count == 0)
            {
                return;
            }

            continuingFiles.Sort(StringComparer.Ordinal);
            warnings.Add(
                string.Format(
                    CultureInfo.InvariantCulture,
                    HotReloadConstants.ContinuingLineShiftWarningFormat,
                    continuingFiles.Count,
                    string.Join(", ", continuingFiles)));
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
