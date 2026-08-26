using System;
using System.IO;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Persists project-scoped (git-shared) Unity CLI Loop settings as a JSON file under
    /// the ProjectSettings folder, so the values are committed and apply to the whole team.
    /// </summary>
    public sealed class UnityCliLoopProjectSettingsRepository : IUnityCliLoopProjectSettingsPort
    {
        private readonly string _settingsDirectory;
        private readonly string _settingsFilePath;

        public UnityCliLoopProjectSettingsRepository(string settingsDirectory)
        {
            Debug.Assert(
                !string.IsNullOrEmpty(settingsDirectory),
                "settingsDirectory must not be null or empty");

            _settingsDirectory = settingsDirectory;
            _settingsFilePath = Path.Combine(
                settingsDirectory,
                ToolContracts.UnityCliLoopConstants.PROJECT_SETTINGS_FILE_NAME);
        }

        public bool GetSuppressSetupWizardAutoShow()
        {
            return LoadSettings().suppressSetupWizardAutoShow;
        }

        public void SetSuppressSetupWizardAutoShow(bool suppressAutoShow)
        {
            ProjectSettingsJsonData settings = LoadSettings();
            settings.suppressSetupWizardAutoShow = suppressAutoShow;
            SaveSettings(settings);
        }

        // The file is git-managed and can change under the editor (git pull, hand edits),
        // so it is re-read on every access instead of being cached in memory.
        private ProjectSettingsJsonData LoadSettings()
        {
            if (!File.Exists(_settingsFilePath))
            {
                return new ProjectSettingsJsonData();
            }

            string json = File.ReadAllText(_settingsFilePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new ProjectSettingsJsonData();
            }

            ProjectSettingsJsonData loadedSettings = JsonUtility.FromJson<ProjectSettingsJsonData>(json);
            return loadedSettings ?? new ProjectSettingsJsonData();
        }

        // The file is created lazily on the first write: a read must never create it,
        // so that teammates who never touch the toggle keep a clean working tree.
        private void SaveSettings(ProjectSettingsJsonData settings)
        {
            if (!Directory.Exists(_settingsDirectory))
            {
                Directory.CreateDirectory(_settingsDirectory);
            }

            string json = JsonUtility.ToJson(settings, true);
            AtomicFileWriter.Write(_settingsFilePath, json);
            AtomicFileWriter.CleanupBackup(_settingsFilePath + AtomicFileWriter.BackupFileSuffix);
        }

        [Serializable]
        private sealed class ProjectSettingsJsonData
        {
            public bool suppressSetupWizardAutoShow = false;
        }
    }
}
