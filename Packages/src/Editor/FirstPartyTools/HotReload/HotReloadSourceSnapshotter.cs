using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

using Mono.Cecil;

using UnityEditor.Compilation;

using UnityEngine;

using UnityCompilationAssembly = UnityEditor.Compilation.Assembly;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Captures byte-exact source snapshots after domain reload for edited-method detection.
    /// </summary>
    internal static class HotReloadSourceSnapshotter
    {
        private const string PackageCacheRelativePrefix = "Library/PackageCache/";
        private const string StampFileExtension = ".stamp";

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

            if (AreAllSourcesUnderPackageCache(projectRoot, sourceFiles))
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
            if (TryReadMatchingStamp(stampPath, dllMtimeTicks, dllByteLength, out string stampedMvid))
            {
                return;
            }

            string mvid = ReadAssemblyMvid(dllPath);
            string assemblySnapshotDirectory = Path.Combine(snapshotRoot, assembly.name + "-" + mvid);
            if (!Directory.Exists(assemblySnapshotDirectory))
            {
                Directory.CreateDirectory(assemblySnapshotDirectory);
                foreach (string projectRelativeSourcePath in sourceFiles)
                {
                    CopySourceFileByteExact(projectRoot, assemblySnapshotDirectory, projectRelativeSourcePath);
                }

                DeleteStaleSnapshotDirectories(snapshotRoot, assembly.name, assemblySnapshotDirectory);
            }

            WriteStamp(stampPath, mvid, dllMtimeTicks, dllByteLength);
        }

        private static bool AreAllSourcesUnderPackageCache(string projectRoot, string[] sourceFiles)
        {
            // Why resolve to a real path: CompilationPipeline reports package scripts as
            // Packages/<id>/... virtual paths, which resolve under Library/PackageCache/ on disk.
            // Matching the virtual prefix alone never skips immutable package assemblies.
            string packageCacheRoot = Path.GetFullPath(Path.Combine(projectRoot, PackageCacheRelativePrefix.TrimEnd('/')))
                .Replace('\\', '/');
            string packageCachePrefix = packageCacheRoot + "/";

            foreach (string sourceFile in sourceFiles)
            {
                string absolutePath = Path.GetFullPath(
                        Path.Combine(projectRoot, sourceFile.Replace('/', Path.DirectorySeparatorChar)))
                    .Replace('\\', '/');
                if (!absolutePath.StartsWith(packageCachePrefix, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TryReadMatchingStamp(
            string stampPath,
            long dllMtimeTicks,
            long dllByteLength,
            out string stampedMvid)
        {
            stampedMvid = null;
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

            if (!long.TryParse(parts[1], out long stampedMtimeTicks)
                || !long.TryParse(parts[2], out long stampedByteLength))
            {
                return false;
            }

            if (stampedMtimeTicks != dllMtimeTicks || stampedByteLength != dllByteLength)
            {
                return false;
            }

            stampedMvid = parts[0];
            return !string.IsNullOrEmpty(stampedMvid);
        }

        private static void WriteStamp(string stampPath, string mvid, long dllMtimeTicks, long dllByteLength)
        {
            File.WriteAllText(stampPath, mvid + "," + dllMtimeTicks + "," + dllByteLength);
        }

        private static string ReadAssemblyMvid(string dllPath)
        {
            ReaderParameters readerParameters = new ReaderParameters { InMemory = true };
            using AssemblyDefinition assemblyDefinition = AssemblyDefinition.ReadAssembly(dllPath, readerParameters);
            return assemblyDefinition.MainModule.Mvid.ToString("N");
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
            byte[] utf8 = Encoding.UTF8.GetBytes(slashNormalizedProjectRelativePath);
            using SHA256 sha256 = SHA256.Create();
            byte[] hash = sha256.ComputeHash(utf8);
            StringBuilder builder = new StringBuilder(hash.Length * 2);
            for (int index = 0; index < hash.Length; index++)
            {
                builder.Append(hash[index].ToString("x2"));
            }

            return builder.ToString();
        }

        private static void DeleteStaleSnapshotDirectories(
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
