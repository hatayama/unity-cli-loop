using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

using Mono.Cecil;

using UnityEditor.Compilation;
using UnityEditor.PackageManager;

using UnityEngine;

using UnityCompilationAssembly = UnityEditor.Compilation.Assembly;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Captures byte-exact source snapshots after domain reload for edited-method detection.
    /// </summary>
    internal static class HotReloadSourceSnapshotter
    {
        private const string StampFileExtension = ".stamp";
        private const string IncompleteSnapshotDirectorySuffix = ".tmp";

        /// <summary>
        /// Captures snapshots for project assemblies that have adjacent portable PDBs.
        /// Why: adoption is decided at use time by PDB document checksum, so a racy capture
        /// (edit between compile and this call) can only fail closed into "no baseline" —
        /// never into a silently wrong method diff.
        /// </summary>
        internal static void CaptureAfterDomainReload()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string snapshotRoot = Path.Combine(projectRoot, HotReloadConstants.SourceSnapshotRelativeDirectory);
            Directory.CreateDirectory(snapshotRoot);

            foreach (UnityCompilationAssembly assembly in CompilationPipeline.GetAssemblies())
            {
                CaptureAssemblyIfNeeded(projectRoot, snapshotRoot, assembly);
            }
        }

        private static void CaptureAssemblyIfNeeded(
            string projectRoot,
            string snapshotRoot,
            UnityCompilationAssembly assembly)
        {
            string[] sourceFiles = assembly.sourceFiles;
            if (sourceFiles == null || sourceFiles.Length == 0)
            {
                return;
            }

            if (ShouldSkipImmutablePackageSources(sourceFiles))
            {
                return;
            }

            string dllPath = Path.Combine(
                projectRoot,
                HotReloadConstants.ScriptAssembliesRelativeDirectory,
                assembly.name + HotReloadConstants.CompiledAssemblyExtension);
            string pdbPath = Path.ChangeExtension(dllPath, ".pdb");
            if (!File.Exists(dllPath) || !File.Exists(pdbPath))
            {
                return;
            }

            FileInfo dllInfo = new FileInfo(dllPath);
            long dllMtimeTicks = dllInfo.LastWriteTimeUtc.Ticks;
            long dllByteLength = dllInfo.Length;
            string stampPath = Path.Combine(snapshotRoot, assembly.name + StampFileExtension);

            // Why stamp short-circuits Cecil: a false stamp (identical mtime+length with different
            // bytes) is vanishingly rare, and even then LoadVerifiedSnapshotSource rejects via PDB
            // checksum — so stamp lies degrade to fallback, never to a wrong method diff.
            if (HasMatchingStamp(stampPath, dllMtimeTicks, dllByteLength))
            {
                return;
            }

            string mvid = ReadAssemblyMvid(dllPath);
            string assemblySnapshotDirectory = Path.Combine(snapshotRoot, assembly.name + "-" + mvid);
            if (!Directory.Exists(assemblySnapshotDirectory))
            {
                CaptureAssemblySourcesAtomically(projectRoot, assemblySnapshotDirectory, sourceFiles);
                DeleteStaleSnapshotDirectories(snapshotRoot, assembly.name, assemblySnapshotDirectory);
            }

            WriteStamp(stampPath, mvid, dllMtimeTicks, dllByteLength);
        }

        /// <summary>
        /// Returns whether an assembly's sources belong to an immutable package and must not be
        /// snapshotted. Uses Package Manager metadata so Windows (where GetFullPath does not
        /// resolve package junctions) and macOS behave the same.
        /// </summary>
        internal static bool ShouldSkipImmutablePackageSources(string[] sourceFiles)
        {
            Debug.Assert(sourceFiles != null, "sourceFiles must not be null.");
            Debug.Assert(sourceFiles.Length > 0, "sourceFiles must not be empty.");

            // asmdef boundaries keep one assembly inside one package scope, so the first file
            // is enough to classify the whole assembly.
            PackageManagerPackageInfo packageInfo = PackageManagerPackageInfo.FindForAssetPath(sourceFiles[0]);
            if (packageInfo == null)
            {
                // Assets/ (and other non-package) scripts are editable — capture them.
                return false;
            }

            if (packageInfo.source == PackageSource.Embedded || packageInfo.source == PackageSource.Local)
            {
                // Project-owned packages remain editable via hot reload — capture them.
                return false;
            }

            // Registry / BuiltIn / Git / LocalTarball are immutable for hot-reload purposes.
            return true;
        }

        internal static bool HasMatchingStamp(string stampPath, long dllMtimeTicks, long dllByteLength)
        {
            if (!File.Exists(stampPath))
            {
                return false;
            }

            string stampText = File.ReadAllText(stampPath).Trim();
            string[] parts = stampText.Split(',');
            if (parts.Length != 3)
            {
                return false;
            }

            if (string.IsNullOrEmpty(parts[0]))
            {
                return false;
            }

            if (!long.TryParse(parts[1], out long stampedMtimeTicks)
                || !long.TryParse(parts[2], out long stampedByteLength))
            {
                return false;
            }

            return stampedMtimeTicks == dllMtimeTicks && stampedByteLength == dllByteLength;
        }

        private static void WriteStamp(string stampPath, string mvid, long dllMtimeTicks, long dllByteLength)
        {
            File.WriteAllText(stampPath, mvid + "," + dllMtimeTicks + "," + dllByteLength);
        }

        internal static string ReadAssemblyMvid(string dllPath)
        {
            ReaderParameters readerParameters = new ReaderParameters { InMemory = true };
            using AssemblyDefinition assemblyDefinition = AssemblyDefinition.ReadAssembly(dllPath, readerParameters);
            return assemblyDefinition.MainModule.Mvid.ToString("N");
        }

        private static void CaptureAssemblySourcesAtomically(
            string projectRoot,
            string assemblySnapshotDirectory,
            string[] sourceFiles)
        {
            // Why temp + Move: Directory.Exists is the "complete" signal. Copying into the final
            // directory first would leave a partial tree on interrupt that later reloads treat as
            // done and stamp-short-circuit forever. A sibling .tmp only becomes visible as complete
            // after Move succeeds.
            string temporaryDirectory = assemblySnapshotDirectory + IncompleteSnapshotDirectorySuffix;
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }

            Directory.CreateDirectory(temporaryDirectory);
            foreach (string projectRelativeSourcePath in sourceFiles)
            {
                CopySourceFileByteExact(projectRoot, temporaryDirectory, projectRelativeSourcePath);
            }

            Directory.Move(temporaryDirectory, assemblySnapshotDirectory);
        }

        private static void CopySourceFileByteExact(
            string projectRoot,
            string assemblySnapshotDirectory,
            string projectRelativeSourcePath)
        {
            string normalizedRelativePath = projectRelativeSourcePath.Replace('\\', '/');
            string absoluteSourcePath = Path.Combine(projectRoot, normalizedRelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(absoluteSourcePath))
            {
                return;
            }

            string snapshotFileName = HashProjectRelativePath(normalizedRelativePath) + ".cs";
            string destinationPath = Path.Combine(assemblySnapshotDirectory, snapshotFileName);
            byte[] bytes = File.ReadAllBytes(absoluteSourcePath);
            File.WriteAllBytes(destinationPath, bytes);
        }

        internal static string HashProjectRelativePath(string slashNormalizedProjectRelativePath)
        {
            Debug.Assert(
                slashNormalizedProjectRelativePath != null,
                "slashNormalizedProjectRelativePath must not be null.");

            string hashInput = slashNormalizedProjectRelativePath;
            // Why lowercase only on Windows: PDB document matching is OrdinalIgnoreCase there, so
            // a case-only path difference must hash to the same snapshot filename. Unix filesystems
            // can be case-sensitive, so leave the path bytes unchanged on those platforms.
            if (Path.DirectorySeparatorChar == '\\')
            {
                hashInput = hashInput.ToLowerInvariant();
            }

            byte[] utf8 = Encoding.UTF8.GetBytes(hashInput);
            using SHA256 sha256 = SHA256.Create();
            byte[] hash = sha256.ComputeHash(utf8);
            StringBuilder builder = new StringBuilder(hash.Length * 2);
            for (int index = 0; index < hash.Length; index++)
            {
                builder.Append(hash[index].ToString("x2"));
            }

            return builder.ToString();
        }

        internal static void DeleteStaleSnapshotDirectories(
            string snapshotRoot,
            string assemblyName,
            string currentSnapshotDirectory)
        {
            string currentFullPath = Path.GetFullPath(currentSnapshotDirectory);
            string prefix = assemblyName + "-";
            foreach (string candidateDirectory in Directory.GetDirectories(snapshotRoot, assemblyName + "-*"))
            {
                if (string.Equals(
                        Path.GetFullPath(candidateDirectory),
                        currentFullPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string directoryName = Path.GetFileName(candidateDirectory);
                if (directoryName.Length <= prefix.Length)
                {
                    continue;
                }

                // Why: the glob is a prefix match, so hyphenated sibling assembly names also match.
                // Only delete when the suffix after "<assemblyName>-" is exactly an Mvid in "N" format.
                string mvidCandidate = directoryName.Substring(prefix.Length);
                if (!Guid.TryParseExact(mvidCandidate, "N", out Guid _))
                {
                    continue;
                }

                Directory.Delete(candidateDirectory, recursive: true);
            }
        }
    }
}
