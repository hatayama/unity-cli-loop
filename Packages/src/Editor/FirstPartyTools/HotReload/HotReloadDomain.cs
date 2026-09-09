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
    /// The hot-reload state of one Unity domain: every file generation currently applied, and the
    /// source hash each file was last applied from. It is the only way in to a file generation.
    /// </summary>
    /// <remarks>
    /// Why the applied-source records live here rather than on a generation: a file is probed for
    /// an unchanged re-apply before anything of it is applied, so the record has to outlive — and
    /// exist without — a generation of that file.
    /// Why nothing here is persisted: a Domain Reload drops the Harmony patches and the shim
    /// assemblies at the same time, so a hash that survived it would short-circuit a reload whose
    /// patches are already gone.
    /// </remarks>
    internal sealed class HotReloadDomain : IDisposable
    {
        private readonly Dictionary<string, HotReloadFileGeneration> _generationsByPath =
            new Dictionary<string, HotReloadFileGeneration>(StringComparer.Ordinal);

        private readonly Dictionary<string, (string Hash, bool IsFullyApplied)> _appliedSourceByPath =
            new Dictionary<string, (string Hash, bool IsFullyApplied)>(StringComparer.Ordinal);

        internal HotReloadDomain(
            HotReloadIntroducedTypeRegistry introducedTypes,
            HotReloadIntroducedTypeAssemblyResolver introducedTypeResolver)
        {
            Debug.Assert(introducedTypes != null, "introducedTypes must not be null.");
            Debug.Assert(introducedTypeResolver != null, "introducedTypeResolver must not be null.");

            IntroducedTypes = introducedTypes;
            IntroducedTypeResolver = introducedTypeResolver;
        }

        /// <summary>
        /// The types this domain introduced. Their lifetime is the Unity domain's, not a
        /// generation's: a revert cannot drop a type whose identity is already bound.
        /// </summary>
        internal HotReloadIntroducedTypeRegistry IntroducedTypes { get; }

        /// <summary>Answers binds for the artifact assemblies the introduced types live in.</summary>
        internal HotReloadIntroducedTypeAssemblyResolver IntroducedTypeResolver { get; }

        /// <summary>The values of the added fields the shims of this domain read and write.</summary>
        internal HotReloadAddedFieldValues AddedFieldValues { get; } = new HotReloadAddedFieldValues();

        /// <summary>How many times each patched body of this domain has run.</summary>
        internal HotReloadInvocationCounts Invocations { get; } = new HotReloadInvocationCounts();

        /// <summary>
        /// Stops this domain's resolver from answering binds. The generations are not reverted
        /// here: Harmony patches are removed through the patcher, which owns that side.
        /// </summary>
        public void Dispose()
        {
            IntroducedTypeResolver.Dispose();
        }

        /// <summary>
        /// Starts a new shim generation for one file, replacing whatever the previous apply left.
        /// </summary>
        internal HotReloadFileGeneration BeginGeneration(
            string projectRelativePath,
            byte[] assemblyBytes,
            byte[] pdbBytes,
            Assembly loadedAssembly)
        {
            HotReloadFileGeneration generation = GetOrCreateGeneration(projectRelativePath);
            generation.BeginShimGeneration(assemblyBytes, pdbBytes, loadedAssembly);
            generation.BeginAddedMemberGeneration();
            return generation;
        }

        /// <summary>
        /// Starts a new added-member generation for one file, leaving its shim generation alone.
        /// </summary>
        /// <remarks>
        /// Why not BeginGeneration: a file that contributed no body to the shim assembly has no
        /// bytes to register a shim generation with. Only the added-member side has stale rows to
        /// drop, so this is the one write that must not move the two sides together.
        /// </remarks>
        internal HotReloadFileGeneration BeginAddedMemberOnlyGeneration(string projectRelativePath)
        {
            HotReloadFileGeneration generation = GetOrCreateGeneration(projectRelativePath);
            generation.BeginAddedMemberGeneration();
            return generation;
        }

        private HotReloadFileGeneration GetOrCreateGeneration(string projectRelativePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");

            if (_generationsByPath.TryGetValue(projectRelativePath, out HotReloadFileGeneration generation))
            {
                return generation;
            }

            generation = new HotReloadFileGeneration(projectRelativePath);
            _generationsByPath[projectRelativePath] = generation;
            return generation;
        }

        /// <summary>The generation of exactly this project-relative path, or null when there is none.</summary>
        internal HotReloadFileGeneration FindGeneration(string projectRelativePath)
        {
            if (string.IsNullOrEmpty(projectRelativePath))
            {
                return null;
            }

            return _generationsByPath.TryGetValue(projectRelativePath, out HotReloadFileGeneration generation)
                ? generation
                : null;
        }

        /// <summary>
        /// The generation a caller outside the apply pipeline means by this path, which may be
        /// absolute or spelled with the other separator. Null when no generation matches, or when
        /// more than one does.
        /// </summary>
        internal HotReloadFileGeneration FindGenerationForRequestedPath(string requestedPath)
        {
            if (string.IsNullOrEmpty(requestedPath))
            {
                return null;
            }

            string normalizedRequest = HotReloadSourcePathNormalizer.ToForwardSlashes(requestedPath);
            StringComparison comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            // Why exact-first: Dictionary enumeration order is non-deterministic; prefer a unique
            // Ordinal(/IgnoreCase) key match before any suffix match.
            foreach (KeyValuePair<string, HotReloadFileGeneration> pair in _generationsByPath)
            {
                if (string.Equals(normalizedRequest, pair.Value.NormalizedPath, comparison))
                {
                    return pair.Value;
                }
            }

            HotReloadFileGeneration suffixMatch = null;
            int suffixMatchCount = 0;
            foreach (KeyValuePair<string, HotReloadFileGeneration> pair in _generationsByPath)
            {
                if (!HotReloadSourcePathNormalizer.PathsReferToSameFile(requestedPath, pair.Key))
                {
                    continue;
                }

                suffixMatchCount++;
                suffixMatch = pair.Value;
                if (suffixMatchCount > 1)
                {
                    // Why null on ambiguity: same fail-closed rule as FindEntryForError — guessing
                    // among multiple suffix hits is worse than no lookup.
                    return null;
                }
            }

            return suffixMatchCount == 1 ? suffixMatch : null;
        }

        /// <summary>
        /// The generation holding a pending or live patch of this method, or null when none does.
        /// </summary>
        internal HotReloadFileGeneration FindGenerationForMethod(MethodBase method)
        {
            if (method == null)
            {
                return null;
            }

            foreach (KeyValuePair<string, HotReloadFileGeneration> pair in _generationsByPath)
            {
                if (pair.Value.HasPatch(method))
                {
                    return pair.Value;
                }
            }

            return null;
        }

        /// <summary>Every file generation this domain currently holds.</summary>
        internal IReadOnlyList<HotReloadFileGeneration> ListGenerations()
        {
            return new List<HotReloadFileGeneration>(_generationsByPath.Values);
        }

        internal IReadOnlyList<string> ListActiveFilePaths()
        {
            List<string> paths = new List<string>();
            foreach (KeyValuePair<string, HotReloadFileGeneration> pair in _generationsByPath)
            {
                if (pair.Value.ActivePatchCount > 0)
                {
                    paths.Add(pair.Key);
                }
            }

            return paths;
        }

        internal IReadOnlyList<string> ListPathsWithActiveAddedMembers()
        {
            List<string> paths = new List<string>();
            foreach (KeyValuePair<string, HotReloadFileGeneration> pair in _generationsByPath)
            {
                if (pair.Value.AddedMemberCount > 0)
                {
                    paths.Add(pair.Key);
                }
            }

            return paths;
        }

        internal IReadOnlyList<string> ListActiveMethodKeys(string projectRelativePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");
            HotReloadFileGeneration generation = FindGeneration(projectRelativePath);
            if (generation == null)
            {
                return Array.Empty<string>();
            }

            List<MethodBase> methods = generation.ListActiveMethods();
            List<string> keys = new List<string>(methods.Count);
            for (int index = 0; index < methods.Count; index++)
            {
                keys.Add(HotReloadMethodKeys.FormatMethodLabel(methods[index]));
            }

            return keys;
        }

        internal IReadOnlyList<string> ListActiveAddedMethodKeys(string projectRelativePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");
            HotReloadFileGeneration generation = FindGeneration(projectRelativePath);
            return generation == null ? Array.Empty<string>() : generation.ListActiveAddedMethodKeys();
        }

        // Why path + key, not a domain-wide key lookup: identity is per file generation. The same
        // method key on another path is a different member.
        internal bool IsActiveMember(string projectRelativePath, string methodKey)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");
            Debug.Assert(!string.IsNullOrEmpty(methodKey), "methodKey must not be empty.");
            HotReloadFileGeneration generation = FindGeneration(projectRelativePath);
            return generation != null && generation.IsActiveMember(methodKey);
        }

        /// <summary>Active patches (method key + source file path), sorted by method key.</summary>
        internal IReadOnlyList<HotReloadActivePatchInfo> DescribeActivePatches()
        {
            List<HotReloadActivePatchInfo> patches = new List<HotReloadActivePatchInfo>();
            foreach (KeyValuePair<string, HotReloadFileGeneration> pair in _generationsByPath)
            {
                List<MethodBase> methods = pair.Value.ListActiveMethods();
                for (int index = 0; index < methods.Count; index++)
                {
                    patches.Add(
                        new HotReloadActivePatchInfo(
                            HotReloadMethodKeys.FormatMethodLabel(methods[index]),
                            pair.Value.Path));
                }
            }

            patches.Sort((left, right) => string.CompareOrdinal(left.MethodKey, right.MethodKey));
            return patches;
        }

        /// <summary>Active added members across every file, sorted by method key.</summary>
        internal IReadOnlyList<HotReloadAddedMemberInfo> DescribeAddedMembers()
        {
            List<HotReloadAddedMemberInfo> members = new List<HotReloadAddedMemberInfo>();
            foreach (KeyValuePair<string, HotReloadFileGeneration> pair in _generationsByPath)
            {
                pair.Value.DescribeAddedMembers(members);
            }

            members.Sort((left, right) => string.CompareOrdinal(left.MethodKey, right.MethodKey));
            return members;
        }

        /// <summary>
        /// The de-duplicated simple field names added to <paramref name="typeName"/> across every
        /// file. The name may be reflection form (<c>Outer+Inner</c>) or Cecil form
        /// (<c>Outer/Inner</c>); '/' is rewritten to '+' before lookup so callers do not normalize.
        /// </summary>
        internal IReadOnlyList<string> GetAddedFieldsForType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return Array.Empty<string>();
            }

            string normalizedType = typeName.Replace('/', '+');
            HashSet<string> unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, HotReloadFileGeneration> pair in _generationsByPath)
            {
                pair.Value.CollectAddedFieldsForType(normalizedType, unique);
            }

            if (unique.Count == 0)
            {
                return Array.Empty<string>();
            }

            List<string> names = new List<string>(unique);
            names.Sort(StringComparer.Ordinal);
            return names;
        }

        /// <summary>Every live added-field row: file path, then type, then field, all ordinal.</summary>
        internal IReadOnlyList<HotReloadAddedFieldDescription> DescribeAddedFields()
        {
            List<HotReloadAddedFieldDescription> descriptions = new List<HotReloadAddedFieldDescription>();
            foreach (KeyValuePair<string, HotReloadFileGeneration> pair in _generationsByPath)
            {
                pair.Value.DescribeAddedFields(descriptions);
            }

            descriptions.Sort(CompareAddedFieldDescriptions);
            return descriptions;
        }

        private static int CompareAddedFieldDescriptions(
            HotReloadAddedFieldDescription left,
            HotReloadAddedFieldDescription right)
        {
            int byPath = string.CompareOrdinal(left.ProjectRelativePath, right.ProjectRelativePath);
            if (byPath != 0)
            {
                return byPath;
            }

            int byType = string.CompareOrdinal(left.TypeName, right.TypeName);
            if (byType != 0)
            {
                return byType;
            }

            return string.CompareOrdinal(left.FieldName, right.FieldName);
        }

        /// <summary>
        /// The replacement display name recorded for a method key a signature change superseded.
        /// </summary>
        internal bool TryGetSupersededReplacement(string methodKey, out string replacementDisplayName)
        {
            foreach (KeyValuePair<string, HotReloadFileGeneration> pair in _generationsByPath)
            {
                if (pair.Value.TryGetSupersededReplacement(methodKey, out replacementDisplayName))
                {
                    return true;
                }
            }

            replacementDisplayName = null;
            return false;
        }

        /// <summary>
        /// The shim lookup pause-point resolves markers against, for a path spelled any way.
        /// </summary>
        internal HotReloadShimFileLookup LookupShimsForFile(string requestedPath)
        {
            return FindGenerationForRequestedPath(requestedPath)?.BuildShimLookup();
        }

        internal string LoadVerifiedSnapshotSourceForFile(string requestedPath)
        {
            return FindGenerationForRequestedPath(requestedPath)?.LoadVerifiedSnapshotSource();
        }

        // Why the flag: a non-baseline entry (Skipped or Failed in the last run) must not
        // short-circuit; it exists only so an identical reload can explain why it re-applies.
        internal void RecordAppliedSource(
            string projectRelativePath,
            string sourceContentSha256,
            bool isFullyApplied)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");
            Debug.Assert(!string.IsNullOrEmpty(sourceContentSha256), "sourceContentSha256 must not be empty.");

            _appliedSourceByPath[projectRelativePath] = (sourceContentSha256, isFullyApplied);
        }

        internal (string Hash, bool IsFullyApplied)? TryGetAppliedSource(string projectRelativePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");

            if (!_appliedSourceByPath.TryGetValue(
                    projectRelativePath,
                    out (string Hash, bool IsFullyApplied) entry))
            {
                return null;
            }

            return entry;
        }

        internal void ClearAppliedSource(string projectRelativePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");

            _appliedSourceByPath.Remove(projectRelativePath);
        }

        /// <summary>
        /// Empties every store this domain owns and hands back the methods that were patched, so
        /// the caller can unpatch them and announce each transition. Harmony is not touched here.
        /// </summary>
        /// <remarks>
        /// Why the generations are dropped before Harmony is: Harmony rebuilds every patched method
        /// during UnpatchAll, and the pause-point transpiler guard must see those methods as
        /// unpatched so armed markers are re-instrumented into the restored original IL.
        /// Why every store: a revert-all has to leave the state a Domain Reload would. A new store
        /// whose contents only make sense within the current domain belongs in this method.
        /// Why HotReloadIntroducedTypeRegistry is not emptied: an introduced type's identity is
        /// fixed until the next Domain Reload, so a revert cannot drop it
        /// (docs/hot-reload-introduced-types.md).
        /// Why HotReloadPlayModeEntryDropLedger is not emptied: it lives on SessionState, and
        /// HotReloadCompositionRoot.Services.StatusExecutor.ExecuteRevertAll clears it through NotifyRevertAll.
        /// </remarks>
        internal IReadOnlyList<MethodBase> RevertAll()
        {
            List<MethodBase> revertedMethods = new List<MethodBase>();
            foreach (KeyValuePair<string, HotReloadFileGeneration> pair in _generationsByPath)
            {
                revertedMethods.AddRange(pair.Value.ListActiveMethods());
            }

            _generationsByPath.Clear();
            _appliedSourceByPath.Clear();
            AddedFieldValues.Clear();
            Invocations.Clear();
            return revertedMethods;
        }

        /// <summary>Harmony patches plus added-method shims: what a run reports against PatchedTotal.</summary>
        internal int ActiveChangeCount
        {
            get
            {
                int count = 0;
                foreach (KeyValuePair<string, HotReloadFileGeneration> pair in _generationsByPath)
                {
                    count += pair.Value.ActivePatchCount + pair.Value.AddedMemberCount;
                }

                return count;
            }
        }

        internal int ActivePatchCount
        {
            get
            {
                int count = 0;
                foreach (KeyValuePair<string, HotReloadFileGeneration> pair in _generationsByPath)
                {
                    count += pair.Value.ActivePatchCount;
                }

                return count;
            }
        }

        /// <summary>
        /// Reads every active-change number once, so one report cannot mix two moments.
        /// </summary>
        /// <remarks>
        /// Why the introduced types belong in the total: they live in artifact assemblies the reload
        /// retained, so a Domain Reload unloads them exactly as it unloads a patch. A run that
        /// patched no method still has something to lose.
        /// </remarks>
        internal HotReloadActiveChangeSnapshot CountActiveChanges()
        {
            return new HotReloadActiveChangeSnapshot(ActiveChangeCount, IntroducedTypeCount);
        }

        // Why one accessor: the registry counts artifact assemblies as well as types, and a report
        // that reached for the artifact count would total two types of one batch as one.
        internal int IntroducedTypeCount => IntroducedTypes.ActiveTypeCount;
    }
}
