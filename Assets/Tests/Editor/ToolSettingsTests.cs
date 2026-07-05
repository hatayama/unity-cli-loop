using NUnit.Framework;
using System.IO;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies Tool Settings behavior.
    /// </summary>
    [TestFixture]
    public class ToolSettingsTests
    {
        private static readonly string SettingsFilePath =
            Path.Combine(UnityCliLoopConstants.ULOOP_DIR, UnityCliLoopConstants.ULOOP_TOOL_SETTINGS_FILE_NAME);
        private static readonly string SettingsBackupPath = SettingsFilePath + AtomicFileWriter.BackupFileSuffix;

        private bool _settingsFileExisted;
        private string _settingsFileContent;
        private bool _backupFileExisted;
        private string _backupFileContent;
        private IToolSettingsPort _toolSettingsPort;

        [SetUp]
        public void SetUp()
        {
            _settingsFileExisted = File.Exists(SettingsFilePath);
            _settingsFileContent = _settingsFileExisted ? File.ReadAllText(SettingsFilePath) : null;

            _backupFileExisted = File.Exists(SettingsBackupPath);
            _backupFileContent = _backupFileExisted ? File.ReadAllText(SettingsBackupPath) : null;
            _toolSettingsPort = new ToolSettingsRepository();

            string uloopDir = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(uloopDir) && !Directory.Exists(uloopDir))
            {
                Directory.CreateDirectory(uloopDir);
            }

            // Neutralize existing files so backup recovery doesn't leak across tests
            DeleteIfExists(SettingsFilePath);
            DeleteIfExists(SettingsBackupPath);
            _toolSettingsPort.InvalidateCache();
        }

        [TearDown]
        public void TearDown()
        {
            RestoreFile(SettingsFilePath, _settingsFileExisted, _settingsFileContent);
            RestoreFile(SettingsBackupPath, _backupFileExisted, _backupFileContent);
            _toolSettingsPort.InvalidateCache();
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
            _toolSettingsPort.SetToolEnabled("compile", false);

            bool result = _toolSettingsPort.IsToolEnabled("compile");

            Assert.IsFalse(result);
        }

        [Test]
        public void SetToolEnabled_DisableThenEnable_ShouldReturnTrue()
        {
            _toolSettingsPort.SetToolEnabled("compile", false);
            _toolSettingsPort.SetToolEnabled("compile", true);

            bool result = _toolSettingsPort.IsToolEnabled("compile");

            Assert.IsTrue(result);
        }

        [Test]
        public void IsToolEnabled_WhenNeverDisabled_ShouldReturnTrue()
        {
            DeleteIfExists(SettingsFilePath);
            _toolSettingsPort.InvalidateCache();

            bool result = _toolSettingsPort.IsToolEnabled("compile");

            Assert.IsTrue(result);
        }

        // ── Round-trip with file reload ────────────────────────────────

        [Test]
        public void SetToolEnabled_ShouldPersistAcrossCacheInvalidation()
        {
            _toolSettingsPort.SetToolEnabled("compile", false);
            _toolSettingsPort.SetToolEnabled("get-logs", false);
            _toolSettingsPort.InvalidateCache();

            Assert.IsFalse(_toolSettingsPort.IsToolEnabled("compile"));
            Assert.IsFalse(_toolSettingsPort.IsToolEnabled("get-logs"));
            Assert.IsTrue(_toolSettingsPort.IsToolEnabled("clear-console"));
        }

        // ── Deduplication ──────────────────────────────────────────────

        [Test]
        public void SetToolEnabled_DisableSameToolTwice_ShouldNotDuplicate()
        {
            _toolSettingsPort.SetToolEnabled("compile", false);
            _toolSettingsPort.SetToolEnabled("compile", false);

            string[] disabledTools = _toolSettingsPort.GetDisabledTools();
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
            _toolSettingsPort.SetToolEnabled("compile", false);
            Assert.IsFalse(_toolSettingsPort.IsToolEnabled("compile"));

            // Externally modify the file to clear disabledTools
            File.WriteAllText(SettingsFilePath, "{\"disabledTools\":[]}");
            _toolSettingsPort.InvalidateCache();

            Assert.IsTrue(_toolSettingsPort.IsToolEnabled("compile"));
        }

        // ── Backup recovery ────────────────────────────────────────────

        [Test]
        public void GetSettings_WhenPrimaryMissingAndBackupExists_ShouldRecover()
        {
            DeleteIfExists(SettingsFilePath);
            File.WriteAllText(SettingsBackupPath, "{\"disabledTools\":[\"compile\"]}");
            _toolSettingsPort.InvalidateCache();

            Assert.IsFalse(_toolSettingsPort.IsToolEnabled("compile"));
            Assert.IsTrue(File.Exists(SettingsFilePath), "Primary file should be recovered from backup");
        }

        // ── Multiple tools ─────────────────────────────────────────────

        [Test]
        public void SetToolEnabled_MultipleTools_ShouldTrackIndependently()
        {
            _toolSettingsPort.SetToolEnabled("compile", false);
            _toolSettingsPort.SetToolEnabled("get-logs", false);
            _toolSettingsPort.SetToolEnabled("compile", true);

            Assert.IsTrue(_toolSettingsPort.IsToolEnabled("compile"));
            Assert.IsFalse(_toolSettingsPort.IsToolEnabled("get-logs"));
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
