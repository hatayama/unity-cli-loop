using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// In-memory ledger of the last successfully applied hot-reload source hash per file.
    /// Domain reload drops this table on purpose: Harmony patches and the shim registry
    /// disappear at the same time, so a persisted hash would short-circuit a reload whose
    /// patches are already gone.
    /// </summary>
    internal static class HotReloadAppliedSourceLedger
    {
        private static readonly Dictionary<string, string> ContentHashByProjectRelativePath =
            new Dictionary<string, string>(StringComparer.Ordinal);

        public static void Record(string projectRelativePath, string contentHash)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");
            Debug.Assert(!string.IsNullOrEmpty(contentHash), "contentHash must not be empty.");

            ContentHashByProjectRelativePath[projectRelativePath] = contentHash;
        }

        public static string TryGetHash(string projectRelativePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");

            if (!ContentHashByProjectRelativePath.TryGetValue(projectRelativePath, out string contentHash))
            {
                return null;
            }

            return contentHash;
        }

        public static void Clear(string projectRelativePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");

            ContentHashByProjectRelativePath.Remove(projectRelativePath);
        }

        public static void ClearAll()
        {
            ContentHashByProjectRelativePath.Clear();
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
