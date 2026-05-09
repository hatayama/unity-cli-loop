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
        private readonly string[] _legacyTransientSettingKeys =
        {
            "customPort",
            "serverPort",
            "port",
            "Port",
            "serverTransportKind",
            "projectRootPath",
            "serverSessionId",
            "connectedLLMTools"
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
            
            string json = JsonUtility.ToJson(settings, true);
            
            // Security: Validate JSON content size
            if (json.Length > UnityCliLoopConstants.MAX_SETTINGS_SIZE_BYTES)
            {
                throw new SecurityException("Settings JSON content exceeds size limit");
            }
            
            AtomicFileWriter.Write(SettingsFilePath, json);
            _cachedSettings = settings;

            // Best-effort cleanup: even if this fails, .bak is overwritten on next save
            AtomicFileWriter.CleanupBackup(SettingsFilePath + ".bak");
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

        public void SetLastSeenSetupWizardVersion(string version)
        {
            string normalizedVersion = version ?? string.Empty;
            UnityCliLoopEditorSettingsData settings = GetSettings();
            UnityCliLoopEditorSettingsData updatedSettings = settings with { lastSeenSetupWizardVersion = normalizedVersion };
            SaveSettings(updatedSettings);
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

        public void SetShowUnityCliLoopSecuritySetting(bool showUnityCliLoopSecuritySetting)
        {
            UnityCliLoopEditorSettingsData settings = GetSettings();
            UnityCliLoopEditorSettingsData newSettings = settings with { showUnityCliLoopSecuritySetting = showUnityCliLoopSecuritySetting };
            SaveSettings(newSettings);
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
        /// Gets the server running state.
        /// </summary>
        public bool GetIsServerRunning()
        {
            return GetSettings().isServerRunning;
        }

        /// <summary>
        /// Sets the server running state.
        /// </summary>
        public void SetIsServerRunning(bool isServerRunning)
        {
            UnityCliLoopEditorSettingsData settings = GetSettings();
            UnityCliLoopEditorSettingsData newSettings = settings with { isServerRunning = isServerRunning };
            SaveSettings(newSettings);
        }

        /// <summary>
        /// Gets the after compile flag.
        /// </summary>
        public bool GetIsAfterCompile()
        {
            return GetSettings().isAfterCompile;
        }

        /// <summary>
        /// Gets the domain reload in progress flag.
        /// </summary>
        public bool GetIsDomainReloadInProgress()
        {
            return GetSettings().isDomainReloadInProgress;
        }

        /// <summary>
        /// Sets the domain reload in progress flag.
        /// </summary>
        public void SetIsDomainReloadInProgress(bool isDomainReloadInProgress)
        {
            UnityCliLoopEditorSettingsData settings = GetSettings();
            UnityCliLoopEditorSettingsData newSettings = settings with { isDomainReloadInProgress = isDomainReloadInProgress };
            SaveSettings(newSettings);
        }

        /// <summary>
        /// Sets the reconnecting flag.
        /// </summary>
        public void SetIsReconnecting(bool isReconnecting)
        {
            UnityCliLoopEditorSettingsData settings = GetSettings();
            UnityCliLoopEditorSettingsData newSettings = settings with { isReconnecting = isReconnecting };
            SaveSettings(newSettings);
        }

        /// <summary>
        /// Gets the show reconnecting UI flag.
        /// </summary>
        public bool GetShowReconnectingUI()
        {
            return GetSettings().showReconnectingUI;
        }

        /// <summary>
        /// Sets the show reconnecting UI flag.
        /// </summary>
        public void SetShowReconnectingUI(bool showReconnectingUI)
        {
            UnityCliLoopEditorSettingsData settings = GetSettings();
            UnityCliLoopEditorSettingsData newSettings = settings with { showReconnectingUI = showReconnectingUI };
            SaveSettings(newSettings);
        }

        /// <summary>
        /// Clear server session.
        /// </summary>
        public void ClearServerSession()
        {
            UpdateSettings(settings => settings with
            {
                isServerRunning = false
            });
        }

        /// <summary>
        /// Clear after compile flag.
        /// </summary>
        public void ClearAfterCompileFlag()
        {
            UpdateSettings(s => s with { isAfterCompile = false });
        }

        /// <summary>
        /// Clear reconnecting flags.
        /// </summary>
        public void ClearReconnectingFlags()
        {
            UpdateSettings(s => s with
            {
                isReconnecting = false,
                showReconnectingUI = false
            });
        }

        /// <summary>
        /// Clear post compile reconnecting UI.
        /// </summary>
        public void ClearPostCompileReconnectingUI()
        {
            UpdateSettings(s => s with { showPostCompileReconnectingUI = false });
        }

        /// <summary>
        /// Clear domain reload flag.
        /// </summary>
        public void ClearDomainReloadFlag()
        {
            SetIsDomainReloadInProgress(false);
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

                    _cachedSettings = JsonUtility.FromJson<UnityCliLoopEditorSettingsData>(json);

                    // Migrate security fields before any potential SaveSettings call from this class.
                    // If SaveSettings runs first, legacy security fields are stripped from JSON
                    // because UnityCliLoopEditorSettingsData no longer defines them.
                    ULoopSettings.GetSettings();
                }
                else
                {
                    _cachedSettings = new UnityCliLoopEditorSettingsData();
                }
            }
            catch (Exception ex)
            {
                // Don't suppress this exception - corrupted settings should be reported
                throw new InvalidOperationException(
                    $"Failed to load Unity CLI Loop Editor settings from: {SettingsFilePath}. Settings file may be corrupted.", ex);
            }
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

        /// <summary>
        /// Security: Validate if the settings file path is safe
        /// </summary>
        private static bool IsValidSettingsPath(string path)
        {
            try
            {
                // Normalize the path to prevent path traversal
                string normalizedPath = Path.GetFullPath(path);
                
                // Must be under UserSettings directory
                string expectedUserSettingsPath = Path.GetFullPath(UnityCliLoopConstants.USER_SETTINGS_FOLDER);
                
                // Check if path is within the expected directory
                return normalizedPath.StartsWith(expectedUserSettingsPath, StringComparison.OrdinalIgnoreCase) &&
                       normalizedPath.EndsWith(UnityCliLoopConstants.SETTINGS_FILE_NAME, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"{UnityCliLoopConstants.SECURITY_LOG_PREFIX} Error validating settings path {path}: {ex.Message}");
                return false;
            }
        }
    }

}
