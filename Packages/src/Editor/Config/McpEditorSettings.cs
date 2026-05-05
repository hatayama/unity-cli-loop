using System;
using System.IO;
using System.Security;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Compile request data for session management.
    /// </summary>
    [Serializable]
    public class CompileRequestData
    {
        public string requestId;
        public string json;
    }

    /// <summary>
    /// Unity CLI Loop Editor settings data.
    /// </summary>
    [Serializable]
    public record McpEditorSettingsData
    {
        public bool showDeveloperTools = false;
        public bool enableCommunicationLogs = false;
        public string lastSeenSetupWizardVersion = "";
        public bool suppressSetupWizardAutoShow = false;

        // UI State Settings
        public bool showSecuritySettings = true;
        public bool showToolSettings = true;

        // Default to flat installation so first-time setup does not add an extra grouping layer unless requested.
        public bool installSkillsFlat = true;
        
        // Session State Settings (moved from McpSessionManager)
        // Default to true so the server starts automatically on fresh install
        public bool isServerRunning = true;
        public bool isAfterCompile = false;
        public bool isDomainReloadInProgress = false;
        public bool isReconnecting = false;
        public bool showReconnectingUI = false;
        public bool showPostCompileReconnectingUI = false;
        public float communicationLogHeight = McpUIConstants.DEFAULT_COMMUNICATION_LOG_HEIGHT;
        public string communicationLogsJson = "[]";
        public string pendingRequestsJson = "{}";
        public bool compileWindowHasData = false;
        public string[] pendingCompileRequestIds = new string[0];
        public CompileRequestData[] compileRequests = new CompileRequestData[0];
    }

    /// <summary>
    /// Management class for Unity CLI Loop Editor settings.
    /// Saves as a JSON file in the UserSettings folder.
    /// </summary>
    public static class McpEditorSettings
    {
        private static string SettingsFilePath => Path.Combine(McpConstants.USER_SETTINGS_FOLDER, McpConstants.SETTINGS_FILE_NAME);
        private static readonly string[] LegacyTransientSettingKeys =
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

        private static McpEditorSettingsData _cachedSettings;

        internal static void InvalidateCache()
        {
            _cachedSettings = null;
        }

        [InitializeOnLoadMethod]
        private static void RecoverSettingsFileOnEditorLoad()
        {
            if (AssetDatabase.IsAssetImportWorkerProcess())
            {
                return;
            }

            RecoverSettingsFileIfNeeded();
        }

        internal static void RecoverSettingsFileIfNeeded()
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
        public static McpEditorSettingsData GetSettings()
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
        public static void SaveSettings(McpEditorSettingsData settings)
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
            if (json.Length > McpConstants.MAX_SETTINGS_SIZE_BYTES)
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
        public static void UpdateSettings(Func<McpEditorSettingsData, McpEditorSettingsData> transform)
        {
            Debug.Assert(transform != null, "transform must not be null");

            McpEditorSettingsData current = GetSettings();
            McpEditorSettingsData updated = transform(current);
            SaveSettings(updated);
        }

        /// <summary>
        /// Gets the Developer Tools display setting.
        /// </summary>
        public static bool GetShowDeveloperTools()
        {
            return GetSettings().showDeveloperTools;
        }

        /// <summary>
        /// Saves the Developer Tools display setting.
        /// </summary>
        public static void SetShowDeveloperTools(bool show)
        {
            McpEditorSettingsData settings = GetSettings();
            McpEditorSettingsData updatedSettings = settings with { showDeveloperTools = show };
            SaveSettings(updatedSettings);
        }

        /// <summary>
        /// Gets the communication logs enabled flag.
        /// </summary>
        public static bool GetEnableCommunicationLogs()
        {
            return GetSettings().enableCommunicationLogs;
        }

        public static string GetLastSeenSetupWizardVersion()
        {
            return GetSettings().lastSeenSetupWizardVersion ?? string.Empty;
        }

        public static void SetLastSeenSetupWizardVersion(string version)
        {
            string normalizedVersion = version ?? string.Empty;
            McpEditorSettingsData settings = GetSettings();
            McpEditorSettingsData updatedSettings = settings with { lastSeenSetupWizardVersion = normalizedVersion };
            SaveSettings(updatedSettings);
        }

        public static bool GetSuppressSetupWizardAutoShow()
        {
            return GetSettings().suppressSetupWizardAutoShow;
        }

        public static void SetSuppressSetupWizardAutoShow(bool suppressAutoShow)
        {
            McpEditorSettingsData settings = GetSettings();
            McpEditorSettingsData updatedSettings = settings with { suppressSetupWizardAutoShow = suppressAutoShow };
            SaveSettings(updatedSettings);
        }

        /// <summary>
        /// Sets the communication logs enabled flag.
        /// </summary>
        public static void SetEnableCommunicationLogs(bool enableCommunicationLogs)
        {
            McpEditorSettingsData settings = GetSettings();
            McpEditorSettingsData newSettings = settings with { enableCommunicationLogs = enableCommunicationLogs };
            SaveSettings(newSettings);
        }

        /// <summary>
        /// Gets the show security settings flag.
        /// </summary>
        public static bool GetShowSecuritySettings()
        {
            return GetSettings().showSecuritySettings;
        }

        /// <summary>
        /// Sets the show security settings flag.
        /// </summary>
        public static void SetShowSecuritySettings(bool showSecuritySettings)
        {
            McpEditorSettingsData settings = GetSettings();
            McpEditorSettingsData newSettings = settings with { showSecuritySettings = showSecuritySettings };
            SaveSettings(newSettings);
        }

        public static bool GetShowToolSettings()
        {
            return GetSettings().showToolSettings;
        }

        public static void SetShowToolSettings(bool showToolSettings)
        {
            McpEditorSettingsData settings = GetSettings();
            McpEditorSettingsData newSettings = settings with { showToolSettings = showToolSettings };
            SaveSettings(newSettings);
        }

        public static bool GetInstallSkillsFlat()
        {
            return GetSettings().installSkillsFlat;
        }

        public static void SetInstallSkillsFlat(bool installSkillsFlat)
        {
            McpEditorSettingsData settings = GetSettings();
            McpEditorSettingsData newSettings = settings with { installSkillsFlat = installSkillsFlat };
            SaveSettings(newSettings);
        }

        // Session State Settings Methods (moved from McpSessionManager)

        /// <summary>
        /// Gets the server running state.
        /// </summary>
        public static bool GetIsServerRunning()
        {
            return GetSettings().isServerRunning;
        }

        /// <summary>
        /// Sets the server running state.
        /// </summary>
        public static void SetIsServerRunning(bool isServerRunning)
        {
            McpEditorSettingsData settings = GetSettings();
            McpEditorSettingsData newSettings = settings with { isServerRunning = isServerRunning };
            SaveSettings(newSettings);
        }

        /// <summary>
        /// Gets the after compile flag.
        /// </summary>
        public static bool GetIsAfterCompile()
        {
            return GetSettings().isAfterCompile;
        }

        /// <summary>
        /// Sets the after compile flag.
        /// </summary>
        public static void SetIsAfterCompile(bool isAfterCompile)
        {
            McpEditorSettingsData settings = GetSettings();
            McpEditorSettingsData newSettings = settings with { isAfterCompile = isAfterCompile };
            SaveSettings(newSettings);
        }

        /// <summary>
        /// Gets the domain reload in progress flag.
        /// </summary>
        public static bool GetIsDomainReloadInProgress()
        {
            return GetSettings().isDomainReloadInProgress;
        }

        /// <summary>
        /// Sets the domain reload in progress flag.
        /// </summary>
        public static void SetIsDomainReloadInProgress(bool isDomainReloadInProgress)
        {
            McpEditorSettingsData settings = GetSettings();
            McpEditorSettingsData newSettings = settings with { isDomainReloadInProgress = isDomainReloadInProgress };
            SaveSettings(newSettings);
        }

        /// <summary>
        /// Gets the reconnecting flag.
        /// </summary>
        public static bool GetIsReconnecting()
        {
            return GetSettings().isReconnecting;
        }

        /// <summary>
        /// Sets the reconnecting flag.
        /// </summary>
        public static void SetIsReconnecting(bool isReconnecting)
        {
            McpEditorSettingsData settings = GetSettings();
            McpEditorSettingsData newSettings = settings with { isReconnecting = isReconnecting };
            SaveSettings(newSettings);
        }

        /// <summary>
        /// Gets the show reconnecting UI flag.
        /// </summary>
        public static bool GetShowReconnectingUI()
        {
            return GetSettings().showReconnectingUI;
        }

        /// <summary>
        /// Sets the show reconnecting UI flag.
        /// </summary>
        public static void SetShowReconnectingUI(bool showReconnectingUI)
        {
            McpEditorSettingsData settings = GetSettings();
            McpEditorSettingsData newSettings = settings with { showReconnectingUI = showReconnectingUI };
            SaveSettings(newSettings);
        }

        /// <summary>
        /// Gets the show post compile reconnecting UI flag.
        /// </summary>
        public static bool GetShowPostCompileReconnectingUI()
        {
            return GetSettings().showPostCompileReconnectingUI;
        }

        /// <summary>
        /// Sets the show post compile reconnecting UI flag.
        /// </summary>
        public static void SetShowPostCompileReconnectingUI(bool showPostCompileReconnectingUI)
        {
            McpEditorSettingsData settings = GetSettings();
            McpEditorSettingsData newSettings = settings with { showPostCompileReconnectingUI = showPostCompileReconnectingUI };
            SaveSettings(newSettings);
        }

        /// <summary>
        /// Gets the communication log height.
        /// </summary>
        public static float GetCommunicationLogHeight()
        {
            return GetSettings().communicationLogHeight;
        }

        /// <summary>
        /// Sets the communication log height.
        /// </summary>
        public static void SetCommunicationLogHeight(float communicationLogHeight)
        {
            McpEditorSettingsData settings = GetSettings();
            McpEditorSettingsData newSettings = settings with { communicationLogHeight = communicationLogHeight };
            SaveSettings(newSettings);
        }

        /// <summary>
        /// Gets the communication logs JSON.
        /// </summary>
        public static string GetCommunicationLogsJson()
        {
            return GetSettings().communicationLogsJson;
        }

        /// <summary>
        /// Sets the communication logs JSON.
        /// </summary>
        public static void SetCommunicationLogsJson(string communicationLogsJson)
        {
            McpEditorSettingsData settings = GetSettings();
            McpEditorSettingsData newSettings = settings with { communicationLogsJson = communicationLogsJson };
            SaveSettings(newSettings);
        }

        /// <summary>
        /// Gets the pending requests JSON.
        /// </summary>
        public static string GetPendingRequestsJson()
        {
            return GetSettings().pendingRequestsJson;
        }

        /// <summary>
        /// Sets the pending requests JSON.
        /// </summary>
        public static void SetPendingRequestsJson(string pendingRequestsJson)
        {
            McpEditorSettingsData settings = GetSettings();
            McpEditorSettingsData newSettings = settings with { pendingRequestsJson = pendingRequestsJson };
            SaveSettings(newSettings);
        }


        /// <summary>
        /// Gets the compile window has data flag.
        /// </summary>
        public static bool GetCompileWindowHasData()
        {
            return GetSettings().compileWindowHasData;
        }

        /// <summary>
        /// Sets the compile window has data flag.
        /// </summary>
        public static void SetCompileWindowHasData(bool compileWindowHasData)
        {
            McpEditorSettingsData settings = GetSettings();
            McpEditorSettingsData newSettings = settings with { compileWindowHasData = compileWindowHasData };
            SaveSettings(newSettings);
        }

        // Helper methods that mirror McpSessionManager functionality

        /// <summary>
        /// Clear server session.
        /// </summary>
        public static void ClearServerSession()
        {
            UpdateSettings(settings => settings with
            {
                isServerRunning = false
            });
        }

        /// <summary>
        /// Clear after compile flag.
        /// </summary>
        public static void ClearAfterCompileFlag()
        {
            SetIsAfterCompile(false);
        }

        /// <summary>
        /// Clear reconnecting flags.
        /// </summary>
        public static void ClearReconnectingFlags()
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
        public static void ClearPostCompileReconnectingUI()
        {
            SetShowPostCompileReconnectingUI(false);
        }

        /// <summary>
        /// Clear domain reload flag.
        /// </summary>
        public static void ClearDomainReloadFlag()
        {
            SetIsDomainReloadInProgress(false);
        }

        /// <summary>
        /// Clear communication logs.
        /// </summary>
        public static void ClearCommunicationLogs()
        {
            UpdateSettings(s => s with
            {
                communicationLogsJson = "[]",
                pendingRequestsJson = "{}"
            });
        }

        /// <summary>
        /// Clear compile window data.
        /// </summary>
        public static void ClearCompileWindowData()
        {
            SetCompileWindowHasData(false);
        }

        // Compile request management methods

        /// <summary>
        /// Gets the pending compile request IDs.
        /// </summary>
        public static string[] GetPendingCompileRequestIds()
        {
            return GetSettings().pendingCompileRequestIds;
        }

        /// <summary>
        /// Sets the pending compile request IDs.
        /// </summary>
        public static void SetPendingCompileRequestIds(string[] pendingCompileRequestIds)
        {
            McpEditorSettingsData settings = GetSettings();
            McpEditorSettingsData newSettings = settings with { pendingCompileRequestIds = pendingCompileRequestIds };
            SaveSettings(newSettings);
        }

        /// <summary>
        /// Gets the compile requests.
        /// </summary>
        public static CompileRequestData[] GetCompileRequests()
        {
            return GetSettings().compileRequests;
        }

        /// <summary>
        /// Sets the compile requests.
        /// </summary>
        public static void SetCompileRequests(CompileRequestData[] compileRequests)
        {
            McpEditorSettingsData settings = GetSettings();
            McpEditorSettingsData newSettings = settings with { compileRequests = compileRequests };
            SaveSettings(newSettings);
        }

        /// <summary>
        /// Gets the compile request JSON by request ID.
        /// </summary>
        public static string GetCompileRequestJson(string requestId)
        {
            CompileRequestData[] requests = GetCompileRequests();
            CompileRequestData request = System.Array.Find(requests, r => r.requestId == requestId);
            return request?.json;
        }

        /// <summary>
        /// Sets the compile request JSON for a specific request ID.
        /// </summary>
        public static void SetCompileRequestJson(string requestId, string json)
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
        public static void ClearAllCompileRequests()
        {
            SetCompileRequests(new CompileRequestData[0]);
            SetPendingCompileRequestIds(new string[0]);
        }

        /// <summary>
        /// Adds a pending compile request.
        /// </summary>
        public static void AddPendingCompileRequest(string requestId)
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
        public static void RemovePendingCompileRequest(string requestId)
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
        private static void LoadSettings()
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
                    FileInfo fileInfo = new FileInfo(SettingsFilePath);
                    if (fileInfo.Length > McpConstants.MAX_SETTINGS_SIZE_BYTES)
                    {
                        throw new SecurityException("Settings file exceeds size limit");
                    }

                    string json = File.ReadAllText(SettingsFilePath);

                    // Security: Validate JSON content
                    if (string.IsNullOrWhiteSpace(json))
                    {
                        throw new InvalidDataException("Settings file contains invalid JSON content");
                    }

                    _cachedSettings = JsonUtility.FromJson<McpEditorSettingsData>(json);

                    // Migrate security fields before any potential SaveSettings call from this class.
                    // If SaveSettings runs first, legacy security fields are stripped from JSON
                    // because McpEditorSettingsData no longer defines them.
                    ULoopSettings.GetSettings();
                }
                else
                {
                    _cachedSettings = new McpEditorSettingsData();
                }
            }
            catch (Exception ex)
            {
                // Don't suppress this exception - corrupted settings should be reported
                throw new InvalidOperationException(
                    $"Failed to load Unity CLI Loop Editor settings from: {SettingsFilePath}. Settings file may be corrupted.", ex);
            }
        }

        private static void RemoveLegacyTransientFieldsIfNeeded(string settingsPath)
        {
            if (!File.Exists(settingsPath))
            {
                return;
            }

            FileInfo fileInfo = new FileInfo(settingsPath);
            if (fileInfo.Length > McpConstants.MAX_SETTINGS_SIZE_BYTES)
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

        private static bool RemoveLegacyTransientFields(JToken token)
        {
            Debug.Assert(token != null, "token must not be null");

            bool removed = false;
            if (token is JObject jsonObject)
            {
                foreach (string legacyKey in LegacyTransientSettingKeys)
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
                string expectedUserSettingsPath = Path.GetFullPath(McpConstants.USER_SETTINGS_FOLDER);
                
                // Check if path is within the expected directory
                return normalizedPath.StartsWith(expectedUserSettingsPath, StringComparison.OrdinalIgnoreCase) &&
                       normalizedPath.EndsWith(McpConstants.SETTINGS_FILE_NAME, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"{McpConstants.SECURITY_LOG_PREFIX} Error validating settings path {path}: {ex.Message}");
                return false;
            }
        }

    }
}
