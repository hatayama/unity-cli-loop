using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Decision for an unchanged-source probe against the applied-source ledger.
    /// </summary>
    internal enum HotReloadUnchangedSourceDecision
    {
        NotUnchanged,
        ShortCircuited,
        ReapplyNonBaseline
    }

    /// <summary>
    /// In-memory ledger of the last hot-reload source hash per file, plus whether that
    /// run was fully applied. Domain reload drops this table on purpose: Harmony patches
    /// and the shim registry disappear at the same time, so a persisted hash would
    /// short-circuit a reload whose patches are already gone.
    /// Why the flag: a non-baseline entry (Skipped or Failed in the last run) must not
    /// short-circuit; it exists only so an identical reload can explain why it re-applies.
    /// </summary>
    internal static class HotReloadAppliedSourceLedger
    {
        private static readonly Dictionary<string, (string Hash, bool IsFullyApplied)>
            EntryByProjectRelativePath =
                new Dictionary<string, (string Hash, bool IsFullyApplied)>(StringComparer.Ordinal);

        public static void Record(
            string projectRelativePath,
            string sourceContentSha256,
            bool isFullyApplied)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");
            Debug.Assert(!string.IsNullOrEmpty(sourceContentSha256), "sourceContentSha256 must not be empty.");

            EntryByProjectRelativePath[projectRelativePath] = (sourceContentSha256, isFullyApplied);
        }

        public static (string Hash, bool IsFullyApplied)? TryGet(string projectRelativePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");

            if (!EntryByProjectRelativePath.TryGetValue(
                    projectRelativePath,
                    out (string Hash, bool IsFullyApplied) entry))
            {
                return null;
            }

            return entry;
        }

        public static void Clear(string projectRelativePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");

            EntryByProjectRelativePath.Remove(projectRelativePath);
        }

        public static void ClearAll()
        {
            EntryByProjectRelativePath.Clear();
        }

        public static string ComputeContentHash(byte[] bytes)
        {
            Debug.Assert(bytes != null, "bytes must not be null.");

            using SHA256 sha256 = SHA256.Create();
            byte[] hash = sha256.ComputeHash(bytes);
            StringBuilder builder = new StringBuilder(hash.Length * 2);
            for (int index = 0; index < hash.Length; index++)
            {
                builder.Append(hash[index].ToString("x2"));
            }

            return builder.ToString();
        }
    }
}
