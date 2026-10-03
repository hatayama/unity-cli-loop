using System;
using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies the package-removal reset through a fake settings port, without touching the project's settings file.
    /// </summary>
    public sealed class UnityCliLoopPackageRemovalSettingsResetterPortTests
    {
        /// <summary>
        /// Verifies that removing an unrelated package does not update the stored editor settings.
        /// </summary>
        [Test]
        public void ResetSetupWizardStateIfPackageRemoved_WhenOtherPackageRemoved_DoesNotUpdateSettings()
        {
            CountingEditorSettingsPort editorSettingsPort = new CountingEditorSettingsPort();

            UnityCliLoopPackageRemovalSettingsResetter.ResetSetupWizardStateIfPackageRemoved(
                editorSettingsPort,
                new List<string> { "com.example.other-package" },
                "com.example.own-package");

            Assert.That(editorSettingsPort.UpdateSettingsCallCount, Is.EqualTo(0));
        }

        private sealed class CountingEditorSettingsPort : IUnityCliLoopEditorSettingsPort
        {
            public int UpdateSettingsCallCount { get; private set; }

            public void RecoverSettingsFileIfNeeded()
            {
            }

            public UnityCliLoopEditorSettingsData GetSettings()
            {
                return new UnityCliLoopEditorSettingsData();
            }

            public void SaveSettings(UnityCliLoopEditorSettingsData settings)
            {
            }

            public void UpdateSettings(Func<UnityCliLoopEditorSettingsData, UnityCliLoopEditorSettingsData> transform)
            {
                UpdateSettingsCallCount++;
            }

            public string GetLastSeenSetupWizardVersion()
            {
                return string.Empty;
            }

            public bool GetSuppressSetupWizardAutoShow()
            {
                return false;
            }

            public void SetSuppressSetupWizardAutoShow(bool suppressAutoShow)
            {
            }

            public void SetShowToolSettings(bool showToolSettings)
            {
            }

            public void SetInstallSkillsFlat(bool installSkillsFlat)
            {
            }
        }
    }
}
