using System;
using System.IO;
using System.Security;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Management class for Unity CLI Loop Editor settings.
    /// Saves as a JSON file in the UserSettings folder.
    /// </summary>
    public sealed class UnityCliLoopEditorSettingsRepository : IUnityCliLoopEditorSettingsPort
    {
        private string SettingsFilePath => Path.Combine(UnityCliLoopConstants.USER_SETTINGS_FOLDER, UnityCliLoopConstants.SETTINGS_FILE_NAME);
        private string LegacySettingsFilePath => Path.Combine(UnityCliLoopConstants.USER_SETTINGS_FOLDER, UnityCliLoopConstants.LEGACY_SETTINGS_FILE_NAME);
        private readonly string[] _legacyTransientSettingKeys =
        {
            "customPort",
            "serverPort",
            "port",
            "Port",
            "serverTransportKind",
            "projectRootPath",
            "serverSessionId",
            "connectedLLMTools",
            "isServerRunning",
            "isAfterCompile",
            "isDomainReloadInProgress",
            "isReconnecting",
            "showReconnectingUI",
            "showPostCompileReconnectingUI"
        };

        private UnityCliLoopEditorSettingsData _cachedSettings;

        public void InvalidateCache()
        {
            _cachedSettings = null;
        }

        public void RecoverSettingsFileIfNeeded()
        {
            if (!IsValidSettingsPath(SettingsFilePath))
            {
                throw new SecurityException($"Invalid settings file path: {SettingsFilePath}");
            }

            AtomicFileWriter.RecoverSidecarFiles(SettingsFilePath);
            RemoveLegacyTransientFieldsIfNeeded(SettingsFilePath);
        }

        /// <summary>
        /// Gets the settings data.
        /// </summary>
        public UnityCliLoopEditorSettingsData GetSettings()
        {
            if (_cachedSettings == null)
            {
                LoadSettings();
            }

            return _cachedSettings;
        }

        /// <summary>
        /// Saves the settings data.
        /// </summary>
        public void SaveSettings(UnityCliLoopEditorSettingsData settings)
        {
            // Security: Validate settings file path
            if (!IsValidSettingsPath(SettingsFilePath))
            {
                throw new SecurityException($"Invalid settings file path: {SettingsFilePath}");
            }
            
            // Security: Ensure directory exists and create it safely
            string directory = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            UnityCliLoopEditorSettingsJsonData jsonData =
                UnityCliLoopEditorSettingsJsonData.FromDomain(settings);
            string json = JsonUtility.ToJson(jsonData, true);
            
            // Security: Validate JSON content size
            if (json.Length > UnityCliLoopConstants.MAX_SETTINGS_SIZE_BYTES)
            {
                throw new SecurityException("Settings JSON content exceeds size limit");
            }
            
            AtomicFileWriter.Write(SettingsFilePath, json);
            _cachedSettings = settings;

            // Best-effort cleanup: even if this fails, the backup sidecar is overwritten on next save.
            AtomicFileWriter.CleanupBackup(SettingsFilePath + AtomicFileWriter.BackupFileSuffix);
        }

        /// <summary>
        /// Applies a transformation to the current settings and saves once.
        /// Use when multiple fields need to be updated together to avoid redundant writes.
        /// </summary>
        public void UpdateSettings(Func<UnityCliLoopEditorSettingsData, UnityCliLoopEditorSettingsData> transform)
        {
            Debug.Assert(transform != null, "transform must not be null");

            UnityCliLoopEditorSettingsData current = GetSettings();
            UnityCliLoopEditorSettingsData updated = transform(current);
            SaveSettings(updated);
        }

        public string GetLastSeenSetupWizardVersion()
        {
            return GetSettings().lastSeenSetupWizardVersion ?? string.Empty;
        }

        public bool GetSuppressSetupWizardAutoShow()
        {
            return GetSettings().suppressSetupWizardAutoShow;
        }

        public void SetSuppressSetupWizardAutoShow(bool suppressAutoShow)
        {
            UnityCliLoopEditorSettingsData settings = GetSettings();
            UnityCliLoopEditorSettingsData updatedSettings = settings with { suppressSetupWizardAutoShow = suppressAutoShow };
            SaveSettings(updatedSettings);
        }

        public void SetShowToolSettings(bool showToolSettings)
        {
            UnityCliLoopEditorSettingsData settings = GetSettings();
            UnityCliLoopEditorSettingsData newSettings = settings with { showToolSettings = showToolSettings };
            SaveSettings(newSettings);
        }

        public void SetInstallSkillsFlat(bool installSkillsFlat)
        {
            UnityCliLoopEditorSettingsData settings = GetSettings();
            UnityCliLoopEditorSettingsData newSettings = settings with { installSkillsFlat = installSkillsFlat };
            SaveSettings(newSettings);
        }

        /// <summary>
        /// Loads the settings file.
        /// </summary>
        private void LoadSettings()
        {
            try
            {
                // Security: Validate settings file path
                if (!IsValidSettingsPath(SettingsFilePath))
                {
                    throw new SecurityException($"Invalid settings file path: {SettingsFilePath}");
                }

                RecoverSettingsFileIfNeeded();

                if (File.Exists(SettingsFilePath))
                {
                    // Security: Check file size before reading
                    FileInfo fileInfo = new(SettingsFilePath);
                    if (fileInfo.Length > UnityCliLoopConstants.MAX_SETTINGS_SIZE_BYTES)
                    {
                        throw new SecurityException("Settings file exceeds size limit");
                    }

                    string json = File.ReadAllText(SettingsFilePath);

                    // Security: Validate JSON content
                    if (string.IsNullOrWhiteSpace(json))
                    {
                        throw new InvalidDataException("Settings file contains invalid JSON content");
                    }

                    UnityCliLoopEditorSettingsJsonData loadedSettings =
                        JsonUtility.FromJson<UnityCliLoopEditorSettingsJsonData>(json);
                    _cachedSettings = loadedSettings.ToDomain();
                }
                else
                {
                    _cachedSettings = new UnityCliLoopEditorSettingsData();
                }

                bool migratedLegacySetupWizardState = ApplyLegacySetupWizardStateIfNeeded();
                if (migratedLegacySetupWizardState)
                {
                    SaveSettings(_cachedSettings);
                }
            }
            catch (Exception ex)
            {
                // Don't suppress this exception - corrupted settings should be reported
                throw new InvalidOperationException(
                    $"Failed to load Unity CLI Loop Editor settings from: {SettingsFilePath}. Settings file may be corrupted.", ex);
            }
        }

        private bool ApplyLegacySetupWizardStateIfNeeded()
        {
            Debug.Assert(_cachedSettings != null, "_cachedSettings must not be null");

            if (_cachedSettings.legacySetupWizardStateMigrated)
            {
                DeleteLegacySettingsFileIfExists();
                return false;
            }

            if (!File.Exists(LegacySettingsFilePath))
            {
                return false;
            }

            if (!IsValidLegacySettingsPath(LegacySettingsFilePath))
            {
                throw new SecurityException($"Invalid legacy settings file path: {LegacySettingsFilePath}");
            }

            FileInfo fileInfo = new(LegacySettingsFilePath);
            if (fileInfo.Length > UnityCliLoopConstants.MAX_SETTINGS_SIZE_BYTES)
            {
                throw new SecurityException("Legacy settings file exceeds size limit");
            }

            string legacyJson = File.ReadAllText(LegacySettingsFilePath);
            if (string.IsNullOrWhiteSpace(legacyJson))
            {
                DeleteLegacySettingsFileIfExists();
                return false;
            }

            LegacySetupWizardSettingsProbe legacySettings =
                JsonUtility.FromJson<LegacySetupWizardSettingsProbe>(legacyJson);
            if (legacySettings == null)
            {
                DeleteLegacySettingsFileIfExists();
                return false;
            }

            if (string.IsNullOrEmpty(legacySettings.lastSeenSetupWizardVersion)
                && !legacySettings.suppressSetupWizardAutoShow)
            {
                DeleteLegacySettingsFileIfExists();
                return false;
            }

            _cachedSettings = _cachedSettings with
            {
                lastSeenSetupWizardVersion = legacySettings.lastSeenSetupWizardVersion ?? string.Empty,
                suppressSetupWizardAutoShow =
                    _cachedSettings.suppressSetupWizardAutoShow || legacySettings.suppressSetupWizardAutoShow,
                legacySetupWizardStateMigrated = true
            };
            DeleteLegacySettingsFileIfExists();
            return true;
        }

        private void DeleteLegacySettingsFileIfExists()
        {
            if (!File.Exists(LegacySettingsFilePath))
            {
                return;
            }

            if (!IsValidLegacySettingsPath(LegacySettingsFilePath))
            {
                throw new SecurityException($"Invalid legacy settings file path: {LegacySettingsFilePath}");
            }

            File.Delete(LegacySettingsFilePath);
        }

        private void RemoveLegacyTransientFieldsIfNeeded(string settingsPath)
        {
            if (!File.Exists(settingsPath))
            {
                return;
            }

            FileInfo fileInfo = new(settingsPath);
            if (fileInfo.Length > UnityCliLoopConstants.MAX_SETTINGS_SIZE_BYTES)
            {
                throw new SecurityException("Settings file exceeds size limit");
            }

            JToken settingsToken;
            using (StreamReader reader = File.OpenText(settingsPath))
            {
                settingsToken = JToken.ReadFrom(new JsonTextReader(reader));
            }

            bool removed = RemoveLegacyTransientFields(settingsToken);
            if (!removed)
            {
                return;
            }

            AtomicFileWriter.Write(settingsPath, settingsToken.ToString(Formatting.Indented));
        }

        private bool RemoveLegacyTransientFields(JToken token)
        {
            Debug.Assert(token != null, "token must not be null");

            bool removed = false;
            if (token is JObject jsonObject)
            {
                foreach (string legacyKey in _legacyTransientSettingKeys)
                {
                    removed |= jsonObject.Remove(legacyKey);
                }

                foreach (JProperty property in jsonObject.Properties())
                {
                    removed |= RemoveLegacyTransientFields(property.Value);
                }

                return removed;
            }

            if (token is JArray jsonArray)
            {
                foreach (JToken item in jsonArray)
                {
                    removed |= RemoveLegacyTransientFields(item);
                }
            }

            return removed;
        }

        [Serializable]
        private sealed class LegacySetupWizardSettingsProbe
        {
            public string lastSeenSetupWizardVersion = string.Empty;
            public bool suppressSetupWizardAutoShow = false;
        }

        /// <summary>
        /// Security: Validate if the settings file path is safe
        /// </summary>
        private static bool IsValidSettingsPath(string path)
        {
            return IsValidSettingsPathForFileName(path, UnityCliLoopConstants.SETTINGS_FILE_NAME);
        }

        private static bool IsValidLegacySettingsPath(string path)
        {
            return IsValidSettingsPathForFileName(path, UnityCliLoopConstants.LEGACY_SETTINGS_FILE_NAME);
        }

        private static bool IsValidSettingsPathForFileName(string path, string settingsFileName)
        {
            try
            {
                Debug.Assert(!string.IsNullOrEmpty(settingsFileName), "settingsFileName must not be null or empty");

                // Normalize the path to prevent path traversal
                string normalizedPath = Path.GetFullPath(path);
                
                // Must be under UserSettings directory
                string expectedUserSettingsPath = Path.GetFullPath(UnityCliLoopConstants.USER_SETTINGS_FOLDER);
                
                // Check if path is within the expected directory
                return normalizedPath.StartsWith(expectedUserSettingsPath, StringComparison.OrdinalIgnoreCase) &&
                       normalizedPath.EndsWith(settingsFileName, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"{UnityCliLoopConstants.SECURITY_LOG_PREFIX} Error validating settings path {path}: {ex.Message}");
                return false;
            }
        }
    }

}
