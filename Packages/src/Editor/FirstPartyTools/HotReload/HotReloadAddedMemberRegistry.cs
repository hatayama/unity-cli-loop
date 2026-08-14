using System;
using System.Collections.Generic;
using System.Reflection;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Per-file ledger of added-method shims that have no compiled MethodBase to patch.
    /// Pause-point does not consult this ledger; added methods stay unregistered there.
    /// </summary>
    internal static class HotReloadAddedMemberRegistry
    {
        private static readonly Dictionary<string, FileGeneration> GenerationsByPath =
            new Dictionary<string, FileGeneration>(StringComparer.Ordinal);

        /// <summary>
        /// Drops any prior added members for <paramref name="projectRelativePath"/> so a
        /// re-apply cannot keep methods the edited source no longer declares.
        /// </summary>
        public static void BeginFileGeneration(string projectRelativePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");
            GenerationsByPath[projectRelativePath] = new FileGeneration();
        }

        public static void Register(
            string projectRelativePath,
            string methodKey,
            MethodInfo shimMethod,
            string filePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");
            Debug.Assert(!string.IsNullOrEmpty(methodKey), "methodKey must not be empty.");
            Debug.Assert(shimMethod != null, "shimMethod must not be null.");
            Debug.Assert(
                GenerationsByPath.ContainsKey(projectRelativePath),
                "BeginFileGeneration must run before Register for this path.");

            GenerationsByPath[projectRelativePath].Members[methodKey] =
                new AddedMemberEntry(shimMethod, filePath ?? string.Empty);
        }

        public static void Clear()
        {
            GenerationsByPath.Clear();
        }

        public static int Count
        {
            get
            {
                int count = 0;
                foreach (KeyValuePair<string, FileGeneration> pair in GenerationsByPath)
                {
                    count += pair.Value.Members.Count;
                }

                return count;
            }
        }

        public static IReadOnlyList<HotReloadAddedMemberInfo> Describe()
        {
            List<HotReloadAddedMemberInfo> members = new List<HotReloadAddedMemberInfo>(Count);
            foreach (KeyValuePair<string, FileGeneration> pair in GenerationsByPath)
            {
                foreach (KeyValuePair<string, AddedMemberEntry> member in pair.Value.Members)
                {
                    members.Add(
                        new HotReloadAddedMemberInfo(
                            member.Key,
                            member.Value.FilePath,
                            member.Value.ShimMethod));
                }
            }

            members.Sort(
                (left, right) => string.CompareOrdinal(left.MethodKey, right.MethodKey));
            return members;
        }

        private sealed class FileGeneration
        {
            public Dictionary<string, AddedMemberEntry> Members { get; } =
                new Dictionary<string, AddedMemberEntry>(StringComparer.Ordinal);
        }

        private sealed class AddedMemberEntry
        {
            public MethodInfo ShimMethod { get; }

            public string FilePath { get; }

            public AddedMemberEntry(MethodInfo shimMethod, string filePath)
            {
                ShimMethod = shimMethod;
                FilePath = filePath;
            }
        }
    }

    /// <summary>
    /// One added-method shim recorded for --status Kind "Added".
    /// </summary>
    internal sealed class HotReloadAddedMemberInfo
    {
        public string MethodKey { get; }

        public string FilePath { get; }

        public MethodInfo ShimMethod { get; }

        public HotReloadAddedMemberInfo(string methodKey, string filePath, MethodInfo shimMethod)
        {
            MethodKey = methodKey ?? string.Empty;
            FilePath = filePath ?? string.Empty;
            ShimMethod = shimMethod;
        }
    }
}
