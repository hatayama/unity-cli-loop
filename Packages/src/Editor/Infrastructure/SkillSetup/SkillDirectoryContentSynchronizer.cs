using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Debug = UnityEngine.Debug;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Synchronizes the files inside one installed skill directory.
    /// </summary>
    internal static class SkillDirectoryContentSynchronizer
    {
        private static readonly string[] ExcludedSkillFileNames =
        {
            ".meta",
            ".DS_Store",
            ".gitkeep"
        };

        internal static void SyncInstalledSkillDirectory(
            string skillDirectory,
            IReadOnlyDictionary<string, byte[]> skillFiles)
        {
            Debug.Assert(!string.IsNullOrEmpty(skillDirectory), "skillDirectory must not be null or empty");
            Debug.Assert(skillFiles != null, "skillFiles must not be null");
            Debug.Assert(skillFiles.ContainsKey(SkillInstallLayout.SkillFileName),
                "skillFiles must contain SKILL.md");

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
                WriteSkillFiles(skillDirectory, skillFiles);
                DeleteUnexpectedSkillFiles(skillDirectory, skillFiles.Keys);
                DeleteEmptyDirectories(skillDirectory);
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
                if (IsExcludedSkillFile(fileName))
                {
                    continue;
                }

                string relativePath = Path.GetRelativePath(skillDirectory, filePath);
                files[relativePath] = File.ReadAllBytes(filePath);
            }

            return files;
        }

        internal static void DeleteEmptyDirectories(string skillDirectory)
        {
            foreach (string directoryPath in Directory.EnumerateDirectories(skillDirectory, "*", SearchOption.AllDirectories)
                         .OrderByDescending(path => path.Length))
            {
                if (Directory.EnumerateFileSystemEntries(directoryPath).Any())
                {
                    continue;
                }

                Directory.Delete(directoryPath);
            }
        }

        internal static bool IsExcludedSkillFile(string fileName)
        {
            if (ExcludedSkillFileNames.Contains(fileName))
            {
                return true;
            }

            foreach (string excludedPattern in ExcludedSkillFileNames)
            {
                if (fileName.EndsWith(excludedPattern, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void WriteSkillFiles(
            string skillDirectory,
            IReadOnlyDictionary<string, byte[]> skillFiles)
        {
            foreach (KeyValuePair<string, byte[]> skillFile in skillFiles)
            {
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
            IEnumerable<string> expectedRelativePaths)
        {
            HashSet<string> expectedPaths = new(expectedRelativePaths, StringComparer.Ordinal);
            foreach (string filePath in Directory.EnumerateFiles(skillDirectory, "*", SearchOption.AllDirectories))
            {
                string fileName = Path.GetFileName(filePath);
                if (IsExcludedSkillFile(fileName))
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
            WriteSkillFiles(skillDirectory, backupFiles);
            DeleteUnexpectedSkillFiles(skillDirectory, backupFiles.Keys);
            DeleteEmptyDirectories(skillDirectory);
        }
    }
}
