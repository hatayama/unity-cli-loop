using System;
using System.Collections.Generic;

using UnityEditor;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Stores, in SessionState, the owner file of each introduced type discarded by the Play-entry
    /// domain reload, so an omitted --files run can select those files again after the in-memory
    /// registry that knew them is gone.
    /// </summary>
    /// <remarks>
    /// Revert-all writes here too, despite the name: it records the owner files of the introduced
    /// types it leaves loaded, because the revert drops what later reloads added to them and a
    /// file that was never compiled is not otherwise selected again.
    /// </remarks>
    internal static class HotReloadPlayModeEntryDropSourceLedger
    {
        internal const char FieldSeparator = '\t';

        internal const char LineSeparator = '\n';

        public static void Record(IReadOnlyList<HotReloadPlayModeEntryDropSource> sources)
        {
            Debug.Assert(sources != null, "sources must not be null");
            HashSet<string> stored = ReadLines();
            for (int index = 0; index < sources.Count; index++)
            {
                HotReloadPlayModeEntryDropSource source = sources[index];
                stored.Add(source.Identity + FieldSeparator + source.ProjectRelativePath);
            }

            WriteLines(stored);
        }

        public static void Remove(IReadOnlyList<string> identities)
        {
            Debug.Assert(identities != null, "identities must not be null");
            HashSet<string> removed = new HashSet<string>(identities, StringComparer.Ordinal);
            HashSet<string> stored = ReadLines();
            stored.RemoveWhere(line => removed.Contains(SplitLine(line)[0]));
            WriteLines(stored);
        }

        public static void Clear()
        {
            SessionState.SetString(HotReloadConstants.PlayModeEntryDropSourcesSessionStateKey, string.Empty);
        }

        public static IReadOnlyList<string> GetIdentities()
        {
            SortedSet<string> identities = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string line in ReadLines())
            {
                identities.Add(SplitLine(line)[0]);
            }

            return new List<string>(identities);
        }

        // A file stays listed while any of the types it declares is still recorded as discarded.
        public static IReadOnlyList<string> GetProjectRelativePaths()
        {
            SortedSet<string> paths = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string line in ReadLines())
            {
                paths.Add(SplitLine(line)[1]);
            }

            return new List<string>(paths);
        }

        private static string[] SplitLine(string line)
        {
            string[] parts = line.Split(FieldSeparator);
            // Only Record writes this key, and a source cannot hold a separator, so any other
            // shape means the stored state was corrupted and a path read from it cannot be trusted.
            if (parts.Length != 2)
            {
                throw new InvalidOperationException(
                    "A dropped introduced-type source line must hold one identity and one path: " + line);
            }

            return parts;
        }

        private static HashSet<string> ReadLines()
        {
            string raw = SessionState.GetString(
                HotReloadConstants.PlayModeEntryDropSourcesSessionStateKey,
                string.Empty);
            HashSet<string> stored = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(raw))
            {
                return stored;
            }

            string[] lines = raw.Split(LineSeparator);
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

        private static void WriteLines(HashSet<string> stored)
        {
            List<string> lines = new List<string>(stored);
            lines.Sort(StringComparer.Ordinal);
            SessionState.SetString(
                HotReloadConstants.PlayModeEntryDropSourcesSessionStateKey,
                string.Join(LineSeparator.ToString(), lines));
        }
    }
}
