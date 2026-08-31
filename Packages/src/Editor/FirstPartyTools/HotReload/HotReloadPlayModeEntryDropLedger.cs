using System;
using System.Collections.Generic;

using UnityEditor;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Stores method identities discarded by the Play-entry domain reload in SessionState
    /// so --status can still name them after the in-memory patch ledger is gone.
    /// </summary>
    internal static class HotReloadPlayModeEntryDropLedger
    {
        public static void Record(IReadOnlyList<string> identities)
        {
            Debug.Assert(identities != null, "identities must not be null");
            HashSet<string> stored = ReadSet();
            for (int index = 0; index < identities.Count; index++)
            {
                string identity = identities[index];
                if (string.IsNullOrEmpty(identity))
                {
                    continue;
                }

                stored.Add(identity);
            }

            WriteSet(stored);
        }

        public static void Remove(IReadOnlyList<string> identities)
        {
            Debug.Assert(identities != null, "identities must not be null");
            HashSet<string> stored = ReadSet();
            for (int index = 0; index < identities.Count; index++)
            {
                stored.Remove(identities[index]);
            }

            WriteSet(stored);
        }

        public static void Clear()
        {
            SessionState.SetString(HotReloadConstants.PlayModeEntryDropSessionStateKey, string.Empty);
        }

        public static IReadOnlyList<string> GetIdentities()
        {
            List<string> identities = new List<string>(ReadSet());
            identities.Sort(StringComparer.Ordinal);
            return identities;
        }

        public static int Count => ReadSet().Count;

        private static HashSet<string> ReadSet()
        {
            string raw = SessionState.GetString(
                HotReloadConstants.PlayModeEntryDropSessionStateKey,
                string.Empty);
            HashSet<string> stored = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(raw))
            {
                return stored;
            }

            string[] lines = raw.Split('\n');
            for (int index = 0; index < lines.Length; index++)
            {
                if (string.IsNullOrEmpty(lines[index]))
                {
                    continue;
                }

                stored.Add(lines[index]);
            }

            return stored;
        }

        private static void WriteSet(HashSet<string> stored)
        {
            Debug.Assert(stored != null, "stored must not be null");
            List<string> identities = new List<string>(stored);
            identities.Sort(StringComparer.Ordinal);
            SessionState.SetString(
                HotReloadConstants.PlayModeEntryDropSessionStateKey,
                string.Join("\n", identities));
        }
    }
}
