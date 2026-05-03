using NUnit.Framework;
using System.IO;

namespace io.github.hatayama.UnityCliLoop
{
    [TestFixture]
    public class ToolSettingsTests
    {
        private static readonly string SettingsFilePath =
            Path.Combine(McpConstants.ULOOP_DIR, McpConstants.ULOOP_TOOL_SETTINGS_FILE_NAME);
        private static readonly string SettingsBackupPath = SettingsFilePath + ".bak";

        private bool _settingsFileExisted;
        private string _settingsFileContent;
        private bool _backupFileExisted;
        private string _backupFileContent;

        [SetUp]
        public void SetUp()
        {
            _settingsFileExisted = File.Exists(SettingsFilePath);
            _settingsFileContent = _settingsFileExisted ? File.ReadAllText(SettingsFilePath) : null;

            _backupFileExisted = File.Exists(SettingsBackupPath);
            _backupFileContent = _backupFileExisted ? File.ReadAllText(SettingsBackupPath) : null;

            string uloopDir = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(uloopDir) && !Directory.Exists(uloopDir))
            {
                Directory.CreateDirectory(uloopDir);
            }

            // Neutralize existing files so backup recovery doesn't leak across tests
            DeleteIfExists(SettingsFilePath);
            DeleteIfExists(SettingsBackupPath);
            ToolSettings.InvalidateCache();
        }

        [TearDown]
        public void TearDown()
        {
            RestoreFile(SettingsFilePath, _settingsFileExisted, _settingsFileContent);
            RestoreFile(SettingsBackupPath, _backupFileExisted, _backupFileContent);
            ToolSettings.InvalidateCache();
        }

        private static void RestoreFile(string path, bool existed, string content)
        {
            if (existed)
            {
                File.WriteAllText(path, content);
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        // ── Round-trip ─────────────────────────────────────────────────

        [Test]
        public void SetToolEnabled_Disable_ThenIsToolEnabled_ShouldReturnFalse()
        {
            ToolSettings.SetToolEnabled("compile", false);

            bool result = ToolSettings.IsToolEnabled("compile");

            Assert.IsFalse(result);
        }

        [Test]
        public void SetToolEnabled_DisableThenEnable_ShouldReturnTrue()
        {
            ToolSettings.SetToolEnabled("compile", false);
            ToolSettings.SetToolEnabled("compile", true);

            bool result = ToolSettings.IsToolEnabled("compile");

            Assert.IsTrue(result);
        }

        [Test]
        public void IsToolEnabled_WhenNeverDisabled_ShouldReturnTrue()
        {
            DeleteIfExists(SettingsFilePath);
            ToolSettings.InvalidateCache();

            bool result = ToolSettings.IsToolEnabled("compile");

            Assert.IsTrue(result);
        }

        // ── Round-trip with file reload ────────────────────────────────

        [Test]
        public void SetToolEnabled_ShouldPersistAcrossCacheInvalidation()
        {
            ToolSettings.SetToolEnabled("compile", false);
            ToolSettings.SetToolEnabled("get-logs", false);
            ToolSettings.InvalidateCache();

            Assert.IsFalse(ToolSettings.IsToolEnabled("compile"));
            Assert.IsFalse(ToolSettings.IsToolEnabled("get-logs"));
            Assert.IsTrue(ToolSettings.IsToolEnabled("clear-console"));
        }

        // ── Deduplication ──────────────────────────────────────────────

        [Test]
        public void SetToolEnabled_DisableSameToolTwice_ShouldNotDuplicate()
        {
            ToolSettings.SetToolEnabled("compile", false);
            ToolSettings.SetToolEnabled("compile", false);

            string[] disabledTools = ToolSettings.GetDisabledTools();
            int compileCount = 0;
            foreach (string tool in disabledTools)
            {
                if (tool == "compile") compileCount++;
            }

            Assert.AreEqual(1, compileCount);
        }

        // ── Cache invalidation ─────────────────────────────────────────

        [Test]
        public void InvalidateCache_ShouldReloadFromFile()
        {
            ToolSettings.SetToolEnabled("compile", false);
            Assert.IsFalse(ToolSettings.IsToolEnabled("compile"));

            // Externally modify the file to clear disabledTools
            File.WriteAllText(SettingsFilePath, "{\"disabledTools\":[]}");
            ToolSettings.InvalidateCache();

            Assert.IsTrue(ToolSettings.IsToolEnabled("compile"));
        }

        // ── Backup recovery ────────────────────────────────────────────

        [Test]
        public void GetSettings_WhenPrimaryMissingAndBackupExists_ShouldRecover()
        {
            DeleteIfExists(SettingsFilePath);
            File.WriteAllText(SettingsBackupPath, "{\"disabledTools\":[\"compile\"]}");
            ToolSettings.InvalidateCache();

            Assert.IsFalse(ToolSettings.IsToolEnabled("compile"));
            Assert.IsTrue(File.Exists(SettingsFilePath), "Primary file should be recovered from backup");
        }

        // ── Multiple tools ─────────────────────────────────────────────

        [Test]
        public void SetToolEnabled_MultipleTools_ShouldTrackIndependently()
        {
            ToolSettings.SetToolEnabled("compile", false);
            ToolSettings.SetToolEnabled("get-logs", false);
            ToolSettings.SetToolEnabled("compile", true);

            Assert.IsTrue(ToolSettings.IsToolEnabled("compile"));
            Assert.IsFalse(ToolSettings.IsToolEnabled("get-logs"));
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
