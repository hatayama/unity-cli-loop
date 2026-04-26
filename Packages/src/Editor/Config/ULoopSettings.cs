using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Security settings management for .uloop/settings.permissions.json.
    /// This file is stored in the project root so it can be git-tracked
    /// and shared across team members as a security policy.
    /// </summary>
    public static class ULoopSettings
    {
        private static string SettingsFilePath =>
            Path.Combine(McpConstants.ULOOP_DIR, McpConstants.ULOOP_SETTINGS_FILE_NAME);

        private static string LegacySettingsFilePath =>
            Path.Combine(McpConstants.USER_SETTINGS_FOLDER, McpConstants.SETTINGS_FILE_NAME);

        private static ULoopSettingsData _cachedSettings;

        public static ULoopSettingsData GetSettings()
        {
            if (_cachedSettings == null)
            {
                LoadSettings();
            }
            return _cachedSettings;
        }

        public static void SaveSettings(ULoopSettingsData settings)
        {
            Debug.Assert(settings != null, "settings must not be null");

            string directory = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonUtility.ToJson(settings, true);

            Debug.Assert(json.Length <= McpConstants.MAX_SETTINGS_SIZE_BYTES,
                "Settings JSON content exceeds size limit");

            AtomicFileWriter.Write(SettingsFilePath, json);
            _cachedSettings = settings;

            AtomicFileWriter.CleanupBackup(SettingsFilePath + ".bak");
        }

        public static void UpdateSettings(Func<ULoopSettingsData, ULoopSettingsData> transform)
        {
            Debug.Assert(transform != null, "transform must not be null");

            ULoopSettingsData current = GetSettings();
            ULoopSettingsData updated = transform(current);
            SaveSettings(updated);
        }

        public static bool GetAllowThirdPartyTools()
        {
            return GetSettings().allowThirdPartyTools;
        }

        public static void SetAllowThirdPartyTools(bool value)
        {
            ULoopSettingsData settings = GetSettings();
            ULoopSettingsData updated = settings with { allowThirdPartyTools = value };
            SaveSettings(updated);
        }

        public static DynamicCodeSecurityLevel GetDynamicCodeSecurityLevel()
        {
            ULoopSettingsData settings = GetSettings();
            int persistedValue = settings.dynamicCodeSecurityLevel;

            // Disabled(0) was removed; migrate any undefined value to Restricted and persist
            if (!Enum.IsDefined(typeof(DynamicCodeSecurityLevel), persistedValue))
            {
                DynamicCodeSecurityLevel fallback = DynamicCodeSecurityLevel.Restricted;
                ULoopSettingsData migrated = settings with { dynamicCodeSecurityLevel = (int)fallback };
                SaveSettings(migrated);
                return fallback;
            }
            return (DynamicCodeSecurityLevel)persistedValue;
        }

        public static void SetDynamicCodeSecurityLevel(DynamicCodeSecurityLevel level)
        {
            ULoopSettingsData settings = GetSettings();
            ULoopSettingsData updated = settings with { dynamicCodeSecurityLevel = (int)level };
            SaveSettings(updated);

            VibeLogger.LogInfo(
                "editor_settings_security_level_changed",
                $"Security level changed to: {level}",
                new { level = level.ToString() },
                correlationId: McpConstants.GenerateCorrelationId(),
                humanNote: "Security level updated in editor settings",
                aiTodo: "Monitor security level changes"
            );
        }

        // Loading & Migration

        private static void LoadSettings()
        {
            string oldSettingsPath = Path.Combine(McpConstants.ULOOP_DIR, "settings.security.json");
            string oldBackupPath = oldSettingsPath + ".bak";

            AtomicFileWriter.RecoverSidecarFiles(SettingsFilePath);

            // When upgrading directly from v0.67 (or earlier) to v0.69+, the legacy
            // file still contains security fields because v0.68's extraction never ran.
            // Legacy file takes priority over any settings.security.json which may hold
            // stale default values.
            if (!File.Exists(SettingsFilePath) && LegacyFileHasSecurityFields())
            {
                MigrateFromLegacySettings();
                DeleteIfExists(oldSettingsPath);
                DeleteIfExists(oldBackupPath);
                return;
            }

            // v0.68.0 used "settings.security.json"; rename once so existing users keep their settings.
            // This migration block can be removed after a few releases.
            if (!File.Exists(SettingsFilePath))
            {
                if (File.Exists(oldSettingsPath))
                {
                    File.Move(oldSettingsPath, SettingsFilePath);
                }
                else if (File.Exists(oldBackupPath))
                {
                    File.Move(oldBackupPath, SettingsFilePath);
                }
            }

            if (File.Exists(SettingsFilePath))
            {
                FileInfo fileInfo = new FileInfo(SettingsFilePath);
                Debug.Assert(fileInfo.Length <= McpConstants.MAX_SETTINGS_SIZE_BYTES,
                    $"Settings file exceeds size limit: {fileInfo.Length} bytes");

                string json = File.ReadAllText(SettingsFilePath);

                if (string.IsNullOrWhiteSpace(json))
                {
                    _cachedSettings = new ULoopSettingsData();
                    return;
                }

                _cachedSettings = JsonUtility.FromJson<ULoopSettingsData>(json);
                bool migratedToolToggles = ApplyLegacyToolToggleMigrations(json);
                bool normalizedDynamicCode = NormalizeLegacyDisabledDynamicCode();
                if (migratedToolToggles || normalizedDynamicCode)
                {
                    SaveSettings(_cachedSettings);
                }
                return;
            }

            // .uloop/settings.permissions.json does not exist yet — attempt migration from legacy file
            MigrateFromLegacySettings();
        }

        private static bool LegacyFileHasSecurityFields()
        {
            if (!File.Exists(LegacySettingsFilePath))
            {
                return false;
            }

            string json = File.ReadAllText(LegacySettingsFilePath);
            return json.Contains($"\"{nameof(LegacySecuritySettingsProbe.allowThirdPartyTools)}\"")
                || json.Contains($"\"{nameof(LegacySecuritySettingsProbe.dynamicCodeSecurityLevel)}\"");
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        /// <summary>
        /// McpEditorSettingsData no longer contains security fields, so we need
        /// a dedicated probe class to extract them from the legacy JSON.
        /// </summary>
        [Serializable]
        private class LegacySecuritySettingsProbe
        {
            public bool enableTestsExecution = true;
            public bool allowMenuItemExecution = true;
            public bool allowThirdPartyTools = false;
            public int dynamicCodeSecurityLevel = (int)DynamicCodeSecurityLevel.Restricted;
        }

        /// <summary>
        /// One-time migration: .uloop/settings.permissions.json absence is used as the trigger
        /// to guarantee this runs exactly once — after migration the file exists and
        /// this path is never taken again.
        /// </summary>
        private static void MigrateFromLegacySettings()
        {
            if (!File.Exists(LegacySettingsFilePath))
            {
                _cachedSettings = new ULoopSettingsData();
                return;
            }

            string legacyJson = File.ReadAllText(LegacySettingsFilePath);
            if (string.IsNullOrWhiteSpace(legacyJson))
            {
                _cachedSettings = new ULoopSettingsData();
                return;
            }

            LegacySecuritySettingsProbe probe = JsonUtility.FromJson<LegacySecuritySettingsProbe>(legacyJson);

            _cachedSettings = new ULoopSettingsData
            {
                allowThirdPartyTools = probe.allowThirdPartyTools,
                dynamicCodeSecurityLevel = probe.dynamicCodeSecurityLevel
            };

            ApplyLegacyToolToggleMigrations(legacyJson);
            NormalizeLegacyDisabledDynamicCode();
            SaveSettings(_cachedSettings);

            // Re-save legacy file to purge security fields that are no longer in
            // McpEditorSettingsData — JsonUtility.ToJson only serializes defined fields,
            // so the 4 removed fields disappear from the JSON on re-serialization.
            McpEditorSettings.SaveSettings(McpEditorSettings.GetSettings());
        }

        private static bool NormalizeLegacyDisabledDynamicCode()
        {
            Debug.Assert(_cachedSettings != null, "_cachedSettings must not be null");

            if (_cachedSettings.dynamicCodeSecurityLevel != 0)
            {
                return false;
            }

            ToolSettings.SetToolEnabled(McpConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE, false);
            _cachedSettings = _cachedSettings with
            {
                dynamicCodeSecurityLevel = (int)DynamicCodeSecurityLevel.Restricted
            };
            return true;
        }

        private static bool ApplyLegacyToolToggleMigrations(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            LegacySecuritySettingsProbe probe = JsonUtility.FromJson<LegacySecuritySettingsProbe>(json);
            bool migrated = false;

            if (json.Contains($"\"{nameof(LegacySecuritySettingsProbe.enableTestsExecution)}\"")
                && !probe.enableTestsExecution)
            {
                ToolSettings.SetToolEnabled(McpConstants.TOOL_NAME_RUN_TESTS, false);
                migrated = true;
            }

            if (json.Contains($"\"{nameof(LegacySecuritySettingsProbe.allowMenuItemExecution)}\""))
            {
                migrated = true;
            }

            return migrated;
        }

        internal static void InvalidateCache()
        {
            _cachedSettings = null;
        }

        // Roslyn Define Symbol Management

    }
}
