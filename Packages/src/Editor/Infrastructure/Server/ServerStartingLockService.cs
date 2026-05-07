using System;
using System.IO;
using System.Threading;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Application service responsible for server starting lock file management.
    /// Single responsibility: Create/delete lock file during server startup for CLI detection.
    /// Related classes: CompilationLockService, DomainReloadDetectionService (similar patterns)
    /// Design reference: @Packages/docs/ARCHITECTURE_Unity.md - Application Service Layer (Single Function Implementation)
    /// </summary>
    public static class ServerStartingLockService
    {
        private const string LOCK_FILE_NAME = "serverstarting.lock";
        private const int FILE_OPERATION_RETRY_COUNT = 3;
        private const int FILE_OPERATION_RETRY_DELAY_MILLISECONDS = 50;

        private static string LockFilePath => Path.Combine(UnityEngine.Application.dataPath, "..", "Temp", LOCK_FILE_NAME);

        internal static Action<string> OnOwnedLockFileClaimedForDeletionForTests { get; set; }

        /// <summary>
        /// Create lock file to signal server is starting.
        /// </summary>
        public static string CreateLockFile()
        {
            string lockPath = LockFilePath;
            string tempDir = Path.GetDirectoryName(lockPath);

            if (!Directory.Exists(tempDir))
            {
                return null;
            }

            string ownershipToken = System.Guid.NewGuid().ToString("N");
            for (int attempt = 0; attempt < FILE_OPERATION_RETRY_COUNT; attempt++)
            {
                try
                {
                    File.WriteAllText(lockPath, ownershipToken);
                    return ownershipToken;
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }

                if (attempt < FILE_OPERATION_RETRY_COUNT - 1)
                {
                    Thread.Sleep(FILE_OPERATION_RETRY_DELAY_MILLISECONDS);
                }
            }

            VibeLogger.LogWarning("server_starting_lock_create_failed", $"Failed to create lock file: {lockPath}");
            return null;
        }

        /// <summary>
        /// Delete lock file. Called when server startup completes or on crash recovery.
        /// </summary>
        public static void DeleteLockFile(string ownershipToken = null)
        {
            string lockPath = LockFilePath;
            if (File.Exists(lockPath))
            {
                if (string.IsNullOrEmpty(ownershipToken))
                {
                    TryDeleteLockFile(lockPath);
                    return;
                }

                string claimedLockPath = TryClaimOwnedLockFileForDeletion(lockPath, ownershipToken);
                if (string.IsNullOrEmpty(claimedLockPath))
                {
                    return;
                }

                TryDeleteLockFile(claimedLockPath);
            }
        }

        public static void DeleteOwnedLockFile(string ownershipToken)
        {
            if (string.IsNullOrEmpty(ownershipToken))
            {
                return;
            }

            DeleteLockFile(ownershipToken);
        }

        private static string TryClaimOwnedLockFileForDeletion(string lockPath, string ownershipToken)
        {
            for (int attempt = 0; attempt < FILE_OPERATION_RETRY_COUNT; attempt++)
            {
                try
                {
                    using FileStream stream = new FileStream(lockPath, FileMode.Open, FileAccess.Read, FileShare.Delete);
                    using StreamReader reader = new StreamReader(stream);
                    string existingOwnershipToken = reader.ReadToEnd();
                    if (!string.Equals(existingOwnershipToken, ownershipToken, System.StringComparison.Ordinal))
                    {
                        return null;
                    }

                    string claimedLockPath = CreateClaimedLockFilePath(lockPath);
                    File.Move(lockPath, claimedLockPath);
                    // Why: tests need a deterministic point after the old generation has been
                    // detached from the canonical lock path but before we remove the claimed file.
                    // Why not observe this with FileSystemWatcher: the editor test runner missed the
                    // rename often enough to make the race coverage flaky on Windows.
                    OnOwnedLockFileClaimedForDeletionForTests?.Invoke(claimedLockPath);
                    return claimedLockPath;
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }

                if (attempt < FILE_OPERATION_RETRY_COUNT - 1)
                {
                    Thread.Sleep(FILE_OPERATION_RETRY_DELAY_MILLISECONDS);
                }
            }

            VibeLogger.LogWarning("server_starting_lock_claim_failed", $"Failed to claim owned lock file: {lockPath}");
            return null;
        }

        private static string CreateClaimedLockFilePath(string lockPath)
        {
            return $"{lockPath}.{Guid.NewGuid():N}.owneddelete";
        }

        private static void TryDeleteLockFile(string lockPath)
        {
            for (int attempt = 0; attempt < FILE_OPERATION_RETRY_COUNT; attempt++)
            {
                try
                {
                    File.Delete(lockPath);
                    if (!File.Exists(lockPath))
                    {
                        return;
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }

                if (attempt < FILE_OPERATION_RETRY_COUNT - 1)
                {
                    Thread.Sleep(FILE_OPERATION_RETRY_DELAY_MILLISECONDS);
                }
            }

            VibeLogger.LogWarning("server_starting_lock_delete_failed", $"Failed to delete lock file: {lockPath}");
        }
    }
}
