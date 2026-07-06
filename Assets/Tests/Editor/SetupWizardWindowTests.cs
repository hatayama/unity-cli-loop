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
        private UnityCliLoopSessionFlagsRepository _sessionFlagsRepository;
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
            _sessionFlagsRepository = UnityCliLoopEditorSessionStateTestFactory.CreateSessionFlagsRepository();
            _originalSessionState = UnityCliLoopEditorSessionStateTestFactory.CaptureSnapshot();
            UnityCliLoopEditorSessionStateTestFactory.ClearAll();
            SetupWizardWindow.InitializeEditorServices(
                _editorSettingsPort,
                _sessionFlagsRepository,
                CreateCliSetupApplicationService());
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
                SetupWizardWindow.ShouldAutoShowForVersion(
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

            bool hasSkillUpdate = SetupWizardWindow.HasSkillUpdateForSetupWizard(targets);

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

            bool hasSkillUpdate = SetupWizardWindow.HasSkillUpdateForSetupWizard(targets);

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

            bool hasSkillUpdate = SetupWizardWindow.HasSkillUpdateForSetupWizard(targets);

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

            bool hasSkillUpdate = SetupWizardWindow.HasSkillUpdateForSetupWizard(targets);

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
                SetupWizardWindow.ShouldAutoScanThirdPartyToolMigration(currentVersion, lastSeenVersion);

            Assert.That(shouldAutoScan, Is.EqualTo(expected));
        }

        [Test]
        public void MaybeMarkThirdPartyToolMigrationAutoScan_WhenEnabled_SetsSessionFlag()
        {
            // Verifies that the V2-to-V3 upgrade signal is stored only in the current Editor session.
            SetupWizardWindow.MaybeMarkThirdPartyToolMigrationAutoScan(true);

            Assert.That(_sessionFlagsRepository.GetShouldAutoScanThirdPartyToolMigration(), Is.True);
        }

        [Test]
        public void MaybeMarkThirdPartyToolMigrationAutoScan_WhenDisabled_KeepsSessionFlagFalse()
        {
            // Verifies that non-upgrade version checks do not request migration scans.
            SetupWizardWindow.MaybeMarkThirdPartyToolMigrationAutoScan(false);

            Assert.That(_sessionFlagsRepository.GetShouldAutoScanThirdPartyToolMigration(), Is.False);
        }

        [Test]
        public void MaybeRecordLastSeenSetupWizardState_WhenAutoShow_UpdatesStoredState()
        {
            _editorSettingsPort.SaveSettings(new UnityCliLoopEditorSettingsData
            {
                lastSeenSetupWizardVersion = "1.7.2",
                lastSeenSetupWizardMinimumDispatcherVersion = "3.0.1"
            });

            SetupWizardWindow.MaybeRecordLastSeenSetupWizardState(true, "1.7.3", "3.0.2");

            Assert.That(_editorSettingsPort.GetLastSeenSetupWizardVersion(), Is.EqualTo("1.7.3"));
            Assert.That(
                _editorSettingsPort.GetSettings().lastSeenSetupWizardMinimumDispatcherVersion,
                Is.EqualTo("3.0.2"));
        }

        [Test]
        public void MaybeRecordLastSeenSetupWizardState_WhenManualShow_KeepsStoredState()
        {
            _editorSettingsPort.SaveSettings(new UnityCliLoopEditorSettingsData
            {
                lastSeenSetupWizardVersion = "1.7.2",
                lastSeenSetupWizardMinimumDispatcherVersion = "3.0.1"
            });

            SetupWizardWindow.MaybeRecordLastSeenSetupWizardState(false, "1.7.3", "3.0.2");

            Assert.That(_editorSettingsPort.GetLastSeenSetupWizardVersion(), Is.EqualTo("1.7.2"));
            Assert.That(
                _editorSettingsPort.GetSettings().lastSeenSetupWizardMinimumDispatcherVersion,
                Is.EqualTo("3.0.1"));
        }

        [Test]
        public void MaybeRecordSuppressedSetupWizardState_WhenAutoShowSuppressed_UpdatesStoredState()
        {
            _editorSettingsPort.SaveSettings(new UnityCliLoopEditorSettingsData
            {
                lastSeenSetupWizardVersion = "1.7.2",
                lastSeenSetupWizardMinimumDispatcherVersion = "3.0.1"
            });

            SetupWizardWindow.MaybeRecordSuppressedSetupWizardState(true, "1.7.3", "3.0.2");

            Assert.That(_editorSettingsPort.GetLastSeenSetupWizardVersion(), Is.EqualTo("1.7.3"));
            Assert.That(
                _editorSettingsPort.GetSettings().lastSeenSetupWizardMinimumDispatcherVersion,
                Is.EqualTo("3.0.2"));
        }

        [Test]
        public void MaybeRecordSuppressedSetupWizardState_WhenAutoShowAllowed_KeepsStoredState()
        {
            _editorSettingsPort.SaveSettings(new UnityCliLoopEditorSettingsData
            {
                lastSeenSetupWizardVersion = "1.7.2",
                lastSeenSetupWizardMinimumDispatcherVersion = "3.0.1"
            });

            SetupWizardWindow.MaybeRecordSuppressedSetupWizardState(false, "1.7.3", "3.0.2");

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

            Rect resizedRect = SetupWizardWindow.WithContentSize(initialRect, contentSize, frameSize);

            Assert.That(resizedRect.center, Is.EqualTo(initialRect.center));
            Assert.That(resizedRect.size, Is.EqualTo(new Vector2(368f, 380f)));
        }

        [Test]
        public void WithContentSize_WhenMeasuredSizeIsTooSmall_ClampsToMinimumWindowSize()
        {
            Rect initialRect = new(123f, 456f, 520f, 480f);
            Vector2 contentSize = new(120f, 140f);
            Vector2 frameSize = new(18f, 28f);

            Rect resizedRect = SetupWizardWindow.WithContentSize(initialRect, contentSize, frameSize);

            Assert.That(resizedRect.center, Is.EqualTo(initialRect.center));
            Assert.That(resizedRect.size, Is.EqualTo(new Vector2(360f, 380f)));
        }

        [Test]
        public void CreateCenteredRect_CentersRectWithinBounds()
        {
            Rect bounds = new(100f, 200f, 900f, 700f);
            Vector2 size = new(300f, 250f);

            Rect centeredRect = SetupWizardWindow.CreateCenteredRect(bounds, size);

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
            SetupWizardWindow window = ScriptableObject.CreateInstance<SetupWizardWindow>();
            try
            {
                Rect position = new(12f, 34f, 360f, 380f);

                SetupWizardWindow.PrepareForOpen(
                    window,
                    "Unity CLI Loop Setup",
                    position,
                    "1.9.0",
                    true);

                SerializedObject serializedWindow = new(window);
                SerializedProperty lastSeenVersionProperty =
                    serializedWindow.FindProperty("_lastSeenSetupWizardVersionBeforeOpen");
                SerializedProperty recordVersionProperty =
                    serializedWindow.FindProperty("_shouldRecordLastSeenVersionAfterCreateGui");

                Assert.That(window.titleContent.text, Is.EqualTo("Unity CLI Loop Setup"));
                Assert.That(window.position, Is.EqualTo(position));
                Assert.That(lastSeenVersionProperty, Is.Not.Null);
                Assert.That(lastSeenVersionProperty.stringValue, Is.EqualTo("1.9.0"));
                Assert.That(recordVersionProperty, Is.Not.Null);
                Assert.That(recordVersionProperty.boolValue, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void FilterInstallableSkillTargets_ExcludesTargetsWithoutSkillsDirectory()
        {
            List<SkillSetupTargetInfo> targets = new()
            {
                new("Claude Code", ".claude", "--claude", true, true),
                new("Cursor", ".cursor", "--cursor", false, false),
                new("Codex CLI", ".codex", "--codex", true, false, hasDifferentLayoutSkills: true)
            };

            List<SkillSetupTargetInfo> installableTargets =
                SetupWizardWindow.FilterInstallableSkillTargets(targets);

            Assert.That(installableTargets.Count, Is.EqualTo(2));
            Assert.That(installableTargets[0].DirName, Is.EqualTo(".claude"));
            Assert.That(installableTargets[1].DirName, Is.EqualTo(".codex"));
        }

        [Test]
        public void ShouldUseFirstInstallSkillsUi_WhenVersionWasNeverSeen_ReturnsTrue()
        {
            bool shouldUseFirstInstallUi = SetupWizardWindow.ShouldUseFirstInstallSkillsUi("");

            Assert.That(shouldUseFirstInstallUi, Is.True);
        }

        [Test]
        public void ShouldUseFirstInstallSkillsUi_WhenVersionWasSeen_ReturnsFalse()
        {
            bool shouldUseFirstInstallUi = SetupWizardWindow.ShouldUseFirstInstallSkillsUi("1.9.0");

            Assert.That(shouldUseFirstInstallUi, Is.False);
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

        [Test]
        public void ShouldShowSkillsTargetRow_WhenFirstInstallAndCliMissing_ReturnsTrue()
        {
            // Verifies that first-time setup can choose a skill target before CLI installation.
            bool shouldShow = SetupWizardWindow.ShouldShowSkillsTargetRowForSetupWizard(
                shouldUseFirstInstallSkillsUi: true);

            Assert.That(shouldShow, Is.True);
        }

        [Test]
        public void ShouldShowSkillsTargetRow_WhenNotFirstInstall_ReturnsFalse()
        {
            // Verifies that returning setup keeps the compact target row hidden.
            bool shouldShow = SetupWizardWindow.ShouldShowSkillsTargetRowForSetupWizard(
                shouldUseFirstInstallSkillsUi: false);

            Assert.That(shouldShow, Is.False);
        }

        [Test]
        public void ShouldShowSkillsTargetList_WhenCliMissing_ReturnsFalse()
        {
            // Verifies that multi-target status rows stay hidden until the CLI can inspect skill state.
            bool shouldShow = SetupWizardWindow.ShouldShowSkillsTargetListForSetupWizard(
                canManageSkills: false,
                shouldUseFirstInstallSkillsUi: false);

            Assert.That(shouldShow, Is.False);
        }

        [Test]
        public void ShouldShowSkillsTargetList_WhenCliInstalledAndNotFirstInstall_ReturnsTrue()
        {
            // Verifies that returning users keep the multi-target skill status view.
            bool shouldShow = SetupWizardWindow.ShouldShowSkillsTargetListForSetupWizard(
                canManageSkills: true,
                shouldUseFirstInstallSkillsUi: false);

            Assert.That(shouldShow, Is.True);
        }

        [TestCase(false, false, false, false, false, null, "3.0.0", "Install CLI")]
        [TestCase(false, false, false, false, true, null, "3.0.0", "Fix PATH")]
        [TestCase(true, false, false, false, false, "3.0.0", "3.0.0", "Installed")]
        [TestCase(true, false, false, false, true, "3.0.0", "3.0.0", "Fix PATH")]
        [TestCase(true, false, false, true, false, "2.9.0", "3.0.0", "Update CLI (v2.9.0 \u2192 v3.0.0)")]
        [TestCase(true, false, false, true, true, "2.9.0", "3.0.0", "Update CLI (v2.9.0 \u2192 v3.0.0)")]
        [TestCase(true, false, false, true, false, "3.0.0", "3.0.0", "Update CLI (v3.0.0 required)")]
        [TestCase(true, true, false, false, false, "3.0.0", "3.0.0", "Installing...")]
        [TestCase(true, true, false, false, true, "3.0.0", "3.0.0", "Fixing PATH...")]
        [TestCase(false, false, true, false, false, null, "3.0.0", "Checking...")]
        public void GetCliButtonTextForSetupWizard_ReturnsExpectedLabel(
            bool cliInstalled,
            bool isInstallingCli,
            bool isChecking,
            bool needsUpdate,
            bool needsCliPathSetup,
            string cliVersion,
            string requiredCliVersion,
            string expectedLabel)
        {
            string label = SetupWizardWindow.GetCliButtonTextForSetupWizard(
                cliInstalled,
                isInstallingCli,
                isChecking,
                needsUpdate,
                needsCliPathSetup,
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
            string label = SetupWizardWindow.GetCliStatusTextForSetupWizard(
                cliInstalled,
                cliCompatible,
                cliVersion,
                requiredCliVersion);

            Assert.That(label, Is.EqualTo(expectedLabel));
        }

        [TestCase(false, false, false, false, false, true)]
        [TestCase(true, false, true, false, false, true)]
        [TestCase(true, false, false, false, false, true)]
        [TestCase(true, true, false, false, false, false)]
        [TestCase(false, false, false, true, false, false)]
        [TestCase(false, false, false, false, true, false)]
        public void IsCliButtonEnabledForSetupWizard_ReturnsExpectedValue(
            bool cliInstalled,
            bool cliVersionMatched,
            bool needsCliPathSetup,
            bool isInstallingCli,
            bool isChecking,
            bool expectedEnabled)
        {
            bool enabled = SetupWizardWindow.IsCliButtonEnabledForSetupWizard(
                cliInstalled,
                cliVersionMatched,
                needsCliPathSetup,
                isInstallingCli,
                isChecking);

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
        public void CreateFirstInstallSkillTarget_WhenClaudeSelected_ReturnsClaudeProjectTarget()
        {
            SkillSetupTargetInfo target =
                SetupWizardWindow.CreateFirstInstallSkillTarget(SkillsTarget.Claude, true);

            Assert.That(target.DisplayName, Is.EqualTo("Claude Code"));
            Assert.That(target.DirName, Is.EqualTo(".claude"));
            Assert.That(target.InstallFlag, Is.EqualTo("--claude"));
            Assert.That(target.HasSkillsDirectory, Is.False);
            Assert.That(target.HasExistingSkills, Is.False);
        }

        [TestCase(SkillsTarget.Cursor, "Cursor", ".cursor", "--cursor")]
        [TestCase(SkillsTarget.Gemini, "Gemini CLI", ".gemini", "--gemini")]
        [TestCase(SkillsTarget.Codex, "Codex CLI", ".codex", "--codex")]
        [TestCase(SkillsTarget.Agents, "Other (.agents)", ".agents", "--agents")]
        public void CreateFirstInstallSkillTarget_ReturnsMappedTarget(
            SkillsTarget targetType,
            string expectedDisplayName,
            string expectedDirName,
            string expectedInstallFlag)
        {
            SkillSetupTargetInfo target =
                SetupWizardWindow.CreateFirstInstallSkillTarget(targetType, true);

            Assert.That(target.DisplayName, Is.EqualTo(expectedDisplayName));
            Assert.That(target.DirName, Is.EqualTo(expectedDirName));
            Assert.That(target.InstallFlag, Is.EqualTo(expectedInstallFlag));
            Assert.That(target.HasSkillsDirectory, Is.False);
            Assert.That(target.HasExistingSkills, Is.False);
        }

        [Test]
        public void CreateFirstInstallSkillTarget_WhenGroupingDisabled_KeepsTargetMetadata()
        {
            SkillSetupTargetInfo target =
                SetupWizardWindow.CreateFirstInstallSkillTarget(SkillsTarget.Claude, false);

            Assert.That(target.DisplayName, Is.EqualTo("Claude Code"));
            Assert.That(target.DirName, Is.EqualTo(".claude"));
            Assert.That(target.InstallFlag, Is.EqualTo("--claude"));
        }

        [Test]
        public void GetSelectedSkillTargetInfo_WhenDetectedTargetExists_ReturnsDetectedState()
        {
            List<SkillSetupTargetInfo> targets = new()
            {
                new(
                    "Claude Code",
                    ".claude",
                    "--claude",
                    hasSkillsDirectory: true,
                    hasExistingSkills: true,
                    installState: SkillInstallState.Installed)
            };

            SkillSetupTargetInfo target = SetupWizardWindow.GetSelectedSkillTargetInfo(
                targets,
                SkillsTarget.Claude,
                groupSkillsUnderUnityCliLoop: true);

            Assert.That(target.DirName, Is.EqualTo(".claude"));
            Assert.That(target.InstallState, Is.EqualTo(SkillInstallState.Installed));
        }

        [Test]
        public void GetFirstInstallableSkillTargets_WhenSelectedTargetIsInstalled_ReturnsEmpty()
        {
            List<SkillSetupTargetInfo> targets = new()
            {
                new(
                    "Claude Code",
                    ".claude",
                    "--claude",
                    hasSkillsDirectory: true,
                    hasExistingSkills: true,
                    installState: SkillInstallState.Installed)
            };

            List<SkillSetupTargetInfo> installableTargets =
                SetupWizardWindow.GetFirstInstallableSkillTargets(
                    targets,
                    SkillsTarget.Claude,
                    groupSkillsUnderUnityCliLoop: true);

            Assert.That(installableTargets, Is.Empty);
        }

        [Test]
        public void GetFirstInstallableSkillTargets_WhenSelectedTargetIsMissing_ReturnsMappedTarget()
        {
            List<SkillSetupTargetInfo> installableTargets =
                SetupWizardWindow.GetFirstInstallableSkillTargets(
                    new List<SkillSetupTargetInfo>(),
                    SkillsTarget.Claude,
                    groupSkillsUnderUnityCliLoop: true);

            Assert.That(installableTargets.Count, Is.EqualTo(1));
            Assert.That(installableTargets[0].DirName, Is.EqualTo(".claude"));
            Assert.That(installableTargets[0].InstallState, Is.EqualTo(SkillInstallState.Missing));
        }

        [TestCase(SkillInstallState.Installed, false, true, "Installed")]
        [TestCase(SkillInstallState.Checking, false, true, "Checking...")]
        [TestCase(SkillInstallState.Outdated, false, true, "Outdated")]
        [TestCase(SkillInstallState.Missing, false, true, "Missing")]
        [TestCase(SkillInstallState.Missing, true, true, "Not grouped")]
        [TestCase(SkillInstallState.Missing, true, false, "Grouped")]
        public void GetSkillInstallStatusText_ReturnsExpectedLabel(
            SkillInstallState installState,
            bool hasDifferentLayoutSkills,
            bool groupSkillsUnderUnityCliLoop,
            string expectedLabel)
        {
            string label = SetupWizardWindow.GetSkillInstallStatusText(
                installState,
                hasDifferentLayoutSkills,
                groupSkillsUnderUnityCliLoop);

            Assert.That(label, Is.EqualTo(expectedLabel));
        }

        [TestCase(true, false, "Installing...")]
        [TestCase(false, true, "Update Skills")]
        [TestCase(false, false, "Install Skills")]
        public void GetInstallSkillsButtonText_ReturnsExpectedLabel(
            bool isInstallingSkills,
            bool hasOutdatedSkills,
            string expectedLabel)
        {
            string label = SetupWizardWindow.GetInstallSkillsButtonText(
                isInstallingSkills,
                hasOutdatedSkills);

            Assert.That(label, Is.EqualTo(expectedLabel));
        }

        [TestCase(false, false, false, "Install Skills")]
        [TestCase(true, true, false, "Installing...")]
        [TestCase(true, false, true, "Update Skills")]
        [TestCase(true, false, false, "Install Skills")]
        public void GetSkillsButtonTextForSetupWizard_ReturnsExpectedLabel(
            bool cliInstalled,
            bool isInstallingSkills,
            bool hasOutdatedSkills,
            string expectedLabel)
        {
            string label = SetupWizardWindow.GetSkillsButtonTextForSetupWizard(
                cliInstalled,
                isInstallingSkills,
                hasOutdatedSkills);

            Assert.That(label, Is.EqualTo(expectedLabel));
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

        [TestCase(SkillInstallState.Installed, false, true, "setup-target-item__status--installed")]
        [TestCase(SkillInstallState.Checking, false, true, "setup-target-item__status--checking")]
        [TestCase(SkillInstallState.Outdated, false, true, "setup-target-item__status--outdated")]
        [TestCase(SkillInstallState.Missing, false, true, "setup-target-item__status--missing")]
        [TestCase(SkillInstallState.Missing, true, true, "setup-target-item__status--different-layout")]
        public void GetSkillInstallStatusClass_ReturnsExpectedClass(
            SkillInstallState installState,
            bool hasDifferentLayoutSkills,
            bool groupSkillsUnderUnityCliLoop,
            string expectedClass)
        {
            string className = SetupWizardWindow.GetSkillInstallStatusClass(
                installState,
                hasDifferentLayoutSkills,
                groupSkillsUnderUnityCliLoop);

            Assert.That(className, Is.EqualTo(expectedClass));
        }

        [Test]
        public void EstimateWrappedLineCount_WithPositiveHeight_ReturnsRoundedLineCount()
        {
            int lineCount = SetupWizardWindow.EstimateWrappedLineCount(35f, 12f);

            Assert.That(lineCount, Is.EqualTo(3));
        }

        [Test]
        public void SelectPreferredTextWidth_WhenWrappedAcrossManyLines_UsesTwoLineTarget()
        {
            float preferredWidth = SetupWizardWindow.SelectPreferredTextWidth(120f, 320f, 4, WhiteSpace.Normal);

            Assert.That(preferredWidth, Is.EqualTo(160f));
        }

        [Test]
        public void SelectPreferredTextWidth_WhenWrappedAcrossTwoLines_KeepsLaidOutWidth()
        {
            float preferredWidth = SetupWizardWindow.SelectPreferredTextWidth(180f, 320f, 2, WhiteSpace.Normal);

            Assert.That(preferredWidth, Is.EqualTo(180f));
        }

        [Test]
        public void SelectPreferredTextWidth_WhenShorterTextFitsWithinCurrentWidth_ShrinksToMeasuredWidth()
        {
            float preferredWidth = SetupWizardWindow.SelectPreferredTextWidth(420f, 180f, 1, WhiteSpace.Normal);

            Assert.That(preferredWidth, Is.EqualTo(180f));
        }

        [Test]
        public void SelectPreferredTextWidth_WhenTextDoesNotWrap_UsesMeasuredWidth()
        {
            float preferredWidth = SetupWizardWindow.SelectPreferredTextWidth(180f, 320f, 1, WhiteSpace.NoWrap);

            Assert.That(preferredWidth, Is.EqualTo(320f));
        }

        [Test]
        public void HasFiniteSize_WhenVectorContainsNaN_ReturnsFalse()
        {
            bool hasFiniteSize = SetupWizardWindow.HasFiniteSize(new Vector2(float.NaN, 120f));

            Assert.That(hasFiniteSize, Is.False);
        }

        [Test]
        public void HasFiniteSize_WhenVectorContainsFiniteValues_ReturnsTrue()
        {
            bool hasFiniteSize = SetupWizardWindow.HasFiniteSize(new Vector2(240f, 120f));

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
    }
}
