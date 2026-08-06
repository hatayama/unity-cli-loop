using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

using Assembly = System.Reflection.Assembly;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Per-file hot-reload shim generation ledger (bytes + loaded assembly + method map).
    /// Pause-point resolves markers against the active generation via coordination delegates.
    /// </summary>
    internal static class HotReloadShimRegistry
    {
        private static readonly Dictionary<string, FileGeneration> GenerationsByPath =
            new Dictionary<string, FileGeneration>(StringComparer.Ordinal);

        static HotReloadShimRegistry()
        {
            HotReloadPausePointCoordination.GetShimLookupForFile = LookupForFile;
        }

        /// <summary>
        /// Replaces any prior generation for <paramref name="projectRelativePath"/> with a new
        /// empty method map backed by the compiled shim bytes.
        /// </summary>
        public static void BeginFileGeneration(
            string projectRelativePath,
            byte[] assemblyBytes,
            byte[] pdbBytes,
            Assembly loadedAssembly)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");
            Debug.Assert(assemblyBytes != null && assemblyBytes.Length > 0, "assemblyBytes must not be empty.");
            Debug.Assert(loadedAssembly != null, "loadedAssembly must not be null.");

            GenerationsByPath[projectRelativePath] = new FileGeneration(
                assemblyBytes,
                pdbBytes,
                loadedAssembly);
        }

        public static void RegisterMethod(
            string projectRelativePath,
            MethodBase originalMethod,
            MethodEntry entry)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");
            Debug.Assert(originalMethod != null, "originalMethod must not be null.");
            Debug.Assert(entry != null, "entry must not be null.");
            Debug.Assert(
                GenerationsByPath.ContainsKey(projectRelativePath),
                "BeginFileGeneration must run before RegisterMethod for this path.");

            GenerationsByPath[projectRelativePath].Methods[originalMethod] = entry;
        }

        /// <summary>
        /// Removes <paramref name="originalMethod"/> from every file generation. Empty
        /// generations stay so a later RegisterMethod in the same Apply run still finds the
        /// file key (per-method Apply failure must not abort sibling registration).
        /// </summary>
        public static void RemoveMethod(MethodBase originalMethod)
        {
            Debug.Assert(originalMethod != null, "originalMethod must not be null.");

            foreach (KeyValuePair<string, FileGeneration> pair in GenerationsByPath)
            {
                pair.Value.Methods.Remove(originalMethod);
            }
        }

        public static void Clear()
        {
            GenerationsByPath.Clear();
        }

        private static HotReloadShimFileLookup LookupForFile(string requestedPath)
        {
            if (string.IsNullOrEmpty(requestedPath))
            {
                return null;
            }

            FileGeneration matchedGeneration = FindGenerationForPath(requestedPath);
            if (matchedGeneration == null)
            {
                return null;
            }

            List<HotReloadShimMethodLookup> methods = new List<HotReloadShimMethodLookup>();
            foreach (KeyValuePair<MethodBase, MethodEntry> methodPair in matchedGeneration.Methods)
            {
                // Why filter by active shim ledger: BeginFileGeneration can outlive a Revert of
                // individual methods; pause-point must only see methods still patched.
                MethodBase activeShim =
                    HotReloadPausePointCoordination.GetActiveShimForMethod?.Invoke(methodPair.Key);
                if (activeShim == null)
                {
                    continue;
                }

                MethodEntry entry = methodPair.Value;
                methods.Add(
                    new HotReloadShimMethodLookup(
                        methodPair.Key,
                        entry.ShimMethod,
                        entry.IsDelegation,
                        entry.SourceStartLine,
                        entry.SourceEndLine));
            }

            if (methods.Count == 0)
            {
                return null;
            }

            return new HotReloadShimFileLookup(
                matchedGeneration.AssemblyBytes,
                matchedGeneration.PdbBytes,
                matchedGeneration.LoadedAssembly,
                methods);
        }

        private static FileGeneration FindGenerationForPath(string requestedPath)
        {
            string normalizedRequest = HotReloadSourcePathNormalizer.ToForwardSlashes(requestedPath);
            StringComparison comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            // Why exact-first: Dictionary enumeration order is non-deterministic; prefer a
            // unique Ordinal(/IgnoreCase) key match before any suffix match.
            foreach (KeyValuePair<string, FileGeneration> pair in GenerationsByPath)
            {
                string normalizedKey = HotReloadSourcePathNormalizer.ToForwardSlashes(pair.Key);
                if (string.Equals(normalizedRequest, normalizedKey, comparison))
                {
                    return pair.Value;
                }
            }

            FileGeneration suffixMatch = null;
            int suffixMatchCount = 0;
            foreach (KeyValuePair<string, FileGeneration> pair in GenerationsByPath)
            {
                if (!HotReloadSourcePathNormalizer.PathsReferToSameFile(requestedPath, pair.Key))
                {
                    continue;
                }

                suffixMatchCount++;
                suffixMatch = pair.Value;
                if (suffixMatchCount > 1)
                {
                    // Why null on ambiguity: same fail-closed rule as FindEntryForError —
                    // guessing among multiple suffix hits is worse than no lookup.
                    return null;
                }
            }

            return suffixMatchCount == 1 ? suffixMatch : null;
        }

        internal sealed class MethodEntry
        {
            public MethodBase ShimMethod { get; }
            public bool IsDelegation { get; }
            public int SourceStartLine { get; }
            public int SourceEndLine { get; }

            public MethodEntry(
                MethodBase shimMethod,
                bool isDelegation,
                int sourceStartLine,
                int sourceEndLine)
            {
                ShimMethod = shimMethod;
                IsDelegation = isDelegation;
                SourceStartLine = sourceStartLine;
                SourceEndLine = sourceEndLine;
            }
        }

        private sealed class FileGeneration
        {
            public byte[] AssemblyBytes { get; }
            public byte[] PdbBytes { get; }
            public Assembly LoadedAssembly { get; }
            public Dictionary<MethodBase, MethodEntry> Methods { get; }

            public FileGeneration(byte[] assemblyBytes, byte[] pdbBytes, Assembly loadedAssembly)
            {
                AssemblyBytes = assemblyBytes;
                PdbBytes = pdbBytes;
                LoadedAssembly = loadedAssembly;
                Methods = new Dictionary<MethodBase, MethodEntry>();
            }
        }
    }
}
