using System;
using System.Collections.Generic;
using System.IO;

using Mono.Cecil;
using Mono.Cecil.Cil;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Keeps the Cecil view of a compiled assembly (module, state-machine owner index, and the
    /// call / ldftn instructions) alive across hot reload runs while the dll on disk is unchanged.
    /// Why: every hot reload run re-read and re-walked the whole ScriptAssemblies dll to find
    /// callers (~0.24 s on a large test assembly), although the dll only changes on a compile,
    /// which also reloads the domain and therefore empties this cache.
    /// </summary>
    internal sealed class HotReloadCompiledCallSiteCache
    {
        // Why a small cap: each entry holds a full Cecil module in memory. A hot reload run
        // scans the target assembly plus the assemblies that reference it, which is a handful.
        internal const int DefaultCapacity = 8;

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

            public Entry(
                string dllPath,
                DllFingerprint fingerprint,
                AssemblyDefinition assembly,
                Dictionary<string, MethodDefinition> logicalOwners,
                List<CompiledCallSite> callSites)
            {
                DllPath = dllPath;
                Fingerprint = fingerprint;
                _assembly = assembly;
                Module = assembly.MainModule;
                LogicalOwners = logicalOwners;
                CallSites = callSites;
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

        public static HotReloadCompiledCallSiteCache Shared { get; } =
            new HotReloadCompiledCallSiteCache(DefaultCapacity);

        private readonly object _gate = new object();
        private readonly int _capacity;
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private long _accessSequence;
        private int _loadCount;

        public HotReloadCompiledCallSiteCache(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "capacity must be positive.");
            }

            _capacity = capacity;
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
        /// Returns the cached view of <paramref name="dllPath"/> when the file is byte-identical to
        /// the cached one, otherwise reads it and replaces the stale entry. The file must exist.
        /// </summary>
        public Entry GetOrLoad(string dllPath)
        {
            Debug.Assert(!string.IsNullOrEmpty(dllPath), "dllPath must not be null or empty.");

            string fullPath = Path.GetFullPath(dllPath);
            FileInfo fileInfo = new FileInfo(fullPath);
            DllFingerprint fingerprint = new DllFingerprint(
                fileInfo.Length,
                fileInfo.LastWriteTimeUtc.Ticks,
                ReadModuleVersionId(fullPath));

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

                Entry loaded = Load(fullPath, fingerprint);
                loaded.LastAccess = _accessSequence;
                _loadCount++;
                EvictLeastRecentlyUsedWhileOverCapacity();
                _entries[fullPath] = loaded;
                return loaded;
            }
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

        private void EvictLeastRecentlyUsedWhileOverCapacity()
        {
            // The new entry is added after this call, so make room for it.
            while (_entries.Count >= _capacity)
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

                Entry evicted = _entries[leastRecentPath];
                _entries.Remove(leastRecentPath);
                evicted.Dispose();
            }
        }

        private static Guid ReadModuleVersionId(string fullPath)
        {
            // Deferred reading touches only the metadata header, which is what makes this check
            // cheap enough to run on every lookup.
            ReaderParameters headerOnly = new ReaderParameters { ReadingMode = ReadingMode.Deferred };
            using ModuleDefinition module = ModuleDefinition.ReadModule(fullPath, headerOnly);
            return module.Mvid;
        }

        private static Entry Load(string fullPath, DllFingerprint fingerprint)
        {
            // InMemory + no resolver: operand FullName comparison in the scanner does not require
            // type resolution, and the file handle is released as soon as the read completes.
            ReaderParameters readerParameters = new ReaderParameters { InMemory = true };
            AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(fullPath, readerParameters);
            ModuleDefinition module = assembly.MainModule;
            Dictionary<string, MethodDefinition> logicalOwners = new Dictionary<string, MethodDefinition>();
            List<CompiledCallSite> callSites = new List<CompiledCallSite>();
            foreach (TypeDefinition type in module.GetTypes())
            {
                foreach (MethodDefinition method in type.Methods)
                {
                    TryIndexStateMachineOwner(method, logicalOwners);
                    CollectCallSites(method, callSites);
                }
            }

            return new Entry(fullPath, fingerprint, assembly, logicalOwners, callSites);
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
