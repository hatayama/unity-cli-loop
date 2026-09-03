using System;
using System.Collections.Generic;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// In-memory map from an old compiled method key to the replacement display name after
    /// a signature change. Domain reload drops this table on purpose: Harmony patches and
    /// the shim registry disappear at the same time, so a persisted mapping would explain
    /// a superseded Active row whose patch is already gone. Same reason as
    /// <see cref="HotReloadAppliedSourceLedger"/>.
    /// </summary>
    internal static class HotReloadSupersededSignatureRegistry
    {
        private static readonly Dictionary<string, string> ReplacementByOldMethodKey =
            new Dictionary<string, string>(StringComparer.Ordinal);

        public static void Record(string oldMethodKey, string replacementDisplayName)
        {
            Debug.Assert(!string.IsNullOrEmpty(oldMethodKey), "oldMethodKey must not be empty.");
            Debug.Assert(
                !string.IsNullOrEmpty(replacementDisplayName),
                "replacementDisplayName must not be empty.");

            ReplacementByOldMethodKey[oldMethodKey] = replacementDisplayName;
        }

        public static bool TryGetReplacement(string methodKey, out string replacementDisplayName)
        {
            if (string.IsNullOrEmpty(methodKey))
            {
                replacementDisplayName = null;
                return false;
            }

            return ReplacementByOldMethodKey.TryGetValue(methodKey, out replacementDisplayName);
        }

        public static void Remove(string methodKey)
        {
            if (string.IsNullOrEmpty(methodKey))
            {
                return;
            }

            ReplacementByOldMethodKey.Remove(methodKey);
        }

        public static void ClearAll()
        {
            ReplacementByOldMethodKey.Clear();
        }
    }
}
