using System;
using System.IO;
using System.Security;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;
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

        /// <summary>
        /// Gets the Developer Tools display setting.
        /// </summary>
        public bool GetShowDeveloperTools()
        {
            return GetSettings().showDeveloperTools;
        }

        /// <summary>
        /// Saves the Developer Tools display setting.
        /// </summary>
        public void SetShowDeveloperTools(bool show)
        {
            UnityCliLoopEditorSettingsData settings = GetSettings();
            UnityCliLoopEditorSettingsData updatedSettings = settings with { showDeveloperTools = show };
            SaveSettings(updatedSettings);
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

        /// <summary>
        /// Gets the show security settings flag.
        /// </summary>
        public bool GetShowUnityCliLoopSecuritySetting()
        {
            return GetSettings().showUnityCliLoopSecuritySetting;
        }

        /// <summary>
        /// Sets the show security settings flag.
        /// </summary>
        public void SetShowUnityCliLoopSecuritySetting(bool showUnityCliLoopSecuritySetting)
        {
            UnityCliLoopEditorSettingsData settings = GetSettings();
            UnityCliLoopEditorSettingsData newSettings = settings with { showUnityCliLoopSecuritySetting = showUnityCliLoopSecuritySetting };
            SaveSettings(newSettings);
        }

        public bool GetShowToolSettings()
        {
            return GetSettings().showToolSettings;
        }

        public void SetShowToolSettings(bool showToolSettings)
        {
            UnityCliLoopEditorSettingsData settings = GetSettings();
            UnityCliLoopEditorSettingsData newSettings = settings with { showToolSettings = showToolSettings };
            SaveSettings(newSettings);
        }

        public bool GetInstallSkillsFlat()
        {
            return GetSettings().installSkillsFlat;
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
        /// Sets the after compile flag.
        /// </summary>
        public void SetIsAfterCompile(bool isAfterCompile)
        {
            UnityCliLoopEditorSettingsData settings = GetSettings();
            UnityCliLoopEditorSettingsData newSettings = settings with { isAfterCompile = isAfterCompile };
            SaveSettings(newSettings);
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
        /// Gets the reconnecting flag.
        /// </summary>
        public bool GetIsReconnecting()
        {
            return GetSettings().isReconnecting;
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
        /// Gets the show post compile reconnecting UI flag.
        /// </summary>
        public bool GetShowPostCompileReconnectingUI()
        {
            return GetSettings().showPostCompileReconnectingUI;
        }

        /// <summary>
        /// Sets the show post compile reconnecting UI flag.
        /// </summary>
        public void SetShowPostCompileReconnectingUI(bool showPostCompileReconnectingUI)
        {
            UnityCliLoopEditorSettingsData settings = GetSettings();
            UnityCliLoopEditorSettingsData newSettings = settings with { showPostCompileReconnectingUI = showPostCompileReconnectingUI };
            SaveSettings(newSettings);
        }

        /// <summary>
        /// Gets the compile window has data flag.
        /// </summary>
        public bool GetCompileWindowHasData()
        {
            return GetSettings().compileWindowHasData;
        }

        /// <summary>
        /// Sets the compile window has data flag.
        /// </summary>
        public void SetCompileWindowHasData(bool compileWindowHasData)
        {
            UnityCliLoopEditorSettingsData settings = GetSettings();
            UnityCliLoopEditorSettingsData newSettings = settings with { compileWindowHasData = compileWindowHasData };
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
            SetIsAfterCompile(false);
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
            SetShowPostCompileReconnectingUI(false);
        }

        /// <summary>
        /// Clear domain reload flag.
        /// </summary>
        public void ClearDomainReloadFlag()
        {
            SetIsDomainReloadInProgress(false);
        }

        /// <summary>
        /// Clear compile window data.
        /// </summary>
        public void ClearCompileWindowData()
        {
            SetCompileWindowHasData(false);
        }

        // Compile request management methods

        /// <summary>
        /// Gets the pending compile request IDs.
        /// </summary>
        public string[] GetPendingCompileRequestIds()
        {
            return GetSettings().pendingCompileRequestIds;
        }

        /// <summary>
        /// Sets the pending compile request IDs.
        /// </summary>
        public void SetPendingCompileRequestIds(string[] pendingCompileRequestIds)
        {
            UnityCliLoopEditorSettingsData settings = GetSettings();
            UnityCliLoopEditorSettingsData newSettings = settings with { pendingCompileRequestIds = pendingCompileRequestIds };
            SaveSettings(newSettings);
        }

        /// <summary>
        /// Gets the compile requests.
        /// </summary>
        public CompileRequestData[] GetCompileRequests()
        {
            return GetSettings().compileRequests;
        }

        /// <summary>
        /// Sets the compile requests.
        /// </summary>
        public void SetCompileRequests(CompileRequestData[] compileRequests)
        {
            UnityCliLoopEditorSettingsData settings = GetSettings();
            UnityCliLoopEditorSettingsData newSettings = settings with { compileRequests = compileRequests };
            SaveSettings(newSettings);
        }

        /// <summary>
        /// Gets the compile request JSON by request ID.
        /// </summary>
        public string GetCompileRequestJson(string requestId)
        {
            CompileRequestData[] requests = GetCompileRequests();
            CompileRequestData request = System.Array.Find(requests, r => r.requestId == requestId);
            return request?.json;
        }

        /// <summary>
        /// Sets the compile request JSON for a specific request ID.
        /// </summary>
        public void SetCompileRequestJson(string requestId, string json)
        {
            CompileRequestData[] requests = GetCompileRequests();
            CompileRequestData existingRequest = System.Array.Find(requests, r => r.requestId == requestId);
            
            if (existingRequest != null)
            {
                existingRequest.json = json;
            }
            else
            {
                CompileRequestData[] newRequests = new CompileRequestData[requests.Length + 1];
                System.Array.Copy(requests, newRequests, requests.Length);
                newRequests[requests.Length] = new CompileRequestData { requestId = requestId, json = json };
                requests = newRequests;
            }
            
            SetCompileRequests(requests);
        }

        /// <summary>
        /// Clears all compile requests.
        /// </summary>
        public void ClearAllCompileRequests()
        {
            SetCompileRequests(new CompileRequestData[0]);
            SetPendingCompileRequestIds(new string[0]);
        }

        /// <summary>
        /// Adds a pending compile request.
        /// </summary>
        public void AddPendingCompileRequest(string requestId)
        {
            string[] pendingIds = GetPendingCompileRequestIds();
            if (System.Array.IndexOf(pendingIds, requestId) == -1)
            {
                string[] newPendingIds = new string[pendingIds.Length + 1];
                System.Array.Copy(pendingIds, newPendingIds, pendingIds.Length);
                newPendingIds[pendingIds.Length] = requestId;
                SetPendingCompileRequestIds(newPendingIds);
            }
        }

        /// <summary>
        /// Removes a pending compile request.
        /// </summary>
        public void RemovePendingCompileRequest(string requestId)
        {
            string[] pendingIds = GetPendingCompileRequestIds();
            int index = System.Array.IndexOf(pendingIds, requestId);
            if (index != -1)
            {
                string[] newPendingIds = new string[pendingIds.Length - 1];
                System.Array.Copy(pendingIds, 0, newPendingIds, 0, index);
                System.Array.Copy(pendingIds, index + 1, newPendingIds, index, pendingIds.Length - index - 1);
                SetPendingCompileRequestIds(newPendingIds);
            }
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
