using System;
using System.Collections.Generic;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The sibling files a run pulled in to re-apply earlier changes, answering whether an
    /// outcome's file is one of them.
    /// </summary>
    internal sealed class HotReloadReappliedSiblingFiles
    {
        private readonly HashSet<string> _siblingKeys;
        private readonly Func<string, string> _toProjectRelativeScriptPath;

        public HotReloadReappliedSiblingFiles(
            IReadOnlyCollection<string> reappliedSiblingPaths,
            Func<string, string> toProjectRelativeScriptPath)
        {
            if (reappliedSiblingPaths == null)
            {
                throw new ArgumentNullException(nameof(reappliedSiblingPaths));
            }

            if (toProjectRelativeScriptPath == null)
            {
                throw new ArgumentNullException(nameof(toProjectRelativeScriptPath));
            }

            _toProjectRelativeScriptPath = toProjectRelativeScriptPath;
            _siblingKeys = BuildSiblingKeys(reappliedSiblingPaths, toProjectRelativeScriptPath);
        }

        /// <summary>
        /// The siblings a run re-applied for their active patches. The one place both the compile
        /// fallback and the response message build this set, so neither can take every re-applied
        /// sibling instead.
        /// </summary>
        internal static HotReloadReappliedSiblingFiles ForActivePatches(
            HotReloadOrchestratorResult result,
            Func<string, string> toProjectRelativeScriptPath)
        {
            return new HotReloadReappliedSiblingFiles(result.ActivePatchSiblingPaths, toProjectRelativeScriptPath);
        }

        // Why an empty FilePath is never a sibling: an outcome that belongs to no file, such as a
        // file-level row, cannot have come from a pulled-in file.
        public bool Contains(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return false;
            }

            return _siblingKeys.Contains(_toProjectRelativeScriptPath(filePath));
        }

        // Why the path is normalized and compared per platform: an outcome's FilePath can be
        // absolute while the sibling paths are project-relative, and Windows paths ignore case.
        private static HashSet<string> BuildSiblingKeys(
            IReadOnlyCollection<string> reappliedSiblingPaths,
            Func<string, string> toProjectRelativeScriptPath)
        {
            StringComparer fileComparer = Application.platform == RuntimePlatform.WindowsEditor
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
            HashSet<string> keys = new HashSet<string>(fileComparer);
            foreach (string path in reappliedSiblingPaths)
            {
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                keys.Add(toProjectRelativeScriptPath(path));
            }

            return keys;
        }
    }
}
