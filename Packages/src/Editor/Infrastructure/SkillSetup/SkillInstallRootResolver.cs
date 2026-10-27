using System;
using System.IO;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Reads and saves the skill install root in .uloop/settings.skills.json and resolves it to a directory.
    /// The file is shared with the uloop CLI, which installs into the same root when no --root is given.
    /// </summary>
    internal sealed class SkillInstallRootResolver
    {
        internal const string SettingsFileName = "settings.skills.json";

        // These spellings are the file contract with the CLI (cli/dispatcher skills_install_root.go).
        private const string UnityProjectRootValue = "unity";
        private const string GitRepositoryRootValue = "git";
        private const string GitMarkerName = ".git";

        [Serializable]
        private sealed class SkillSettingsJsonData
        {
            public string installRoot = string.Empty;
        }

        internal SkillInstallRootSelection GetSelection(string projectRoot)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            string fullProjectRoot = Path.GetFullPath(projectRoot);
            return new SkillInstallRootSelection(
                LoadSavedRoot(fullProjectRoot),
                fullProjectRoot,
                FindGitRepositoryRoot(fullProjectRoot));
        }

        internal void SaveInstallRoot(string projectRoot, SkillInstallRoot root)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            string settingsPath = GetSettingsPath(projectRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
            SkillSettingsJsonData data = new() { installRoot = ToSettingsValue(root) };
            AtomicFileWriter.Write(settingsPath, JsonUtility.ToJson(data, true));
            AtomicFileWriter.CleanupBackup(settingsPath + AtomicFileWriter.BackupFileSuffix);
        }

        /// <summary>
        /// Returns the nearest directory at or above the Unity project root that holds .git, or null.
        /// A .git file counts too, because worktree and submodule checkouts mark their root with one.
        /// </summary>
        internal static string FindGitRepositoryRoot(string projectRoot)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty");

            DirectoryInfo current = new(Path.GetFullPath(projectRoot));
            while (current != null)
            {
                string marker = Path.Combine(current.FullName, GitMarkerName);
                if (Directory.Exists(marker) || File.Exists(marker))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            return null;
        }

        private static SkillInstallRoot LoadSavedRoot(string projectRoot)
        {
            string settingsPath = GetSettingsPath(projectRoot);
            AtomicFileWriter.RecoverSidecarFiles(settingsPath);
            if (!File.Exists(settingsPath))
            {
                return SkillInstallRoot.UnityProjectRoot;
            }

            string json = File.ReadAllText(settingsPath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return SkillInstallRoot.UnityProjectRoot;
            }

            string value = ReadInstallRootValue(json);
            if (value == null)
            {
                WarnUnusableSettings(settingsPath, "cannot be parsed");
                return SkillInstallRoot.UnityProjectRoot;
            }

            if (value == string.Empty || value == UnityProjectRootValue)
            {
                return SkillInstallRoot.UnityProjectRoot;
            }

            if (value == GitRepositoryRootValue)
            {
                return SkillInstallRoot.GitRepositoryRoot;
            }

            WarnUnusableSettings(settingsPath, $"has an unknown installRoot '{value}'");
            return SkillInstallRoot.UnityProjectRoot;
        }

        // The Editor keeps working so the user can pick a root again, which rewrites the file;
        // the CLI rejects the same file, so the problem cannot go unnoticed there.
        private static void WarnUnusableSettings(string settingsPath, string problem)
        {
            Debug.LogWarning(
                $"[UnityCliLoop] {settingsPath} {problem}. " +
                "Using the Unity project root until an install location is chosen again.");
        }

        // Returns null when the JSON is malformed.
        private static string ReadInstallRootValue(string json)
        {
            try
            {
                SkillSettingsJsonData data = JsonUtility.FromJson<SkillSettingsJsonData>(json);
                return data == null || data.installRoot == null ? string.Empty : data.installRoot;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static string GetSettingsPath(string projectRoot)
        {
            return Path.Combine(projectRoot, UnityCliLoopConstants.ULOOP_DIR, SettingsFileName);
        }

        private static string ToSettingsValue(SkillInstallRoot root)
        {
            return root switch
            {
                SkillInstallRoot.UnityProjectRoot => UnityProjectRootValue,
                SkillInstallRoot.GitRepositoryRoot => GitRepositoryRootValue,
                _ => throw new ArgumentOutOfRangeException(nameof(root), root, "Unknown skill install root")
            };
        }
    }
}
