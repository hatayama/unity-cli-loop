using System.IO;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies persistence behavior of the project-scoped
    /// (git-shared) Unity CLI Loop settings repository.
    /// </summary>
    [TestFixture]
    public class UnityCliLoopProjectSettingsRepositoryTests
    {
        private string _settingsDirectory;
        private string _settingsFilePath;

        [SetUp]
        public void SetUp()
        {
            _settingsDirectory = Path.Combine(
                Path.GetTempPath(),
                "UnityCliLoopProjectSettingsRepositoryTests_" + Path.GetRandomFileName());
            _settingsFilePath = Path.Combine(
                _settingsDirectory,
                UnityCliLoopConstants.PROJECT_SETTINGS_FILE_NAME);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_settingsDirectory))
            {
                Directory.Delete(_settingsDirectory, true);
            }
        }

        private UnityCliLoopProjectSettingsRepository CreateRepository()
        {
            return new UnityCliLoopProjectSettingsRepository(_settingsDirectory);
        }

        /// <summary>
        /// Verifies that a missing settings file yields the default (not suppressed).
        /// </summary>
        [Test]
        public void GetSuppressSetupWizardAutoShow_ReturnsFalse_WhenFileDoesNotExist()
        {
            IUnityCliLoopProjectSettingsPort repository = CreateRepository();

            Assert.That(repository.GetSuppressSetupWizardAutoShow(), Is.False);
        }

        /// <summary>
        /// Verifies that reading never creates the file, so teammates' working trees stay clean.
        /// </summary>
        [Test]
        public void GetSuppressSetupWizardAutoShow_DoesNotCreateFile_WhenFileDoesNotExist()
        {
            IUnityCliLoopProjectSettingsPort repository = CreateRepository();

            repository.GetSuppressSetupWizardAutoShow();

            Assert.That(File.Exists(_settingsFilePath), Is.False);
        }

        /// <summary>
        /// Verifies that a stored value survives a repository re-instantiation (round-trip through disk).
        /// </summary>
        [Test]
        public void SetSuppressSetupWizardAutoShow_PersistsAcrossInstances()
        {
            IUnityCliLoopProjectSettingsPort writer = CreateRepository();
            writer.SetSuppressSetupWizardAutoShow(true);

            IUnityCliLoopProjectSettingsPort reader = CreateRepository();

            Assert.That(reader.GetSuppressSetupWizardAutoShow(), Is.True);
        }

        /// <summary>
        /// Verifies that setting the flag back to false is persisted.
        /// </summary>
        [Test]
        public void SetSuppressSetupWizardAutoShow_False_OverwritesPreviousTrue()
        {
            IUnityCliLoopProjectSettingsPort repository = CreateRepository();
            repository.SetSuppressSetupWizardAutoShow(true);

            repository.SetSuppressSetupWizardAutoShow(false);

            Assert.That(repository.GetSuppressSetupWizardAutoShow(), Is.False);
        }

        /// <summary>
        /// Verifies that an empty or whitespace-only file falls back to the default instead of failing,
        /// because the file is hand-editable and git-merged.
        /// </summary>
        [Test]
        public void GetSuppressSetupWizardAutoShow_ReturnsFalse_WhenFileIsWhitespace()
        {
            Directory.CreateDirectory(_settingsDirectory);
            File.WriteAllText(_settingsFilePath, "   \n");
            IUnityCliLoopProjectSettingsPort repository = CreateRepository();

            Assert.That(repository.GetSuppressSetupWizardAutoShow(), Is.False);
        }

        /// <summary>
        /// Verifies that an external change to the file (e.g. git pull) is picked up
        /// without recreating the repository.
        /// </summary>
        [Test]
        public void GetSuppressSetupWizardAutoShow_ReflectsExternalFileChange()
        {
            IUnityCliLoopProjectSettingsPort repository = CreateRepository();
            repository.SetSuppressSetupWizardAutoShow(true);

            File.WriteAllText(_settingsFilePath, "{\"suppressSetupWizardAutoShow\":false}");

            Assert.That(repository.GetSuppressSetupWizardAutoShow(), Is.False);
        }
    }
}
