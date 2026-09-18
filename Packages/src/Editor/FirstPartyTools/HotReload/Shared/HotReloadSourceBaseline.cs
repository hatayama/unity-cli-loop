using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Pdb;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Loads a PDB-checksum-verified source snapshot for edited-method detection.
    /// </summary>
    internal static class HotReloadSourceBaseline
    {
        /// <summary>
        /// Returns the verified snapshot text for <paramref name="projectRelativeSourcePath"/>,
        /// or null when no snapshot passes the portable-PDB document checksum check.
        /// </summary>
        public static string LoadVerifiedSnapshotSource(string projectRelativeSourcePath, string targetDllPath)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRelativeSourcePath), "projectRelativeSourcePath must not be null or empty.");
            Debug.Assert(!string.IsNullOrEmpty(targetDllPath), "targetDllPath must not be null or empty.");

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return LoadVerifiedSnapshotSourceAt(projectRoot, projectRelativeSourcePath, targetDllPath);
        }

        // projectRoot is injectable so EditMode tests can point at a tampered snapshot tree
        // without expanding the public API surface.
        internal static string LoadVerifiedSnapshotSourceAt(
            string projectRoot,
            string projectRelativeSourcePath,
            string targetDllPath)
        {
            TryLoadVerifiedSnapshotSource(
                projectRoot,
                projectRelativeSourcePath,
                targetDllPath,
                out string source);
            return source;
        }

        /// <summary>
        /// Says why <see cref="LoadVerifiedSnapshotSource"/> returned null for the same arguments.
        /// </summary>
        /// <remarks>
        /// Why a second lookup rather than a richer load result: the loader has several callers
        /// that only need the text, and only the missing-baseline warning needs the reason, so the
        /// cost of reading the PDB again is paid on that rare path alone.
        /// </remarks>
        internal static HotReloadSnapshotMissReason DescribeSnapshotMiss(
            string projectRelativeSourcePath,
            string targetDllPath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return DescribeSnapshotMissAt(projectRoot, projectRelativeSourcePath, targetDllPath);
        }

        internal static HotReloadSnapshotMissReason DescribeSnapshotMissAt(
            string projectRoot,
            string projectRelativeSourcePath,
            string targetDllPath)
        {
            return TryLoadVerifiedSnapshotSource(
                projectRoot,
                projectRelativeSourcePath,
                targetDllPath,
                out string _);
        }

        private static HotReloadSnapshotMissReason TryLoadVerifiedSnapshotSource(
            string projectRoot,
            string projectRelativeSourcePath,
            string targetDllPath,
            out string source)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty.");
            Debug.Assert(!string.IsNullOrEmpty(projectRelativeSourcePath), "projectRelativeSourcePath must not be null or empty.");
            Debug.Assert(!string.IsNullOrEmpty(targetDllPath), "targetDllPath must not be null or empty.");

            source = null;
            string pdbPath = Path.ChangeExtension(targetDllPath, ".pdb");
            if (!File.Exists(targetDllPath) || !File.Exists(pdbPath))
            {
                return HotReloadSnapshotMissReason.NoCompiledAssembly;
            }

            string mvid = HotReloadSourceSnapshotter.ReadAssemblyMvid(targetDllPath);
            string assemblyName = Path.GetFileNameWithoutExtension(targetDllPath);
            string slashNormalizedRelativePath = projectRelativeSourcePath.Replace('\\', '/');
            string snapshotFileName = HotReloadSourceSnapshotter.HashProjectRelativePath(slashNormalizedRelativePath) + ".cs";
            string snapshotPath = Path.Combine(
                projectRoot,
                HotReloadConstants.SourceSnapshotRelativeDirectory,
                assemblyName + "-" + mvid,
                snapshotFileName);
            if (!File.Exists(snapshotPath))
            {
                return HotReloadSnapshotMissReason.NoSnapshotFile;
            }

            // Why read once: the verified bytes must be the exact payload decoded for the worker —
            // a second read could race with another writer and diverge from the checksummed content.
            byte[] snapshotBytes = File.ReadAllBytes(snapshotPath);
            Document document = FindDocumentForProjectRelativePath(targetDllPath, pdbPath, slashNormalizedRelativePath);
            if (document == null)
            {
                return HotReloadSnapshotMissReason.NoDocumentInPdb;
            }

            // A document without a checksum cannot verify the snapshot, which is the same outcome
            // for the caller as a checksum that disagrees.
            if (document.Hash == null || document.Hash.Length == 0)
            {
                return HotReloadSnapshotMissReason.HashMismatch;
            }

            byte[] actualHash = ComputeDocumentHash(document.HashAlgorithm, snapshotBytes);
            if (actualHash == null || !actualHash.SequenceEqual(document.Hash))
            {
                return HotReloadSnapshotMissReason.HashMismatch;
            }

            using MemoryStream memoryStream = new MemoryStream(snapshotBytes, writable: false);
            using StreamReader reader = new StreamReader(memoryStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            source = reader.ReadToEnd();
            return HotReloadSnapshotMissReason.None;
        }

        private static Document FindDocumentForProjectRelativePath(
            string dllPath,
            string pdbPath,
            string projectRelativePath)
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

                        if (HotReloadSourcePathNormalizer.PathsReferToSameFile(
                                sequencePoint.Document.Url,
                                projectRelativePath))
                        {
                            return sequencePoint.Document;
                        }
                    }
                }
            }

            return null;
        }

        private static byte[] ComputeDocumentHash(DocumentHashAlgorithm algorithm, byte[] sourceBytes)
        {
            switch (algorithm)
            {
                case DocumentHashAlgorithm.SHA1:
                    using (SHA1 sha1 = SHA1.Create())
                    {
                        return sha1.ComputeHash(sourceBytes);
                    }
                case DocumentHashAlgorithm.SHA256:
                    using (SHA256 sha256 = SHA256.Create())
                    {
                        return sha256.ComputeHash(sourceBytes);
                    }
                default:
                    return null;
            }
        }
    }
}
