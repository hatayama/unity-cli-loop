using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Writes a migration plan as one batch transaction: every temp sidecar is written first,
    /// then all targets are committed via rename/replace while their backups are kept, and a
    /// mid-batch commit failure rolls the already committed files back from those backups.
    /// </summary>
    internal static class ThirdPartyToolMigrationFileWriter
    {
        private const string TempSidecarExtension = ".tmp";
        private const string BackupSidecarExtension = ".bak";

        internal static void WriteBatch(IReadOnlyList<MigrationFileChange> changes)
        {
            Debug.Assert(changes != null, "changes must not be null");

            List<PreparedWrite> preparedWrites = PrepareAll(changes);
            CommitAll(preparedWrites);
        }

        internal static async Task WriteBatchAsync(IReadOnlyList<MigrationFileChange> changes)
        {
            Debug.Assert(changes != null, "changes must not be null");

            List<PreparedWrite> preparedWrites = await PrepareAllAsync(changes);
            // Commit is rename-only and fast; it must run without yields so no editor callback
            // (asset refresh, domain reload) can observe a half-committed batch.
            CommitAll(preparedWrites);
        }

        private static List<PreparedWrite> PrepareAll(IReadOnlyList<MigrationFileChange> changes)
        {
            List<PreparedWrite> preparedWrites = new(changes.Count);
            bool prepared = false;
            try
            {
                foreach (MigrationFileChange change in changes)
                {
                    Prepare(preparedWrites, change);
                }

                prepared = true;
                return preparedWrites;
            }
            finally
            {
                if (!prepared)
                {
                    // A prepare failure has not touched any target file yet; deleting the temp
                    // sidecars returns the project to its exact pre-apply state.
                    DeleteTempSidecars(preparedWrites, 0);
                }
            }
        }

        private static async Task<List<PreparedWrite>> PrepareAllAsync(
            IReadOnlyList<MigrationFileChange> changes)
        {
            List<PreparedWrite> preparedWrites = new(changes.Count);
            bool prepared = false;
            try
            {
                for (int index = 0; index < changes.Count; index++)
                {
                    Prepare(preparedWrites, changes[index]);
                    if ((index + 1) % ThirdPartyToolMigrationFileServiceConstants.PreviewYieldBatchSize == 0)
                    {
                        await Task.Yield();
                    }
                }

                prepared = true;
                return preparedWrites;
            }
            finally
            {
                if (!prepared)
                {
                    DeleteTempSidecars(preparedWrites, 0);
                }
            }
        }

        private static void Prepare(List<PreparedWrite> preparedWrites, MigrationFileChange change)
        {
            string tempFilePath = CreateUniqueSidecarPath(change.FilePath, TempSidecarExtension);
            // Register before WriteAllText so a mid-write IOException can still clean up a partial .tmp.
            preparedWrites.Add(new PreparedWrite(change.FilePath, tempFilePath));
            ThirdPartyToolMigrationFileAccess.WriteAllText(tempFilePath, change.Content);
        }

        private static void CommitAll(List<PreparedWrite> preparedWrites)
        {
            List<CommittedWrite> committedWrites = new(preparedWrites.Count);
            bool committed = false;
            try
            {
                foreach (PreparedWrite preparedWrite in preparedWrites)
                {
                    committedWrites.Add(Commit(preparedWrite));
                }

                committed = true;
            }
            finally
            {
                if (committed)
                {
                    DeleteBackupSidecars(committedWrites);
                }
                else
                {
                    // The commit exception is propagating right now; rollback and cleanup must be
                    // best-effort so they never replace that original exception.
                    RollbackCommitted(committedWrites);
                    DeleteTempSidecars(preparedWrites, committedWrites.Count);
                }
            }
        }

        private static CommittedWrite Commit(PreparedWrite preparedWrite)
        {
            if (!ThirdPartyToolMigrationFileAccess.Exists(preparedWrite.TargetFilePath))
            {
                ThirdPartyToolMigrationFileAccess.Move(
                    preparedWrite.TempFilePath,
                    preparedWrite.TargetFilePath);
                return new CommittedWrite(preparedWrite.TargetFilePath, null);
            }

            string backupFilePath = CreateUniqueSidecarPath(
                preparedWrite.TargetFilePath,
                BackupSidecarExtension);
            ThirdPartyToolMigrationFileAccess.Replace(
                preparedWrite.TempFilePath,
                preparedWrite.TargetFilePath,
                backupFilePath);
            return new CommittedWrite(preparedWrite.TargetFilePath, backupFilePath);
        }

        private static void RollbackCommitted(List<CommittedWrite> committedWrites)
        {
            for (int index = committedWrites.Count - 1; index >= 0; index--)
            {
                CommittedWrite committedWrite = committedWrites[index];
                try
                {
                    ThirdPartyToolMigrationFileAccess.Delete(committedWrite.TargetFilePath);
                    if (committedWrite.BackupFilePath != null)
                    {
                        ThirdPartyToolMigrationFileAccess.Move(
                            committedWrite.BackupFilePath,
                            committedWrite.TargetFilePath);
                    }
                }
                catch (Exception restoreException)
                {
                    // Keep restoring the remaining files; a file that cannot be restored keeps
                    // its .bak on disk so the user can recover it manually.
                    UnityEngine.Debug.LogError(
                        $"[uloop] Migration rollback failed for '{committedWrite.TargetFilePath}': " +
                        $"{restoreException.Message}" +
                        (committedWrite.BackupFilePath == null
                            ? string.Empty
                            : $" Backup kept at '{committedWrite.BackupFilePath}'."));
                }
            }
        }

        private static void DeleteTempSidecars(List<PreparedWrite> preparedWrites, int firstIndex)
        {
            for (int index = firstIndex; index < preparedWrites.Count; index++)
            {
                TryDeleteSidecar(preparedWrites[index].TempFilePath);
            }
        }

        private static void DeleteBackupSidecars(List<CommittedWrite> committedWrites)
        {
            foreach (CommittedWrite committedWrite in committedWrites)
            {
                if (committedWrite.BackupFilePath != null)
                {
                    TryDeleteSidecar(committedWrite.BackupFilePath);
                }
            }
        }

        private static void TryDeleteSidecar(string sidecarFilePath)
        {
            try
            {
                if (ThirdPartyToolMigrationFileAccess.Exists(sidecarFilePath))
                {
                    ThirdPartyToolMigrationFileAccess.Delete(sidecarFilePath);
                }
            }
            catch (Exception deleteException)
            {
                // Sidecar cleanup must never fail the migration itself; a stray sidecar is
                // visible in the project window and harmless to compilation.
                UnityEngine.Debug.LogError(
                    $"[uloop] Failed to delete migration sidecar '{sidecarFilePath}': " +
                    $"{deleteException.Message}");
            }
        }

        internal static string CreateUniqueSidecarPath(string filePath, string extension)
        {
            Debug.Assert(!string.IsNullOrEmpty(filePath), "filePath must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(extension), "extension must not be null or empty");

            string directory = Path.GetDirectoryName(filePath) ?? string.Empty;
            string fileName = Path.GetFileName(filePath);
            string sidecarPath = Path.Combine(directory, $"{fileName}.{Guid.NewGuid():N}{extension}");
            while (ThirdPartyToolMigrationFileAccess.Exists(sidecarPath))
            {
                sidecarPath = Path.Combine(directory, $"{fileName}.{Guid.NewGuid():N}{extension}");
            }

            return sidecarPath;
        }

        private readonly struct PreparedWrite
        {
            public PreparedWrite(string targetFilePath, string tempFilePath)
            {
                TargetFilePath = targetFilePath;
                TempFilePath = tempFilePath;
            }

            public string TargetFilePath { get; }
            public string TempFilePath { get; }
        }

        private readonly struct CommittedWrite
        {
            public CommittedWrite(string targetFilePath, string backupFilePath)
            {
                TargetFilePath = targetFilePath;
                BackupFilePath = backupFilePath;
            }

            public string TargetFilePath { get; }
            // Null when the target file did not exist before the commit (plain move, no backup).
            public string BackupFilePath { get; }
        }
    }

    /// <summary>
    /// Routes migration file IO through Windows extended-length paths when needed.
    /// </summary>
    internal static class ThirdPartyToolMigrationFileAccess
    {
        internal static string ReadAllText(string filePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(filePath), "filePath must not be null or empty");

            return File.ReadAllText(GetFileSystemPath(filePath));
        }

        internal static void WriteAllText(string filePath, string content)
        {
            Debug.Assert(!string.IsNullOrEmpty(filePath), "filePath must not be null or empty");
            Debug.Assert(content != null, "content must not be null");

            File.WriteAllText(GetFileSystemPath(filePath), content);
        }

        internal static IEnumerable<string> ReadLines(string filePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(filePath), "filePath must not be null or empty");

            return File.ReadLines(GetFileSystemPath(filePath));
        }

        internal static bool Exists(string filePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(filePath), "filePath must not be null or empty");

            return File.Exists(GetFileSystemPath(filePath));
        }

        internal static long GetLength(string filePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(filePath), "filePath must not be null or empty");

            FileInfo fileInfo = new(GetFileSystemPath(filePath));
            return fileInfo.Length;
        }

        internal static DateTime GetLastWriteTimeUtc(string filePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(filePath), "filePath must not be null or empty");

            return File.GetLastWriteTimeUtc(GetFileSystemPath(filePath));
        }

        internal static FileStream OpenRead(string filePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(filePath), "filePath must not be null or empty");

            return File.OpenRead(GetFileSystemPath(filePath));
        }

        internal static void Move(string sourceFilePath, string destinationFilePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(sourceFilePath), "sourceFilePath must not be null or empty");
            Debug.Assert(
                !string.IsNullOrEmpty(destinationFilePath),
                "destinationFilePath must not be null or empty");

            File.Move(GetFileSystemPath(sourceFilePath), GetFileSystemPath(destinationFilePath));
        }

        internal static void Replace(
            string sourceFilePath,
            string destinationFilePath,
            string destinationBackupFilePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(sourceFilePath), "sourceFilePath must not be null or empty");
            Debug.Assert(
                !string.IsNullOrEmpty(destinationFilePath),
                "destinationFilePath must not be null or empty");
            Debug.Assert(
                !string.IsNullOrEmpty(destinationBackupFilePath),
                "destinationBackupFilePath must not be null or empty");

            File.Replace(
                GetFileSystemPath(sourceFilePath),
                GetFileSystemPath(destinationFilePath),
                GetFileSystemPath(destinationBackupFilePath));
        }

        internal static void Delete(string filePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(filePath), "filePath must not be null or empty");

            File.Delete(GetFileSystemPath(filePath));
        }

        private static string GetFileSystemPath(string path)
        {
            Debug.Assert(!string.IsNullOrEmpty(path), "path must not be null or empty");

            string fullPath = Path.GetFullPath(path);
            if (Path.DirectorySeparatorChar != '\\' ||
                fullPath.Length < ThirdPartyToolMigrationFileServiceConstants.WindowsLegacyMaxPathLength ||
                fullPath.StartsWith(
                    ThirdPartyToolMigrationFileServiceConstants.WindowsExtendedLengthPathPrefix,
                    StringComparison.Ordinal))
            {
                return fullPath;
            }

            // Windows Mono still needs the extended-length prefix for file IO beyond legacy MAX_PATH.
            if (fullPath.StartsWith(
                    ThirdPartyToolMigrationFileServiceConstants.WindowsUncPathPrefix,
                    StringComparison.Ordinal))
            {
                return ThirdPartyToolMigrationFileServiceConstants.WindowsExtendedLengthUncPathPrefix +
                    fullPath.Substring(
                        ThirdPartyToolMigrationFileServiceConstants.WindowsUncPathPrefix.Length);
            }

            return ThirdPartyToolMigrationFileServiceConstants.WindowsExtendedLengthPathPrefix + fullPath;
        }
    }
}
