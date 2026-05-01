using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace io.github.hatayama.uLoopMCP
{
    [TestFixture]
    public class ULoopSettingsTests
    {
        private static readonly string SettingsFilePath =
            Path.Combine(McpConstants.ULOOP_DIR, McpConstants.ULOOP_SETTINGS_FILE_NAME);
        private static readonly string ToolSettingsFilePath =
            Path.Combine(McpConstants.ULOOP_DIR, McpConstants.ULOOP_TOOL_SETTINGS_FILE_NAME);
        private static readonly string LegacySettingsFilePath =
            Path.Combine(McpConstants.USER_SETTINGS_FOLDER, McpConstants.SETTINGS_FILE_NAME);
        private static readonly string SettingsBackupPath = SettingsFilePath + ".bak";
        private static readonly string ToolSettingsBackupPath = ToolSettingsFilePath + ".bak";
        private static readonly string SettingsTmpPath = SettingsFilePath + ".tmp";
        private static readonly string ToolSettingsTmpPath = ToolSettingsFilePath + ".tmp";
        private static readonly string LegacyBackupPath = LegacySettingsFilePath + ".bak";
        private static readonly string LegacyTmpPath = LegacySettingsFilePath + ".tmp";
        private static readonly string OldSettingsFilePath =
            Path.Combine(McpConstants.ULOOP_DIR, "settings.security.json");
        private static readonly string OldSettingsBackupPath = OldSettingsFilePath + ".bak";

        private static readonly string[] AllSidecarPaths = new[]
        {
            SettingsBackupPath, SettingsTmpPath, ToolSettingsBackupPath, ToolSettingsTmpPath,
            LegacyBackupPath, LegacyTmpPath,
            OldSettingsFilePath, OldSettingsBackupPath
        };

        private bool _settingsFileExisted;
        private string _settingsFileContent;
        private bool _toolSettingsFileExisted;
        private string _toolSettingsFileContent;
        private bool _legacyFileExisted;
        private string _legacyFileContent;
        private bool[] _sidecarExisted;
        private string[] _sidecarContents;

        [SetUp]
        public void SetUp()
        {
            _settingsFileExisted = File.Exists(SettingsFilePath);
            _settingsFileContent = _settingsFileExisted ? File.ReadAllText(SettingsFilePath) : null;

            _toolSettingsFileExisted = File.Exists(ToolSettingsFilePath);
            _toolSettingsFileContent = _toolSettingsFileExisted ? File.ReadAllText(ToolSettingsFilePath) : null;

            _legacyFileExisted = File.Exists(LegacySettingsFilePath);
            _legacyFileContent = _legacyFileExisted ? File.ReadAllText(LegacySettingsFilePath) : null;

            _sidecarExisted = new bool[AllSidecarPaths.Length];
            _sidecarContents = new string[AllSidecarPaths.Length];
            for (int i = 0; i < AllSidecarPaths.Length; i++)
            {
                _sidecarExisted[i] = File.Exists(AllSidecarPaths[i]);
                _sidecarContents[i] = _sidecarExisted[i] ? File.ReadAllText(AllSidecarPaths[i]) : null;
            }

            string uloopDir = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(uloopDir) && !Directory.Exists(uloopDir))
            {
                Directory.CreateDirectory(uloopDir);
            }

            if (!Directory.Exists(McpConstants.USER_SETTINGS_FOLDER))
            {
                Directory.CreateDirectory(McpConstants.USER_SETTINGS_FOLDER);
            }

            DeleteIfExists(OldSettingsFilePath);
            DeleteIfExists(OldSettingsBackupPath);

            InvalidateBothCaches();
        }

        [TearDown]
        public void TearDown()
        {
            RestoreFile(SettingsFilePath, _settingsFileExisted, _settingsFileContent);
            RestoreFile(ToolSettingsFilePath, _toolSettingsFileExisted, _toolSettingsFileContent);
            RestoreFile(LegacySettingsFilePath, _legacyFileExisted, _legacyFileContent);

            for (int i = 0; i < AllSidecarPaths.Length; i++)
            {
                RestoreFile(AllSidecarPaths[i], _sidecarExisted[i], _sidecarContents[i]);
            }

            InvalidateBothCaches();
        }

        [Test]
        public void GetSettings_WhenNewFileAbsentAndLegacyExists_ShouldMigrateRemainingFieldsOnly()
        {
            DeleteIfExists(SettingsFilePath);

            string legacyJson = JsonUtility.ToJson(new LegacySettingsFixture
            {
                enableTestsExecution = true,
                allowMenuItemExecution = true,
                allowThirdPartyTools = true,
                dynamicCodeSecurityLevel = (int)DynamicCodeSecurityLevel.Restricted
            }, true);
            File.WriteAllText(LegacySettingsFilePath, legacyJson);
            InvalidateBothCaches();

            ULoopSettingsData result = ULoopSettings.GetSettings();

            Assert.IsTrue(result.allowThirdPartyTools);
            Assert.AreEqual((int)DynamicCodeSecurityLevel.Restricted, result.dynamicCodeSecurityLevel);
            Assert.IsTrue(File.Exists(SettingsFilePath), $"{SettingsFilePath} should be created by migration");
        }

        [Test]
        public void GetSettings_WhenNewFileExists_ShouldIgnoreLegacy()
        {
            ULoopSettingsData newSettings = new ULoopSettingsData
            {
                allowThirdPartyTools = true,
                dynamicCodeSecurityLevel = (int)DynamicCodeSecurityLevel.FullAccess
            };
            File.WriteAllText(SettingsFilePath, JsonUtility.ToJson(newSettings, true));

            string legacyJson = JsonUtility.ToJson(new LegacySettingsFixture
            {
                allowThirdPartyTools = false,
                dynamicCodeSecurityLevel = (int)DynamicCodeSecurityLevel.Restricted
            }, true);
            File.WriteAllText(LegacySettingsFilePath, legacyJson);
            InvalidateBothCaches();

            ULoopSettingsData result = ULoopSettings.GetSettings();

            Assert.IsTrue(result.allowThirdPartyTools);
            Assert.AreEqual((int)DynamicCodeSecurityLevel.FullAccess, result.dynamicCodeSecurityLevel);
        }

        [Test]
        public void SetAndGet_RoundTrip_ShouldPreserveRemainingValues()
        {
            ULoopSettingsData written = new ULoopSettingsData
            {
                allowThirdPartyTools = true,
                dynamicCodeSecurityLevel = (int)DynamicCodeSecurityLevel.Restricted
            };
            ULoopSettings.SaveSettings(written);
            ULoopSettings.InvalidateCache();

            ULoopSettingsData readBack = ULoopSettings.GetSettings();

            Assert.AreEqual(written.allowThirdPartyTools, readBack.allowThirdPartyTools);
            Assert.AreEqual(written.dynamicCodeSecurityLevel, readBack.dynamicCodeSecurityLevel);
        }

        [Test]
        public void Migration_ShouldPurgeRemainingSecurityFieldsAndPreserveOtherSettings()
        {
            DeleteIfExists(SettingsFilePath);

            string legacyJson = JsonUtility.ToJson(new LegacySettingsFixture
            {
                allowThirdPartyTools = true,
                dynamicCodeSecurityLevel = (int)DynamicCodeSecurityLevel.Restricted,
                showDeveloperTools = true
            }, true);
            File.WriteAllText(LegacySettingsFilePath, legacyJson);
            InvalidateBothCaches();

            ULoopSettings.GetSettings();

            string updatedLegacy = File.ReadAllText(LegacySettingsFilePath);

            StringAssert.DoesNotContain("\"allowThirdPartyTools\"", updatedLegacy);
            StringAssert.DoesNotContain("\"dynamicCodeSecurityLevel\"", updatedLegacy);
            StringAssert.DoesNotContain("\"customPort\"", updatedLegacy);

            McpEditorSettingsData legacySettings = JsonUtility.FromJson<McpEditorSettingsData>(updatedLegacy);
            Assert.IsTrue(legacySettings.showDeveloperTools, "showDeveloperTools should be preserved");
        }

        [Test]
        public void GetSettings_WhenBothFilesAbsent_ShouldReturnDefaults()
        {
            DeleteIfExists(SettingsFilePath);
            DeleteIfExists(LegacySettingsFilePath);
            InvalidateBothCaches();

            ULoopSettingsData result = ULoopSettings.GetSettings();

            Assert.IsFalse(result.allowThirdPartyTools);
            Assert.AreEqual((int)DynamicCodeSecurityLevel.Restricted, result.dynamicCodeSecurityLevel);
            Assert.IsFalse(File.Exists(LegacySettingsFilePath),
                "Legacy file should not be created when both files are absent");
        }

        [Test]
        public void GetSettings_WhenPrimaryMissingAndBackupExists_ShouldRecoverFromBackup()
        {
            DeleteIfExists(SettingsFilePath);

            ULoopSettingsData backupData = new ULoopSettingsData
            {
                allowThirdPartyTools = false,
                dynamicCodeSecurityLevel = (int)DynamicCodeSecurityLevel.FullAccess
            };
            File.WriteAllText(SettingsBackupPath, JsonUtility.ToJson(backupData, true));
            InvalidateBothCaches();

            ULoopSettingsData result = ULoopSettings.GetSettings();

            Assert.IsTrue(File.Exists(SettingsFilePath), $"{SettingsFilePath} should be recovered from .bak");
            Assert.IsFalse(result.allowThirdPartyTools);
            Assert.AreEqual((int)DynamicCodeSecurityLevel.FullAccess, result.dynamicCodeSecurityLevel);
            Assert.IsFalse(File.Exists(SettingsBackupPath), ".bak should be consumed by recovery");
        }

        [Test]
        public void GetSettings_WhenOldSecurityJsonExists_ShouldRenameToPermissions()
        {
            DeleteIfExists(SettingsFilePath);
            DeleteIfExists(LegacySettingsFilePath);

            ULoopSettingsData oldData = new ULoopSettingsData
            {
                allowThirdPartyTools = false,
                dynamicCodeSecurityLevel = (int)DynamicCodeSecurityLevel.Restricted
            };
            File.WriteAllText(OldSettingsFilePath, JsonUtility.ToJson(oldData, true));
            InvalidateBothCaches();

            ULoopSettingsData result = ULoopSettings.GetSettings();

            Assert.IsTrue(File.Exists(SettingsFilePath), "New settings file should exist after rename");
            Assert.IsFalse(File.Exists(OldSettingsFilePath), "Old settings file should be removed after rename");
            Assert.IsFalse(result.allowThirdPartyTools);
            Assert.AreEqual((int)DynamicCodeSecurityLevel.Restricted, result.dynamicCodeSecurityLevel);
        }

        [Test]
        public void GetSettings_WhenJsonContainsRemovedFields_ShouldIgnoreThem()
        {
            string settingsJson = JsonUtility.ToJson(new SettingsFileFixture
            {
                enableTestsExecution = true,
                allowMenuItemExecution = true,
                allowThirdPartyTools = true,
                dynamicCodeSecurityLevel = (int)DynamicCodeSecurityLevel.FullAccess
            }, true);
            File.WriteAllText(SettingsFilePath, settingsJson);
            InvalidateBothCaches();

            ULoopSettingsData result = ULoopSettings.GetSettings();

            Assert.IsTrue(result.allowThirdPartyTools);
            Assert.AreEqual((int)DynamicCodeSecurityLevel.FullAccess, result.dynamicCodeSecurityLevel);
        }

        [Test]
        public void GetSettings_WhenDynamicCodeLevelIsLegacyDisabled_ShouldDisableExecuteDynamicCodeTool()
        {
            string settingsJson = JsonUtility.ToJson(new SettingsFileFixture
            {
                allowThirdPartyTools = true,
                dynamicCodeSecurityLevel = 0
            }, true);
            File.WriteAllText(SettingsFilePath, settingsJson);
            DeleteIfExists(ToolSettingsFilePath);
            InvalidateBothCaches();
            ToolSettings.InvalidateCache();

            ULoopSettingsData result = ULoopSettings.GetSettings();

            Assert.AreEqual((int)DynamicCodeSecurityLevel.Restricted, result.dynamicCodeSecurityLevel);
            Assert.IsFalse(ToolSettings.IsToolEnabled(McpConstants.TOOL_NAME_EXECUTE_DYNAMIC_CODE));

            string updatedPermissionsJson = File.ReadAllText(SettingsFilePath);
            StringAssert.Contains("\"dynamicCodeSecurityLevel\": 1", updatedPermissionsJson);
        }

        [Test]
        public void GetSettings_WhenEnableTestsExecutionIsFalse_ShouldDisableRunTestsTool()
        {
            string settingsJson = JsonUtility.ToJson(new SettingsFileFixture
            {
                enableTestsExecution = false,
                allowThirdPartyTools = true,
                dynamicCodeSecurityLevel = (int)DynamicCodeSecurityLevel.Restricted
            }, true);
            File.WriteAllText(SettingsFilePath, settingsJson);
            DeleteIfExists(ToolSettingsFilePath);
            InvalidateBothCaches();
            ToolSettings.InvalidateCache();

            ULoopSettings.GetSettings();

            Assert.IsFalse(ToolSettings.IsToolEnabled(McpConstants.TOOL_NAME_RUN_TESTS));

            string updatedPermissionsJson = File.ReadAllText(SettingsFilePath);
            StringAssert.DoesNotContain("\"enableTestsExecution\"", updatedPermissionsJson);
        }

        [Test]
        public void GetSettings_WhenAllowMenuItemExecutionIsFalse_ShouldRemoveLegacyFieldOnly()
        {
            string settingsJson = JsonUtility.ToJson(new SettingsFileFixture
            {
                enableTestsExecution = true,
                allowMenuItemExecution = false,
                allowThirdPartyTools = true,
                dynamicCodeSecurityLevel = (int)DynamicCodeSecurityLevel.Restricted
            }, true);
            File.WriteAllText(SettingsFilePath, settingsJson);
            DeleteIfExists(ToolSettingsFilePath);
            InvalidateBothCaches();
            ToolSettings.InvalidateCache();

            ULoopSettings.GetSettings();

            Assert.AreEqual(0, ToolSettings.GetDisabledTools().Length);

            string updatedPermissionsJson = File.ReadAllText(SettingsFilePath);
            StringAssert.DoesNotContain("\"allowMenuItemExecution\"", updatedPermissionsJson);
        }

        [Test]
        public void SaveSettings_WhenJsonContainsRemovedFields_ShouldRewriteWithoutThem()
        {
            string settingsJson = JsonUtility.ToJson(new SettingsFileFixture
            {
                enableTestsExecution = true,
                allowMenuItemExecution = true,
                allowThirdPartyTools = true,
                dynamicCodeSecurityLevel = (int)DynamicCodeSecurityLevel.FullAccess
            }, true);
            File.WriteAllText(SettingsFilePath, settingsJson);
            InvalidateBothCaches();

            ULoopSettingsData settings = ULoopSettings.GetSettings();
            ULoopSettings.SaveSettings(settings);

            string updatedJson = File.ReadAllText(SettingsFilePath);

            StringAssert.DoesNotContain("\"enableTestsExecution\"", updatedJson);
            StringAssert.DoesNotContain("\"allowMenuItemExecution\"", updatedJson);
            StringAssert.Contains("\"allowThirdPartyTools\"", updatedJson);
            StringAssert.Contains("\"dynamicCodeSecurityLevel\"", updatedJson);
        }

        private static void RestoreFile(string path, bool existed, string content)
        {
            if (existed)
            {
                File.WriteAllText(path, content);
                return;
            }

            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private void InvalidateBothCaches()
        {
            ULoopSettings.InvalidateCache();
            McpEditorSettings.InvalidateCache();
            ToolSettings.InvalidateCache();
        }

        [System.Serializable]
        private class LegacySettingsFixture
        {
            public bool enableTestsExecution = false;
            public bool allowMenuItemExecution = false;
            public bool allowThirdPartyTools = false;
            public int dynamicCodeSecurityLevel = (int)DynamicCodeSecurityLevel.Restricted;
            public bool showDeveloperTools = false;
        }

        [System.Serializable]
        private class SettingsFileFixture
        {
            public bool enableTestsExecution = false;
            public bool allowMenuItemExecution = false;
            public bool allowThirdPartyTools = false;
            public int dynamicCodeSecurityLevel = (int)DynamicCodeSecurityLevel.Restricted;
        }
    }
}
