using System.IO;
using System.Collections.Generic;

using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.Presentation;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies Setup Wizard Window behavior.
    /// </summary>
    public class SetupWizardWindowTests
    {
        private static readonly string SettingsFilePath =
            Path.Combine(UnityCliLoopConstants.USER_SETTINGS_FOLDER, UnityCliLoopConstants.SETTINGS_FILE_NAME);

        private bool _settingsFileExisted;
        private string _settingsFileContent;
        private IUnityCliLoopEditorSettingsPort _editorSettingsPort;
        private UnityCliLoopEditorSettingsRepository _editorSettingsRepository;
        private UnityCliLoopEditorSessionStateSnapshot _originalSessionState;

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
            _originalSessionState = UnityCliLoopEditorSessionStateTestFactory.CaptureSnapshot();
            UnityCliLoopEditorSessionStateTestFactory.ClearAll();
            SetupWizardWindow.InitializeEditorServices(
                _editorSettingsPort,
                CreateCliSetupApplicationService(),
                CreateSkillSetupUseCase());
            _editorSettingsRepository.InvalidateCache();
        }

        [TearDown]
        public void TearDown()
        {
            RestoreFile(SettingsFilePath, _settingsFileExisted, _settingsFileContent);
            _editorSettingsRepository.InvalidateCache();
            _originalSessionState.Restore();
        }

        [TestCase("", "1.7.3", "", "3.0.1", false, false, false, true)]
        [TestCase("1.7.2", "1.7.3", "3.0.1", "3.0.1", false, false, false, false)]
        [TestCase("1.7.2", "1.7.3", "3.0.1", "3.0.1", false, true, false, true)]
        [TestCase("1.7.2", "1.7.3", "3.0.1", "3.0.1", false, false, true, true)]
        [TestCase("1.7.4", "1.7.3", "3.0.1", "3.0.1", false, false, true, true)]
        [TestCase("1.7.3", "1.7.3", "3.0.1", "3.0.1", false, true, true, false)]
        [TestCase("1.7.3", "1.7.3", "3.0.1", "3.0.2", false, true, false, true)]
        [TestCase("1.7.3", "1.7.3", "3.0.1", "3.0.2", false, false, false, false)]
        [TestCase("", "1.7.3", "", "3.0.1", true, true, true, false)]
        [TestCase("1.7.2", "1.7.3", "3.0.1", "3.0.1", true, true, true, false)]
        [TestCase("1.7.3", "1.7.3", "3.0.1", "3.0.2", true, true, true, false)]
        public void ShouldAutoShowForVersion_ReturnsExpectedValue(
            string lastSeenVersion,
            string currentVersion,
            string lastSeenMinimumDispatcherVersion,
            string currentMinimumDispatcherVersion,
            bool suppressAutoShow,
            bool needsCliUpdate,
            bool hasSkillUpdate,
            bool expected)
        {
            // Verifies that package and dispatcher requirement changes auto-show only for actionable updates.
            bool shouldAutoShow =
                SetupWizardStartupFlow.ShouldAutoShowForVersion(
                    currentVersion,
                    lastSeenVersion,
                    currentMinimumDispatcherVersion,
                    lastSeenMinimumDispatcherVersion,
                    suppressAutoShow,
                    needsCliUpdate,
                    hasSkillUpdate);

            Assert.That(shouldAutoShow, Is.EqualTo(expected));
        }

        [Test]
        public void HasSkillUpdateForSetupWizard_WhenOutdatedTargetHasSkillsDirectory_ReturnsTrue()
        {
            // Verifies that outdated installed skills request the upgrade-time wizard.
            List<SkillSetupTargetInfo> targets = new()
            {
                CreateSkillTarget(
                    hasSkillsDirectory: true,
                    installState: SkillInstallState.Outdated)
            };

            bool hasSkillUpdate = SetupWizardStartupFlow.HasSkillUpdateForSetupWizard(targets);

            Assert.That(hasSkillUpdate, Is.True);
        }

        [TestCase(SkillInstallState.Installed)]
        [TestCase(SkillInstallState.Missing)]
        [TestCase(SkillInstallState.Checking)]
        public void HasSkillUpdateForSetupWizard_WhenTargetIsNotOutdated_ReturnsFalse(
            SkillInstallState installState)
        {
            // Verifies that non-outdated skill states do not request the upgrade-time wizard.
            List<SkillSetupTargetInfo> targets = new()
            {
                CreateSkillTarget(
                    hasSkillsDirectory: true,
                    installState)
            };

            bool hasSkillUpdate = SetupWizardStartupFlow.HasSkillUpdateForSetupWizard(targets);

            Assert.That(hasSkillUpdate, Is.False);
        }

        [Test]
        public void HasSkillUpdateForSetupWizard_WhenOutdatedTargetHasNoSkillsDirectory_ReturnsFalse()
        {
            // Verifies that missing opt-in skills directories are not treated as skill updates.
            List<SkillSetupTargetInfo> targets = new()
            {
                CreateSkillTarget(
                    hasSkillsDirectory: false,
                    installState: SkillInstallState.Outdated)
            };

            bool hasSkillUpdate = SetupWizardStartupFlow.HasSkillUpdateForSetupWizard(targets);

            Assert.That(hasSkillUpdate, Is.False);
        }

        [Test]
        public void HasSkillUpdateForSetupWizard_WhenTargetHasDifferentLayoutSkills_ReturnsTrue()
        {
            // Verifies that existing skills in the old layout request the upgrade-time wizard.
            List<SkillSetupTargetInfo> targets = new()
            {
                CreateSkillTarget(
                    hasSkillsDirectory: true,
                    installState: SkillInstallState.Missing,
                    hasDifferentLayoutSkills: true)
            };

            bool hasSkillUpdate = SetupWizardStartupFlow.HasSkillUpdateForSetupWizard(targets);

            Assert.That(hasSkillUpdate, Is.True);
        }

        [TestCase("2.1.1", "3.0.0-beta.7", true)]
        [TestCase("1.9.0", "3.0.0", true)]
        [TestCase("", "3.0.0-beta.7", true)]
        [TestCase("", "4.0.0", false)]
        [TestCase("3.0.0-beta.6", "3.0.0-beta.7", false)]
        [TestCase("3.0.0-beta.7", "4.0.0", false)]
        [TestCase("not-a-version", "3.0.0-beta.7", false)]
        public void ShouldAutoScanThirdPartyToolMigration_ReturnsExpectedValue(
            string lastSeenVersion,
            string currentVersion,
            bool expected)
        {
            // Verifies that V3 startup scans run for V2 upgrades or missing prior setup state.
            bool shouldAutoScan =
                SetupWizardStartupFlow.ShouldAutoScanThirdPartyToolMigration(currentVersion, lastSeenVersion);

            Assert.That(shouldAutoScan, Is.EqualTo(expected));
        }

        [TestCase(true, false, 0d, 10d, MigrationAutoScanPollAction.ContinueWaiting)]
        [TestCase(true, true, 0d, 10d, MigrationAutoScanPollAction.ContinueWaiting)]
        [TestCase(false, false, 0d, 10d, MigrationAutoScanPollAction.Terminate)]
        [TestCase(false, true, 0d, 10d, MigrationAutoScanPollAction.RunDetection)]
        [TestCase(false, true, 5d, 10d, MigrationAutoScanPollAction.RunDetection)]
        [TestCase(false, true, 10d, 10d, MigrationAutoScanPollAction.FallBackToFullScan)]
        [TestCase(false, true, 15d, 10d, MigrationAutoScanPollAction.FallBackToFullScan)]
        public void DecideMigrationAutoScanPollAction_ReturnsExpectedAction(
            bool isCompiling,
            bool scriptCompilationFailed,
            double elapsedSeconds,
            double timeoutSeconds,
            MigrationAutoScanPollAction expected)
        {
            // Verifies the pure poll decision function used to replace the unreliable
            // delayCall-based migration auto-scan trigger with an EditorApplication.update poll.
            MigrationAutoScanPollAction action = SetupWizardStartupFlow.DecideMigrationAutoScanPollAction(
                isCompiling,
                scriptCompilationFailed,
                elapsedSeconds,
                timeoutSeconds);

            Assert.That(action, Is.EqualTo(expected));
        }

        [Test]
        public void MaybeRecordLastSeenSetupWizardState_WhenAutoShow_UpdatesStoredState()
        {
            // Verifies that auto-show records the setup wizard version state.
            _editorSettingsPort.SaveSettings(new UnityCliLoopEditorSettingsData
            {
                lastSeenSetupWizardVersion = "1.7.2",
                lastSeenSetupWizardMinimumDispatcherVersion = "3.0.1"
            });

            SetupWizardStartupFlow.MaybeRecordLastSeenSetupWizardState(
                _editorSettingsPort,
                true,
                "1.7.3",
                "3.0.2");

            Assert.That(_editorSettingsPort.GetLastSeenSetupWizardVersion(), Is.EqualTo("1.7.3"));
            Assert.That(
                _editorSettingsPort.GetSettings().lastSeenSetupWizardMinimumDispatcherVersion,
                Is.EqualTo("3.0.2"));
        }

        [Test]
        public void MaybeRecordLastSeenSetupWizardState_WhenManualShow_KeepsStoredState()
        {
            // Verifies that manual opens do not update the setup wizard version state.
            _editorSettingsPort.SaveSettings(new UnityCliLoopEditorSettingsData
            {
                lastSeenSetupWizardVersion = "1.7.2",
                lastSeenSetupWizardMinimumDispatcherVersion = "3.0.1"
            });

            SetupWizardStartupFlow.MaybeRecordLastSeenSetupWizardState(
                _editorSettingsPort,
                false,
                "1.7.3",
                "3.0.2");

            Assert.That(_editorSettingsPort.GetLastSeenSetupWizardVersion(), Is.EqualTo("1.7.2"));
            Assert.That(
                _editorSettingsPort.GetSettings().lastSeenSetupWizardMinimumDispatcherVersion,
                Is.EqualTo("3.0.1"));
        }

        [Test]
        public void MaybeRecordSuppressedSetupWizardState_WhenAutoShowSuppressed_UpdatesStoredState()
        {
            // Verifies that suppressing auto-show records the current setup wizard state.
            _editorSettingsPort.SaveSettings(new UnityCliLoopEditorSettingsData
            {
                lastSeenSetupWizardVersion = "1.7.2",
                lastSeenSetupWizardMinimumDispatcherVersion = "3.0.1"
            });

            SetupWizardStartupFlow.MaybeRecordSuppressedSetupWizardState(
                _editorSettingsPort,
                true,
                "1.7.3",
                "3.0.2");

            Assert.That(_editorSettingsPort.GetLastSeenSetupWizardVersion(), Is.EqualTo("1.7.3"));
            Assert.That(
                _editorSettingsPort.GetSettings().lastSeenSetupWizardMinimumDispatcherVersion,
                Is.EqualTo("3.0.2"));
        }

        [Test]
        public void MaybeRecordSuppressedSetupWizardState_WhenAutoShowAllowed_KeepsStoredState()
        {
            // Verifies that allowing auto-show leaves the stored setup wizard state unchanged.
            _editorSettingsPort.SaveSettings(new UnityCliLoopEditorSettingsData
            {
                lastSeenSetupWizardVersion = "1.7.2",
                lastSeenSetupWizardMinimumDispatcherVersion = "3.0.1"
            });

            SetupWizardStartupFlow.MaybeRecordSuppressedSetupWizardState(
                _editorSettingsPort,
                false,
                "1.7.3",
                "3.0.2");

            Assert.That(_editorSettingsPort.GetLastSeenSetupWizardVersion(), Is.EqualTo("1.7.2"));
            Assert.That(
                _editorSettingsPort.GetSettings().lastSeenSetupWizardMinimumDispatcherVersion,
                Is.EqualTo("3.0.1"));
        }

        [Test]
        public void TryReuseOpenWindow_WhenExistingWindowAndAutoShow_FocusesWindowAndRecordsVersion()
        {
            bool focusedExistingWindow = false;
            _editorSettingsPort.SaveSettings(new UnityCliLoopEditorSettingsData
            {
                lastSeenSetupWizardVersion = "1.7.2",
                lastSeenSetupWizardMinimumDispatcherVersion = "3.0.1"
            });

            bool reused = SetupWizardWindow.TryReuseOpenWindow(
                hasOpenWindow: true,
                shouldRecordVersion: true,
                currentVersion: "1.7.3",
                currentMinimumDispatcherVersion: "3.0.2",
                focusExistingWindow: () => focusedExistingWindow = true);

            Assert.That(reused, Is.True);
            Assert.That(focusedExistingWindow, Is.True);
            Assert.That(_editorSettingsPort.GetLastSeenSetupWizardVersion(), Is.EqualTo("1.7.3"));
            Assert.That(
                _editorSettingsPort.GetSettings().lastSeenSetupWizardMinimumDispatcherVersion,
                Is.EqualTo("3.0.2"));
        }

        [Test]
        public void TryReuseOpenWindow_WhenExistingWindowAndManualShow_FocusesWindowWithoutRecordingVersion()
        {
            bool focusedExistingWindow = false;
            _editorSettingsPort.SaveSettings(new UnityCliLoopEditorSettingsData
            {
                lastSeenSetupWizardVersion = "1.7.2",
                lastSeenSetupWizardMinimumDispatcherVersion = "3.0.1"
            });

            bool reused = SetupWizardWindow.TryReuseOpenWindow(
                hasOpenWindow: true,
                shouldRecordVersion: false,
                currentVersion: "1.7.3",
                currentMinimumDispatcherVersion: "3.0.2",
                focusExistingWindow: () => focusedExistingWindow = true);

            Assert.That(reused, Is.True);
            Assert.That(focusedExistingWindow, Is.True);
            Assert.That(_editorSettingsPort.GetLastSeenSetupWizardVersion(), Is.EqualTo("1.7.2"));
            Assert.That(
                _editorSettingsPort.GetSettings().lastSeenSetupWizardMinimumDispatcherVersion,
                Is.EqualTo("3.0.1"));
        }

        [Test]
        public void TryReuseOpenWindow_WhenNoExistingWindow_DoesNotFocusOrRecordVersion()
        {
            bool focusedExistingWindow = false;
            _editorSettingsPort.SaveSettings(new UnityCliLoopEditorSettingsData
            {
                lastSeenSetupWizardVersion = "1.7.2",
                lastSeenSetupWizardMinimumDispatcherVersion = "3.0.1"
            });

            bool reused = SetupWizardWindow.TryReuseOpenWindow(
                hasOpenWindow: false,
                shouldRecordVersion: true,
                currentVersion: "1.7.3",
                currentMinimumDispatcherVersion: "3.0.2",
                focusExistingWindow: () => focusedExistingWindow = true);

            Assert.That(reused, Is.False);
            Assert.That(focusedExistingWindow, Is.False);
            Assert.That(_editorSettingsPort.GetLastSeenSetupWizardVersion(), Is.EqualTo("1.7.2"));
            Assert.That(
                _editorSettingsPort.GetSettings().lastSeenSetupWizardMinimumDispatcherVersion,
                Is.EqualTo("3.0.1"));
        }

        [Test]
        public void WithContentSize_OverridesSizeAndPreservesCenter()
        {
            Rect initialRect = new(123f, 456f, 789f, 321f);
            Vector2 contentSize = new(350f, 280f);
            Vector2 frameSize = new(18f, 28f);

            Rect resizedRect = SetupWizardWindowResizer.WithContentSize(initialRect, contentSize, frameSize);

            Assert.That(resizedRect.center, Is.EqualTo(initialRect.center));
            Assert.That(resizedRect.size, Is.EqualTo(new Vector2(368f, 380f)));
        }

        [Test]
        public void WithContentSize_WhenMeasuredSizeIsTooSmall_ClampsToMinimumWindowSize()
        {
            Rect initialRect = new(123f, 456f, 520f, 480f);
            Vector2 contentSize = new(120f, 140f);
            Vector2 frameSize = new(18f, 28f);

            Rect resizedRect = SetupWizardWindowResizer.WithContentSize(initialRect, contentSize, frameSize);

            Assert.That(resizedRect.center, Is.EqualTo(initialRect.center));
            Assert.That(resizedRect.size, Is.EqualTo(new Vector2(360f, 380f)));
        }

        [Test]
        public void CreateCenteredRect_CentersRectWithinBounds()
        {
            Rect bounds = new(100f, 200f, 900f, 700f);
            Vector2 size = new(300f, 250f);

            Rect centeredRect = SetupWizardWindowResizer.CreateCenteredRect(bounds, size);

            Assert.That(centeredRect.center, Is.EqualTo(bounds.center));
            Assert.That(centeredRect.size, Is.EqualTo(size));
        }

        [Test]
        public void GetGitHubRepositoryUrl_ReturnsProjectRepositoryUrl()
        {
            string repositoryUrl = SetupWizardWindow.GetGitHubRepositoryUrl();

            Assert.That(repositoryUrl, Is.EqualTo("https://github.com/hatayama/unity-cli-loop"));
        }

        [Test]
        public void PrepareForOpen_PopulatesWindowStateBeforeShowing()
        {
            // Verifies PrepareForOpen writes title, position, and record-version flag before Show.
            SetupWizardWindow window = ScriptableObject.CreateInstance<SetupWizardWindow>();
            try
            {
                Rect position = new(12f, 34f, 360f, 380f);

                SetupWizardWindow.PrepareForOpen(
                    window,
                    "Unity CLI Loop Setup",
                    position,
                    true);

                SerializedObject serializedWindow = new(window);
                SerializedProperty recordVersionProperty =
                    serializedWindow.FindProperty("_shouldRecordLastSeenVersionAfterCreateGui");

                Assert.That(window.titleContent.text, Is.EqualTo("Unity CLI Loop Setup"));
                Assert.That(window.position, Is.EqualTo(position));
                Assert.That(recordVersionProperty, Is.Not.Null);
                Assert.That(recordVersionProperty.boolValue, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void CanManageSkills_WhenCliIsMissing_ReturnsFalse()
        {
            bool canManageSkills = SetupWizardWindow.CanManageSkills(cliInstalled: false);

            Assert.That(canManageSkills, Is.False);
        }

        [Test]
        public void CanManageSkills_WhenCliIsInstalled_ReturnsTrue()
        {
            bool canManageSkills = SetupWizardWindow.CanManageSkills(cliInstalled: true);

            Assert.That(canManageSkills, Is.True);
        }

        [TestCase(false, false, false, false, false, false, null, "3.0.0", "Install CLI")]
        [TestCase(false, false, false, false, true, false, null, "3.0.0", "Fix PATH")]
        [TestCase(true, false, false, false, false, false, "3.0.0", "3.0.0", "Installed")]
        [TestCase(true, false, false, false, true, false, "3.0.0", "3.0.0", "Fix PATH")]
        [TestCase(true, false, false, true, false, false, "2.9.0", "3.0.0", "Update CLI (v2.9.0 \u2192 v3.0.0)")]
        [TestCase(true, false, false, true, true, false, "2.9.0", "3.0.0", "Update CLI (v2.9.0 \u2192 v3.0.0)")]
        [TestCase(true, false, false, true, false, false, "3.0.0", "3.0.0", "Update CLI (v3.0.0 required)")]
        [TestCase(true, true, false, false, false, false, "3.0.0", "3.0.0", "Installing...")]
        [TestCase(true, true, false, false, true, false, "3.0.0", "3.0.0", "Fixing PATH...")]
        [TestCase(false, false, true, false, false, false, null, "3.0.0", "Checking...")]
        [TestCase(true, false, false, false, false, true, "3.0.0", "3.0.0", "Managed by Homebrew")]
        [TestCase(true, false, false, true, false, true, "2.9.0", "3.0.0", "Managed by Homebrew")]
        [TestCase(false, false, false, false, false, true, null, "3.0.0", "Managed by Homebrew")]
        [TestCase(true, false, false, false, true, true, "3.0.0", "3.0.0", "Fix PATH")]
        [TestCase(true, true, false, false, true, true, "3.0.0", "3.0.0", "Fixing PATH...")]
        public void GetCliButtonTextForSetupWizard_ReturnsExpectedLabel(
            bool cliInstalled,
            bool isInstallingCli,
            bool isChecking,
            bool needsUpdate,
            bool needsCliPathSetup,
            bool isHomebrewManagedCli,
            string cliVersion,
            string requiredCliVersion,
            string expectedLabel)
        {
            string label = SetupWizardCliStepPresenter.GetCliButtonTextForSetupWizard(
                cliInstalled,
                isInstallingCli,
                isChecking,
                needsUpdate,
                needsCliPathSetup,
                isHomebrewManagedCli,
                cliVersion,
                requiredCliVersion);

            Assert.That(label, Is.EqualTo(expectedLabel));
        }

        [TestCase(false, false, null, "3.0.0", "Not installed")]
        [TestCase(true, true, "3.0.0", "3.0.0", "v3.0.0")]
        [TestCase(true, false, "2.9.0", "3.0.0", "v2.9.0 (requires v3.0.0)")]
        [TestCase(true, false, "3.0.0", "3.0.0", "v3.0.0 (update required)")]
        public void GetCliStatusTextForSetupWizard_ReturnsExpectedLabel(
            bool cliInstalled,
            bool cliCompatible,
            string cliVersion,
            string requiredCliVersion,
            string expectedLabel)
        {
            // Verifies that same-version replacement prompts do not expose dispatcher internals.
            string label = SetupWizardCliStepPresenter.GetCliStatusTextForSetupWizard(
                cliInstalled,
                cliCompatible,
                cliVersion,
                requiredCliVersion);

            Assert.That(label, Is.EqualTo(expectedLabel));
        }

        [TestCase(
            true,
            "2.9.0",
            true,
            "Homebrew-managed CLI v2.9.0 does not meet the required v3.0.0.\n"
            + "Run this command in your terminal:\nbrew upgrade uloop")]
        [TestCase(
            true,
            null,
            true,
            "Homebrew-managed CLI did not report a version.\n"
            + "Run this command in your terminal:\nbrew reinstall uloop")]
        [TestCase(true, "3.0.0", false, "")]
        [TestCase(false, "2.9.0", false, "")]
        [TestCase(false, null, false, "")]
        public void Update_TogglesHomebrewUpgradeWarning(
            bool isHomebrewManagedCli,
            string cliVersion,
            bool expectedVisible,
            string expectedText)
        {
            // Verifies the wizard explains every unusable Homebrew CLI, and stays silent otherwise.
            VisualElement statusIcon = new();
            Label statusLabel = new();
            Label homebrewUpgradeMessage = new() { name = "cli-homebrew-upgrade-message" };
            Button installButton = new();
            SetupWizardCliStepPresenter presenter = new(
                statusIcon,
                statusLabel,
                homebrewUpgradeMessage,
                installButton,
                () => { });

            presenter.Update(
                cliInstalled: !string.IsNullOrEmpty(cliVersion),
                cliVersion,
                cliIsDispatcher: true,
                requiredCliVersion: "3.0.0",
                isInstallingCli: false,
                needsCliPathSetup: false,
                isHomebrewManagedCli);

            Assert.That(
                homebrewUpgradeMessage.ClassListContains("setup-warning-message--visible"),
                Is.EqualTo(expectedVisible));
            Assert.That(homebrewUpgradeMessage.text, Is.EqualTo(expectedText));
        }

        [TestCase(false, false, false, false, false, false, true)]
        [TestCase(true, false, true, false, false, false, true)]
        [TestCase(true, false, false, false, false, false, true)]
        [TestCase(true, true, false, false, false, false, false)]
        [TestCase(false, false, false, true, false, false, false)]
        [TestCase(false, false, false, false, true, false, false)]
        [TestCase(true, false, false, false, false, true, false)]
        [TestCase(false, false, false, false, false, true, false)]
        [TestCase(true, false, true, false, false, true, true)]
        public void IsCliButtonEnabledForSetupWizard_ReturnsExpectedValue(
            bool cliInstalled,
            bool cliVersionMatched,
            bool needsCliPathSetup,
            bool isInstallingCli,
            bool isChecking,
            bool isHomebrewManagedCli,
            bool expectedEnabled)
        {
            // Verifies a Homebrew-managed CLI leaves only PATH repair, which writes no binary, enabled.
            bool enabled = SetupWizardCliStepPresenter.IsCliButtonEnabledForSetupWizard(
                cliInstalled,
                cliVersionMatched,
                needsCliPathSetup,
                isInstallingCli,
                isChecking,
                isHomebrewManagedCli);

            Assert.That(enabled, Is.EqualTo(expectedEnabled));
        }

        [TestCase(false, false, false)]
        [TestCase(true, false, true)]
        [TestCase(true, true, false)]
        public void ShouldRepairCliPathFromPrimaryButton_ReturnsExpectedAction(
            bool needsCliPathSetup,
            bool needsUpdate,
            bool expected)
        {
            // Verifies that setup wizard chooses PATH repair only after the CLI version is already usable.
            bool result = SetupWizardWindow.ShouldRepairCliPathFromPrimaryButton(
                needsCliPathSetup,
                needsUpdate);

            Assert.That(result, Is.EqualTo(expected));
        }

        [TestCase(RuntimePlatform.OSXEditor, true, true)]
        [TestCase(RuntimePlatform.OSXEditor, false, false)]
        [TestCase(RuntimePlatform.WindowsEditor, true, false)]
        public void ShouldCheckCliPathSetupForSetupWizard_RequiresPackageOwnedCli(
            RuntimePlatform platform,
            bool hasPackageOwnedCurrentUserInstall,
            bool expected)
        {
            // Verifies that setup wizard only repairs PATH for package-owned POSIX CLIs.
            bool result = SetupWizardWindow.ShouldCheckCliPathSetupForSetupWizard(
                platform,
                hasPackageOwnedCurrentUserInstall);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void ShouldShowSkillsInstalledDialog_WhenTargetsAreMissing_ReturnsTrue()
        {
            // Verifies that Setup Wizard keeps the success dialog for first install.
            List<SkillSetupTargetInfo> targets = new()
            {
                CreateSkillTarget(
                    hasSkillsDirectory: true,
                    SkillInstallState.Missing)
            };

            bool shouldShowDialog = SetupWizardWindow.ShouldShowSkillsInstalledDialog(targets);

            Assert.That(shouldShowDialog, Is.True);
        }

        [Test]
        public void ShouldShowSkillsInstalledDialog_WhenAnyTargetIsOutdated_ReturnsFalse()
        {
            // Verifies that Setup Wizard suppresses the success dialog for skill updates.
            List<SkillSetupTargetInfo> targets = new()
            {
                CreateSkillTarget(
                    hasSkillsDirectory: true,
                    SkillInstallState.Missing),
                CreateSkillTarget(
                    hasSkillsDirectory: true,
                    SkillInstallState.Outdated)
            };

            bool shouldShowDialog = SetupWizardWindow.ShouldShowSkillsInstalledDialog(targets);

            Assert.That(shouldShowDialog, Is.False);
        }

        [Test]
        public void ShouldShowSkillsInstalledDialog_WhenAnyTargetUsesDifferentLayout_ReturnsFalse()
        {
            // Verifies that Setup Wizard suppresses the success dialog for skill layout updates.
            List<SkillSetupTargetInfo> targets = new()
            {
                CreateSkillTarget(
                    hasSkillsDirectory: true,
                    SkillInstallState.Missing,
                    hasDifferentLayoutSkills: true)
            };

            bool shouldShowDialog = SetupWizardWindow.ShouldShowSkillsInstalledDialog(targets);

            Assert.That(shouldShowDialog, Is.False);
        }

        [Test]
        public void EstimateWrappedLineCount_WithPositiveHeight_ReturnsRoundedLineCount()
        {
            int lineCount = SetupWizardWindowResizer.EstimateWrappedLineCount(35f, 12f);

            Assert.That(lineCount, Is.EqualTo(3));
        }

        [Test]
        public void SelectPreferredTextWidth_WhenWrappedAcrossManyLines_UsesTwoLineTarget()
        {
            float preferredWidth = SetupWizardWindowResizer.SelectPreferredTextWidth(120f, 320f, 4, WhiteSpace.Normal);

            Assert.That(preferredWidth, Is.EqualTo(160f));
        }

        [Test]
        public void SelectPreferredTextWidth_WhenWrappedAcrossTwoLines_KeepsLaidOutWidth()
        {
            float preferredWidth = SetupWizardWindowResizer.SelectPreferredTextWidth(180f, 320f, 2, WhiteSpace.Normal);

            Assert.That(preferredWidth, Is.EqualTo(180f));
        }

        [Test]
        public void SelectPreferredTextWidth_WhenShorterTextFitsWithinCurrentWidth_ShrinksToMeasuredWidth()
        {
            float preferredWidth = SetupWizardWindowResizer.SelectPreferredTextWidth(420f, 180f, 1, WhiteSpace.Normal);

            Assert.That(preferredWidth, Is.EqualTo(180f));
        }

        [Test]
        public void SelectPreferredTextWidth_WhenTextDoesNotWrap_UsesMeasuredWidth()
        {
            float preferredWidth = SetupWizardWindowResizer.SelectPreferredTextWidth(180f, 320f, 1, WhiteSpace.NoWrap);

            Assert.That(preferredWidth, Is.EqualTo(320f));
        }

        [Test]
        public void HasFiniteSize_WhenVectorContainsNaN_ReturnsFalse()
        {
            bool hasFiniteSize = SetupWizardWindowResizer.HasFiniteSize(new Vector2(float.NaN, 120f));

            Assert.That(hasFiniteSize, Is.False);
        }

        [Test]
        public void HasFiniteSize_WhenVectorContainsFiniteValues_ReturnsTrue()
        {
            bool hasFiniteSize = SetupWizardWindowResizer.HasFiniteSize(new Vector2(240f, 120f));

            Assert.That(hasFiniteSize, Is.True);
        }

        private static SkillSetupTargetInfo CreateSkillTarget(
            bool hasSkillsDirectory,
            SkillInstallState installState,
            bool hasDifferentLayoutSkills = false)
        {
            return new SkillSetupTargetInfo(
                "Claude Code",
                ".claude",
                "--claude",
                hasSkillsDirectory,
                hasExistingSkills: hasSkillsDirectory,
                hasDifferentLayoutSkills,
                installState);
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

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private static CliSetupApplicationService CreateCliSetupApplicationService()
        {
            CliPinReaderService cliPinReaderService = new();
            return new CliSetupApplicationService(
                new CliInstallationDetector(cliPinReaderService),
                new NativeCliInstallerService(),
                cliPinReaderService);
        }

        private static SkillSetupUseCase CreateSkillSetupUseCase()
        {
            return new SkillSetupUseCase(new ToolSkillSetupService(new ToolSettingsRepository()));
        }
    }
}
