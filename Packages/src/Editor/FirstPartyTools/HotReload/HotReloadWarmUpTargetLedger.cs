using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The names of the assemblies the hot reload runs of a project edited, most recent first.
    /// Runs record into it; the warm-up after a domain reload reads it to choose what to load
    /// before the first run of that domain. Kept in a file under Library so it survives reloads.
    /// </summary>
    internal static class HotReloadWarmUpTargetLedger
    {
        internal const int MaxRecordedTargets = 32;

        private const string FormatHeader = "uloop-hot-reload-warm-up-targets 1";

        /// <summary>
        /// The recorded names, most recent first. Empty when there is no ledger or it has another header.
        /// </summary>
        internal static IReadOnlyList<string> Read(string projectRoot)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty.");

            string path = LedgerPath(projectRoot);
            if (!File.Exists(path))
            {
                return Array.Empty<string>();
            }

            string[] lines = File.ReadAllText(path, Encoding.UTF8).Split('\n');
            if (!string.Equals(lines[0].TrimEnd('\r'), FormatHeader, StringComparison.Ordinal))
            {
                return Array.Empty<string>();
            }

            List<string> names = new List<string>();
            for (int index = 1; index < lines.Length; index++)
            {
                string name = lines[index].Trim();
                if (name.Length != 0)
                {
                    names.Add(name);
                }
            }

            return names;
        }

        /// <summary>
        /// Puts <paramref name="assemblyNames"/> first, keeps the earlier names after them once each,
        /// and keeps the most recent <see cref="MaxRecordedTargets"/>. Leaves the file untouched when
        /// that changes nothing, so a run that edits the same assembly again writes nothing.
        /// </summary>
        internal static void Record(string projectRoot, IReadOnlyList<string> assemblyNames)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty.");
            Debug.Assert(assemblyNames != null, "assemblyNames must not be null.");
            if (assemblyNames.Count == 0)
            {
                return;
            }

            IReadOnlyList<string> existing = Read(projectRoot);
            List<string> merged = Merge(assemblyNames, existing);
            if (SameNames(merged, existing))
            {
                return;
            }

            Write(LedgerPath(projectRoot), merged);
        }

        private static List<string> Merge(IReadOnlyList<string> recent, IReadOnlyList<string> existing)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            List<string> merged = new List<string>();
            AppendUnseen(recent, seen, merged);
            AppendUnseen(existing, seen, merged);
            if (merged.Count > MaxRecordedTargets)
            {
                merged.RemoveRange(MaxRecordedTargets, merged.Count - MaxRecordedTargets);
            }

            return merged;
        }

        private static void AppendUnseen(IReadOnlyList<string> names, HashSet<string> seen, List<string> merged)
        {
            foreach (string name in names)
            {
                if (!string.IsNullOrEmpty(name) && seen.Add(name))
                {
                    merged.Add(name);
                }
            }
        }

        private static bool SameNames(List<string> merged, IReadOnlyList<string> existing)
        {
            if (merged.Count != existing.Count)
            {
                return false;
            }

            for (int index = 0; index < merged.Count; index++)
            {
                if (!string.Equals(merged[index], existing[index], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        // File format: UTF-8 without a BOM, every line ends with "\n"; the header, then one name per line.
        private static void Write(string path, List<string> names)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            StringBuilder text = new StringBuilder();
            text.Append(FormatHeader).Append('\n');
            foreach (string name in names)
            {
                text.Append(name).Append('\n');
            }

            // Why a temp file and a move: a warm-up in another domain never reads a half-written ledger.
            string tempPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(tempPath, text.ToString(), new UTF8Encoding(false));
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                File.Move(tempPath, path);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        private static string LedgerPath(string projectRoot)
        {
            return Path.Combine(
                projectRoot,
                HotReloadConstants.WarmUpRelativeDirectory,
                HotReloadConstants.WarmUpTargetsFileName);
        }
    }
}
