using System.IO;
using System.Collections.Generic;

using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace io.github.hatayama.uLoopMCP.Tests.Editor
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
        public void FilterInstallableSkillTargets_ReturnsOnlyOptedInTargets()
        {
            List<ToolSkillSynchronizer.SkillTargetInfo> targets = new()
            {
                new("Claude Code", ".claude", "--claude", true, true),
                new("Cursor", ".cursor", "--cursor", false, false)
            };

            List<ToolSkillSynchronizer.SkillTargetInfo> installableTargets =
                SetupWizardWindow.FilterInstallableSkillTargets(targets);

            Assert.That(installableTargets.Count, Is.EqualTo(1));
            Assert.That(installableTargets[0].DirName, Is.EqualTo(".claude"));
        }

        [Test]
        public void ShouldUseFirstInstallSkillsUi_WhenSelectionHasNotBeenShown_ReturnsTrue()
        {
            bool shouldUseFirstInstallUi = SetupWizardWindow.ShouldUseFirstInstallSkillsUi(false);

            Assert.That(shouldUseFirstInstallUi, Is.True);
        }

        [Test]
        public void ShouldUseFirstInstallSkillsUi_WhenSelectionHasAlreadyBeenShown_ReturnsFalse()
        {
            bool shouldUseFirstInstallUi = SetupWizardWindow.ShouldUseFirstInstallSkillsUi(true);

            Assert.That(shouldUseFirstInstallUi, Is.False);
        }

        [Test]
        public void CreateFirstInstallSkillTarget_WhenClaudeSelected_ReturnsClaudeProjectTarget()
        {
            ToolSkillSynchronizer.SkillTargetInfo target =
                SetupWizardWindow.CreateFirstInstallSkillTarget(SkillsTarget.Claude);

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
                SetupWizardWindow.CreateFirstInstallSkillTarget(targetType);

            Assert.That(target.DisplayName, Is.EqualTo(expectedDisplayName));
            Assert.That(target.DirName, Is.EqualTo(expectedDirName));
            Assert.That(target.InstallFlag, Is.EqualTo(expectedInstallFlag));
            Assert.That(target.HasSkillsDirectory, Is.False);
            Assert.That(target.HasExistingSkills, Is.False);
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
