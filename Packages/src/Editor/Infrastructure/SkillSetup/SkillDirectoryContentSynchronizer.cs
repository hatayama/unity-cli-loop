using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

using Debug = UnityEngine.Debug;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Synchronizes the files inside one installed skill directory.
    /// </summary>
    internal static class SkillDirectoryContentSynchronizer
    {
        internal static void SyncInstalledSkillDirectory(
            string skillDirectory,
            IReadOnlyDictionary<string, byte[]> skillFiles,
            CancellationToken ct)
        {
            Debug.Assert(!string.IsNullOrEmpty(skillDirectory), "skillDirectory must not be null or empty");
            Debug.Assert(skillFiles != null, "skillFiles must not be null");
            Debug.Assert(skillFiles.ContainsKey(SkillInstallLayout.SkillFileName),
                "skillFiles must contain SKILL.md");
            ct.ThrowIfCancellationRequested();

            string parentDirectory = Path.GetDirectoryName(skillDirectory);
            Debug.Assert(!string.IsNullOrEmpty(parentDirectory), "parentDirectory must not be null or empty");
            Directory.CreateDirectory(parentDirectory);
            bool skillDirectoryExisted = Directory.Exists(skillDirectory);
            Dictionary<string, byte[]> backupFiles = skillDirectoryExisted
                ? ReadSkillFilesForRollback(skillDirectory)
                : new Dictionary<string, byte[]>(StringComparer.Ordinal);
            Directory.CreateDirectory(skillDirectory);

            bool syncCompleted = false;
            try
            {
                WriteSkillFiles(skillDirectory, skillFiles, ct);
                DeleteUnexpectedSkillFiles(skillDirectory, skillFiles.Keys, ct);
                DeleteEmptyDirectories(skillDirectory, ct);
                syncCompleted = true;
            }
            finally
            {
                if (!syncCompleted)
                {
                    RollbackSkillDirectory(skillDirectory, backupFiles, skillDirectoryExisted);
                }
            }
        }

        internal static Dictionary<string, byte[]> ReadSkillFilesForRollback(string skillDirectory)
        {
            Dictionary<string, byte[]> files = new(StringComparer.Ordinal);
            foreach (string filePath in Directory.EnumerateFiles(skillDirectory, "*", SearchOption.AllDirectories))
            {
                string fileName = Path.GetFileName(filePath);
                if (SkillSetupFileExclusion.IsExcludedSkillFile(fileName))
                {
                    continue;
                }

                string relativePath = Path.GetRelativePath(skillDirectory, filePath);
                files[relativePath] = File.ReadAllBytes(filePath);
            }

            return files;
        }

        internal static void DeleteEmptyDirectories(string skillDirectory, CancellationToken ct)
        {
            foreach (string directoryPath in Directory.EnumerateDirectories(skillDirectory, "*", SearchOption.AllDirectories)
                         .OrderByDescending(path => path.Length))
            {
                ct.ThrowIfCancellationRequested();
                if (Directory.EnumerateFileSystemEntries(directoryPath).Any())
                {
                    continue;
                }

                Directory.Delete(directoryPath);
            }
        }

        private static void WriteSkillFiles(
            string skillDirectory,
            IReadOnlyDictionary<string, byte[]> skillFiles,
            CancellationToken ct)
        {
            foreach (KeyValuePair<string, byte[]> skillFile in skillFiles)
            {
                ct.ThrowIfCancellationRequested();
                string fullPath = Path.Combine(skillDirectory, skillFile.Key);
                string fileDirectory = Path.GetDirectoryName(fullPath);
                Debug.Assert(!string.IsNullOrEmpty(fileDirectory), "fileDirectory must not be null or empty");
                Directory.CreateDirectory(fileDirectory);
                if (File.Exists(fullPath) && File.ReadAllBytes(fullPath).SequenceEqual(skillFile.Value))
                {
                    continue;
                }

                WriteFileAtomically(fullPath, skillFile.Value);
            }
        }

        private static void WriteFileAtomically(string fullPath, byte[] content)
        {
            string fileDirectory = Path.GetDirectoryName(fullPath);
            Debug.Assert(!string.IsNullOrEmpty(fileDirectory), "fileDirectory must not be null or empty");

            string tempPath = Path.Combine(
                fileDirectory,
                $"{Path.GetFileName(fullPath)}.tmp-{Guid.NewGuid():N}");
            File.WriteAllBytes(tempPath, content);

            try
            {
                if (File.Exists(fullPath))
                {
                    File.Replace(tempPath, fullPath, null, true);
                    return;
                }

                File.Move(tempPath, fullPath);
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        private static void DeleteUnexpectedSkillFiles(
            string skillDirectory,
            IEnumerable<string> expectedRelativePaths,
            CancellationToken ct)
        {
            HashSet<string> expectedPaths = new(expectedRelativePaths, StringComparer.Ordinal);
            foreach (string filePath in Directory.EnumerateFiles(skillDirectory, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                string fileName = Path.GetFileName(filePath);
                if (SkillSetupFileExclusion.IsExcludedSkillFile(fileName))
                {
                    continue;
                }

                string relativePath = Path.GetRelativePath(skillDirectory, filePath);
                if (expectedPaths.Contains(relativePath))
                {
                    continue;
                }

                File.Delete(filePath);
            }
        }

        private static void RollbackSkillDirectory(
            string skillDirectory,
            IReadOnlyDictionary<string, byte[]> backupFiles,
            bool skillDirectoryExisted)
        {
            if (!skillDirectoryExisted)
            {
                if (Directory.Exists(skillDirectory))
                {
                    Directory.Delete(skillDirectory, true);
                }

                return;
            }

            Directory.CreateDirectory(skillDirectory);
            WriteSkillFiles(skillDirectory, backupFiles, CancellationToken.None);
            DeleteUnexpectedSkillFiles(skillDirectory, backupFiles.Keys, CancellationToken.None);
            DeleteEmptyDirectories(skillDirectory, CancellationToken.None);
        }
    }
}
