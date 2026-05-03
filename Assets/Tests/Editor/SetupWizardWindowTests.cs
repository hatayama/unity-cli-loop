using System.IO;
using System.Collections.Generic;

using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    public class SetupWizardWindowTests
    {
        private static readonly string SettingsFilePath =
            Path.Combine(McpConstants.USER_SETTINGS_FOLDER, McpConstants.SETTINGS_FILE_NAME);

        private bool _settingsFileExisted;
        private string _settingsFileContent;

        [SetUp]
        public void SetUp()
        {
            _settingsFileExisted = File.Exists(SettingsFilePath);
            _settingsFileContent = _settingsFileExisted ? File.ReadAllText(SettingsFilePath) : null;

            if (!Directory.Exists(McpConstants.USER_SETTINGS_FOLDER))
            {
                Directory.CreateDirectory(McpConstants.USER_SETTINGS_FOLDER);
            }

            DeleteIfExists(SettingsFilePath);
            McpEditorSettings.InvalidateCache();
        }

        [TearDown]
        public void TearDown()
        {
            RestoreFile(SettingsFilePath, _settingsFileExisted, _settingsFileContent);
            McpEditorSettings.InvalidateCache();
        }

        [TestCase("", "1.7.3", false, true)]
        [TestCase("1.7.2", "1.7.3", false, true)]
        [TestCase("1.7.4", "1.7.3", false, true)]
        [TestCase("1.7.3", "1.7.3", false, false)]
        [TestCase("", "1.7.3", true, false)]
        [TestCase("1.7.2", "1.7.3", true, false)]
        public void ShouldAutoShowForVersion_ReturnsExpectedValue(
            string lastSeenVersion,
            string currentVersion,
            bool suppressAutoShow,
            bool expected)
        {
            bool shouldAutoShow =
                SetupWizardWindow.ShouldAutoShowForVersion(currentVersion, lastSeenVersion, suppressAutoShow);

            Assert.That(shouldAutoShow, Is.EqualTo(expected));
        }

        [Test]
        public void MaybeRecordLastSeenVersion_WhenAutoShow_UpdatesStoredVersion()
        {
            McpEditorSettings.SaveSettings(new McpEditorSettingsData
            {
                lastSeenSetupWizardVersion = "1.7.2"
            });

            SetupWizardWindow.MaybeRecordLastSeenVersion(true, "1.7.3");

            Assert.That(McpEditorSettings.GetLastSeenSetupWizardVersion(), Is.EqualTo("1.7.3"));
        }

        [Test]
        public void MaybeRecordLastSeenVersion_WhenManualShow_KeepsStoredVersion()
        {
            McpEditorSettings.SaveSettings(new McpEditorSettingsData
            {
                lastSeenSetupWizardVersion = "1.7.2"
            });

            SetupWizardWindow.MaybeRecordLastSeenVersion(false, "1.7.3");

            Assert.That(McpEditorSettings.GetLastSeenSetupWizardVersion(), Is.EqualTo("1.7.2"));
        }

        [Test]
        public void MaybeRecordSuppressedVersion_WhenAutoShowSuppressed_UpdatesStoredVersion()
        {
            McpEditorSettings.SaveSettings(new McpEditorSettingsData
            {
                lastSeenSetupWizardVersion = "1.7.2"
            });

            SetupWizardWindow.MaybeRecordSuppressedVersion(true, "1.7.3");

            Assert.That(McpEditorSettings.GetLastSeenSetupWizardVersion(), Is.EqualTo("1.7.3"));
        }

        [Test]
        public void MaybeRecordSuppressedVersion_WhenAutoShowAllowed_KeepsStoredVersion()
        {
            McpEditorSettings.SaveSettings(new McpEditorSettingsData
            {
                lastSeenSetupWizardVersion = "1.7.2"
            });

            SetupWizardWindow.MaybeRecordSuppressedVersion(false, "1.7.3");

            Assert.That(McpEditorSettings.GetLastSeenSetupWizardVersion(), Is.EqualTo("1.7.2"));
        }

        [Test]
        public void TryReuseOpenWindow_WhenExistingWindowAndAutoShow_FocusesWindowAndRecordsVersion()
        {
            bool focusedExistingWindow = false;
            McpEditorSettings.SaveSettings(new McpEditorSettingsData
            {
                lastSeenSetupWizardVersion = "1.7.2"
            });

            bool reused = SetupWizardWindow.TryReuseOpenWindow(
                hasOpenWindow: true,
                shouldRecordVersion: true,
                currentVersion: "1.7.3",
                focusExistingWindow: () => focusedExistingWindow = true);

            Assert.That(reused, Is.True);
            Assert.That(focusedExistingWindow, Is.True);
            Assert.That(McpEditorSettings.GetLastSeenSetupWizardVersion(), Is.EqualTo("1.7.3"));
        }

        [Test]
        public void TryReuseOpenWindow_WhenExistingWindowAndManualShow_FocusesWindowWithoutRecordingVersion()
        {
            bool focusedExistingWindow = false;
            McpEditorSettings.SaveSettings(new McpEditorSettingsData
            {
                lastSeenSetupWizardVersion = "1.7.2"
            });

            bool reused = SetupWizardWindow.TryReuseOpenWindow(
                hasOpenWindow: true,
                shouldRecordVersion: false,
                currentVersion: "1.7.3",
                focusExistingWindow: () => focusedExistingWindow = true);

            Assert.That(reused, Is.True);
            Assert.That(focusedExistingWindow, Is.True);
            Assert.That(McpEditorSettings.GetLastSeenSetupWizardVersion(), Is.EqualTo("1.7.2"));
        }

        [Test]
        public void TryReuseOpenWindow_WhenNoExistingWindow_DoesNotFocusOrRecordVersion()
        {
            bool focusedExistingWindow = false;
            McpEditorSettings.SaveSettings(new McpEditorSettingsData
            {
                lastSeenSetupWizardVersion = "1.7.2"
            });

            bool reused = SetupWizardWindow.TryReuseOpenWindow(
                hasOpenWindow: false,
                shouldRecordVersion: true,
                currentVersion: "1.7.3",
                focusExistingWindow: () => focusedExistingWindow = true);

            Assert.That(reused, Is.False);
            Assert.That(focusedExistingWindow, Is.False);
            Assert.That(McpEditorSettings.GetLastSeenSetupWizardVersion(), Is.EqualTo("1.7.2"));
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

                SetupWizardWindow.PrepareForOpen(window, "Unity CLI Loop Setup", position, "1.9.0");

                SerializedObject serializedWindow = new(window);
                SerializedProperty lastSeenVersionProperty =
                    serializedWindow.FindProperty("_lastSeenSetupWizardVersionBeforeOpen");

                Assert.That(window.titleContent.text, Is.EqualTo("Unity CLI Loop Setup"));
                Assert.That(window.position, Is.EqualTo(position));
                Assert.That(lastSeenVersionProperty, Is.Not.Null);
                Assert.That(lastSeenVersionProperty.stringValue, Is.EqualTo("1.9.0"));
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void FilterInstallableSkillTargets_ExcludesTargetsWithoutSkillsDirectory()
        {
            List<ToolSkillSynchronizer.SkillTargetInfo> targets = new()
            {
                new("Claude Code", ".claude", "--claude", true, true),
                new("Cursor", ".cursor", "--cursor", false, false),
                new("Codex CLI", ".codex", "--codex", true, false, hasDifferentLayoutSkills: true)
            };

            List<ToolSkillSynchronizer.SkillTargetInfo> installableTargets =
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
        public void CreateFirstInstallSkillTarget_WhenClaudeSelected_ReturnsClaudeProjectTarget()
        {
            ToolSkillSynchronizer.SkillTargetInfo target =
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
            ToolSkillSynchronizer.SkillTargetInfo target =
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
            ToolSkillSynchronizer.SkillTargetInfo target =
                SetupWizardWindow.CreateFirstInstallSkillTarget(SkillsTarget.Claude, false);

            Assert.That(target.DisplayName, Is.EqualTo("Claude Code"));
            Assert.That(target.DirName, Is.EqualTo(".claude"));
            Assert.That(target.InstallFlag, Is.EqualTo("--claude"));
        }

        [Test]
        public void GetSelectedSkillTargetInfo_WhenDetectedTargetExists_ReturnsDetectedState()
        {
            List<ToolSkillSynchronizer.SkillTargetInfo> targets = new()
            {
                new(
                    "Claude Code",
                    ".claude",
                    "--claude",
                    hasSkillsDirectory: true,
                    hasExistingSkills: true,
                    installState: SkillInstallState.Installed)
            };

            ToolSkillSynchronizer.SkillTargetInfo target = SetupWizardWindow.GetSelectedSkillTargetInfo(
                targets,
                SkillsTarget.Claude,
                groupSkillsUnderUnityCliLoop: true);

            Assert.That(target.DirName, Is.EqualTo(".claude"));
            Assert.That(target.InstallState, Is.EqualTo(SkillInstallState.Installed));
        }

        [Test]
        public void GetFirstInstallableSkillTargets_WhenSelectedTargetIsInstalled_ReturnsEmpty()
        {
            List<ToolSkillSynchronizer.SkillTargetInfo> targets = new()
            {
                new(
                    "Claude Code",
                    ".claude",
                    "--claude",
                    hasSkillsDirectory: true,
                    hasExistingSkills: true,
                    installState: SkillInstallState.Installed)
            };

            List<ToolSkillSynchronizer.SkillTargetInfo> installableTargets =
                SetupWizardWindow.GetFirstInstallableSkillTargets(
                    targets,
                    SkillsTarget.Claude,
                    groupSkillsUnderUnityCliLoop: true);

            Assert.That(installableTargets, Is.Empty);
        }

        [Test]
        public void GetFirstInstallableSkillTargets_WhenSelectedTargetIsMissing_ReturnsMappedTarget()
        {
            List<ToolSkillSynchronizer.SkillTargetInfo> installableTargets =
                SetupWizardWindow.GetFirstInstallableSkillTargets(
                    new List<ToolSkillSynchronizer.SkillTargetInfo>(),
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
    }
}
