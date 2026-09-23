using System;
using System.Collections.Generic;

using UnityEditor;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Stores the added fields whose wired values a domain reload discarded, as
    /// "Type.field" display names, in SessionState so the next apply that adds them again can
    /// ask for them to be wired again.
    /// </summary>
    /// <remarks>
    /// Why apart from the Play-entry drop ledger: that ledger's count is what --status reports as
    /// discarded changes, and an added field is not a change a re-apply brings back on its own.
    /// </remarks>
    internal static class HotReloadRewireLedger
    {
        public static void Record(IReadOnlyList<string> fields)
        {
            Debug.Assert(fields != null, "fields must not be null");
            HashSet<string> stored = ReadSet();
            for (int index = 0; index < fields.Count; index++)
            {
                string field = fields[index];
                if (string.IsNullOrEmpty(field))
                {
                    continue;
                }

                stored.Add(field);
            }

            WriteSet(stored);
        }

        public static void Remove(IReadOnlyList<string> fields)
        {
            Debug.Assert(fields != null, "fields must not be null");
            HashSet<string> stored = ReadSet();
            for (int index = 0; index < fields.Count; index++)
            {
                stored.Remove(fields[index]);
            }

            WriteSet(stored);
        }

        public static void Clear()
        {
            SessionState.SetString(HotReloadConstants.RewireFieldsSessionStateKey, string.Empty);
        }

        public static IReadOnlyList<string> GetFields()
        {
            List<string> fields = new List<string>(ReadSet());
            fields.Sort(StringComparer.Ordinal);
            return fields;
        }

        private static HashSet<string> ReadSet()
        {
            string raw = SessionState.GetString(HotReloadConstants.RewireFieldsSessionStateKey, string.Empty);
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
            List<string> fields = new List<string>(stored);
            fields.Sort(StringComparer.Ordinal);
            SessionState.SetString(HotReloadConstants.RewireFieldsSessionStateKey, string.Join("\n", fields));
        }
    }
}
