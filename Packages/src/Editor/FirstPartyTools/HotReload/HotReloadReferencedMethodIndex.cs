using System;
using System.Collections.Generic;
using System.IO;

using Mono.Cecil;

using UnityEngine;

using DllFingerprint = io.github.hatayama.UnityCliLoop.FirstPartyTools.HotReloadCompiledCallSiteCache.DllFingerprint;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Per compiled assembly, the methods of other assemblies its MemberRef table names, keyed
    /// "&lt;assembly&gt;|&lt;open declaring type full name&gt;::&lt;method name&gt;". Why: a caller scan of a
    /// method reads and walks every assembly that references the method's assembly, although
    /// an assembly whose MemberRef table does not name the method cannot call it (a cross-assembly
    /// call site is a MemberRef row); the table is read in milliseconds, the walk in hundreds.
    /// Kept while the dll's length, write time and MVID match, like the compiled call-site cache.
    /// </summary>
    internal sealed class HotReloadReferencedMethodIndex
    {
        public static HotReloadReferencedMethodIndex Shared { get; } = new HotReloadReferencedMethodIndex();

        private readonly object _gate = new object();
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private int _loadCount;

        /// <summary>Number of times a dll's MemberRef table was read.</summary>
        internal int LoadCount
        {
            get
            {
                lock (_gate)
                {
                    return _loadCount;
                }
            }
        }

        /// <summary>Key of a method as the index files it; the scan builds the same key from a target.</summary>
        internal static string BuildKey(string assemblyName, string openDeclaringTypeFullName, string methodName)
        {
            return assemblyName + "|" + openDeclaringTypeFullName + "::" + methodName;
        }

        /// <summary>
        /// Whether the dll's MemberRef table names any of <paramref name="keys"/>. True is a
        /// necessary condition for a call site, not a sufficient one; false means the dll holds
        /// no call site of those methods. The file must exist.
        /// </summary>
        internal bool MentionsAny(string dllPath, IReadOnlyCollection<string> keys)
        {
            Debug.Assert(!string.IsNullOrEmpty(dllPath), "dllPath must not be null or empty.");
            Debug.Assert(keys != null, "keys must not be null.");

            string fullPath = Path.GetFullPath(dllPath);
            DllFingerprint fingerprint = HotReloadCompiledCallSiteCache.ReadFingerprint(fullPath);
            lock (_gate)
            {
                if (!_entries.TryGetValue(fullPath, out Entry entry) || !entry.Fingerprint.Equals(fingerprint))
                {
                    // Why stored under the fingerprint read before the table: a dll replaced while
                    // it is read shows another fingerprint next time and is read again.
                    HashSet<string> referenced = ReadReferencedMethodKeys(fullPath);
                    _loadCount++;
                    entry = new Entry(fingerprint, referenced);
                    _entries[fullPath] = entry;
                }

                foreach (string key in keys)
                {
                    if (entry.Keys.Contains(key))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        internal void Clear()
        {
            lock (_gate)
            {
                _entries.Clear();
            }
        }

        private static HashSet<string> ReadReferencedMethodKeys(string fullDllPath)
        {
            // Deferred reading leaves method bodies untouched; only the MemberRef table is walked.
            ReaderParameters metadataOnly = new ReaderParameters { ReadingMode = ReadingMode.Deferred };
            using (AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(fullDllPath, metadataOnly))
            {
                HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (MemberReference member in assembly.MainModule.GetMemberReferences())
                {
                    MethodReference method = member as MethodReference;
                    // Fields, and a method row without a declaring type, are never a scan's match.
                    if (method == null || method.DeclaringType == null)
                    {
                        continue;
                    }

                    // The same opening the scan's identity match uses, so a call through Host<int>
                    // is filed under Host`1.
                    TypeReference open = HotReloadCompiledCallSiteIndex.GetOpenDeclaringType(method.DeclaringType);
                    // Why every assembly scope and not only project assemblies: the project's
                    // assembly list is empty while Unity compiles, and a key set built then would
                    // hide real callers until this dll is rebuilt. A module-scoped row names a type
                    // of this dll itself, which a scan of another assembly never matches.
                    AssemblyNameReference scope = open.Scope as AssemblyNameReference;
                    if (scope == null)
                    {
                        continue;
                    }

                    keys.Add(BuildKey(scope.Name, open.FullName, method.Name));
                }

                return keys;
            }
        }

        private sealed class Entry
        {
            public readonly DllFingerprint Fingerprint;
            public readonly HashSet<string> Keys;

            public Entry(DllFingerprint fingerprint, HashSet<string> keys)
            {
                Fingerprint = fingerprint;
                Keys = keys;
            }
        }
    }
}
