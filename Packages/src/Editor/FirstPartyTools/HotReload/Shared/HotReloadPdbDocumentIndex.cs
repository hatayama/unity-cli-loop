using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Pdb;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// One source document a portable PDB names, with the checksum the compiler recorded for it.
    /// </summary>
    internal readonly struct HotReloadPdbDocument
    {
        public readonly string Url;
        public readonly DocumentHashAlgorithm HashAlgorithm;
        public readonly byte[] Hash;

        public HotReloadPdbDocument(string url, DocumentHashAlgorithm hashAlgorithm, byte[] hash)
        {
            Url = url;
            HashAlgorithm = hashAlgorithm;
            Hash = hash;
        }
    }

    /// <summary>
    /// Keeps, per compiled assembly, the documents its sequence points refer to, so finding the
    /// document of one source file does not read the dll and the PDB and walk every sequence
    /// point again on each hot reload run. The files only change on a compile. A list is read
    /// again when the dll's length, write time or MVID, or the PDB's length or write time,
    /// differs from the files it was read from. There is no capacity limit: an entry is a short
    /// list of urls and checksums, and there is at most one entry per assembly of the project.
    /// Each list is also written to a file under the persistence directory with the same five
    /// values, so a new index in the next domain reads it from there instead of walking the PDB.
    /// </summary>
    internal sealed class HotReloadPdbDocumentIndex
    {
        // Identity of the files a list was read from. Why all five: the length and the write time
        // are the cheap check for each file, and the MVID catches a dll rewritten to the same size
        // within one timestamp tick, as in HotReloadCompiledCallSiteCache.
        private readonly struct FileStamp : IEquatable<FileStamp>
        {
            public readonly long DllLength;
            public readonly long DllLastWriteTimeUtcTicks;
            public readonly string ModuleVersionId;
            public readonly long PdbLength;
            public readonly long PdbLastWriteTimeUtcTicks;

            public FileStamp(
                long dllLength,
                long dllLastWriteTimeUtcTicks,
                string moduleVersionId,
                long pdbLength,
                long pdbLastWriteTimeUtcTicks)
            {
                DllLength = dllLength;
                DllLastWriteTimeUtcTicks = dllLastWriteTimeUtcTicks;
                ModuleVersionId = moduleVersionId;
                PdbLength = pdbLength;
                PdbLastWriteTimeUtcTicks = pdbLastWriteTimeUtcTicks;
            }

            public bool Equals(FileStamp other)
            {
                return DllLength == other.DllLength
                    && DllLastWriteTimeUtcTicks == other.DllLastWriteTimeUtcTicks
                    && string.Equals(ModuleVersionId, other.ModuleVersionId, StringComparison.Ordinal)
                    && PdbLength == other.PdbLength
                    && PdbLastWriteTimeUtcTicks == other.PdbLastWriteTimeUtcTicks;
            }

            public override bool Equals(object obj)
            {
                return obj is FileStamp other && Equals(other);
            }

            public override int GetHashCode()
            {
                return StringComparer.Ordinal.GetHashCode(ModuleVersionId);
            }
        }

        private const string PersistedFormatHeader = "uloop-pdb-documents 1";
        private const int PersistedStampFieldCount = 6;
        private const int PersistedDocumentFieldCount = 3;

        private sealed class Entry
        {
            public readonly FileStamp Stamp;
            public readonly List<HotReloadPdbDocument> Documents;

            public Entry(FileStamp stamp, List<HotReloadPdbDocument> documents)
            {
                Stamp = stamp;
                Documents = documents;
            }
        }

        // Why a shared instance: the snapshot loader is static and has static callers in
        // several assemblies, like the compiled call-site cache this mirrors.
        public static HotReloadPdbDocumentIndex Shared { get; } = new HotReloadPdbDocumentIndex(
            Path.Combine(
                Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
                HotReloadConstants.PdbDocumentsRelativeDirectory));

        private readonly object _gate = new object();
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly string _persistenceDirectory;
        private int _loadCount;
        private int _persistedLoadCount;

        internal HotReloadPdbDocumentIndex(string persistenceDirectory)
        {
            Debug.Assert(!string.IsNullOrEmpty(persistenceDirectory), "persistenceDirectory must not be null or empty.");
            _persistenceDirectory = persistenceDirectory;
        }

        /// <summary>
        /// Number of times a dll and its PDB were read and walked.
        /// </summary>
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

        /// <summary>
        /// Number of times a list was read from its persisted file instead of walking the PDB.
        /// </summary>
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

        /// <summary>
        /// Finds the document whose url names <paramref name="projectRelativePath"/> among the
        /// documents a sequence point refers to. False when no sequence point refers to such a
        /// document, which is the case for a file without a method body: the PDB still lists that
        /// file, but only a document some code maps to is one a method can be verified against.
        /// <paramref name="moduleVersionId"/> is the dll's MVID as the caller already read it.
        /// </summary>
        internal bool TryFindDocument(
            string dllPath,
            string pdbPath,
            string moduleVersionId,
            string projectRelativePath,
            out HotReloadPdbDocument document)
        {
            Debug.Assert(!string.IsNullOrEmpty(dllPath), "dllPath must not be null or empty.");
            Debug.Assert(!string.IsNullOrEmpty(pdbPath), "pdbPath must not be null or empty.");
            Debug.Assert(!string.IsNullOrEmpty(moduleVersionId), "moduleVersionId must not be null or empty.");
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be null or empty.");

            string fullDllPath = Path.GetFullPath(dllPath);
            FileStamp stamp = ReadStamp(fullDllPath, pdbPath, moduleVersionId);
            lock (_gate)
            {
                if (!_entries.TryGetValue(fullDllPath, out Entry entry) || !entry.Stamp.Equals(stamp))
                {
                    string persistedPath = PersistedListPath(fullDllPath);
                    List<HotReloadPdbDocument> documents = TryReadPersistedList(persistedPath, stamp);
                    if (documents != null)
                    {
                        _persistedLoadCount++;
                    }
                    else
                    {
                        // Why the walk before any write: a walk that throws leaves no entry and no
                        // file, so the next lookup reads the files again instead of answering from
                        // a list of other files.
                        documents = ReadDocuments(fullDllPath, pdbPath);
                        _loadCount++;
                        WritePersistedList(persistedPath, stamp, documents);
                    }

                    // Why stored under the stamp read before the walk: if a file is replaced while
                    // it is being read, the next lookup sees another stamp and reads again, so a
                    // list is never served for files it was not read from.
                    entry = new Entry(stamp, documents);
                    _entries[fullDllPath] = entry;
                }

                return TryFind(entry.Documents, projectRelativePath, out document);
            }
        }

        private string PersistedListPath(string fullDllPath)
        {
            return Path.Combine(_persistenceDirectory, Path.GetFileNameWithoutExtension(fullDllPath) + ".txt");
        }

        // File format: UTF-8 without a BOM, every line ends with "\n", fields are TAB-separated and
        // numbers are invariant.
        //   line 1: the format header
        //   line 2: dll length, dll write time ticks, MVID, PDB length, PDB write time ticks, count
        //   then count lines: (int)HashAlgorithm, the hash in hex ("" for none), url
        // Null for a missing file, another stamp, or any malformed part: a file cut short or edited
        // by hand must not answer with part of a list. Why the url last: it is the only field that
        // can hold anything, so splitting into three keeps a TAB in a url intact.
        private static List<HotReloadPdbDocument> TryReadPersistedList(string path, FileStamp stamp)
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

            if (!TryParseStampLine(lines[1], out FileStamp persistedStamp, out int count)
                || !persistedStamp.Equals(stamp))
            {
                return null;
            }

            // The header, the stamp, count documents and the empty string after the last "\n".
            if (lines.Length != 3 + count || lines[lines.Length - 1].Length != 0)
            {
                return null;
            }

            List<HotReloadPdbDocument> documents = new List<HotReloadPdbDocument>(count);
            for (int index = 0; index < count; index++)
            {
                if (!TryParseDocumentLine(lines[2 + index], out HotReloadPdbDocument document))
                {
                    return null;
                }

                documents.Add(document);
            }

            return documents;
        }

        private static bool TryParseStampLine(string line, out FileStamp stamp, out int count)
        {
            stamp = default(FileStamp);
            count = 0;
            string[] fields = line.Split('\t');
            if (fields.Length != PersistedStampFieldCount
                || !TryParseLong(fields[0], out long dllLength)
                || !TryParseLong(fields[1], out long dllTicks)
                || fields[2].Length == 0
                || !TryParseLong(fields[3], out long pdbLength)
                || !TryParseLong(fields[4], out long pdbTicks)
                || !int.TryParse(fields[5], NumberStyles.None, CultureInfo.InvariantCulture, out count))
            {
                return false;
            }

            stamp = new FileStamp(dllLength, dllTicks, fields[2], pdbLength, pdbTicks);
            return true;
        }

        private static bool TryParseDocumentLine(string line, out HotReloadPdbDocument document)
        {
            document = default(HotReloadPdbDocument);
            string[] fields = line.Split(new[] { '\t' }, PersistedDocumentFieldCount);
            if (fields.Length != PersistedDocumentFieldCount || fields[2].Length == 0)
            {
                return false;
            }

            if (!int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out int algorithmValue)
                || !Enum.IsDefined(typeof(DocumentHashAlgorithm), algorithmValue))
            {
                return false;
            }

            if (!TryParseHex(fields[1], out byte[] hash))
            {
                return false;
            }

            document = new HotReloadPdbDocument(fields[2], (DocumentHashAlgorithm)algorithmValue, hash);
            return true;
        }

        private static bool TryParseLong(string text, out long value)
        {
            return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
        }

        // "" is the empty hash; an odd length or a character that is not a hex digit is malformed.
        private static bool TryParseHex(string text, out byte[] bytes)
        {
            bytes = null;
            if (text.Length % 2 != 0)
            {
                return false;
            }

            if (text.Length == 0)
            {
                bytes = Array.Empty<byte>();
                return true;
            }

            byte[] parsed = new byte[text.Length / 2];
            for (int index = 0; index < parsed.Length; index++)
            {
                if (!byte.TryParse(
                        text.Substring(index * 2, 2),
                        NumberStyles.AllowHexSpecifier,
                        CultureInfo.InvariantCulture,
                        out parsed[index]))
                {
                    return false;
                }
            }

            bytes = parsed;
            return true;
        }

        private void WritePersistedList(string path, FileStamp stamp, List<HotReloadPdbDocument> documents)
        {
            Directory.CreateDirectory(_persistenceDirectory);
            StringBuilder text = new StringBuilder();
            text.Append(PersistedFormatHeader).Append('\n');
            text.Append(stamp.DllLength.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .Append(stamp.DllLastWriteTimeUtcTicks.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .Append(stamp.ModuleVersionId).Append('\t')
                .Append(stamp.PdbLength.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .Append(stamp.PdbLastWriteTimeUtcTicks.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .Append(documents.Count.ToString(CultureInfo.InvariantCulture)).Append('\n');
            foreach (HotReloadPdbDocument document in documents)
            {
                text.Append(((int)document.HashAlgorithm).ToString(CultureInfo.InvariantCulture)).Append('\t')
                    .Append(document.Hash == null ? string.Empty : BitConverter.ToString(document.Hash).Replace("-", string.Empty))
                    .Append('\t')
                    .Append(document.Url).Append('\n');
            }

            // Why a temp file and a move: a reader in another domain never sees a half-written list.
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

        private static FileStamp ReadStamp(string fullDllPath, string pdbPath, string moduleVersionId)
        {
            FileInfo dll = new FileInfo(fullDllPath);
            FileInfo pdb = new FileInfo(pdbPath);
            return new FileStamp(
                dll.Length,
                dll.LastWriteTimeUtc.Ticks,
                moduleVersionId,
                pdb.Length,
                pdb.LastWriteTimeUtc.Ticks);
        }

        // The first document, in the order the walk met them, whose url names the path: the same
        // document a walk that stopped at the first matching sequence point returns.
        private static bool TryFind(
            List<HotReloadPdbDocument> documents,
            string projectRelativePath,
            out HotReloadPdbDocument document)
        {
            foreach (HotReloadPdbDocument candidate in documents)
            {
                if (HotReloadSourcePathNormalizer.PathsReferToSameFile(candidate.Url, projectRelativePath))
                {
                    document = candidate;
                    return true;
                }
            }

            document = default(HotReloadPdbDocument);
            return false;
        }

        // Every distinct document a sequence point refers to, in the order a walk over types,
        // methods and sequence points first meets them. Why not the PDB's document table: it
        // also lists files without a method body, which the walk this replaces never returned.
        internal static List<HotReloadPdbDocument> ReadDocuments(string dllPath, string pdbPath)
        {
            using FileStream dllStream = File.Open(dllPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using FileStream pdbStream = File.Open(pdbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            ReaderParameters readerParameters = new ReaderParameters
            {
                InMemory = true,
                ReadSymbols = true,
                SymbolReaderProvider = new PortablePdbReaderProvider(),
                SymbolStream = pdbStream
            };

            using AssemblyDefinition assemblyDefinition = AssemblyDefinition.ReadAssembly(dllStream, readerParameters);
            List<HotReloadPdbDocument> documents = new List<HotReloadPdbDocument>();
            // Why by reference: Cecil hands out one Document per row of the PDB's document table.
            HashSet<Document> seen = new HashSet<Document>();
            foreach (TypeDefinition type in assemblyDefinition.MainModule.GetTypes())
            {
                foreach (MethodDefinition method in type.Methods)
                {
                    if (!method.HasBody)
                    {
                        continue;
                    }

                    MethodDebugInformation debugInformation = method.DebugInformation;
                    if (debugInformation == null || !debugInformation.HasSequencePoints)
                    {
                        continue;
                    }

                    foreach (SequencePoint sequencePoint in debugInformation.SequencePoints)
                    {
                        if (sequencePoint.IsHidden || sequencePoint.Document == null)
                        {
                            continue;
                        }

                        if (seen.Add(sequencePoint.Document))
                        {
                            documents.Add(new HotReloadPdbDocument(
                                sequencePoint.Document.Url,
                                sequencePoint.Document.HashAlgorithm,
                                sequencePoint.Document.Hash));
                        }
                    }
                }
            }

            return documents;
        }
    }
}
