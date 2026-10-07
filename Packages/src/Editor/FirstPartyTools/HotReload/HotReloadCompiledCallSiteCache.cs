using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

using Mono.Cecil;
using Mono.Cecil.Cil;

using UnityEngine;

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
        // Why 64: an entry keeps the dll's bytes (InMemory) and its metadata only, because the
        // methods' instruction lists are released once the call sites are collected. In a large
        // project one run scanned the target assembly plus the 20 assemblies that reference it,
        // and a cap of 8 made every scan read all 21 again. Within a run nothing is evicted
        // whatever the cap (HoldEntriesForRun); the cap bounds what stays cached between runs.
        internal const int DefaultCapacity = 64;

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
            new HotReloadCompiledCallSiteCache(DefaultCapacity);

        private readonly object _gate = new object();
        private readonly int _capacity;
        private readonly LoadProbes _probes;
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private long _accessSequence;
        private int _loadCount;
        // Number of open holds; touched only under _gate.
        private int _holdDepth;

        public HotReloadCompiledCallSiteCache(int capacity, LoadProbes probes = null)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "capacity must be positive.");
            }

            _capacity = capacity;
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
                        return existing;
                    }

                    _entries.Remove(fullPath);
                    existing.Dispose();
                }

                Entry loaded = LoadConsistent(fullPath, fingerprint);
                loaded.LastAccess = _accessSequence;
                // Why not while held: a run scans the same dlls once per caller it looks up, so an
                // eviction inside the run turns one read per dll into one read per scan.
                if (_holdDepth == 0)
                {
                    EvictLeastRecentlyUsedWhileOverCapacity();
                }

                _entries[fullPath] = loaded;
                return loaded;
            }
        }

        /// <summary>
        /// Keeps every entry until the returned hold is disposed, however many dlls are read in the
        /// meantime; when the outermost hold ends, the least recently used entries are evicted down
        /// to the capacity. Holds nest, and disposing one hold twice releases it once.
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

                owner.ReleaseHold();
            }
        }

        private void ReleaseHold()
        {
            lock (_gate)
            {
                Debug.Assert(_holdDepth > 0, "ReleaseHold without a matching hold.");
                _holdDepth--;
                if (_holdDepth > 0)
                {
                    return;
                }

                EvictLeastRecentlyUsedDownToCapacity();
            }
        }

        private void EvictLeastRecentlyUsedWhileOverCapacity()
        {
            // The new entry is added after this call, so make room for it.
            while (_entries.Count >= _capacity)
            {
                EvictEntry(FindLeastRecentlyUsedPath());
            }
        }

        private void EvictLeastRecentlyUsedDownToCapacity()
        {
            // Nothing is added after this call, so keep entries up to the capacity itself.
            while (_entries.Count > _capacity)
            {
                EvictEntry(FindLeastRecentlyUsedPath());
            }
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
            evicted.Dispose();
        }

        private static DllFingerprint ReadFingerprint(string fullPath)
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
