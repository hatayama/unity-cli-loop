using System.Collections.Generic;
using System.IO;

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
    /// point again on each hot reload run. The files only change on a compile.
    /// </summary>
    internal sealed class HotReloadPdbDocumentIndex
    {
        // Why a shared instance: the snapshot loader is static and has static callers in
        // several assemblies, like the compiled call-site cache this mirrors.
        public static HotReloadPdbDocumentIndex Shared { get; } = new HotReloadPdbDocumentIndex();

        private readonly object _gate = new object();
        private int _loadCount;

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

            lock (_gate)
            {
                List<HotReloadPdbDocument> documents = ReadDocuments(dllPath, pdbPath);
                _loadCount++;
                return TryFind(documents, projectRelativePath, out document);
            }
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
        private static List<HotReloadPdbDocument> ReadDocuments(string dllPath, string pdbPath)
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
