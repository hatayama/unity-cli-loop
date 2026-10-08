using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

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
    /// Kept while the dll's length, write time and MVID match, like the compiled call-site cache,
    /// both in memory and in a file per dll so that a new domain does not read the dll again.
    /// </summary>
    internal sealed class HotReloadReferencedMethodIndex
    {
        public static HotReloadReferencedMethodIndex Shared { get; } = new HotReloadReferencedMethodIndex(
            Path.Combine(
                Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
                HotReloadConstants.ReferencedMethodsRelativeDirectory));

        private readonly object _gate = new object();
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly string _persistenceDirectory;
        private const string PersistedFormatHeader = "uloop-referenced-methods 1";

        private int _loadCount;
        private int _persistedLoadCount;

        internal HotReloadReferencedMethodIndex(string persistenceDirectory)
        {
            Debug.Assert(!string.IsNullOrEmpty(persistenceDirectory), "persistenceDirectory must not be null or empty.");
            _persistenceDirectory = persistenceDirectory;
        }

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

        /// <summary>Number of times a dll's set was read from its persisted file instead of the dll.</summary>
        internal int PersistedLoadCount
        {
            get
            {
                lock (_gate)
                {
                    return _persistedLoadCount;
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
                    string persistedPath = PersistedSetPath(fullPath);
                    HashSet<string> referenced = TryReadPersistedSet(persistedPath, fingerprint);
                    if (referenced != null)
                    {
                        _persistedLoadCount++;
                    }
                    else
                    {
                        // Why the read before any write: a read that throws leaves no entry and no file.
                        referenced = ReadReferencedMethodKeys(fullPath);
                        _loadCount++;
                        WritePersistedSet(persistedPath, fingerprint, referenced);
                    }

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

        private string PersistedSetPath(string fullDllPath)
        {
            return Path.Combine(_persistenceDirectory, Path.GetFileNameWithoutExtension(fullDllPath) + ".txt");
        }

        // File format: UTF-8 without a BOM, every line ends with "\n", fields are TAB-separated and
        // numbers are invariant.
        //   line 1: the format header
        //   line 2: dll length, dll write time ticks, MVID ("N" format), count
        //   then count lines: one key each
        // Null for a missing file, another stamp, or any malformed part: a file cut short or edited
        // by hand must not answer with part of a set, because a missing key would skip a caller.
        private static HashSet<string> TryReadPersistedSet(string path, DllFingerprint fingerprint)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            string[] lines = File.ReadAllText(path, Encoding.UTF8).Split('\n');
            for (int index = 0; index < lines.Length; index++)
            {
                lines[index] = lines[index].TrimEnd('\r');
            }

            if (lines.Length < 3 || !string.Equals(lines[0], PersistedFormatHeader, StringComparison.Ordinal))
            {
                return null;
            }

            if (!TryParseStampLine(lines[1], out DllFingerprint persisted, out int count)
                || !persisted.Equals(fingerprint))
            {
                return null;
            }

            // The header, the stamp, count keys and the empty string after the last "\n".
            if (lines.Length != 3 + count || lines[lines.Length - 1].Length != 0)
            {
                return null;
            }

            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < count; index++)
            {
                string key = lines[2 + index];
                if (key.Length == 0)
                {
                    return null;
                }

                keys.Add(key);
            }

            return keys;
        }

        private static bool TryParseStampLine(string line, out DllFingerprint fingerprint, out int count)
        {
            fingerprint = default;
            count = 0;
            string[] fields = line.Split('\t');
            if (fields.Length != 4
                || !long.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out long length)
                || !long.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out long ticks)
                || !Guid.TryParseExact(fields[2], "N", out Guid moduleVersionId)
                || !int.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out count))
            {
                return false;
            }

            fingerprint = new DllFingerprint(length, ticks, moduleVersionId);
            return true;
        }

        private void WritePersistedSet(string path, DllFingerprint fingerprint, HashSet<string> keys)
        {
            Directory.CreateDirectory(_persistenceDirectory);
            StringBuilder text = new StringBuilder();
            text.Append(PersistedFormatHeader).Append('\n');
            text.Append(fingerprint.Length.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .Append(fingerprint.LastWriteTimeUtcTicks.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .Append(fingerprint.ModuleVersionId.ToString("N")).Append('\t')
                .Append(keys.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
            foreach (string key in keys)
            {
                text.Append(key).Append('\n');
            }

            // Why a temp file and a move: a reader in another domain never sees a half-written set.
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
