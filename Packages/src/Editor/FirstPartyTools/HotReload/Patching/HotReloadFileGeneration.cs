using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

using Assembly = System.Reflection.Assembly;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Everything one edited source file contributes to the running domain: its shim generation,
    /// the added members and fields it declared, the live Harmony patches of its methods, and the
    /// signatures those patches superseded.
    /// </summary>
    /// <remarks>
    /// Why one aggregate per file: all of this is created, replaced and discarded per file, and the
    /// pieces constrain each other. A patch may only exist for a method whose shim this generation
    /// registered, and a re-apply that replaced the shim generation must not leave a patch pointing
    /// at a shim the edited source no longer declares. Keeping the pieces in separate tables let a
    /// caller move one without the other; the commands here are the only way to move any of them.
    /// </remarks>
    internal sealed class HotReloadFileGeneration
    {
        private readonly Dictionary<MethodBase, HotReloadShimMethodEntry> _shimMethodsByMethod =
            new Dictionary<MethodBase, HotReloadShimMethodEntry>();

        private readonly Dictionary<string, HotReloadAddedMemberInfo> _addedMembersByMethodKey =
            new Dictionary<string, HotReloadAddedMemberInfo>(StringComparer.Ordinal);

        private readonly Dictionary<string, List<string>> _addedFieldsByTypeKey =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);

        // The initializer text each added field was last committed with, keyed by its display
        // name. Kept beside the type map because it answers a question about the previous
        // reload, not about which fields a type currently holds.
        private readonly Dictionary<string, string> _addedFieldInitializerByFullName =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private readonly Dictionary<MethodBase, HotReloadActivePatchEntry> _patchesByMethod =
            new Dictionary<MethodBase, HotReloadActivePatchEntry>();

        private readonly Dictionary<string, string> _supersededReplacementByOldMethodKey =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private byte[] _assemblyBytes;
        private byte[] _pdbBytes;
        private Assembly _loadedAssembly;

        internal HotReloadFileGeneration(string projectRelativePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");

            Path = projectRelativePath;
            NormalizedPath = projectRelativePath.Replace('\\', '/');
        }

        /// <summary>The project-relative source path this generation is the identity of.</summary>
        internal string Path { get; }

        /// <summary>The same path with forward slashes, as added-field rows report it.</summary>
        internal string NormalizedPath { get; }

        // Why a generation probe rather than an empty method map: a generation starts empty, so
        // emptiness cannot tell a preflight skip from an applied-then-empty generation.
        internal bool HasShimGeneration { get; private set; }

        internal bool HasAddedMemberGeneration { get; private set; }

        /// <summary>
        /// Replaces any prior shim generation with an empty method map backed by the compiled
        /// shim bytes. Live patches and superseded signatures survive: the re-apply that follows
        /// re-registers each shim and its own re-apply path retires the patches it replaces.
        /// </summary>
        internal void BeginShimGeneration(byte[] assemblyBytes, byte[] pdbBytes, Assembly loadedAssembly)
        {
            Debug.Assert(assemblyBytes != null && assemblyBytes.Length > 0, "assemblyBytes must not be empty.");
            Debug.Assert(loadedAssembly != null, "loadedAssembly must not be null.");

            _assemblyBytes = assemblyBytes;
            _pdbBytes = pdbBytes;
            _loadedAssembly = loadedAssembly;
            _shimMethodsByMethod.Clear();
            HasShimGeneration = true;
        }

        /// <summary>
        /// Drops any prior added members and fields so a re-apply cannot keep members the edited
        /// source no longer declares.
        /// </summary>
        internal void BeginAddedMemberGeneration()
        {
            _addedMembersByMethodKey.Clear();
            _addedFieldsByTypeKey.Clear();
            _addedFieldInitializerByFullName.Clear();
            HasAddedMemberGeneration = true;
        }

        internal void RegisterShimMethod(MethodBase originalMethod, HotReloadShimMethodEntry entry)
        {
            Debug.Assert(originalMethod != null, "originalMethod must not be null.");
            Debug.Assert(entry != null, "entry must not be null.");
            if (!HasShimGeneration)
            {
                throw new InvalidOperationException(
                    "BeginShimGeneration must run before RegisterShimMethod.");
            }

            _shimMethodsByMethod[originalMethod] = entry;
        }

        internal void RegisterAddedMethod(
            string methodKey,
            MethodInfo shimMethod,
            string filePath,
            string methodName,
            string declaringTypeMetadataName,
            int sourceStartLine = 0,
            int sourceEndLine = 0,
            string compiledAssemblyPath = null)
        {
            Debug.Assert(!string.IsNullOrEmpty(methodKey), "methodKey must not be empty.");
            Debug.Assert(shimMethod != null, "shimMethod must not be null.");
            if (string.IsNullOrEmpty(methodName) || string.IsNullOrEmpty(declaringTypeMetadataName))
            {
                throw new ArgumentException(
                    "An added method is registered with its own name and its declaring type's metadata name.");
            }

            if (!HasAddedMemberGeneration)
            {
                throw new InvalidOperationException(
                    "BeginAddedMemberGeneration must run before RegisterAddedMethod.");
            }

            _addedMembersByMethodKey[methodKey] =
                new HotReloadAddedMemberInfo(
                    methodKey,
                    filePath ?? string.Empty,
                    shimMethod,
                    sourceStartLine,
                    sourceEndLine,
                    compiledAssemblyPath,
                    methodName,
                    declaringTypeMetadataName);
        }

        /// <summary>
        /// Drops one method's shim registration, keeping the generation so a later registration in
        /// the same apply run still lands here (a per-method failure must not abort its siblings).
        /// </summary>
        internal void RemoveShimMethod(MethodBase originalMethod)
        {
            Debug.Assert(originalMethod != null, "originalMethod must not be null.");
            if (IsPatchActive(originalMethod))
            {
                throw new InvalidOperationException(
                    "A method with an active patch must be deactivated before its shim is removed.");
            }

            _shimMethodsByMethod.Remove(originalMethod);
        }

        /// <summary>
        /// Replaces every added-field entry with <paramref name="addedFieldFullNames"/>
        /// (Type.field display names). Type keys are stored in reflection form (nested types
        /// use '+'). <paramref name="addedFieldInitializers"/> holds the initializer text of each
        /// name in the same order, and is ignored when it does not line up with the names.
        /// </summary>
        internal void ReplaceAddedFields(
            IReadOnlyList<string> addedFieldFullNames,
            IReadOnlyList<string> addedFieldInitializers)
        {
            Debug.Assert(addedFieldFullNames != null, "addedFieldFullNames must not be null.");

            _addedFieldsByTypeKey.Clear();
            _addedFieldInitializerByFullName.Clear();
            bool hasInitializers =
                addedFieldInitializers != null
                && addedFieldInitializers.Count == addedFieldFullNames.Count;
            for (int index = 0; index < addedFieldFullNames.Count; index++)
            {
                AddAddedField(addedFieldFullNames[index]);
                if (hasInitializers && !string.IsNullOrEmpty(addedFieldFullNames[index]))
                {
                    _addedFieldInitializerByFullName[addedFieldFullNames[index]] =
                        addedFieldInitializers[index] ?? string.Empty;
                }
            }
        }

        /// <summary>
        /// Adds to <paramref name="changedFullNames"/> each name of
        /// <paramref name="addedFieldFullNames"/> this generation already holds an initializer
        /// for that differs from <paramref name="addedFieldInitializers"/>.
        /// </summary>
        /// <remarks>
        /// Why it has to run before the generation starts: BeginAddedMemberGeneration drops the
        /// ledger, and the previous reload's initializers go with it.
        /// </remarks>
        internal void CollectAddedFieldsWithChangedInitializer(
            IReadOnlyList<string> addedFieldFullNames,
            IReadOnlyList<string> addedFieldInitializers,
            List<string> changedFullNames)
        {
            Debug.Assert(changedFullNames != null, "changedFullNames must not be null.");
            if (addedFieldFullNames == null
                || addedFieldInitializers == null
                || addedFieldInitializers.Count != addedFieldFullNames.Count)
            {
                return;
            }

            for (int index = 0; index < addedFieldFullNames.Count; index++)
            {
                // Why a declaration that lost its initializer is not reported: the run has no
                // initializer to run anywhere, so there is nothing that fails to reach a value.
                if (string.IsNullOrEmpty(addedFieldInitializers[index]))
                {
                    continue;
                }

                if (!_addedFieldInitializerByFullName.TryGetValue(
                        addedFieldFullNames[index],
                        out string committedInitializer))
                {
                    continue;
                }

                if (string.Equals(
                        committedInitializer,
                        addedFieldInitializers[index],
                        StringComparison.Ordinal))
                {
                    continue;
                }

                changedFullNames.Add(addedFieldFullNames[index]);
            }
        }

        private void AddAddedField(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
            {
                return;
            }

            int lastDot = fullName.LastIndexOf('.');
            Debug.Assert(
                lastDot > 0 && lastDot < fullName.Length - 1,
                "added field display names are Type.field with a type segment.");
            if (lastDot <= 0 || lastDot >= fullName.Length - 1)
            {
                return;
            }

            string typeKey = NormalizeTypeKey(fullName.Substring(0, lastDot));
            string fieldName = fullName.Substring(lastDot + 1);
            if (!_addedFieldsByTypeKey.TryGetValue(typeKey, out List<string> fields))
            {
                fields = new List<string>();
                _addedFieldsByTypeKey[typeKey] = fields;
            }

            if (!fields.Contains(fieldName))
            {
                fields.Add(fieldName);
            }
        }

        /// <summary>
        /// Opens a patch of <paramref name="method"/> with <paramref name="shim"/>, pending until
        /// Harmony accepts it. The shim has to be registered first: that is what makes every patch
        /// this generation holds a patch of a method the edited source still declares.
        /// </summary>
        internal void BeginPatch(MethodBase method, MethodInfo shim)
        {
            Debug.Assert(method != null, "method must not be null.");
            Debug.Assert(shim != null, "shim must not be null.");
            if (!_shimMethodsByMethod.ContainsKey(method))
            {
                throw new InvalidOperationException(
                    "A shim must be registered for this method before its patch begins.");
            }

            if (_patchesByMethod.ContainsKey(method))
            {
                throw new InvalidOperationException(
                    "This method already holds a pending or active patch.");
            }

            _patchesByMethod[method] = new HotReloadActivePatchEntry(method, shim);
        }

        /// <summary>Turns this method's pending patch into the live one, keeping what the rebuild recorded.</summary>
        internal void CommitPatch(MethodBase method)
        {
            Debug.Assert(method != null, "method must not be null.");
            if (!_patchesByMethod.TryGetValue(method, out HotReloadActivePatchEntry entry))
            {
                throw new InvalidOperationException("CommitPatch needs a pending patch for this method.");
            }

            if (entry.IsActive)
            {
                throw new InvalidOperationException("CommitPatch needs a pending patch, not an active one.");
            }
            entry.Activate();
        }

        /// <summary>Drops this method's pending patch. Does nothing when the patch is already live.</summary>
        internal void AbandonPatch(MethodBase method)
        {
            Debug.Assert(method != null, "method must not be null.");
            if (!_patchesByMethod.TryGetValue(method, out HotReloadActivePatchEntry entry) || entry.IsActive)
            {
                return;
            }

            _patchesByMethod.Remove(method);
        }

        /// <summary>
        /// Drops this method's live patch and hands the removed entry back, so a caller whose
        /// unpatch fails can put it back whole. Returns null when no live patch was recorded.
        /// </summary>
        internal HotReloadActivePatchEntry DeactivatePatch(MethodBase method)
        {
            Debug.Assert(method != null, "method must not be null.");
            if (!_patchesByMethod.TryGetValue(method, out HotReloadActivePatchEntry entry) || !entry.IsActive)
            {
                return null;
            }

            _patchesByMethod.Remove(method);
            return entry;
        }

        // Why this one restore skips the shim-registration check: Revert removes the shim
        // registration before it unpatches, so an unpatch that fails restores a patch whose shim is
        // already gone. Refusing it here would drop the record of a patch that is still live.
        internal void ReactivatePatch(HotReloadActivePatchEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            _patchesByMethod[entry.Method] = entry;
        }

        // Why pending entries accept these too: the transpiler records them from inside Harmony's
        // Patch call, before the patch is committed. A method with no entry at all is a rebuild
        // outside any hot-reload patch, and has nothing this generation should record.
        internal void RecordTransplantLocals(MethodBase method, IReadOnlyList<LocalBuilder> locals)
        {
            Debug.Assert(method != null, "method must not be null.");
            if (!_patchesByMethod.TryGetValue(method, out HotReloadActivePatchEntry entry))
            {
                return;
            }

            entry.RecordTransplantLocals(locals);
        }

        internal void RecordTransplantPreambleLength(MethodBase method, int length)
        {
            Debug.Assert(method != null, "method must not be null.");
            if (!_patchesByMethod.TryGetValue(method, out HotReloadActivePatchEntry entry))
            {
                return;
            }

            entry.RecordTransplantPreambleLength(length);
        }

        internal void RecordSupersededSignature(string oldMethodKey, string replacementDisplayName)
        {
            Debug.Assert(!string.IsNullOrEmpty(oldMethodKey), "oldMethodKey must not be empty.");
            Debug.Assert(
                !string.IsNullOrEmpty(replacementDisplayName),
                "replacementDisplayName must not be empty.");

            _supersededReplacementByOldMethodKey[oldMethodKey] = replacementDisplayName;
        }

        internal void RemoveSupersededSignature(string oldMethodKey)
        {
            if (string.IsNullOrEmpty(oldMethodKey))
            {
                return;
            }

            _supersededReplacementByOldMethodKey.Remove(oldMethodKey);
        }

        /// <summary>The registered shim of this method, whatever its patch state.</summary>
        internal MethodBase FindShim(MethodBase method)
        {
            Debug.Assert(method != null, "method must not be null.");
            return _shimMethodsByMethod.TryGetValue(method, out HotReloadShimMethodEntry entry)
                ? entry.ShimMethod
                : null;
        }

        // Why pending counts as a shim to report: with Priority.First, hot-reload's transpiler runs
        // before pause-point's during Apply, and the patch is committed only after Patch returns.
        // Without the pending shim pause-point would inject original-body indexes into the shim
        // stream.
        internal MethodInfo FindPatchShim(MethodBase method)
        {
            Debug.Assert(method != null, "method must not be null.");
            return _patchesByMethod.TryGetValue(method, out HotReloadActivePatchEntry entry)
                ? entry.Shim
                : null;
        }

        internal bool IsPatchActive(MethodBase method)
        {
            Debug.Assert(method != null, "method must not be null.");
            return _patchesByMethod.TryGetValue(method, out HotReloadActivePatchEntry entry) && entry.IsActive;
        }

        internal bool IsPatchPending(MethodBase method)
        {
            Debug.Assert(method != null, "method must not be null.");
            return _patchesByMethod.TryGetValue(method, out HotReloadActivePatchEntry entry) && !entry.IsActive;
        }

        /// <summary>Whether this generation holds a pending or live patch of the method.</summary>
        internal bool HasPatch(MethodBase method)
        {
            Debug.Assert(method != null, "method must not be null.");
            return _patchesByMethod.ContainsKey(method);
        }

        internal IReadOnlyList<LocalBuilder> FindTransplantLocals(MethodBase method)
        {
            Debug.Assert(method != null, "method must not be null.");
            return _patchesByMethod.TryGetValue(method, out HotReloadActivePatchEntry entry)
                ? entry.TransplantLocals
                : null;
        }

        internal int FindTransplantPreambleLength(MethodBase method)
        {
            Debug.Assert(method != null, "method must not be null.");
            return _patchesByMethod.TryGetValue(method, out HotReloadActivePatchEntry entry)
                ? entry.TransplantPreambleLength
                : 0;
        }

        internal List<MethodBase> ListActiveMethods()
        {
            List<MethodBase> methods = new List<MethodBase>(_patchesByMethod.Count);
            foreach (KeyValuePair<MethodBase, HotReloadActivePatchEntry> pair in _patchesByMethod)
            {
                if (pair.Value.IsActive)
                {
                    methods.Add(pair.Key);
                }
            }

            return methods;
        }

        internal int ActivePatchCount
        {
            get
            {
                int count = 0;
                foreach (KeyValuePair<MethodBase, HotReloadActivePatchEntry> pair in _patchesByMethod)
                {
                    if (pair.Value.IsActive)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        internal int AddedMemberCount => _addedMembersByMethodKey.Count;

        /// <summary>
        /// Whether this file still has a live patch or an added method, which is what makes its
        /// edited lines differ from the compiled line map pause points resolve against.
        /// </summary>
        internal bool HasActiveHotReloadChanges => ActivePatchCount > 0 || AddedMemberCount > 0;

        internal bool IsActiveMember(string methodKey)
        {
            Debug.Assert(!string.IsNullOrEmpty(methodKey), "methodKey must not be empty.");
            return _addedMembersByMethodKey.ContainsKey(methodKey);
        }

        internal IReadOnlyList<string> ListActiveAddedMethodKeys()
        {
            return new List<string>(_addedMembersByMethodKey.Keys);
        }

        /// <summary>
        /// The added method whose source range holds the 1-based line, or null when no added method
        /// of this file covers it.
        /// </summary>
        internal HotReloadAddedMethodAtLine FindAddedMethodContainingLine(int line)
        {
            foreach (KeyValuePair<string, HotReloadAddedMemberInfo> pair in _addedMembersByMethodKey)
            {
                if (pair.Value.ContainsSourceLine(line))
                {
                    return pair.Value.ToAddedMethodAtLine();
                }
            }

            return null;
        }

        internal void DescribeAddedMembers(List<HotReloadAddedMemberInfo> members)
        {
            Debug.Assert(members != null, "members must not be null.");
            foreach (KeyValuePair<string, HotReloadAddedMemberInfo> pair in _addedMembersByMethodKey)
            {
                members.Add(pair.Value);
            }
        }

        internal void CollectAddedFieldsForType(string normalizedTypeName, HashSet<string> fieldNames)
        {
            Debug.Assert(fieldNames != null, "fieldNames must not be null.");
            if (!_addedFieldsByTypeKey.TryGetValue(normalizedTypeName, out List<string> fields))
            {
                return;
            }

            for (int index = 0; index < fields.Count; index++)
            {
                fieldNames.Add(fields[index]);
            }
        }

        internal void DescribeAddedFields(List<HotReloadAddedFieldDescription> descriptions)
        {
            Debug.Assert(descriptions != null, "descriptions must not be null.");
            foreach (KeyValuePair<string, List<string>> typePair in _addedFieldsByTypeKey)
            {
                for (int index = 0; index < typePair.Value.Count; index++)
                {
                    descriptions.Add(
                        new HotReloadAddedFieldDescription(
                            NormalizedPath,
                            typePair.Key,
                            typePair.Value[index]));
                }
            }
        }

        internal bool TryGetSupersededReplacement(string oldMethodKey, out string replacementDisplayName)
        {
            if (string.IsNullOrEmpty(oldMethodKey))
            {
                replacementDisplayName = null;
                return false;
            }

            return _supersededReplacementByOldMethodKey.TryGetValue(oldMethodKey, out replacementDisplayName);
        }

        /// <summary>
        /// The source this generation's shim assembly was compiled from, or null when this
        /// generation registered no method that names a compiled assembly on disk.
        /// </summary>
        internal string LoadVerifiedSnapshotSource()
        {
            string dllPath = FindCompiledAssemblyLocation();
            if (string.IsNullOrEmpty(dllPath))
            {
                return null;
            }

            return HotReloadSourceBaseline.LoadVerifiedSnapshotSource(Path, dllPath);
        }

        /// <summary>
        /// The compiled assembly the verified snapshot of this file is keyed on, or null when no
        /// registered patched or added method names one.
        /// </summary>
        /// <remarks>
        /// Why the first method: every method registered for one source file lives in the same
        /// compiled assembly. Why added methods too: a reload that only added methods patches
        /// nothing, and without them its file would lose the compiled line map that pause points
        /// and the line-shift warning read.
        /// </remarks>
        internal string FindCompiledAssemblyLocation()
        {
            foreach (MethodBase originalMethod in _shimMethodsByMethod.Keys)
            {
                Type declaringType = originalMethod.DeclaringType;
                if (declaringType == null)
                {
                    continue;
                }

                string dllPath = declaringType.Assembly.Location;
                if (!string.IsNullOrEmpty(dllPath))
                {
                    return dllPath;
                }
            }

            foreach (HotReloadAddedMemberInfo member in _addedMembersByMethodKey.Values)
            {
                if (!string.IsNullOrEmpty(member.CompiledAssemblyPath))
                {
                    return member.CompiledAssemblyPath;
                }
            }

            return null;
        }

        /// <summary>
        /// This file's shim bytes and the methods pause-point may resolve markers against, or null
        /// when no registered method is still patched.
        /// </summary>
        internal HotReloadShimFileLookup BuildShimLookup()
        {
            List<HotReloadShimMethodLookup> methods = new List<HotReloadShimMethodLookup>();
            foreach (KeyValuePair<MethodBase, HotReloadShimMethodEntry> methodPair in _shimMethodsByMethod)
            {
                // Why filter by live patch: a shim generation can outlive a Revert of individual
                // methods; pause-point must only see methods still patched.
                if (!IsPatchActive(methodPair.Key))
                {
                    continue;
                }

                HotReloadShimMethodEntry entry = methodPair.Value;
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

            return new HotReloadShimFileLookup(_assemblyBytes, _pdbBytes, _loadedAssembly, methods);
        }

        private static string NormalizeTypeKey(string typeName)
        {
            return typeName.Replace('/', '+');
        }
    }
}
