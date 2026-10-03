using System.Collections.Generic;
using System.IO;

using NUnit.Framework;
using Newtonsoft.Json.Linq;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    public sealed class UnityCliLoopPackageRemovalSettingsResetterTests
    {
        private static readonly string SettingsFilePath =
            Path.Combine(UnityCliLoopConstants.USER_SETTINGS_FOLDER, UnityCliLoopConstants.SETTINGS_FILE_NAME);

        private bool _settingsFileExisted;
        private string _settingsFileContent;
        private IUnityCliLoopEditorSettingsPort _editorSettingsPort;
        private UnityCliLoopEditorSettingsRepository _editorSettingsRepository;

        [SetUp]
        public void SetUp()
        {
            _settingsFileExisted = File.Exists(SettingsFilePath);
            _settingsFileContent = _settingsFileExisted ? File.ReadAllText(SettingsFilePath) : null;

            if (!Directory.Exists(UnityCliLoopConstants.USER_SETTINGS_FOLDER))
            {
                Directory.CreateDirectory(UnityCliLoopConstants.USER_SETTINGS_FOLDER);
            }

            DeleteIfExists(SettingsFilePath);
            _editorSettingsPort =
                UnityCliLoopEditorSettingsTestFactory.CreatePortWithRepository(out _editorSettingsRepository);
            _editorSettingsRepository.InvalidateCache();
        }

        [TearDown]
        public void TearDown()
        {
            RestoreFile(SettingsFilePath, _settingsFileExisted, _settingsFileContent);
            _editorSettingsRepository.InvalidateCache();
        }

        [Test]
        public void PackageNameConstant_WhenComparedWithPackageManifest_Matches()
        {
            // Verifies that uninstall detection uses the package manifest identity.
            string packageJson = File.ReadAllText("Packages/src/package.json");
            JObject packageManifest = JObject.Parse(packageJson);

            Assert.That(UnityCliLoopConstants.PACKAGE_NAME, Is.EqualTo(packageManifest["name"]?.Value<string>()));
        }

        [Test]
        public void ShouldResetSetupWizardState_WhenOwnPackageRemoved_ReturnsTrue()
        {
            // Verifies that uninstall detection matches this package name.
            List<string> removedPackageNames = new()
            {
                UnityCliLoopConstants.PACKAGE_NAME
            };

            bool shouldReset = UnityCliLoopPackageRemovalSettingsResetter.ShouldResetSetupWizardState(
                removedPackageNames,
                UnityCliLoopConstants.PACKAGE_NAME);

            Assert.That(shouldReset, Is.True);
        }

        [Test]
        public void ShouldResetSetupWizardState_WhenOtherPackageRemoved_ReturnsFalse()
        {
            // Verifies that unrelated package removals do not reset setup state.
            List<string> removedPackageNames = new()
            {
                "com.unity.textmeshpro"
            };

            bool shouldReset = UnityCliLoopPackageRemovalSettingsResetter.ShouldResetSetupWizardState(
                removedPackageNames,
                UnityCliLoopConstants.PACKAGE_NAME);

            Assert.That(shouldReset, Is.False);
        }

        [Test]
        public void ResetSetupWizardState_WhenWizardStateExists_ClearsOnlyWizardFields()
        {
            // Verifies that package uninstall resets auto-show state without discarding user settings.
            UnityCliLoopEditorSettingsData settings = CreateSettingsWithNonWizardPreferences();

            UnityCliLoopEditorSettingsData resetSettings =
                UnityCliLoopPackageRemovalSettingsResetter.ResetSetupWizardState(settings);

            Assert.That(resetSettings.lastSeenSetupWizardVersion, Is.Empty);
            Assert.That(resetSettings.lastSeenSetupWizardMinimumDispatcherVersion, Is.Empty);
            Assert.That(resetSettings.suppressSetupWizardAutoShow, Is.False);
            Assert.That(resetSettings.showDeveloperTools, Is.EqualTo(settings.showDeveloperTools));
            Assert.That(resetSettings.showToolSettings, Is.EqualTo(settings.showToolSettings));
            Assert.That(resetSettings.installSkillsFlat, Is.EqualTo(settings.installSkillsFlat));
        }

        [Test]
        public void ResetSetupWizardStateIfPackageRemoved_WhenOwnPackageRemoved_ClearsStoredWizardFields()
        {
            // Verifies that the removal handler persists the setup wizard reset.
            UnityCliLoopEditorSettingsData settings = CreateSettingsWithNonWizardPreferences();
            _editorSettingsPort.SaveSettings(settings);

            UnityCliLoopPackageRemovalSettingsResetter.ResetSetupWizardStateIfPackageRemoved(
                _editorSettingsPort,
                new List<string> { UnityCliLoopConstants.PACKAGE_NAME },
                UnityCliLoopConstants.PACKAGE_NAME);

            UnityCliLoopEditorSettingsData updatedSettings = _editorSettingsPort.GetSettings();
            Assert.That(updatedSettings.lastSeenSetupWizardVersion, Is.Empty);
            Assert.That(updatedSettings.lastSeenSetupWizardMinimumDispatcherVersion, Is.Empty);
            Assert.That(updatedSettings.suppressSetupWizardAutoShow, Is.False);
            Assert.That(updatedSettings.showDeveloperTools, Is.EqualTo(settings.showDeveloperTools));
            Assert.That(updatedSettings.showToolSettings, Is.EqualTo(settings.showToolSettings));
            Assert.That(updatedSettings.installSkillsFlat, Is.EqualTo(settings.installSkillsFlat));
        }

        private static UnityCliLoopEditorSettingsData CreateSettingsWithNonWizardPreferences()
        {
            return new UnityCliLoopEditorSettingsData
            {
                showDeveloperTools = true,
                lastSeenSetupWizardVersion = "3.0.0-beta.7",
                lastSeenSetupWizardMinimumDispatcherVersion = "3.0.1-beta.1",
                suppressSetupWizardAutoShow = true,
                showUnityCliLoopSecuritySetting = false,
                showToolSettings = false,
                installSkillsFlat = false
            };
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private static void RestoreFile(string path, bool existed, string content)
        {
            if (existed)
            {
                File.WriteAllText(path, content);
                return;
            }

            DeleteIfExists(path);
        }
    }
}
