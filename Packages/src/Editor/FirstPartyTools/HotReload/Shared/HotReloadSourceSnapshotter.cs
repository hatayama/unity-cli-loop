using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

using Mono.Cecil;

using UnityEditor.PackageManager;

using io.github.hatayama.UnityCliLoop.ToolContracts;

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
        /// A copy of a source written since the compile started is checked against the PDB checksum
        /// when it is captured, and marked in the manifest when it does not match, so the default file
        /// selection, the sibling scan and the skip check treat that file as changed. The method-diff
        /// baseline checks every copy against the PDB when it uses one.
        /// Returns false when Unity listed no compilation assembly (as while it compiles).
        /// </summary>
        internal static bool CaptureAfterDomainReload()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            IReadOnlyList<UnityCompilationAssembly> assemblies = HotReloadCompilationAssemblies.Current();
            CaptureAssemblies(
                projectRoot,
                assemblies,
                HotReloadCompileStartRecord.Read(),
                HotReloadPdbDocumentIndex.Shared);
            return assemblies.Count > 0;
        }

        // Why separate from CaptureAfterDomainReload: the project root and the compilation assemblies come
        // from Unity, so taking them as arguments lets tests drive the per-assembly loop in a temporary root.
        internal static void CaptureAssemblies(
            string projectRoot,
            IEnumerable<UnityCompilationAssembly> assemblies,
            HotReloadCompileStart compileStart,
            HotReloadPdbDocumentIndex documentIndex)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty.");
            Debug.Assert(assemblies != null, "assemblies must not be null.");
            Debug.Assert(documentIndex != null, "documentIndex must not be null.");

            string snapshotRoot = Path.Combine(projectRoot, HotReloadConstants.SourceSnapshotRelativeDirectory);
            Directory.CreateDirectory(snapshotRoot);

            foreach (UnityCompilationAssembly assembly in assemblies)
            {
                try
                {
                    CaptureAssemblyIfNeeded(projectRoot, snapshotRoot, assembly, compileStart, documentIndex);
                }
                catch (Exception ex) when (IsSkippableAssemblyCaptureException(ex))
                {
                    // Approved deviation from the no-try-catch rule: one assembly's transient IO
                    // or Cecil read failure must not deprive every remaining assembly of a baseline.
                    // Continuing preserves the fail-closed invariant because the method-diff baseline
                    // rejects missing snapshots by PDB checksum rather than using them for an incorrect diff.
                    UnityEngine.Debug.LogWarning(
                        $"[UnityCliLoop] Snapshot capture failed for assembly {assembly.name}: {ex.Message}");
                }
            }
        }

        private static bool IsSkippableAssemblyCaptureException(Exception ex)
        {
            Debug.Assert(ex != null, "ex must not be null");

            return ex is IOException ||
                   ex is UnauthorizedAccessException ||
                   ex is BadImageFormatException;
        }

        internal static void CaptureAssemblyIfNeeded(
            string projectRoot,
            string snapshotRoot,
            UnityCompilationAssembly assembly,
            HotReloadCompileStart compileStart,
            HotReloadPdbDocumentIndex documentIndex)
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

            // Why projectRoot stays the snapshot owner: only the compiled assemblies move to the main
            // project for a Virtual Player; its snapshots stay under its own root.
            CompiledAssemblyLayout layout = CompiledAssemblyLayout.Resolve(projectRoot);
            string dllPath = layout.DllPath(assembly.name);
            string pdbPath = layout.PdbPath(assembly.name);
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
                HotReloadSnapshotSourceCheck check = new HotReloadSnapshotSourceCheck(
                    compileStart.SuspectWritesFrom(dllMtimeTicks),
                    dllPath,
                    pdbPath,
                    mvid,
                    documentIndex);
                CaptureAssemblySourcesAtomically(
                    projectRoot,
                    assemblySnapshotDirectory,
                    sourceFiles,
                    assembly.name,
                    check);
                DeleteStaleSnapshotDirectories(snapshotRoot, assembly.name, assemblySnapshotDirectory);
            }

            // Write the stamp even when individual sources were skipped: retrying every domain
            // reload would only repeat IO and warning spam. The partial snapshot remains fail-closed
            // because the method-diff baseline requires a PDB checksum match, and a changed DLL
            // invalidates this stamp through its mtime or length before the next capture.
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

        internal static void CaptureAssemblySourcesAtomically(
            string projectRoot,
            string assemblySnapshotDirectory,
            string[] sourceFiles,
            string assemblyName,
            HotReloadSnapshotSourceCheck check)
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
            int skippedSourceCount = 0;
            string firstSkippedSourcePath = null;
            int suspectSourceCount = 0;
            int editedAfterCompileCount = 0;
            long checkTicks = 0;
            List<string> manifestLines = new List<string>(sourceFiles.Length);
            foreach (string projectRelativeSourcePath in sourceFiles)
            {
                SourceCopyOutcome outcome = CopySourceFileByteExact(
                    projectRoot,
                    temporaryDirectory,
                    projectRelativeSourcePath,
                    check,
                    manifestLines);
                if (outcome.Kind == SourceCopyKind.Unreadable)
                {
                    skippedSourceCount++;
                    firstSkippedSourcePath ??= projectRelativeSourcePath;
                    continue;
                }

                if (outcome.Kind == SourceCopyKind.Checked || outcome.Kind == SourceCopyKind.CheckedEditedAfterCompile)
                {
                    suspectSourceCount++;
                    checkTicks += outcome.CheckTicks;
                }

                if (outcome.Kind == SourceCopyKind.CheckedEditedAfterCompile)
                {
                    editedAfterCompileCount++;
                }
            }

            HotReloadSourceStampManifest.Write(temporaryDirectory, manifestLines);
            Directory.Move(temporaryDirectory, assemblySnapshotDirectory);
            if (suspectSourceCount > 0)
            {
                VibeLogger.LogInfo(
                    HotReloadConstants.VibeLogSourceSnapshotChecked,
                    "Hot reload checked the sources written since the compile started against the PDB.",
                    new
                    {
                        assemblyName,
                        suspect = suspectSourceCount,
                        editedAfterCompile = editedAfterCompileCount,
                        checkMs = (long)TimeSpan.FromTicks(checkTicks).TotalMilliseconds
                    });
            }

            if (skippedSourceCount > 0)
            {
                UnityEngine.Debug.LogWarning(
                    $"[UnityCliLoop] Skipped {skippedSourceCount} unreadable source(s) while snapshotting " +
                    $"{assemblyName}: {firstSkippedSourcePath}");
            }
        }

        private static SourceCopyOutcome CopySourceFileByteExact(
            string projectRoot,
            string assemblySnapshotDirectory,
            string projectRelativeSourcePath,
            HotReloadSnapshotSourceCheck check,
            List<string> manifestLines)
        {
            string normalizedRelativePath = projectRelativeSourcePath.Replace('\\', '/');
            string absoluteSourcePath = Path.Combine(projectRoot, normalizedRelativePath.Replace('/', Path.DirectorySeparatorChar));
            string fileSystemSourcePath = HotReloadFileSystemPath.GetFileSystemPath(absoluteSourcePath);
            FileInfo sourceBefore = new FileInfo(fileSystemSourcePath);
            if (!sourceBefore.Exists)
            {
                return new SourceCopyOutcome(SourceCopyKind.NotFound, 0);
            }

            long length = sourceBefore.Length;
            long lastWriteTimeUtcTicks = sourceBefore.LastWriteTimeUtc.Ticks;
            string snapshotFileName = HashProjectRelativePath(normalizedRelativePath) + ".cs";
            if (!TryCopySource(
                    fileSystemSourcePath,
                    Path.Combine(assemblySnapshotDirectory, snapshotFileName),
                    length,
                    lastWriteTimeUtcTicks,
                    out byte[] bytes,
                    out bool stampHeldDuringRead))
            {
                return new SourceCopyOutcome(SourceCopyKind.Unreadable, 0);
            }

            // Why the PDB check stays outside TryCopySource's catch: that catch counts an IO failure as
            // an unreadable source, and a PDB read failure would make that warning name the wrong file.
            // The assembly-level catch in CaptureAssemblies handles it instead.
            System.Diagnostics.Stopwatch checkWatch = null;
            HotReloadSnapshotCopyVerdict verdict = JudgeCopy(
                stampHeldDuringRead,
                lastWriteTimeUtcTicks,
                check.SuspectWritesFromUtcTicks,
                () =>
                {
                    checkWatch = System.Diagnostics.Stopwatch.StartNew();
                    bool isCompiledSource = check.IsCompiledSource(projectRoot, normalizedRelativePath, bytes);
                    checkWatch.Stop();
                    return isCompiledSource;
                });
            if (verdict != HotReloadSnapshotCopyVerdict.NoStamp)
            {
                manifestLines.Add(HotReloadSourceStampManifest.FormatLine(
                    snapshotFileName,
                    length,
                    lastWriteTimeUtcTicks,
                    verdict == HotReloadSnapshotCopyVerdict.EditedAfterCompile));
            }

            if (checkWatch == null)
            {
                return new SourceCopyOutcome(SourceCopyKind.Trusted, 0);
            }

            SourceCopyKind checkedKind = verdict == HotReloadSnapshotCopyVerdict.EditedAfterCompile
                ? SourceCopyKind.CheckedEditedAfterCompile
                : SourceCopyKind.Checked;
            return new SourceCopyOutcome(checkedKind, checkWatch.Elapsed.Ticks);
        }

        // Copies the source and reports whether its stamp held while it was read. Returns false when the
        // source could not be read or the copy could not be written.
        private static bool TryCopySource(
            string fileSystemSourcePath,
            string destinationPath,
            long length,
            long lastWriteTimeUtcTicks,
            out byte[] bytes,
            out bool stampHeldDuringRead)
        {
            try
            {
                bytes = File.ReadAllBytes(fileSystemSourcePath);
                File.WriteAllBytes(destinationPath, bytes);
                // Why the stamp is taken before the read and checked again after it: a write during the
                // read moves the stamp on, and a stamp recorded then could vouch for bytes the copy does
                // not hold. Such a source is checked against the PDB: it gets no line when it matches,
                // so readers compare it by bytes until the next capture, and a marked line when it does not.
                FileInfo sourceAfter = new FileInfo(fileSystemSourcePath);
                stampHeldDuringRead = sourceAfter.Exists
                    && sourceAfter.Length == length
                    && sourceAfter.LastWriteTimeUtc.Ticks == lastWriteTimeUtcTicks;
                return true;
            }
            catch (Exception ex) when (IsSkippableSourceReadException(ex))
            {
                // Approved deviation from the no-try-catch rule: source IO can fail independently
                // while an assembly is being snapshotted. Skipping preserves the fail-closed
                // invariant because the method-diff baseline rejects any missing snapshot by PDB
                // checksum at use time.
                bytes = null;
                stampHeldDuringRead = false;
                return false;
            }
        }

        /// <summary>
        /// Decides what the manifest records for a copied source. Only a source that may have been
        /// saved after the compiler read it is checked against the PDB: one written at or after the
        /// start of the suspect window, or one that changed while it was read.
        /// </summary>
        internal static HotReloadSnapshotCopyVerdict JudgeCopy(
            bool stampHeldDuringRead,
            long lastWriteTimeUtcTicks,
            long suspectWritesFromUtcTicks,
            Func<bool> copyIsCompiledSource)
        {
            // Why a source that changed while read is checked whatever its write time: the stat before
            // the read can be old while the copy holds bytes written during the read.
            bool suspect = !stampHeldDuringRead || lastWriteTimeUtcTicks >= suspectWritesFromUtcTicks;
            if (!suspect)
            {
                // Written before the compile started and unchanged while read: what the compiler read.
                return HotReloadSnapshotCopyVerdict.StampTrusted;
            }

            if (copyIsCompiledSource())
            {
                // Why a matching source that changed while read still gets no line: its stamp does not
                // vouch for the copy's bytes, so readers compare by bytes as before.
                return stampHeldDuringRead
                    ? HotReloadSnapshotCopyVerdict.StampTrusted
                    : HotReloadSnapshotCopyVerdict.NoStamp;
            }

            return HotReloadSnapshotCopyVerdict.EditedAfterCompile;
        }

        private static bool IsSkippableSourceReadException(Exception ex)
        {
            Debug.Assert(ex != null, "ex must not be null");

            return ex is IOException ||
                   ex is UnauthorizedAccessException;
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
                // Only delete when the suffix after "<assemblyName>-" is exactly an Mvid in "N"
                // format, optionally followed by the capture-only .tmp suffix.
                string mvidCandidate = directoryName.Substring(prefix.Length);
                if (mvidCandidate.EndsWith(
                        IncompleteSnapshotDirectorySuffix,
                        StringComparison.Ordinal))
                {
                    mvidCandidate = mvidCandidate.Substring(
                        0,
                        mvidCandidate.Length - IncompleteSnapshotDirectorySuffix.Length);
                }

                if (!Guid.TryParseExact(mvidCandidate, "N", out Guid _))
                {
                    continue;
                }

                // Capture and cleanup run serially on the main thread, and cleanup starts only
                // after the current capture's Move succeeds, so no active .tmp can be removed here.
                Directory.Delete(candidateDirectory, recursive: true);
            }
        }

        private enum SourceCopyKind
        {
            NotFound,
            Unreadable,
            // Copied and recorded without a PDB check.
            Trusted,
            // Checked against the PDB and confirmed.
            Checked,
            // Checked against the PDB and marked as edited after the compile.
            CheckedEditedAfterCompile
        }

        private readonly struct SourceCopyOutcome
        {
            internal readonly SourceCopyKind Kind;
            internal readonly long CheckTicks;

            internal SourceCopyOutcome(SourceCopyKind kind, long checkTicks)
            {
                Kind = kind;
                CheckTicks = checkTicks;
            }
        }
    }
}
