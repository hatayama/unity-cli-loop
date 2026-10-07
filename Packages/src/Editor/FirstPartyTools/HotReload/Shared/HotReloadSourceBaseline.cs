using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

using Mono.Cecil.Cil;

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
            return LoadVerifiedSnapshotSourceAt(
                projectRoot,
                projectRelativeSourcePath,
                targetDllPath,
                HotReloadPdbDocumentIndex.Shared);
        }

        // projectRoot is injectable so EditMode tests can point at a tampered snapshot tree
        // without expanding the public API surface, and documentIndex so they can count how
        // often the PDB is read.
        internal static string LoadVerifiedSnapshotSourceAt(
            string projectRoot,
            string projectRelativeSourcePath,
            string targetDllPath,
            HotReloadPdbDocumentIndex documentIndex)
        {
            TryLoadVerifiedSnapshotSource(
                projectRoot,
                projectRelativeSourcePath,
                targetDllPath,
                documentIndex,
                out string source);
            return source;
        }

        /// <summary>
        /// Says why <see cref="LoadVerifiedSnapshotSource"/> returned null for the same arguments.
        /// </summary>
        /// <remarks>
        /// Why a second lookup rather than a richer load result: the loader has several callers
        /// that only need the text, and only the missing-baseline warning needs the reason. The
        /// second lookup reuses the document list the first one kept, so it does not read the PDB
        /// again.
        /// </remarks>
        internal static HotReloadSnapshotMissReason DescribeSnapshotMiss(
            string projectRelativeSourcePath,
            string targetDllPath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return DescribeSnapshotMissAt(
                projectRoot,
                projectRelativeSourcePath,
                targetDllPath,
                HotReloadPdbDocumentIndex.Shared);
        }

        internal static HotReloadSnapshotMissReason DescribeSnapshotMissAt(
            string projectRoot,
            string projectRelativeSourcePath,
            string targetDllPath,
            HotReloadPdbDocumentIndex documentIndex)
        {
            return TryLoadVerifiedSnapshotSource(
                projectRoot,
                projectRelativeSourcePath,
                targetDllPath,
                documentIndex,
                out string _);
        }

        private static HotReloadSnapshotMissReason TryLoadVerifiedSnapshotSource(
            string projectRoot,
            string projectRelativeSourcePath,
            string targetDllPath,
            HotReloadPdbDocumentIndex documentIndex,
            out string source)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty.");
            Debug.Assert(!string.IsNullOrEmpty(projectRelativeSourcePath), "projectRelativeSourcePath must not be null or empty.");
            Debug.Assert(!string.IsNullOrEmpty(targetDllPath), "targetDllPath must not be null or empty.");
            Debug.Assert(documentIndex != null, "documentIndex must not be null.");

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
            // Why the physical path here and the asset path for the snapshot file: the snapshot is keyed
            // by the asset path Unity reports for the file, but the PDB records the path the compiler was
            // given, which for an embedded or local package is the folder behind the virtual
            // Packages/<name> path. Why the Package Manager is asked rather than the package roots a
            // run captures: those live in the main hot-reload assembly, which this one cannot see, and
            // every caller of the loader already runs on the Unity main thread the Package Manager
            // requires: a run's group step, and the pause-point port, which the pause-point tools and a
            // run's patch step call.
            string pdbLookupPath = ScriptPackageRoots.ToPhysicalPath(projectRoot, slashNormalizedRelativePath);
            if (!documentIndex.TryFindDocument(
                    targetDllPath,
                    pdbPath,
                    mvid,
                    pdbLookupPath,
                    out HotReloadPdbDocument document))
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
