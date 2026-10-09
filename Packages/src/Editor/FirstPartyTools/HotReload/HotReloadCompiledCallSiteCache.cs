using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

using Mono.Cecil;
using Mono.Cecil.Cil;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Keeps the Cecil view of a compiled assembly (module, state-machine owner index, the call /
    /// ldftn instructions, and an index of them by the method they reference) alive across hot
    /// reload runs while the dll on disk is unchanged.
    /// Why: every hot reload run re-read and re-walked the whole ScriptAssemblies dll to find
    /// callers (~0.24 s on a large test assembly), although the dll only changes on a compile,
    /// which also reloads the domain and therefore empties this cache.
    /// </summary>
    internal sealed class HotReloadCompiledCallSiteCache
    {
        // Why a byte budget: an entry costs about its dll's length, because it keeps the dll's
        // bytes (InMemory) and its metadata only; the methods' instruction lists are released
        // once the call sites are collected. Dlls range from kilobytes to megabytes, so a count
        // bounds neither memory nor a run: with a cap of 64, a run that scanned 68 assemblies
        // (42 MB) evicted 9 of them (29 MB, the edited assembly among them) at every hold release
        // and read them again on the next run, about 2 s per run. 256 MB keeps every assembly
        // such a run touches and still bounds the Editor's memory. Within a run nothing is
        // evicted whatever the budget (HoldEntriesForRun); the budget bounds what stays cached
        // between runs. The budget counts file lengths, not the exact in-memory size.
        internal const long DefaultBudgetBytes = 256L * 1024 * 1024;

        /// <summary>
        /// One compiled instruction that may reference a target method.
        /// </summary>
        internal readonly struct CompiledCallSite
        {
            public readonly MethodDefinition Caller;
            public readonly MethodReference Operand;
            public readonly bool IsFunctionPointerLoad;

            public CompiledCallSite(MethodDefinition caller, MethodReference operand, bool isFunctionPointerLoad)
            {
                Caller = caller;
                Operand = operand;
                IsFunctionPointerLoad = isFunctionPointerLoad;
            }
        }

        /// <summary>
        /// The reusable Cecil view of one dll file, valid while <see cref="Fingerprint"/> matches the file.
        /// An entry is returned outside the cache lock. While a hold from
        /// <see cref="HoldEntriesForRun"/> is open no entry is evicted, so an entry is disposed only
        /// when a later lookup finds its file changed and replaces it. An entry kept after the hold
        /// ends, or used without one, may also be disposed by a later lookup that evicts it.
        /// </summary>
        internal sealed class Entry : IDisposable
        {
            public readonly string DllPath;
            public readonly DllFingerprint Fingerprint;
            public readonly ModuleDefinition Module;
            public readonly Dictionary<string, MethodDefinition> LogicalOwners;
            public readonly List<CompiledCallSite> CallSites;
            public long LastAccess;

            private readonly AssemblyDefinition _assembly;
            private readonly HotReloadCompiledCallSiteIndex _callSiteIndex;

            public Entry(
                string dllPath,
                DllFingerprint fingerprint,
                AssemblyDefinition assembly,
                Dictionary<string, MethodDefinition> logicalOwners,
                List<CompiledCallSite> callSites,
                HotReloadCompiledCallSiteIndex callSiteIndex)
            {
                DllPath = dllPath;
                Fingerprint = fingerprint;
                _assembly = assembly;
                Module = assembly.MainModule;
                LogicalOwners = logicalOwners;
                CallSites = callSites;
                _callSiteIndex = callSiteIndex;
            }

            /// <summary>
            /// Positions in <see cref="CallSites"/>, in ascending order, of the call sites whose
            /// operand's open declaring type is named <paramref name="openDeclaringTypeFullName"/>
            /// and whose method is named <paramref name="methodName"/>; empty when there are none.
            /// </summary>
            public IReadOnlyList<int> LookupCallSiteIndices(string openDeclaringTypeFullName, string methodName)
            {
                return _callSiteIndex.Lookup(openDeclaringTypeFullName, methodName);
            }

            public void Dispose()
            {
                _assembly.Dispose();
            }
        }

        /// <summary>
        /// Identity of the dll an entry was built from. Length and write time are the cheap
        /// first check; the module version id (MVID) catches a same-size rewrite within the same
        /// timestamp granularity. Why MVID and not a content hash: hashing a megabyte-sized dll
        /// costs about as much as the Cecil read this cache avoids, while the MVID is read from
        /// the metadata header in under a millisecond and the compiler assigns a new one to
        /// every distinct build output.
        /// </summary>
        internal readonly struct DllFingerprint : IEquatable<DllFingerprint>
        {
            public readonly long Length;
            public readonly long LastWriteTimeUtcTicks;
            public readonly Guid ModuleVersionId;

            public DllFingerprint(long length, long lastWriteTimeUtcTicks, Guid moduleVersionId)
            {
                Length = length;
                LastWriteTimeUtcTicks = lastWriteTimeUtcTicks;
                ModuleVersionId = moduleVersionId;
            }

            public bool Equals(DllFingerprint other)
            {
                return Length == other.Length
                    && LastWriteTimeUtcTicks == other.LastWriteTimeUtcTicks
                    && ModuleVersionId == other.ModuleVersionId;
            }

            public override bool Equals(object obj)
            {
                return obj is DllFingerprint other && Equals(other);
            }

            public override int GetHashCode()
            {
                return ModuleVersionId.GetHashCode();
            }
        }

        /// <summary>
        /// Test-only hooks into the load path, used to force a file change between the fingerprint
        /// and the Cecil read, or a failure while the index is being built.
        /// </summary>
        internal sealed class LoadProbes
        {
            public Action<string> BeforeAssemblyRead;
            public Action<AssemblyDefinition> AfterAssemblyRead;
        }

        // Why two attempts: the fingerprint and the full Cecil read are separate opens, so the
        // file can be replaced in between. One retry absorbs a single replacement; a second
        // mismatch means the file is being rewritten continuously and the caller must fail.
        private const int MaxConsistentLoadAttempts = 2;

        public static HotReloadCompiledCallSiteCache Shared { get; } =
            new HotReloadCompiledCallSiteCache(DefaultBudgetBytes);

        private readonly object _gate = new object();
        private readonly long _budgetBytes;
        private readonly LoadProbes _probes;
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private long _accessSequence;
        private int _loadCount;
        // Sum of the cached entries' dll lengths; kept in step with _entries under _gate.
        private long _cachedBytes;
        private int _lastHoldReleaseEvictedCount;
        private long _lastHoldReleaseEvictedBytes;
        // Number of open holds; touched only under _gate.
        private int _holdDepth;

        public HotReloadCompiledCallSiteCache(long budgetBytes, LoadProbes probes = null)
        {
            if (budgetBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(budgetBytes), "budgetBytes must be positive.");
            }

            _budgetBytes = budgetBytes;
            _probes = probes;
        }

        /// <summary>
        /// Number of entries currently held.
        /// </summary>
        public int Count
        {
            get
            {
                lock (_gate)
                {
                    return _entries.Count;
                }
            }
        }

        /// <summary>
        /// Number of times a dll was read and walked with Cecil, i.e. cache misses.
        /// </summary>
        public int LoadCount
        {
            get
            {
                lock (_gate)
                {
                    return _loadCount;
                }
            }
        }

        /// <summary>
        /// Sum of the dll lengths of the entries currently held. It exceeds the budget only while
        /// a hold is open, or when a single entry is larger than the budget.
        /// </summary>
        public long CachedBytes
        {
            get
            {
                lock (_gate)
                {
                    return _cachedBytes;
                }
            }
        }

        /// <summary>
        /// Number of entries evicted when the outermost hold last ended; 0 before any hold ended.
        /// </summary>
        public int LastHoldReleaseEvictedCount
        {
            get
            {
                lock (_gate)
                {
                    return _lastHoldReleaseEvictedCount;
                }
            }
        }

        /// <summary>
        /// Sum of the dll lengths of the entries evicted when the outermost hold last ended.
        /// </summary>
        public long LastHoldReleaseEvictedBytes
        {
            get
            {
                lock (_gate)
                {
                    return _lastHoldReleaseEvictedBytes;
                }
            }
        }

        /// <summary>
        /// Returns the cached view of <paramref name="dllPath"/> while the file's length, last
        /// write time, and module version id (MVID) all match the cached entry; otherwise reads the
        /// file and replaces the stale entry. This is a heuristic identity, not a content hash: it
        /// assumes a recompile assigns a new MVID, which the compiler does for every build output.
        /// A rewrite that keeps all three (a metadata-preserving edit with the timestamp restored)
        /// is served stale. The file must exist.
        /// </summary>
        /// <exception cref="IOException">The file kept changing while it was being read.</exception>
        public Entry GetOrLoad(string dllPath)
        {
            TryGetOrLoad(dllPath, true, out Entry entry, out bool ignored);
            return entry;
        }

        /// <summary>
        /// Like <see cref="GetOrLoad"/>, but reads the file only when <paramref name="allowLoad"/>
        /// is true: with it false, a dll that is not cached, or whose cached entry is stale, is
        /// reported as not available (false) and the stale entry is kept for the next allowed read.
        /// <paramref name="loaded"/> is true only when this call read the file.
        /// </summary>
        /// <remarks>
        /// Why one method rather than a peek plus <see cref="GetOrLoad"/>: a stale entry must count
        /// as a read for whoever pays for reads, and the fingerprint must be checked once.
        /// </remarks>
        /// <exception cref="IOException">The file kept changing while it was being read.</exception>
        public bool TryGetOrLoad(string dllPath, bool allowLoad, out Entry entry, out bool loaded)
        {
            Debug.Assert(!string.IsNullOrEmpty(dllPath), "dllPath must not be null or empty.");

            string fullPath = Path.GetFullPath(dllPath);
            DllFingerprint fingerprint = ReadFingerprint(fullPath);

            lock (_gate)
            {
                _accessSequence++;
                if (_entries.TryGetValue(fullPath, out Entry existing))
                {
                    if (existing.Fingerprint.Equals(fingerprint))
                    {
                        existing.LastAccess = _accessSequence;
                        entry = existing;
                        loaded = false;
                        return true;
                    }

                    if (!allowLoad)
                    {
                        entry = null;
                        loaded = false;
                        return false;
                    }

                    _entries.Remove(fullPath);
                    _cachedBytes -= existing.Fingerprint.Length;
                    existing.Dispose();
                }
                else if (!allowLoad)
                {
                    entry = null;
                    loaded = false;
                    return false;
                }

                Entry loadedEntry = LoadConsistent(fullPath, fingerprint);
                loadedEntry.LastAccess = _accessSequence;
                // Why not while held: a run scans the same dlls once per caller it looks up, so an
                // eviction inside the run turns one read per dll into one read per scan.
                if (_holdDepth == 0)
                {
                    EvictLeastRecentlyUsedUntilFits(loadedEntry.Fingerprint.Length);
                }

                _entries[fullPath] = loadedEntry;
                _cachedBytes += loadedEntry.Fingerprint.Length;
                entry = loadedEntry;
                loaded = true;
                return true;
            }
        }

        /// <summary>
        /// Keeps every entry until the returned hold is disposed, however many dlls are read in the
        /// meantime; when the outermost hold ends, the least recently used entries are evicted until
        /// the cached bytes fit the budget, keeping at least one entry. Holds nest, and disposing one hold twice releases it once.
        /// </summary>
        public IDisposable HoldEntriesForRun()
        {
            lock (_gate)
            {
                _holdDepth++;
            }

            return new Hold(this);
        }

        /// <summary>
        /// Drops every entry. Intended for tests and for callers that know the dlls changed.
        /// </summary>
        public void Clear()
        {
            lock (_gate)
            {
                foreach (Entry entry in _entries.Values)
                {
                    entry.Dispose();
                }

                _entries.Clear();
                _cachedBytes = 0;
            }
        }

        // One open hold. Not tied to a thread: a run's using block may end on another thread
        // after an await, so the state it changes lives in the cache, under _gate.
        private sealed class Hold : IDisposable
        {
            private HotReloadCompiledCallSiteCache _owner;

            public Hold(HotReloadCompiledCallSiteCache owner)
            {
                _owner = owner;
            }

            public void Dispose()
            {
                // Why Exchange: a second Dispose, even a concurrent one, must find no owner, or it
                // would end an outer hold that is still open.
                HotReloadCompiledCallSiteCache owner = Interlocked.Exchange(ref _owner, null);
                if (owner == null)
                {
                    return;
                }

                HoldRelease release = owner.ReleaseHold();
                if (release.EvictedCount == 0)
                {
                    return;
                }

                // Why outside the lock: the vibe log writes a file, which must not block lookups.
                // Only numbers are logged; dll paths may name the user's project.
                VibeLogger.LogInfo(
                    HotReloadConstants.VibeLogCallSiteCacheEvicted,
                    "Hot reload call-site cache evicted entries at the end of a run.",
                    new
                    {
                        evictedCount = release.EvictedCount,
                        evictedBytes = release.EvictedBytes,
                        cachedBytes = release.CachedBytes,
                        budgetBytes = owner._budgetBytes
                    });
            }
        }

        // What ending a hold evicted, read under the lock and logged after it is released.
        private readonly struct HoldRelease
        {
            public readonly int EvictedCount;
            public readonly long EvictedBytes;
            public readonly long CachedBytes;

            public HoldRelease(int evictedCount, long evictedBytes, long cachedBytes)
            {
                EvictedCount = evictedCount;
                EvictedBytes = evictedBytes;
                CachedBytes = cachedBytes;
            }
        }

        private HoldRelease ReleaseHold()
        {
            lock (_gate)
            {
                Debug.Assert(_holdDepth > 0, "ReleaseHold without a matching hold.");
                _holdDepth--;
                if (_holdDepth > 0)
                {
                    return new HoldRelease(0, 0, _cachedBytes);
                }

                EvictLeastRecentlyUsedDownToBudget();
                return new HoldRelease(_lastHoldReleaseEvictedCount, _lastHoldReleaseEvictedBytes, _cachedBytes);
            }
        }

        private void EvictLeastRecentlyUsedUntilFits(long incomingBytes)
        {
            // The new entry is added after this call, so make room for it. Stops once the cache is
            // empty: an entry larger than the budget is still added, alone.
            while (_entries.Count > 0 && _cachedBytes + incomingBytes > _budgetBytes)
            {
                EvictEntry(FindLeastRecentlyUsedPath());
            }
        }

        private void EvictLeastRecentlyUsedDownToBudget()
        {
            // Nothing is added after this call, so evict down to the budget itself, but keep the
            // last entry even when it alone exceeds the budget, as a lookup without a hold would.
            int evictedCount = 0;
            long evictedBytes = 0;
            while (_cachedBytes > _budgetBytes && _entries.Count > 1)
            {
                string path = FindLeastRecentlyUsedPath();
                evictedBytes += _entries[path].Fingerprint.Length;
                EvictEntry(path);
                evictedCount++;
            }

            _lastHoldReleaseEvictedCount = evictedCount;
            _lastHoldReleaseEvictedBytes = evictedBytes;
        }

        private string FindLeastRecentlyUsedPath()
        {
            string leastRecentPath = null;
            long leastRecentAccess = long.MaxValue;
            foreach (KeyValuePair<string, Entry> pair in _entries)
            {
                if (pair.Value.LastAccess < leastRecentAccess)
                {
                    leastRecentAccess = pair.Value.LastAccess;
                    leastRecentPath = pair.Key;
                }
            }

            return leastRecentPath;
        }

        private void EvictEntry(string path)
        {
            Entry evicted = _entries[path];
            _entries.Remove(path);
            _cachedBytes -= evicted.Fingerprint.Length;
            evicted.Dispose();
        }

        // The referenced-method index reads the same identity, so both caches agree on when a dll changed.
        internal static DllFingerprint ReadFingerprint(string fullPath)
        {
            FileInfo fileInfo = new FileInfo(fullPath);
            return new DllFingerprint(
                fileInfo.Length,
                fileInfo.LastWriteTimeUtc.Ticks,
                ReadModuleVersionId(fullPath));
        }

        private static Guid ReadModuleVersionId(string fullPath)
        {
            // Deferred reading touches only the metadata header, which is what makes this check
            // cheap enough to run on every lookup.
            ReaderParameters headerOnly = new ReaderParameters { ReadingMode = ReadingMode.Deferred };
            using ModuleDefinition module = ModuleDefinition.ReadModule(fullPath, headerOnly);
            return module.Mvid;
        }

        // Publishes an entry only when the file read by Cecil is the file the fingerprint
        // describes: the module's own MVID must equal the fingerprinted one, and the length and
        // write time must still match after the read.
        private Entry LoadConsistent(string fullPath, DllFingerprint fingerprint)
        {
            DllFingerprint expected = fingerprint;
            for (int attempt = 1; attempt <= MaxConsistentLoadAttempts; attempt++)
            {
                Entry loaded = Load(fullPath, expected);
                _loadCount++;
                FileInfo afterRead = new FileInfo(fullPath);
                DllFingerprint observed = new DllFingerprint(
                    afterRead.Length,
                    afterRead.LastWriteTimeUtc.Ticks,
                    loaded.Module.Mvid);
                if (observed.Equals(expected))
                {
                    return loaded;
                }

                loaded.Dispose();
                expected = ReadFingerprint(fullPath);
            }

            throw new IOException(
                "Compiled assembly changed while it was being read " + MaxConsistentLoadAttempts
                + " times in a row: " + fullPath);
        }

        private Entry Load(string fullPath, DllFingerprint fingerprint)
        {
            _probes?.BeforeAssemblyRead?.Invoke(fullPath);

            // InMemory + no resolver: operand FullName comparison in the scanner does not require
            // type resolution, and the file handle is released as soon as the read completes.
            ReaderParameters readerParameters = new ReaderParameters { InMemory = true };
            AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(fullPath, readerParameters);
            // Why an ownership flag: the entry takes over disposal once constructed; until then a
            // failure while indexing must release the assembly here.
            bool ownershipTransferred = false;
            try
            {
                _probes?.AfterAssemblyRead?.Invoke(assembly);
                ModuleDefinition module = assembly.MainModule;
                Dictionary<string, MethodDefinition> logicalOwners = new Dictionary<string, MethodDefinition>();
                List<CompiledCallSite> callSites = new List<CompiledCallSite>();
                foreach (TypeDefinition type in module.GetTypes())
                {
                    foreach (MethodDefinition method in type.Methods)
                    {
                        TryIndexStateMachineOwner(method, logicalOwners);
                        CollectCallSites(method, callSites);
                        ReleaseMethodBody(method);
                    }
                }

                HotReloadCompiledCallSiteIndex callSiteIndex = HotReloadCompiledCallSiteIndex.Build(callSites);
                Entry entry = new Entry(fullPath, fingerprint, assembly, logicalOwners, callSites, callSiteIndex);
                ownershipTransferred = true;
                return entry;
            }
            finally
            {
                if (!ownershipTransferred)
                {
                    assembly.Dispose();
                }
            }
        }

        private static void CollectCallSites(MethodDefinition method, List<CompiledCallSite> callSites)
        {
            if (!method.HasBody)
            {
                return;
            }

            foreach (Instruction instruction in method.Body.Instructions)
            {
                if (!IsCallSiteOpcode(instruction.OpCode))
                {
                    continue;
                }

                MethodReference operand = instruction.Operand as MethodReference;
                if (operand == null)
                {
                    continue;
                }

                callSites.Add(new CompiledCallSite(method, operand, IsFunctionPointerLoadOpcode(instruction.OpCode)));
            }
        }

        // Why release: the walk above is the only reader of instruction lists, and an entry that
        // kept them would hold every method body of every cached dll. The call sites keep only
        // the caller and operand references, which are metadata. Why only after the walk: Cecil
        // reads a body when it is first asked for, so releasing before the walk frees nothing.
        private static void ReleaseMethodBody(MethodDefinition method)
        {
            if (!method.HasBody)
            {
                return;
            }

            method.Body = null;
        }

        private static void TryIndexStateMachineOwner(
            MethodDefinition method,
            Dictionary<string, MethodDefinition> index)
        {
            if (!method.HasCustomAttributes)
            {
                return;
            }

            foreach (CustomAttribute attribute in method.CustomAttributes)
            {
                string attributeName = attribute.AttributeType.Name;
                if (attributeName != HotReloadConstants.AsyncStateMachineAttributeTypeName
                    && attributeName != HotReloadConstants.IteratorStateMachineAttributeTypeName)
                {
                    continue;
                }

                if (!attribute.HasConstructorArguments || attribute.ConstructorArguments.Count == 0)
                {
                    continue;
                }

                TypeReference stateMachineType = attribute.ConstructorArguments[0].Value as TypeReference;
                if (stateMachineType == null)
                {
                    continue;
                }

                index[stateMachineType.FullName] = method;
            }
        }

        private static bool IsCallSiteOpcode(OpCode opCode)
        {
            return opCode == OpCodes.Call
                || opCode == OpCodes.Callvirt
                || opCode == OpCodes.Ldftn
                || opCode == OpCodes.Ldvirtftn;
        }

        private static bool IsFunctionPointerLoadOpcode(OpCode opCode)
        {
            return opCode == OpCodes.Ldftn || opCode == OpCodes.Ldvirtftn;
        }
    }
}
