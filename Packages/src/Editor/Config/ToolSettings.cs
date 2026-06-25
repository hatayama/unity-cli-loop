using System;
using System.IO;
using System.Linq;

using UnityEngine;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Tool toggle settings management for .uloop/settings.tools.json.
    /// Controls which tools are enabled/disabled in the MCP tool list.
    /// </summary>
    public static class ToolSettings
    {
        private static string SettingsFilePath =>
            Path.Combine(McpConstants.ULOOP_DIR, McpConstants.ULOOP_TOOL_SETTINGS_FILE_NAME);

        private static ToolSettingsData _cachedSettings;

        public static ToolSettingsData GetSettings()
        {
            if (_cachedSettings == null)
            {
                LoadSettings();
            }
            return _cachedSettings;
        }

        public static void SaveSettings(ToolSettingsData settings)
        {
            Debug.Assert(settings != null, "settings must not be null");

            ToolSettingsData normalizedSettings = NormalizeSettings(settings);
            string directory = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonUtility.ToJson(normalizedSettings, true);

            Debug.Assert(json.Length <= McpConstants.MAX_SETTINGS_SIZE_BYTES,
                "Settings JSON content exceeds size limit");

            AtomicFileWriter.Write(SettingsFilePath, json);
            _cachedSettings = normalizedSettings;

            AtomicFileWriter.CleanupBackup(SettingsFilePath + ".bak");
        }

        public static bool IsToolEnabled(string toolName)
        {
            Debug.Assert(!string.IsNullOrEmpty(toolName), "toolName must not be null or empty");

            string[] disabledTools = GetSettings().disabledTools;
            return !disabledTools.Contains(toolName);
        }

        public static void SetToolEnabled(string toolName, bool enabled)
        {
            Debug.Assert(!string.IsNullOrEmpty(toolName), "toolName must not be null or empty");

            ToolSettingsData settings = GetSettings();
            string[] currentDisabled = settings.disabledTools;

            string[] newDisabled;
            if (enabled)
            {
                newDisabled = currentDisabled.Where(t => t != toolName).ToArray();
            }
            else
            {
                if (currentDisabled.Contains(toolName))
                {
                    return;
                }
                newDisabled = currentDisabled.Append(toolName).ToArray();
            }

            ToolSettingsData updated = settings with { disabledTools = newDisabled };
            SaveSettings(updated);
        }

        public static string[] GetDisabledTools()
        {
            return GetSettings().disabledTools;
        }

        public static string GetSkillCliInvocation()
        {
            return NormalizeSkillCliInvocation(GetSettings().skillCliInvocation);
        }

        public static void SetSkillCliInvocation(string invocation)
        {
            Debug.Assert(!string.IsNullOrEmpty(invocation), "invocation must not be null or empty");

            ToolSettingsData settings = GetSettings();
            string normalizedInvocation = NormalizeSkillCliInvocation(invocation);
            if (settings.skillCliInvocation == normalizedInvocation)
            {
                return;
            }

            ToolSettingsData updated = settings with { skillCliInvocation = normalizedInvocation };
            SaveSettings(updated);
        }

        public static void InvalidateCache()
        {
            _cachedSettings = null;
        }

        private static void LoadSettings()
        {
            AtomicFileWriter.RecoverSidecarFiles(SettingsFilePath);

            if (File.Exists(SettingsFilePath))
            {
                FileInfo fileInfo = new FileInfo(SettingsFilePath);
                Debug.Assert(fileInfo.Length <= McpConstants.MAX_SETTINGS_SIZE_BYTES,
                    $"Settings file exceeds size limit: {fileInfo.Length} bytes");

                string json = File.ReadAllText(SettingsFilePath);

                if (string.IsNullOrWhiteSpace(json))
                {
                    _cachedSettings = new ToolSettingsData();
                    return;
                }

                ToolSettingsData loaded = JsonUtility.FromJson<ToolSettingsData>(json);
                if (loaded == null)
                {
                    _cachedSettings = new ToolSettingsData();
                    return;
                }
                _cachedSettings = NormalizeSettings(loaded);
                return;
            }

            _cachedSettings = new ToolSettingsData();
        }

        private static ToolSettingsData NormalizeSettings(ToolSettingsData settings)
        {
            Debug.Assert(settings != null, "settings must not be null");

            string[] disabledTools = settings.disabledTools ?? Array.Empty<string>();
            string skillCliInvocation = NormalizeSkillCliInvocation(settings.skillCliInvocation);
            return settings with
            {
                disabledTools = disabledTools,
                skillCliInvocation = skillCliInvocation
            };
        }

        private static string NormalizeSkillCliInvocation(string invocation)
        {
            return invocation == CliConstants.SKILL_CLI_INVOCATION_GLOBAL
                ? CliConstants.SKILL_CLI_INVOCATION_GLOBAL
                : CliConstants.SKILL_CLI_INVOCATION_NPX;
        }
    }
}
