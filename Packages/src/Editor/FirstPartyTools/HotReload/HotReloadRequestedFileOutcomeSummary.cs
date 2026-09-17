using System;
using System.Collections.Generic;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Decides whether a run left the requested files untouched, ignoring sibling re-applies.
    /// </summary>
    internal static class HotReloadRequestedFileOutcomeSummary
    {
        // Why the siblings are excluded: a sibling file is re-applied on its own initiative, so
        // its Patched/Added rows would hide that nothing asked for in this run was applied.
        // Why an empty FilePath is skipped: an outcome that belongs to no file, such as a
        // file-level row, says nothing about the requested files. A Failed one of those is
        // already reported by the failure path, which runs before this conclusion is used.
        public static bool AreAllRequestedOutcomesSkipped(
            IReadOnlyList<HotReloadMethodOutcome> methods,
            IReadOnlyCollection<string> reappliedSiblingPaths,
            Func<string, string> toProjectRelativeScriptPath)
        {
            Debug.Assert(methods != null, "methods must not be null.");
            Debug.Assert(reappliedSiblingPaths != null, "reappliedSiblingPaths must not be null.");
            Debug.Assert(
                toProjectRelativeScriptPath != null,
                "toProjectRelativeScriptPath must not be null.");

            StringComparer fileComparer = Application.platform == RuntimePlatform.WindowsEditor
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
            HashSet<string> siblingKeys = BuildSiblingKeys(
                reappliedSiblingPaths,
                toProjectRelativeScriptPath,
                fileComparer);
            int requestedCount = 0;
            for (int index = 0; index < methods.Count; index++)
            {
                HotReloadMethodOutcome outcome = methods[index];
                if (string.IsNullOrEmpty(outcome.FilePath))
                {
                    continue;
                }

                if (siblingKeys.Contains(toProjectRelativeScriptPath(outcome.FilePath)))
                {
                    continue;
                }

                requestedCount++;
                if (outcome.Kind != HotReloadMethodOutcomeKind.Skipped)
                {
                    return false;
                }
            }

            return requestedCount > 0;
        }

        private static HashSet<string> BuildSiblingKeys(
            IReadOnlyCollection<string> reappliedSiblingPaths,
            Func<string, string> toProjectRelativeScriptPath,
            StringComparer fileComparer)
        {
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
