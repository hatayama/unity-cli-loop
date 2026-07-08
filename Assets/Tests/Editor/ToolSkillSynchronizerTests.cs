using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies Tool Skill Synchronizer behavior.
    /// </summary>
    [TestFixture]
    public class ToolSkillSynchronizerTests
    {
        private static readonly string ToolSettingsFilePath =
            Path.Combine(UnityCliLoopConstants.ULOOP_DIR, UnityCliLoopConstants.ULOOP_TOOL_SETTINGS_FILE_NAME);

        private string _projectRoot;
        private string[] _nonExistentDirsBefore;
        private string[] _temporaryRoots;
        private bool _toolSettingsFileExisted;
        private string _toolSettingsFileContent;

        [SetUp]
        public void SetUp()
        {
            _projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            _toolSettingsFileExisted = File.Exists(ToolSettingsFilePath);
            _toolSettingsFileContent = _toolSettingsFileExisted ? File.ReadAllText(ToolSettingsFilePath) : null;

            _nonExistentDirsBefore = SkillTargetDetector.SkillTargetDirs
                .Where(dir => !Directory.Exists(Path.Combine(_projectRoot, dir)))
                .ToArray();
            _temporaryRoots = new string[0];
        }

        [TearDown]
        public void TearDown()
        {
            // Clean up any directories that were created during the test
            foreach (string dir in _nonExistentDirsBefore)
            {
                string fullPath = Path.Combine(_projectRoot, dir);
                if (Directory.Exists(fullPath))
                {
                    // Only delete if it was created by this test (didn't exist before)
                    string skillsPath = Path.Combine(fullPath, SkillInstallLayout.SkillsDirName);
                    if (Directory.Exists(skillsPath))
                    {
                        Directory.Delete(skillsPath, true);
                    }
                    // Remove the parent dir only if it's now empty
                    if (Directory.Exists(fullPath) && !Directory.EnumerateFileSystemEntries(fullPath).Any())
                    {
                        Directory.Delete(fullPath);
                    }
                }
            }

            foreach (string temporaryRoot in _temporaryRoots)
            {
                if (Directory.Exists(temporaryRoot))
                {
                    Directory.Delete(temporaryRoot, true);
                }
            }

            RestoreToolSettingsFile();
        }

        [Test]
        public async Task InstallSkillFiles_DoesNotCreateNonExistentTargetDirectories()
        {
            // Arrange: record which target directories don't exist
            UnityEngine.Debug.Assert(_nonExistentDirsBefore.Length > 0,
                "At least one target directory should not exist for this test to be meaningful");

            // Act
            List<ToolSkillSynchronizer.SkillTargetInfo> targets =
                SkillTargetDetector.DetectTargetsForLayoutStateAtProjectRoot(
                    _projectRoot,
                    requireSkillsDirectory: true,
                    groupSkillsUnderUnityCliLoop: false,
                    includeFreshnessCheck: false);
            await ToolSkillSynchronizer.InstallSkillFiles(
                targets,
                groupSkillsUnderUnityCliLoop: false,
                Array.Empty<string>(),
                ct: CancellationToken.None);

            // Assert: directories that didn't exist before should still not exist
            foreach (string dir in _nonExistentDirsBefore)
            {
                string fullPath = Path.Combine(_projectRoot, dir);
                Assert.IsFalse(Directory.Exists(fullPath),
                    $"Directory '{dir}' should not be created by InstallSkillFiles when '{dir}' did not exist");
            }
        }

        // Tests that disabled tool inputs prevent skill recreation.
        [Test]
        public async Task InstallSkillFiles_WhenToolIsDisabled_DoesNotRecreateSkill()
        {
            string temporaryRoot = CreateTemporaryProjectRoot();
            string targetRoot = Path.Combine(temporaryRoot, ".claude");
            string skillsRoot = Path.Combine(targetRoot, SkillInstallLayout.SkillsDirName);
            string disabledSkillDir = Path.Combine(skillsRoot, "uloop-compile");
            WriteSkillFile(disabledSkillDir);
            ToolSkillSynchronizer.SkillTargetInfo target = new(
                "Claude Code",
                targetRoot,
                "--claude",
                hasSkillsDirectory: true,
                hasExistingSkills: true);

            ToolSkillSynchronizer.SkillInstallResult result =
                await ToolSkillSynchronizer.InstallSkillFiles(
                    new List<ToolSkillSynchronizer.SkillTargetInfo> { target },
                    groupSkillsUnderUnityCliLoop: false,
                    disabledTools: new[] { "compile" },
                    ct: CancellationToken.None);

            Assert.That(result.IsSuccessful, Is.True);
            Assert.That(Directory.Exists(disabledSkillDir), Is.False);
        }

        [Test]
        public async Task InstallSkillFilesAtProjectRoot_WhenManagedSkillWasDeleted_RestoresIt()
        {
            string temporaryRoot = CreateTemporaryProjectRoot();
            CreateFakeSourceSkill(
                temporaryRoot,
                "uloop-fake-skill",
                "FakeTool",
                "reference.md",
                "reference");

            string targetRoot = Path.Combine(temporaryRoot, ".claude");
            Directory.CreateDirectory(Path.Combine(targetRoot, SkillInstallLayout.SkillsDirName));

            ToolSkillSynchronizer.SkillTargetInfo target = new(
                "Claude Code",
                ".claude",
                "--claude",
                hasSkillsDirectory: true,
                hasExistingSkills: false);

            ToolSkillSynchronizer.SkillInstallResult result =
                await ToolSkillSynchronizer.InstallSkillFilesAtProjectRoot(
                    temporaryRoot,
                    new[] { target },
                    groupSkillsUnderUnityCliLoop: true,
                    disabledTools: Array.Empty<string>(),
                    ct: CancellationToken.None);

            string installedSkillDir = Path.Combine(
                targetRoot,
                SkillInstallLayout.SkillsDirName,
                SkillInstallLayout.ManagedSkillsDirName,
                "uloop-fake-skill");
            string skillFilePath = Path.Combine(installedSkillDir, SkillInstallLayout.SkillFileName);
            string referencePath = Path.Combine(installedSkillDir, "reference.md");

            Assert.That(result.IsSuccessful, Is.True);
            Assert.That(File.Exists(skillFilePath), Is.True);
            Assert.That(File.ReadAllText(skillFilePath), Does.Contain("name: uloop-fake-skill"));
            Assert.That(File.Exists(referencePath), Is.True);
            Assert.That(File.ReadAllText(referencePath), Is.EqualTo("reference"));
        }

        [Test]
        public async Task InstallSkillFilesAtProjectRoot_WhenFlatLayoutRequested_InstallsUnderSkillsRoot()
        {
            string temporaryRoot = CreateTemporaryProjectRoot();
            CreateFakeSourceSkill(
                temporaryRoot,
                "uloop-fake-skill",
                "FakeTool",
                "reference.md",
                "reference");

            string targetRoot = Path.Combine(temporaryRoot, ".claude");
            Directory.CreateDirectory(Path.Combine(targetRoot, SkillInstallLayout.SkillsDirName));
            WriteSkillFile(Path.Combine(
                targetRoot,
                SkillInstallLayout.SkillsDirName,
                SkillInstallLayout.ManagedSkillsDirName,
                "uloop-fake-skill"));

            ToolSkillSynchronizer.SkillTargetInfo target = new(
                "Claude Code",
                ".claude",
                "--claude",
                hasSkillsDirectory: true,
                hasExistingSkills: true);

            ToolSkillSynchronizer.SkillInstallResult result =
                await ToolSkillSynchronizer.InstallSkillFilesAtProjectRoot(
                    temporaryRoot,
                    new[] { target },
                    groupSkillsUnderUnityCliLoop: false,
                    disabledTools: Array.Empty<string>(),
                    ct: CancellationToken.None);

            string installedSkillDir = Path.Combine(
                targetRoot,
                SkillInstallLayout.SkillsDirName,
                "uloop-fake-skill");
            string groupedSkillDir = Path.Combine(
                targetRoot,
                SkillInstallLayout.SkillsDirName,
                SkillInstallLayout.ManagedSkillsDirName,
                "uloop-fake-skill");

            Assert.That(result.IsSuccessful, Is.True);
            Assert.That(File.Exists(Path.Combine(installedSkillDir, SkillInstallLayout.SkillFileName)), Is.True);
            Assert.That(File.ReadAllText(Path.Combine(installedSkillDir, "reference.md")), Is.EqualTo("reference"));
            Assert.That(Directory.Exists(groupedSkillDir), Is.False);
        }

        [Test]
        public async Task InstallSkillFilesAtProjectRoot_WhenFlatLayoutRequested_RemovesEmptyManagedSkillsParentDirectory()
        {
            string temporaryRoot = CreateTemporaryProjectRoot();
            CreateFakeSourceSkill(
                temporaryRoot,
                "uloop-fake-skill",
                "FakeTool",
                "reference.md",
                "reference");

            string targetRoot = Path.Combine(temporaryRoot, ".claude");
            string skillsRoot = Path.Combine(targetRoot, SkillInstallLayout.SkillsDirName);
            string managedSkillsRoot = Path.Combine(
                skillsRoot,
                SkillInstallLayout.ManagedSkillsDirName);
            Directory.CreateDirectory(skillsRoot);
            WriteSkillFile(Path.Combine(
                managedSkillsRoot,
                "uloop-fake-skill"));

            ToolSkillSynchronizer.SkillTargetInfo target = new(
                "Claude Code",
                ".claude",
                "--claude",
                hasSkillsDirectory: true,
                hasExistingSkills: true);

            ToolSkillSynchronizer.SkillInstallResult result =
                await ToolSkillSynchronizer.InstallSkillFilesAtProjectRoot(
                    temporaryRoot,
                    new[] { target },
                    groupSkillsUnderUnityCliLoop: false,
                    disabledTools: Array.Empty<string>(),
                    ct: CancellationToken.None);

            string installedSkillDir = Path.Combine(
                skillsRoot,
                "uloop-fake-skill");

            Assert.That(result.IsSuccessful, Is.True);
            Assert.That(File.Exists(Path.Combine(installedSkillDir, SkillInstallLayout.SkillFileName)), Is.True);
            Assert.That(Directory.Exists(managedSkillsRoot), Is.False);
        }

        [Test]
        public async Task InstallSkillFilesAtProjectRoot_WhenFlatLayoutRequested_RemovesDeprecatedManagedSkillDirectories()
        {
            string temporaryRoot = CreateTemporaryProjectRoot();
            CreateFakeSourceSkill(
                temporaryRoot,
                "uloop-fake-skill",
                "FakeTool",
                "reference.md",
                "reference");

            string targetRoot = Path.Combine(temporaryRoot, ".claude");
            string skillsRoot = Path.Combine(targetRoot, SkillInstallLayout.SkillsDirName);
            string managedSkillsRoot = Path.Combine(
                skillsRoot,
                SkillInstallLayout.ManagedSkillsDirName);
            Directory.CreateDirectory(skillsRoot);
            WriteSkillFile(Path.Combine(managedSkillsRoot, "uloop-fake-skill"));
            WriteSkillFile(Path.Combine(managedSkillsRoot, "uloop-capture-window"));

            ToolSkillSynchronizer.SkillTargetInfo target = new(
                "Claude Code",
                ".claude",
                "--claude",
                hasSkillsDirectory: true,
                hasExistingSkills: true);

            ToolSkillSynchronizer.SkillInstallResult result =
                await ToolSkillSynchronizer.InstallSkillFilesAtProjectRoot(
                    temporaryRoot,
                    new[] { target },
                    groupSkillsUnderUnityCliLoop: false,
                    disabledTools: Array.Empty<string>(),
                    ct: CancellationToken.None);

            string installedSkillDir = Path.Combine(
                skillsRoot,
                "uloop-fake-skill");

            Assert.That(result.IsSuccessful, Is.True);
            Assert.That(File.Exists(Path.Combine(installedSkillDir, SkillInstallLayout.SkillFileName)), Is.True);
            Assert.That(Directory.Exists(managedSkillsRoot), Is.False);
        }

        [Test]
        public async Task InstallSkillFilesAtProjectRoot_WhenDisabledSkillExistsInBothLayouts_RemovesBothLayouts()
        {
            // Tests that full sync removes disabled skills through the scope cleanup before installing enabled skills.
            string temporaryRoot = CreateTemporaryProjectRoot();
            CreateFakeSourceSkill(
                temporaryRoot,
                "uloop-enabled-skill",
                "EnabledTool",
                "reference.md",
                "enabled-reference");
            CreateFakeSourceSkill(
                temporaryRoot,
                "uloop-disabled-skill",
                "DisabledTool",
                "reference.md",
                "disabled-reference");

            string targetRoot = Path.Combine(temporaryRoot, ".claude");
            string skillsRoot = Path.Combine(targetRoot, SkillInstallLayout.SkillsDirName);
            string managedSkillsRoot = Path.Combine(
                skillsRoot,
                SkillInstallLayout.ManagedSkillsDirName);
            string flatDisabledSkillDir = Path.Combine(skillsRoot, "uloop-disabled-skill");
            string groupedDisabledSkillDir = Path.Combine(managedSkillsRoot, "uloop-disabled-skill");
            WriteSkillFile(flatDisabledSkillDir, "---\nname: uloop-disabled-skill\n---\n");
            WriteSkillFile(groupedDisabledSkillDir, "---\nname: uloop-disabled-skill\n---\n");

            ToolSkillSynchronizer.SkillTargetInfo target = new(
                "Claude Code",
                ".claude",
                "--claude",
                hasSkillsDirectory: true,
                hasExistingSkills: true);

            ToolSkillSynchronizer.SkillInstallResult result =
                await ToolSkillSynchronizer.InstallSkillFilesAtProjectRoot(
                    temporaryRoot,
                    new[] { target },
                    groupSkillsUnderUnityCliLoop: false,
                    disabledTools: new[] { "disabled-skill" },
                    ct: CancellationToken.None);

            string enabledSkillDir = Path.Combine(skillsRoot, "uloop-enabled-skill");

            Assert.That(result.IsSuccessful, Is.True);
            Assert.That(File.Exists(Path.Combine(enabledSkillDir, SkillInstallLayout.SkillFileName)), Is.True);
            Assert.That(Directory.Exists(flatDisabledSkillDir), Is.False);
            Assert.That(Directory.Exists(groupedDisabledSkillDir), Is.False);
        }

        [Test]
        public async Task InstallSkillFilesAtProjectRoot_WhenFlatLayoutRequested_InstallsProjectLocalCustomSkills()
        {
            string temporaryRoot = CreateTemporaryProjectRoot();
            string projectLocalToolDir = CreateFakeProjectLocalSkill(
                temporaryRoot,
                "uloop-get-unitask-tracker",
                "GetUniTaskTracker");
            File.WriteAllText(
                Path.Combine(projectLocalToolDir, "GetUniTaskTrackerTool.cs"),
                "internal sealed class GetUniTaskTrackerTool {}");

            string targetRoot = Path.Combine(temporaryRoot, ".claude");
            string skillsRoot = Path.Combine(targetRoot, SkillInstallLayout.SkillsDirName);
            string managedSkillsRoot = Path.Combine(
                skillsRoot,
                SkillInstallLayout.ManagedSkillsDirName);
            Directory.CreateDirectory(skillsRoot);
            WriteSkillFile(Path.Combine(managedSkillsRoot, "uloop-get-unitask-tracker"));

            ToolSkillSynchronizer.SkillTargetInfo target = new(
                "Claude Code",
                ".claude",
                "--claude",
                hasSkillsDirectory: true,
                hasExistingSkills: true);

            ToolSkillSynchronizer.SkillInstallResult result =
                await ToolSkillSynchronizer.InstallSkillFilesAtProjectRoot(
                    temporaryRoot,
                    new[] { target },
                    groupSkillsUnderUnityCliLoop: false,
                    disabledTools: Array.Empty<string>(),
                    ct: CancellationToken.None);

            string installedSkillDir = Path.Combine(
                skillsRoot,
                "uloop-get-unitask-tracker");
            string groupedSkillDir = Path.Combine(
                managedSkillsRoot,
                "uloop-get-unitask-tracker");

            Assert.That(result.IsSuccessful, Is.True);
            Assert.That(File.Exists(Path.Combine(installedSkillDir, SkillInstallLayout.SkillFileName)), Is.True);
            Assert.That(File.Exists(Path.Combine(installedSkillDir, "GetUniTaskTrackerTool.cs")), Is.False);
            Assert.That(Directory.Exists(groupedSkillDir), Is.False);
        }

        [Test]
        public async Task InstallSkillFilesForToolAtProjectRoot_DoesNotUpdateUnrelatedSkills()
        {
            string temporaryRoot = CreateTemporaryProjectRoot();
            CreateFakeSourceSkill(
                temporaryRoot,
                "uloop-enabled-skill",
                "EnabledTool",
                "reference.md",
                "enabled-reference");
            CreateFakeSourceSkill(
                temporaryRoot,
                "uloop-unrelated-skill",
                "UnrelatedTool",
                "reference.md",
                "new-unrelated-reference");

            string targetRoot = Path.Combine(temporaryRoot, ".claude");
            string skillsRoot = Path.Combine(targetRoot, SkillInstallLayout.SkillsDirName);
            Directory.CreateDirectory(skillsRoot);
            string unrelatedSkillDir = Path.Combine(skillsRoot, "uloop-unrelated-skill");
            WriteSkillFile(unrelatedSkillDir, "---\nname: uloop-unrelated-skill\n---\n");
            File.WriteAllText(Path.Combine(unrelatedSkillDir, "reference.md"), "old-unrelated-reference");

            ToolSkillSynchronizer.SkillInstallResult result =
                await ToolSkillSynchronizer.InstallSkillFilesForToolAtProjectRoot(
                    temporaryRoot,
                    "enabled-skill",
                    groupSkillsUnderUnityCliLoop: false,
                    disabledTools: Array.Empty<string>(),
                    ct: CancellationToken.None);

            string enabledSkillDir = Path.Combine(skillsRoot, "uloop-enabled-skill");

            Assert.That(result.IsSuccessful, Is.True);
            Assert.That(result.AttemptedTargets, Is.EqualTo(1));
            Assert.That(File.ReadAllText(Path.Combine(enabledSkillDir, "reference.md")), Is.EqualTo("enabled-reference"));
            Assert.That(
                File.ReadAllText(Path.Combine(unrelatedSkillDir, "reference.md")),
                Is.EqualTo("old-unrelated-reference"));
        }

        // Tests that per-tool installation skips disabled tools.
        [Test]
        public async Task InstallSkillFilesForToolAtProjectRoot_WhenRequestedToolIsDisabled_DoesNotInstallSkill()
        {
            string temporaryRoot = CreateTemporaryProjectRoot();
            CreateFakeSourceSkill(
                temporaryRoot,
                "uloop-disabled-skill",
                "DisabledTool",
                "reference.md",
                "disabled-reference");
            string[] disabledTools = { "disabled-skill" };

            string targetRoot = Path.Combine(temporaryRoot, ".claude");
            string skillsRoot = Path.Combine(targetRoot, SkillInstallLayout.SkillsDirName);
            Directory.CreateDirectory(skillsRoot);

            ToolSkillSynchronizer.SkillInstallResult result =
                await ToolSkillSynchronizer.InstallSkillFilesForToolAtProjectRoot(
                    temporaryRoot,
                    "disabled-skill",
                    groupSkillsUnderUnityCliLoop: false,
                    disabledTools,
                    ct: CancellationToken.None);

            string disabledSkillDir = Path.Combine(skillsRoot, "uloop-disabled-skill");

            Assert.That(result.IsSuccessful, Is.True);
            Assert.That(result.AttemptedTargets, Is.EqualTo(0));
            Assert.That(Directory.Exists(disabledSkillDir), Is.False);
        }

        [Test]
        public void IsSkillDisabledByToolSettings_WhenRunTestsIsDisabled_ReturnsTrue()
        {
            // Tests that explicit tool settings disable run-tests skill installation.
            SkillInstallLayout.SkillSourceInfo skill = new(
                "uloop-run-tests",
                UnityCliLoopConstants.TOOL_NAME_RUN_TESTS,
                new Dictionary<string, byte[]>());
            string[] disabledTools = { UnityCliLoopConstants.TOOL_NAME_RUN_TESTS };

            bool isDisabled = SkillDisabledToolFilter.IsSkillDisabledByToolSettings(
                skill,
                disabledTools);

            Assert.That(isDisabled, Is.True);
        }

        [Test]
        public void IsSkillDisabledByToolSettings_WhenRunTestsIsNotDisabled_ReturnsFalse()
        {
            // Tests that run-tests skill installation remains enabled without an explicit setting.
            SkillInstallLayout.SkillSourceInfo skill = new(
                "uloop-run-tests",
                UnityCliLoopConstants.TOOL_NAME_RUN_TESTS,
                new Dictionary<string, byte[]>());
            string[] disabledTools = Array.Empty<string>();

            bool isDisabled = SkillDisabledToolFilter.IsSkillDisabledByToolSettings(
                skill,
                disabledTools);

            Assert.That(isDisabled, Is.False);
        }

        [Test]
        public async Task InstallSkillFilesForToolAtProjectRoot_RemovesDisabledAndDeprecatedSkillsWithoutUpdatingUnrelatedSkills()
        {
            string temporaryRoot = CreateTemporaryProjectRoot();
            CreateFakeSourceSkill(
                temporaryRoot,
                "uloop-enabled-skill",
                "EnabledTool",
                "reference.md",
                "enabled-reference");
            CreateFakeSourceSkill(
                temporaryRoot,
                "uloop-disabled-skill",
                "DisabledTool",
                "reference.md",
                "disabled-reference");
            CreateFakeSourceSkill(
                temporaryRoot,
                "uloop-unrelated-skill",
                "UnrelatedTool",
                "reference.md",
                "new-unrelated-reference");
            string[] disabledTools = { "disabled-skill" };

            string targetRoot = Path.Combine(temporaryRoot, ".claude");
            string skillsRoot = Path.Combine(targetRoot, SkillInstallLayout.SkillsDirName);
            Directory.CreateDirectory(skillsRoot);
            string disabledSkillDir = Path.Combine(skillsRoot, "uloop-disabled-skill");
            string deprecatedSkillDir = Path.Combine(skillsRoot, "uloop-capture-window");
            string unrelatedSkillDir = Path.Combine(skillsRoot, "uloop-unrelated-skill");
            string thirdPartySkillDir = Path.Combine(skillsRoot, "acme-third-party");
            WriteSkillFile(disabledSkillDir, "---\nname: uloop-disabled-skill\n---\n");
            WriteSkillFile(deprecatedSkillDir, "---\nname: uloop-capture-window\n---\n");
            WriteSkillFile(unrelatedSkillDir, "---\nname: uloop-unrelated-skill\n---\n");
            File.WriteAllText(Path.Combine(unrelatedSkillDir, "reference.md"), "old-unrelated-reference");
            WriteSkillFile(
                thirdPartySkillDir,
                "---\nname: acme-third-party\ntoolName: acme-third-party\n---\n");

            ToolSkillSynchronizer.SkillInstallResult result =
                await ToolSkillSynchronizer.InstallSkillFilesForToolAtProjectRoot(
                    temporaryRoot,
                    "enabled-skill",
                    groupSkillsUnderUnityCliLoop: false,
                    disabledTools,
                    ct: CancellationToken.None);

            string enabledSkillDir = Path.Combine(skillsRoot, "uloop-enabled-skill");

            Assert.That(result.IsSuccessful, Is.True);
            Assert.That(File.ReadAllText(Path.Combine(enabledSkillDir, "reference.md")), Is.EqualTo("enabled-reference"));
            Assert.That(Directory.Exists(disabledSkillDir), Is.False);
            Assert.That(Directory.Exists(deprecatedSkillDir), Is.False);
            Assert.That(
                File.ReadAllText(Path.Combine(unrelatedSkillDir, "reference.md")),
                Is.EqualTo("old-unrelated-reference"));
            Assert.That(Directory.Exists(thirdPartySkillDir), Is.True);
        }

        [Test]
        public async Task InstallSkillFilesForToolAtProjectRoot_WhenFlatLayoutRequested_PreservesDisabledAndDeprecatedGroupedSkills()
        {
            string temporaryRoot = CreateTemporaryProjectRoot();
            CreateFakeSourceSkill(
                temporaryRoot,
                "uloop-enabled-skill",
                "EnabledTool",
                "reference.md",
                "enabled-reference");
            CreateFakeSourceSkill(
                temporaryRoot,
                "uloop-disabled-skill",
                "DisabledTool",
                "reference.md",
                "disabled-reference");
            string[] disabledTools = { "disabled-skill" };

            string targetRoot = Path.Combine(temporaryRoot, ".claude");
            string skillsRoot = Path.Combine(targetRoot, SkillInstallLayout.SkillsDirName);
            string managedSkillsRoot = Path.Combine(skillsRoot, SkillInstallLayout.ManagedSkillsDirName);
            string flatDisabledSkillDir = Path.Combine(skillsRoot, "uloop-disabled-skill");
            string flatDeprecatedSkillDir = Path.Combine(skillsRoot, "uloop-capture-window");
            string groupedDisabledSkillDir = Path.Combine(managedSkillsRoot, "uloop-disabled-skill");
            string groupedDeprecatedSkillDir = Path.Combine(managedSkillsRoot, "uloop-capture-window");
            WriteSkillFile(flatDisabledSkillDir, "---\nname: uloop-disabled-skill\n---\n");
            WriteSkillFile(flatDeprecatedSkillDir, "---\nname: uloop-capture-window\n---\n");
            WriteSkillFile(groupedDisabledSkillDir, "---\nname: uloop-disabled-skill\n---\n");
            WriteSkillFile(groupedDeprecatedSkillDir, "---\nname: uloop-capture-window\n---\n");

            ToolSkillSynchronizer.SkillInstallResult result =
                await ToolSkillSynchronizer.InstallSkillFilesForToolAtProjectRoot(
                    temporaryRoot,
                    "enabled-skill",
                    groupSkillsUnderUnityCliLoop: false,
                    disabledTools,
                    ct: CancellationToken.None);

            Assert.That(result.IsSuccessful, Is.True);
            Assert.That(Directory.Exists(flatDisabledSkillDir), Is.False);
            Assert.That(Directory.Exists(flatDeprecatedSkillDir), Is.False);
            Assert.That(Directory.Exists(groupedDisabledSkillDir), Is.True);
            Assert.That(Directory.Exists(groupedDeprecatedSkillDir), Is.True);
        }

        [Test]
        public async Task InstallSkillFilesAtProjectRoot_WhenFlatLayoutRequested_PreservesThirdPartyManagedSkillDirectories()
        {
            string temporaryRoot = CreateTemporaryProjectRoot();
            CreateFakeSourceSkill(
                temporaryRoot,
                "uloop-fake-skill",
                "FakeTool",
                "reference.md",
                "reference");

            string targetRoot = Path.Combine(temporaryRoot, ".claude");
            string skillsRoot = Path.Combine(targetRoot, SkillInstallLayout.SkillsDirName);
            string managedSkillsRoot = Path.Combine(
                skillsRoot,
                SkillInstallLayout.ManagedSkillsDirName);
            Directory.CreateDirectory(skillsRoot);
            WriteSkillFile(Path.Combine(managedSkillsRoot, "uloop-fake-skill"));
            WriteSkillFile(
                Path.Combine(managedSkillsRoot, "acme-third-party"),
                "---\nname: acme-third-party\ntoolName: acme-third-party\n---\n");

            ToolSkillSynchronizer.SkillTargetInfo target = new(
                "Claude Code",
                ".claude",
                "--claude",
                hasSkillsDirectory: true,
                hasExistingSkills: true);

            ToolSkillSynchronizer.SkillInstallResult result =
                await ToolSkillSynchronizer.InstallSkillFilesAtProjectRoot(
                    temporaryRoot,
                    new[] { target },
                    groupSkillsUnderUnityCliLoop: false,
                    disabledTools: Array.Empty<string>(),
                    ct: CancellationToken.None);

            string installedSkillDir = Path.Combine(
                skillsRoot,
                "uloop-fake-skill");
            string thirdPartySkillDir = Path.Combine(
                managedSkillsRoot,
                "acme-third-party");

            Assert.That(result.IsSuccessful, Is.True);
            Assert.That(File.Exists(Path.Combine(installedSkillDir, SkillInstallLayout.SkillFileName)), Is.True);
            Assert.That(Directory.Exists(thirdPartySkillDir), Is.True);
        }

        [Test]
        public async Task InstallSkillFilesAtProjectRoot_WhenManagedSkillsParentOnlyHasExcludedFiles_RemovesParentDirectory()
        {
            string temporaryRoot = CreateTemporaryProjectRoot();
            CreateFakeSourceSkill(
                temporaryRoot,
                "uloop-fake-skill",
                "FakeTool",
                "reference.md",
                "reference");

            string targetRoot = Path.Combine(temporaryRoot, ".claude");
            string skillsRoot = Path.Combine(targetRoot, SkillInstallLayout.SkillsDirName);
            string managedSkillsRoot = Path.Combine(
                skillsRoot,
                SkillInstallLayout.ManagedSkillsDirName);
            Directory.CreateDirectory(skillsRoot);
            WriteSkillFile(Path.Combine(managedSkillsRoot, "uloop-fake-skill"));
            File.WriteAllText(Path.Combine(managedSkillsRoot, ".DS_Store"), "ignored");

            ToolSkillSynchronizer.SkillTargetInfo target = new(
                "Claude Code",
                ".claude",
                "--claude",
                hasSkillsDirectory: true,
                hasExistingSkills: true);

            ToolSkillSynchronizer.SkillInstallResult result =
                await ToolSkillSynchronizer.InstallSkillFilesAtProjectRoot(
                    temporaryRoot,
                    new[] { target },
                    groupSkillsUnderUnityCliLoop: false,
                    disabledTools: Array.Empty<string>(),
                    ct: CancellationToken.None);

            Assert.That(result.IsSuccessful, Is.True);
            Assert.That(Directory.Exists(managedSkillsRoot), Is.False);
        }

        [Test]
        public void DetectTargets_DoesNotIncludeTargetsWithOnlyParentDirectory()
        {
            // Arrange
            string temporaryRoot = CreateTemporaryProjectRoot();
            foreach (string dir in SkillTargetDetector.SkillTargetDirs)
            {
                Directory.CreateDirectory(Path.Combine(temporaryRoot, dir));
            }

            // Act
            string[] detectedTargetDirs = SkillTargetDetector.DetectTargetsForLayoutStateAtProjectRoot(
                    temporaryRoot,
                    requireSkillsDirectory: true,
                    groupSkillsUnderUnityCliLoop: false,
                    includeFreshnessCheck: true)
                .Select(target => target.DirName)
                .ToArray();

            // Assert
            foreach (string dir in SkillTargetDetector.SkillTargetDirs)
            {
                Assert.IsFalse(detectedTargetDirs.Contains(dir),
                    $"Target '{dir}' should not be detected when only the parent directory exists");
            }
        }

        [Test]
        public void DetectTargets_WhenParentDirectoryExists_ReportsTargetAsNotOptedIn()
        {
            string temporaryRoot = CreateTemporaryProjectRoot();
            foreach (string dir in SkillTargetDetector.SkillTargetDirs)
            {
                Directory.CreateDirectory(Path.Combine(temporaryRoot, dir));
            }

            ToolSkillSynchronizer.SkillTargetInfo[] detectedTargets = SkillTargetDetector.DetectTargetsForLayoutStateAtProjectRoot(
                    temporaryRoot,
                    requireSkillsDirectory: false,
                    groupSkillsUnderUnityCliLoop: false,
                    includeFreshnessCheck: true)
                .ToArray();

            Assert.AreEqual(SkillTargetDetector.SkillTargetDirs.Length, detectedTargets.Length);
            foreach (ToolSkillSynchronizer.SkillTargetInfo target in detectedTargets)
            {
                Assert.IsNotEmpty(target.InstallFlag,
                    $"Target '{target.DirName}' should keep its install flag when detected");
                Assert.IsFalse(target.HasSkillsDirectory,
                    $"Target '{target.DirName}' should not be opted in without a skills directory");
                Assert.IsFalse(target.HasExistingSkills,
                    $"Target '{target.DirName}' should not be treated as installed without a skills directory");
            }
        }

        [Test]
        public void DetectTargets_IncludesTargetsWhenSkillsDirectoryExists()
        {
            // Arrange
            string temporaryRoot = CreateTemporaryProjectRoot();
            foreach (string dir in SkillTargetDetector.SkillTargetDirs)
            {
                Directory.CreateDirectory(Path.Combine(temporaryRoot, dir, SkillInstallLayout.SkillsDirName));
            }

            // Act
            ToolSkillSynchronizer.SkillTargetInfo[] detectedTargets = SkillTargetDetector.DetectTargetsForLayoutStateAtProjectRoot(
                    temporaryRoot,
                    requireSkillsDirectory: true,
                    groupSkillsUnderUnityCliLoop: false,
                    includeFreshnessCheck: true)
                .ToArray();

            // Assert
            Assert.AreEqual(SkillTargetDetector.SkillTargetDirs.Length, detectedTargets.Length,
                "Targets with a skills directory should be detected");

            foreach (ToolSkillSynchronizer.SkillTargetInfo target in detectedTargets)
            {
                Assert.IsTrue(target.HasSkillsDirectory,
                    $"Target '{target.DirName}' should report that its skills directory exists");
                Assert.IsFalse(target.HasExistingSkills,
                    $"Target '{target.DirName}' should not be treated as already installed when skills directory is empty");
            }
        }

        [Test]
        public void RemoveSkillFiles_DoesNotCreateNonExistentTargetDirectories()
        {
            string temporaryRoot = CreateTemporaryProjectRoot();
            string testToolName = "compile";

            ToolSkillSynchronizer.RemoveSkillFilesAtProjectRoot(temporaryRoot, testToolName);

            foreach (string dir in SkillTargetDetector.SkillTargetDirs)
            {
                string fullPath = Path.Combine(temporaryRoot, dir);
                Assert.IsFalse(Directory.Exists(fullPath),
                    $"Directory '{dir}' should not be created by RemoveSkillFiles");
            }
        }

        [Test]
        public void IsSkillInstalled_DoesNotCreateNonExistentTargetDirectories()
        {
            // Arrange
            string testToolName = "compile";

            // Act
            ToolSkillSynchronizer.IsSkillInstalled(testToolName);

            // Assert: directories that didn't exist before should still not exist
            foreach (string dir in _nonExistentDirsBefore)
            {
                string fullPath = Path.Combine(_projectRoot, dir);
                Assert.IsFalse(Directory.Exists(fullPath),
                    $"Directory '{dir}' should not be created by IsSkillInstalled");
            }
        }

        [Test]
        public void DetectTargets_WhenManagedSkillsDirectoryContainsSkills_ReportsInstalled()
        {
            string temporaryRoot = CreateTemporaryProjectRoot();
            CreateFakeSourceSkill(temporaryRoot, "uloop-compile", "CompileTool", "reference.md", "reference");
            foreach (string dir in SkillTargetDetector.SkillTargetDirs)
            {
                string targetRoot = Path.Combine(temporaryRoot, dir);
                WriteSkillFile(Path.Combine(
                    targetRoot,
                    SkillInstallLayout.SkillsDirName,
                    SkillInstallLayout.ManagedSkillsDirName,
                    "uloop-compile"));
            }

            ToolSkillSynchronizer.SkillTargetInfo[] detectedTargets = SkillTargetDetector.DetectTargetsForLayoutStateAtProjectRoot(
                    temporaryRoot,
                    requireSkillsDirectory: true,
                    groupSkillsUnderUnityCliLoop: true,
                    includeFreshnessCheck: true)
                .ToArray();

            Assert.AreEqual(SkillTargetDetector.SkillTargetDirs.Length, detectedTargets.Length);
            foreach (ToolSkillSynchronizer.SkillTargetInfo target in detectedTargets)
            {
                Assert.IsTrue(target.HasExistingSkills,
                    $"Target '{target.DirName}' should detect managed skills under unity-cli-loop");
            }
        }

        [Test]
        public void DetectTargets_WhenGroupedLayoutRequested_IgnoresFlatInstalledSkills()
        {
            string temporaryRoot = CreateTemporaryProjectRoot();
            foreach (string dir in SkillTargetDetector.SkillTargetDirs)
            {
                string targetRoot = Path.Combine(temporaryRoot, dir);
                WriteSkillFile(Path.Combine(targetRoot, SkillInstallLayout.SkillsDirName, "uloop-compile"));
            }

            ToolSkillSynchronizer.SkillTargetInfo[] detectedTargets = SkillTargetDetector.DetectTargetsForLayoutStateAtProjectRoot(
                    temporaryRoot,
                    requireSkillsDirectory: true,
                    groupSkillsUnderUnityCliLoop: true,
                    includeFreshnessCheck: true)
                .ToArray();

            foreach (ToolSkillSynchronizer.SkillTargetInfo target in detectedTargets)
            {
                Assert.IsFalse(target.HasExistingSkills,
                    $"Target '{target.DirName}' should ignore flat installs when grouped layout is selected");
                Assert.IsTrue(target.HasDifferentLayoutSkills,
                    $"Target '{target.DirName}' should detect flat installs as a different layout");
            }
        }

        [Test]
        public void DetectTargets_WhenGroupedLayoutRequested_DetectsEmptyFlatManagedDirectories()
        {
            string temporaryRoot = CreateTemporaryProjectRoot();
            string targetRoot = Path.Combine(temporaryRoot, ".cursor");
            Directory.CreateDirectory(Path.Combine(targetRoot, SkillInstallLayout.SkillsDirName, "uloop-compile"));

            ToolSkillSynchronizer.SkillTargetInfo[] detectedTargets = SkillTargetDetector.DetectTargetsForLayoutStateAtProjectRoot(
                    temporaryRoot,
                    requireSkillsDirectory: true,
                    groupSkillsUnderUnityCliLoop: true,
                    includeFreshnessCheck: true)
                .ToArray();

            Assert.That(detectedTargets.Length, Is.EqualTo(1));
            Assert.IsFalse(detectedTargets[0].HasExistingSkills,
                "Grouped layout should still treat empty flat directories as not installed");
            Assert.IsTrue(detectedTargets[0].HasDifferentLayoutSkills,
                "Empty flat managed directories should still be surfaced as a different layout");
        }

        [Test]
        public void DetectTargets_WhenFlatLayoutRequested_IgnoresGroupedInstalledSkills()
        {
            string temporaryRoot = CreateTemporaryProjectRoot();
            CreateFakeSourceSkill(temporaryRoot, "uloop-compile", "CompileTool", "reference.md", "reference");
            foreach (string dir in SkillTargetDetector.SkillTargetDirs)
            {
                string targetRoot = Path.Combine(temporaryRoot, dir);
                WriteSkillFile(Path.Combine(
                    targetRoot,
                    SkillInstallLayout.SkillsDirName,
                    SkillInstallLayout.ManagedSkillsDirName,
                    "uloop-compile"));
            }

            ToolSkillSynchronizer.SkillTargetInfo[] detectedTargets = SkillTargetDetector.DetectTargetsForLayoutStateAtProjectRoot(
                    temporaryRoot,
                    requireSkillsDirectory: true,
                    groupSkillsUnderUnityCliLoop: false,
                    includeFreshnessCheck: true)
                .ToArray();

            foreach (ToolSkillSynchronizer.SkillTargetInfo target in detectedTargets)
            {
                Assert.IsFalse(target.HasExistingSkills,
                    $"Target '{target.DirName}' should ignore grouped installs when flat layout is selected");
                Assert.IsTrue(target.HasDifferentLayoutSkills,
                    $"Target '{target.DirName}' should detect grouped installs as a different layout");
            }
        }

        [Test]
        public void DetectTargets_WhenLegacyThirdPartySkillsExist_ReportsInstalled()
        {
            string temporaryRoot = CreateTemporaryProjectRoot();
            foreach (string dir in SkillTargetDetector.SkillTargetDirs)
            {
                string targetRoot = Path.Combine(temporaryRoot, dir);
                WriteSkillFile(
                    Path.Combine(targetRoot, SkillInstallLayout.SkillsDirName, "acme-third-party"),
                    "---\nname: acme-third-party\ntoolName: acme-third-party\n---\n");
            }

            ToolSkillSynchronizer.SkillTargetInfo[] detectedTargets = SkillTargetDetector.DetectTargetsForLayoutStateAtProjectRoot(
                    temporaryRoot,
                    requireSkillsDirectory: true,
                    groupSkillsUnderUnityCliLoop: false,
                    includeFreshnessCheck: true)
                .ToArray();

            Assert.AreEqual(SkillTargetDetector.SkillTargetDirs.Length, detectedTargets.Length);
            foreach (ToolSkillSynchronizer.SkillTargetInfo target in detectedTargets)
            {
                Assert.IsTrue(target.HasExistingSkills,
                    $"Target '{target.DirName}' should treat legacy third-party skills as installed");
            }
        }

        [Test]
        public void DetectTargets_WhenOnlyGroupedThirdPartySkillsExist_DoesNotReportInstalled()
        {
            string temporaryRoot = CreateTemporaryProjectRoot();
            foreach (string dir in SkillTargetDetector.SkillTargetDirs)
            {
                string targetRoot = Path.Combine(temporaryRoot, dir);
                WriteSkillFile(
                    Path.Combine(
                        targetRoot,
                        SkillInstallLayout.SkillsDirName,
                        SkillInstallLayout.ManagedSkillsDirName,
                        "acme-third-party"),
                    "---\nname: acme-third-party\ntoolName: acme-third-party\n---\n");
            }

            ToolSkillSynchronizer.SkillTargetInfo[] detectedTargets = SkillTargetDetector.DetectTargetsForLayoutStateAtProjectRoot(
                    temporaryRoot,
                    requireSkillsDirectory: true,
                    groupSkillsUnderUnityCliLoop: true,
                    includeFreshnessCheck: true)
                .ToArray();

            Assert.AreEqual(SkillTargetDetector.SkillTargetDirs.Length, detectedTargets.Length);
            foreach (ToolSkillSynchronizer.SkillTargetInfo target in detectedTargets)
            {
                Assert.IsFalse(target.HasExistingSkills,
                    $"Target '{target.DirName}' should ignore grouped third-party skills outside uLoop management");
            }
        }

        [Test]
        public void DetectTargets_WhenOnlyManualLegacySkillsExist_DoesNotReportInstalled()
        {
            string temporaryRoot = CreateTemporaryProjectRoot();
            foreach (string dir in SkillTargetDetector.SkillTargetDirs)
            {
                string targetRoot = Path.Combine(temporaryRoot, dir);
                WriteSkillFile(
                    Path.Combine(targetRoot, SkillInstallLayout.SkillsDirName, "find-orphaned-meta"),
                    "---\nname: find-orphaned-meta\n---\n");
            }

            ToolSkillSynchronizer.SkillTargetInfo[] detectedTargets = SkillTargetDetector.DetectTargetsForLayoutStateAtProjectRoot(
                    temporaryRoot,
                    requireSkillsDirectory: true,
                    groupSkillsUnderUnityCliLoop: false,
                    includeFreshnessCheck: true)
                .ToArray();

            Assert.AreEqual(SkillTargetDetector.SkillTargetDirs.Length, detectedTargets.Length);
            foreach (ToolSkillSynchronizer.SkillTargetInfo target in detectedTargets)
            {
                Assert.IsFalse(target.HasExistingSkills,
                    $"Target '{target.DirName}' should ignore local manual skills outside uLoop management");
            }
        }

        [Test]
        public void DetectTargets_WhenSkillExistsInDependentPackageCache_ReportsInstalled()
        {
            string temporaryRoot = CreateTemporaryProjectRoot();
            WriteManifestDependencies(
                temporaryRoot,
                "\"com.example.cached\": \"1.0.0\"");

            string skillDir = Path.Combine(
                temporaryRoot,
                "Library",
                "PackageCache",
                "com.example.cached@1.0.0",
                "Editor",
                "CachedTool",
                "Skill");
            Directory.CreateDirectory(skillDir);
            File.WriteAllText(
                Path.Combine(skillDir, SkillInstallLayout.SkillFileName),
                "---\nname: uloop-cached-skill\n---\n");
            File.WriteAllText(Path.Combine(skillDir, "reference.md"), "reference");

            string installedSkillDir = Path.Combine(
                temporaryRoot,
                ".claude",
                SkillInstallLayout.SkillsDirName,
                SkillInstallLayout.ManagedSkillsDirName,
                "uloop-cached-skill");
            WriteSkillFile(installedSkillDir, "---\nname: uloop-cached-skill\n---\n");
            File.WriteAllText(Path.Combine(installedSkillDir, "reference.md"), "reference");

            ToolSkillSynchronizer.SkillTargetInfo[] detectedTargets = SkillTargetDetector.DetectTargetsForLayoutStateAtProjectRoot(
                    temporaryRoot,
                    requireSkillsDirectory: true,
                    groupSkillsUnderUnityCliLoop: true,
                    includeFreshnessCheck: true)
                .ToArray();

            Assert.That(detectedTargets.Length, Is.EqualTo(1));
            Assert.That(detectedTargets[0].InstallState, Is.EqualTo(SkillInstallState.Installed));
        }

        [Test]
        public void DetectTargets_WhenExpectedLayoutMatchesSourceContent_ReportsInstalled()
        {
            string temporaryRoot = CreateTemporaryProjectRoot();
            CreateFakeSourceSkill(temporaryRoot, "uloop-fake-skill", "FakeTool", "reference.md", "reference");

            string targetRoot = Path.Combine(temporaryRoot, ".claude");
            WriteSkillFile(
                Path.Combine(
                    targetRoot,
                    SkillInstallLayout.SkillsDirName,
                    SkillInstallLayout.ManagedSkillsDirName,
                    "uloop-fake-skill"),
                "---\nname: uloop-fake-skill\n---\n");
            File.WriteAllText(Path.Combine(
                targetRoot,
                SkillInstallLayout.SkillsDirName,
                SkillInstallLayout.ManagedSkillsDirName,
                "uloop-fake-skill",
                "reference.md"), "reference");

            ToolSkillSynchronizer.SkillTargetInfo[] detectedTargets = SkillTargetDetector.DetectTargetsForLayoutStateAtProjectRoot(
                    temporaryRoot,
                    requireSkillsDirectory: true,
                    groupSkillsUnderUnityCliLoop: true,
                    includeFreshnessCheck: true)
                .ToArray();

            Assert.That(detectedTargets.Length, Is.EqualTo(1));
            Assert.That(detectedTargets[0].InstallState, Is.EqualTo(SkillInstallState.Installed));
            Assert.That(detectedTargets[0].HasExistingSkills, Is.True);
        }

        [Test]
        public void DetectTargets_WhenExpectedLayoutDiffersFromSourceContent_ReportsOutdated()
        {
            string temporaryRoot = CreateTemporaryProjectRoot();
            CreateFakeSourceSkill(temporaryRoot, "uloop-fake-skill", "FakeTool", "reference.md", "reference");

            string installedSkillDir = Path.Combine(
                temporaryRoot,
                ".claude",
                SkillInstallLayout.SkillsDirName,
                SkillInstallLayout.ManagedSkillsDirName,
                "uloop-fake-skill");
            WriteSkillFile(installedSkillDir, "---\nname: uloop-fake-skill\n---\nchanged");
            File.WriteAllText(Path.Combine(installedSkillDir, "reference.md"), "reference");

            ToolSkillSynchronizer.SkillTargetInfo[] detectedTargets = SkillTargetDetector.DetectTargetsForLayoutStateAtProjectRoot(
                    temporaryRoot,
                    requireSkillsDirectory: true,
                    groupSkillsUnderUnityCliLoop: true,
                    includeFreshnessCheck: true)
                .ToArray();

            Assert.That(detectedTargets.Length, Is.EqualTo(1));
            Assert.That(detectedTargets[0].InstallState, Is.EqualTo(SkillInstallState.Outdated));
            Assert.That(detectedTargets[0].HasExistingSkills, Is.True);
        }

        [Test]
        public void DetectTargets_WhenOnlyInternalSkillsAreMissing_IgnoresThem()
        {
            string temporaryRoot = CreateTemporaryProjectRoot();
            CreateFakeSourceSkill(temporaryRoot, "uloop-public-skill", "PublicTool", "reference.md", "reference");
            CreateFakeSourceSkill(
                temporaryRoot,
                "uloop-internal-skill",
                "InternalTool",
                "reference.md",
                "internal-reference",
                isInternal: true);

            string installedSkillDir = Path.Combine(
                temporaryRoot,
                ".claude",
                SkillInstallLayout.SkillsDirName,
                SkillInstallLayout.ManagedSkillsDirName,
                "uloop-public-skill");
            WriteSkillFile(installedSkillDir, "---\nname: uloop-public-skill\n---\n");
            File.WriteAllText(Path.Combine(installedSkillDir, "reference.md"), "reference");

            ToolSkillSynchronizer.SkillTargetInfo[] detectedTargets = SkillTargetDetector.DetectTargetsForLayoutStateAtProjectRoot(
                    temporaryRoot,
                    requireSkillsDirectory: true,
                    groupSkillsUnderUnityCliLoop: true,
                    includeFreshnessCheck: true)
                .ToArray();

            Assert.That(detectedTargets.Length, Is.EqualTo(1));
            Assert.That(detectedTargets[0].InstallState, Is.EqualTo(SkillInstallState.Installed));
            Assert.That(detectedTargets[0].HasExistingSkills, Is.True);
        }

        [Test]
        public void DetectTargets_WhenSourceOnlyHasMetaSidecars_IgnoresThem()
        {
            string temporaryRoot = CreateTemporaryProjectRoot();
            CreateFakeSourceSkill(
                temporaryRoot,
                "uloop-public-skill",
                "PublicTool",
                "reference.md",
                "reference",
                sourceMetaFileRelativePath: "reference.md.meta");

            string installedSkillDir = Path.Combine(
                temporaryRoot,
                ".claude",
                SkillInstallLayout.SkillsDirName,
                SkillInstallLayout.ManagedSkillsDirName,
                "uloop-public-skill");
            WriteSkillFile(installedSkillDir, "---\nname: uloop-public-skill\n---\n");
            File.WriteAllText(Path.Combine(installedSkillDir, "reference.md"), "reference");

            ToolSkillSynchronizer.SkillTargetInfo[] detectedTargets = SkillTargetDetector.DetectTargetsForLayoutStateAtProjectRoot(
                    temporaryRoot,
                    requireSkillsDirectory: true,
                    groupSkillsUnderUnityCliLoop: true,
                    includeFreshnessCheck: true)
                .ToArray();

            Assert.That(detectedTargets.Length, Is.EqualTo(1));
            Assert.That(detectedTargets[0].InstallState, Is.EqualTo(SkillInstallState.Installed));
            Assert.That(detectedTargets[0].HasExistingSkills, Is.True);
        }

        [Test]
        public void DetectTargets_WhenInstalledSkillHasExtraFiles_ReportsOutdated()
        {
            string temporaryRoot = CreateTemporaryProjectRoot();
            CreateFakeSourceSkill(
                temporaryRoot,
                "uloop-public-skill",
                "PublicTool",
                "reference.md",
                "reference");

            string installedSkillDir = Path.Combine(
                temporaryRoot,
                ".claude",
                SkillInstallLayout.SkillsDirName,
                SkillInstallLayout.ManagedSkillsDirName,
                "uloop-public-skill");
            WriteSkillFile(installedSkillDir, "---\nname: uloop-public-skill\n---\n");
            File.WriteAllText(Path.Combine(installedSkillDir, "reference.md"), "reference");
            File.WriteAllText(Path.Combine(installedSkillDir, "stale.md"), "stale");

            ToolSkillSynchronizer.SkillTargetInfo[] detectedTargets = SkillTargetDetector.DetectTargetsForLayoutStateAtProjectRoot(
                    temporaryRoot,
                    requireSkillsDirectory: true,
                    groupSkillsUnderUnityCliLoop: true,
                    includeFreshnessCheck: true)
                .ToArray();

            Assert.That(detectedTargets.Length, Is.EqualTo(1));
            Assert.That(detectedTargets[0].InstallState, Is.EqualTo(SkillInstallState.Outdated));
            Assert.That(detectedTargets[0].HasExistingSkills, Is.True);
        }

        // Tests that CRLF-only drift from Windows checkouts does not mark installed skills stale.
        [Test]
        public void DetectTargets_WhenInstalledSkillUsesCrlfLineEndings_ReportsInstalled()
        {
            string temporaryRoot = CreateTemporaryProjectRoot();
            CreateFakeSourceSkill(
                temporaryRoot,
                "uloop-public-skill",
                "PublicTool",
                "reference.md",
                "line1\nline2\n");

            string installedSkillDir = Path.Combine(
                temporaryRoot,
                ".claude",
                SkillInstallLayout.SkillsDirName,
                SkillInstallLayout.ManagedSkillsDirName,
                "uloop-public-skill");
            WriteSkillFile(installedSkillDir, "---\r\nname: uloop-public-skill\r\n---\r\n");
            File.WriteAllText(Path.Combine(installedSkillDir, "reference.md"), "line1\r\nline2\r\n");

            ToolSkillSynchronizer.SkillTargetInfo[] detectedTargets = SkillTargetDetector.DetectTargetsForLayoutStateAtProjectRoot(
                    temporaryRoot,
                    requireSkillsDirectory: true,
                    groupSkillsUnderUnityCliLoop: true,
                    includeFreshnessCheck: true)
                .ToArray();

            Assert.That(detectedTargets.Length, Is.EqualTo(1));
            Assert.That(detectedTargets[0].InstallState, Is.EqualTo(SkillInstallState.Installed));
            Assert.That(detectedTargets[0].HasExistingSkills, Is.True);
        }

        // Tests that synchronizing skills writes deterministic LF line endings.
        [Test]
        public async Task InstallSkillFilesAtProjectRoot_WhenSourceSkillUsesCrlfLineEndings_WritesLfGeneratedCopy()
        {
            string temporaryRoot = CreateTemporaryProjectRoot();
            CreateFakeSourceSkill(
                temporaryRoot,
                "uloop-public-skill",
                "PublicTool",
                "reference.md",
                "line1\r\nline2\r\n");

            ToolSkillSynchronizer.SkillTargetInfo target = new(
                "Claude Code",
                ".claude",
                "--claude",
                hasSkillsDirectory: true,
                hasExistingSkills: false);

            await ToolSkillSynchronizer.InstallSkillFilesAtProjectRoot(
                temporaryRoot,
                new[] { target },
                groupSkillsUnderUnityCliLoop: true,
                disabledTools: Array.Empty<string>(),
                ct: CancellationToken.None);

            string installedReferencePath = Path.Combine(
                temporaryRoot,
                ".claude",
                SkillInstallLayout.SkillsDirName,
                SkillInstallLayout.ManagedSkillsDirName,
                "uloop-public-skill",
                "reference.md");
            byte[] installedBytes = File.ReadAllBytes(installedReferencePath);

            Assert.That(installedBytes, Has.No.Member((byte)'\r'));
        }

        // Tests that rollback backups preserve the previous generated skill bytes.
        [Test]
        public void ReadSkillFilesForRollback_WhenGeneratedSkillUsesCrlfLineEndings_PreservesRawBytes()
        {
            string temporaryRoot = CreateTemporaryProjectRoot();
            string installedSkillDir = Path.Combine(
                temporaryRoot,
                ".claude",
                SkillInstallLayout.SkillsDirName,
                SkillInstallLayout.ManagedSkillsDirName,
                "uloop-public-skill");
            WriteSkillFile(installedSkillDir, "---\r\nname: uloop-public-skill\r\n---\r\n");
            string installedReferencePath = Path.Combine(installedSkillDir, "reference.md");
            File.WriteAllText(installedReferencePath, "line1\r\nline2\r\n");
            byte[] backupBytes = File.ReadAllBytes(installedReferencePath);

            Dictionary<string, byte[]> backupFiles =
                SkillDirectoryContentSynchronizer.ReadSkillFilesForRollback(installedSkillDir);

            Assert.That(backupFiles["reference.md"], Is.EqualTo(backupBytes));
        }

        [Test]
        public void DetectTargets_WhenDeprecatedManagedSkillDirectoryExists_DoesNotReportOutdated()
        {
            string temporaryRoot = CreateTemporaryProjectRoot();
            CreateFakeSourceSkill(
                temporaryRoot,
                "uloop-public-skill",
                "PublicTool",
                "reference.md",
                "reference");

            string targetRoot = Path.Combine(temporaryRoot, ".claude");
            string installedSkillDir = Path.Combine(
                targetRoot,
                SkillInstallLayout.SkillsDirName,
                SkillInstallLayout.ManagedSkillsDirName,
                "uloop-public-skill");
            WriteSkillFile(installedSkillDir, "---\nname: uloop-public-skill\n---\n");
            File.WriteAllText(Path.Combine(installedSkillDir, "reference.md"), "reference");

            string deprecatedSkillDir = Path.Combine(
                targetRoot,
                SkillInstallLayout.SkillsDirName,
                SkillInstallLayout.ManagedSkillsDirName,
                "uloop-capture-window");
            WriteSkillFile(deprecatedSkillDir, "---\nname: uloop-capture-window\n---\n");
            string executeMenuItemSkillDir = Path.Combine(
                targetRoot,
                SkillInstallLayout.SkillsDirName,
                SkillInstallLayout.ManagedSkillsDirName,
                "uloop-execute-menu-item");
            WriteSkillFile(executeMenuItemSkillDir, "---\nname: uloop-execute-menu-item\n---\n");

            ToolSkillSynchronizer.SkillTargetInfo[] detectedTargets = SkillTargetDetector.DetectTargetsForLayoutStateAtProjectRoot(
                    temporaryRoot,
                    requireSkillsDirectory: true,
                    groupSkillsUnderUnityCliLoop: true,
                    includeFreshnessCheck: true)
                .ToArray();

            Assert.That(detectedTargets.Length, Is.EqualTo(1));
            Assert.That(detectedTargets[0].InstallState, Is.EqualTo(SkillInstallState.Installed));
        }

        [Test]
        public async Task InstallSkillFilesAtProjectRoot_WhenDeprecatedManagedSkillDirectoryExists_RemovesIt()
        {
            string temporaryRoot = CreateTemporaryProjectRoot();
            CreateFakeSourceSkill(
                temporaryRoot,
                "uloop-public-skill",
                "PublicTool",
                "reference.md",
                "reference");

            string targetRoot = Path.Combine(temporaryRoot, ".claude");
            Directory.CreateDirectory(Path.Combine(targetRoot, SkillInstallLayout.SkillsDirName));

            string deprecatedSkillDir = Path.Combine(
                targetRoot,
                SkillInstallLayout.SkillsDirName,
                SkillInstallLayout.ManagedSkillsDirName,
                "uloop-capture-window");
            WriteSkillFile(deprecatedSkillDir, "---\nname: uloop-capture-window\n---\n");
            string executeMenuItemSkillDir = Path.Combine(
                targetRoot,
                SkillInstallLayout.SkillsDirName,
                SkillInstallLayout.ManagedSkillsDirName,
                "uloop-execute-menu-item");
            WriteSkillFile(executeMenuItemSkillDir, "---\nname: uloop-execute-menu-item\n---\n");

            ToolSkillSynchronizer.SkillTargetInfo target = new(
                "Claude Code",
                ".claude",
                "--claude",
                hasSkillsDirectory: true,
                hasExistingSkills: true,
                installState: SkillInstallState.Outdated);

            ToolSkillSynchronizer.SkillInstallResult result =
                await ToolSkillSynchronizer.InstallSkillFilesAtProjectRoot(
                    temporaryRoot,
                    new[] { target },
                    groupSkillsUnderUnityCliLoop: true,
                    disabledTools: Array.Empty<string>(),
                    ct: CancellationToken.None);

            string installedSkillDir = Path.Combine(
                targetRoot,
                SkillInstallLayout.SkillsDirName,
                SkillInstallLayout.ManagedSkillsDirName,
                "uloop-public-skill");

            Assert.That(result.IsSuccessful, Is.True);
            Assert.That(Directory.Exists(deprecatedSkillDir), Is.False);
            Assert.That(Directory.Exists(executeMenuItemSkillDir), Is.False);
            Assert.That(File.Exists(Path.Combine(installedSkillDir, SkillInstallLayout.SkillFileName)), Is.True);
            Assert.That(File.ReadAllText(Path.Combine(installedSkillDir, "reference.md")), Is.EqualTo("reference"));
        }

        [Test]
        public async Task InstallSkillFilesAtProjectRoot_WhenSettingsUpdateButtonTargetIsUsed_RemovesDeprecatedSkill()
        {
            string temporaryRoot = CreateTemporaryProjectRoot();
            CreateFakeSourceSkill(
                temporaryRoot,
                "uloop-public-skill",
                "PublicTool",
                "reference.md",
                "reference");

            string targetRoot = Path.Combine(temporaryRoot, ".claude");
            Directory.CreateDirectory(Path.Combine(targetRoot, SkillInstallLayout.SkillsDirName));

            string executeMenuItemSkillDir = Path.Combine(
                targetRoot,
                SkillInstallLayout.SkillsDirName,
                "uloop-execute-menu-item");
            WriteSkillFile(executeMenuItemSkillDir, "---\nname: uloop-execute-menu-item\n---\n");

            ToolSkillSynchronizer.SkillTargetInfo target = new(
                "Claude Code",
                ".claude",
                "--claude",
                hasSkillsDirectory: true,
                hasExistingSkills: false);

            ToolSkillSynchronizer.SkillInstallResult result =
                await ToolSkillSynchronizer.InstallSkillFilesAtProjectRoot(
                    temporaryRoot,
                    new[] { target },
                    groupSkillsUnderUnityCliLoop: false,
                    disabledTools: Array.Empty<string>(),
                    ct: CancellationToken.None);

            Assert.That(result.IsSuccessful, Is.True);
            Assert.That(Directory.Exists(executeMenuItemSkillDir), Is.False);
        }

        [Test]
        public void DetectTargets_WhenSourceSkillNameIsNotSafePathComponent_IgnoresIt()
        {
            string temporaryRoot = CreateTemporaryProjectRoot();
            CreateFakeSourceSkill(
                temporaryRoot,
                "../uloop-stale-skill",
                "UnsafeTool",
                "reference.md",
                "reference");
            Directory.CreateDirectory(Path.Combine(
                temporaryRoot,
                ".claude",
                SkillInstallLayout.SkillsDirName));

            ToolSkillSynchronizer.SkillTargetInfo[] detectedTargets = SkillTargetDetector.DetectTargetsForLayoutStateAtProjectRoot(
                    temporaryRoot,
                    requireSkillsDirectory: true,
                    groupSkillsUnderUnityCliLoop: true,
                    includeFreshnessCheck: true)
                .ToArray();

            Assert.That(detectedTargets.Length, Is.EqualTo(1));
            Assert.That(detectedTargets[0].InstallState, Is.EqualTo(SkillInstallState.Missing));
        }

        [TestCase("C:\\temp\\uloop-bad-skill")]
        [TestCase("uloop:bad-skill")]
        [TestCase("uloop*bad-skill")]
        public void DetectTargets_WhenSourceSkillNameContainsUnsafePathCharacters_IgnoresIt(string unsafeSkillName)
        {
            string temporaryRoot = CreateTemporaryProjectRoot();
            CreateFakeSourceSkill(
                temporaryRoot,
                unsafeSkillName,
                "UnsafeTool",
                "reference.md",
                "reference");
            Directory.CreateDirectory(Path.Combine(
                temporaryRoot,
                ".claude",
                SkillInstallLayout.SkillsDirName));

            ToolSkillSynchronizer.SkillTargetInfo[] detectedTargets = SkillTargetDetector.DetectTargetsForLayoutStateAtProjectRoot(
                    temporaryRoot,
                    requireSkillsDirectory: true,
                    groupSkillsUnderUnityCliLoop: true,
                    includeFreshnessCheck: true)
                .ToArray();

            Assert.That(detectedTargets.Length, Is.EqualTo(1));
            Assert.That(detectedTargets[0].InstallState, Is.EqualTo(SkillInstallState.Missing));
        }

        [Test]
        public async Task InstallSpecificSkillFilesAtProjectRoot_WhenSingleSkillSourceIsProvided_InstallsOnlyThatSkill()
        {
            // Tests that temporary migration skill installation can reuse the synchronizer without installing normal skills.
            string temporaryRoot = CreateTemporaryProjectRoot();
            ToolSkillSynchronizer.SkillTargetInfo target = new(
                "Codex CLI",
                ".codex",
                "--codex",
                hasSkillsDirectory: true,
                hasExistingSkills: false);
            SkillInstallLayout.SkillSourceInfo skill = new(
                CliConstants.V3_CLI_INVOCATION_MIGRATION_SKILL_NAME,
                string.Empty,
                new Dictionary<string, byte[]>
                {
                    [SkillInstallLayout.SkillFileName] = Encoding.UTF8.GetBytes(
                        "---\nname: v3-cli-invocation-migration\n---\n"),
                    ["scripts/detect.sh"] = Encoding.UTF8.GetBytes("#!/bin/sh\n")
                });

            ToolSkillSynchronizer.SkillInstallResult result =
                await V3MigrationSkillInstaller.InstallSpecificSkillFilesAtProjectRoot(
                    temporaryRoot,
                    new[] { target },
                    skill,
                    groupSkillsUnderUnityCliLoop: false,
                    ct: CancellationToken.None);

            string installedSkillDir = Path.Combine(
                temporaryRoot,
                ".codex",
                SkillInstallLayout.SkillsDirName,
                CliConstants.V3_CLI_INVOCATION_MIGRATION_SKILL_NAME);
            SkillInstallState installState = V3MigrationSkillInstaller.GetSkillInstallStateAtProjectRoot(
                temporaryRoot,
                target,
                skill,
                groupSkillsUnderUnityCliLoop: false);

            Assert.That(result.IsSuccessful, Is.True);
            Assert.That(File.Exists(Path.Combine(installedSkillDir, SkillInstallLayout.SkillFileName)), Is.True);
            Assert.That(File.Exists(Path.Combine(installedSkillDir, "scripts", "detect.sh")), Is.True);
            Assert.That(installState, Is.EqualTo(SkillInstallState.Installed));
        }

        [Test]
        public void InstallSpecificSkillsForTarget_WhenCancellationRequested_DoesNotWriteSkill()
        {
            // Tests that target installation observes cancellation before writing managed skill files.
            string temporaryRoot = CreateTemporaryProjectRoot();
            ToolSkillSynchronizer.SkillTargetInfo target = new(
                "Codex CLI",
                ".codex",
                "--codex",
                hasSkillsDirectory: true,
                hasExistingSkills: false);
            SkillInstallLayout.SkillSourceInfo skill = new(
                "uloop-cancelled-skill",
                string.Empty,
                new Dictionary<string, byte[]>
                {
                    [SkillInstallLayout.SkillFileName] = Encoding.UTF8.GetBytes(
                        "---\nname: uloop-cancelled-skill\n---\n")
                });
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(() =>
                SkillTargetInstaller.InstallSpecificSkillsForTarget(
                    temporaryRoot,
                    target,
                    Array.Empty<SkillInstallLayout.SkillSourceInfo>(),
                    new[] { skill },
                    groupSkillsUnderUnityCliLoop: false,
                    cancellation.Token));
            Assert.That(
                Directory.Exists(Path.Combine(
                    temporaryRoot,
                    ".codex",
                    SkillInstallLayout.SkillsDirName,
                    "uloop-cancelled-skill")),
                Is.False);
        }

        [Test]
        public void ValidateV3MigrationSkillSourceName_WhenNameDiffers_Throws()
        {
            // Tests that the packaged migration skill is rejected if its frontmatter name violates the contract.
            SkillInstallLayout.SkillSourceInfo skill = new(
                "unexpected-migration-skill",
                string.Empty,
                new Dictionary<string, byte[]>
                {
                    [SkillInstallLayout.SkillFileName] = Encoding.UTF8.GetBytes(
                        "---\nname: unexpected-migration-skill\n---\n")
                });

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                V3MigrationSkillInstaller.ValidateV3MigrationSkillSourceName(skill));

            Assert.That(exception.Message, Does.Contain(CliConstants.V3_CLI_INVOCATION_MIGRATION_SKILL_NAME));
        }

        [Test]
        public async Task GetV3MigrationSkillInstallStateAtProjectRoot_WhenSkillExistsInAlternateLayout_ReturnsInstalled()
        {
            // Tests that the temporary migration skill is detected even when it exists in the alternate layout.
            string temporaryRoot = CreateTemporaryProjectRoot();
            ToolSkillSynchronizer.SkillTargetInfo target = new(
                "Claude Code",
                ".claude",
                "--claude",
                hasSkillsDirectory: true,
                hasExistingSkills: false);

            ToolSkillSynchronizer.SkillInstallResult result =
                await V3MigrationSkillInstaller.InstallV3MigrationSkillFilesAtProjectRoot(
                    temporaryRoot,
                    new[] { target },
                    groupSkillsUnderUnityCliLoop: true,
                    ct: CancellationToken.None);

            SkillInstallState installState = V3MigrationSkillInstaller.GetV3MigrationSkillInstallStateAtProjectRoot(
                temporaryRoot,
                target,
                groupSkillsUnderUnityCliLoop: false);

            Assert.That(result.IsSuccessful, Is.True);
            Assert.That(installState, Is.EqualTo(SkillInstallState.Installed));
        }

        [Test]
        public void GetV3MigrationSkillInstallStateAtProjectRoot_WhenSkillDirectoryLacksSkillFile_ReturnsMissing()
        {
            // Tests that a leftover migration skill directory without SKILL.md is not treated as installed.
            string temporaryRoot = CreateTemporaryProjectRoot();
            ToolSkillSynchronizer.SkillTargetInfo target = new(
                "Codex CLI",
                ".codex",
                "--codex",
                hasSkillsDirectory: true,
                hasExistingSkills: false);
            string targetRoot = Path.Combine(temporaryRoot, ".codex");
            string migrationSkillDir = SkillInstallLayout.GetInstalledSkillDirectoryPathForLayout(
                targetRoot,
                CliConstants.V3_CLI_INVOCATION_MIGRATION_SKILL_NAME,
                groupSkillsUnderUnityCliLoop: false);
            Directory.CreateDirectory(Path.Combine(migrationSkillDir, "references"));

            SkillInstallState installState = V3MigrationSkillInstaller.GetV3MigrationSkillInstallStateAtProjectRoot(
                temporaryRoot,
                target,
                groupSkillsUnderUnityCliLoop: false);

            Assert.That(installState, Is.EqualTo(SkillInstallState.Missing));
        }

        [Test]
        public async Task RemoveSpecificSkillFilesAtProjectRoot_WhenSkillExists_RemovesOnlyThatSkill()
        {
            // Tests that temporary migration skill removal leaves unrelated installed skills in place.
            string temporaryRoot = CreateTemporaryProjectRoot();
            ToolSkillSynchronizer.SkillTargetInfo target = new(
                "Codex CLI",
                ".codex",
                "--codex",
                hasSkillsDirectory: true,
                hasExistingSkills: false);
            string targetRoot = Path.Combine(temporaryRoot, ".codex");
            string migrationSkillDir = Path.Combine(
                targetRoot,
                SkillInstallLayout.SkillsDirName,
                CliConstants.V3_CLI_INVOCATION_MIGRATION_SKILL_NAME);
            string unrelatedSkillDir = Path.Combine(
                targetRoot,
                SkillInstallLayout.SkillsDirName,
                "uloop-compile");
            WriteSkillFile(migrationSkillDir, "---\nname: v3-cli-invocation-migration\n---\n");
            WriteSkillFile(unrelatedSkillDir, "---\nname: uloop-compile\n---\n");

            ToolSkillSynchronizer.SkillInstallResult result =
                await V3MigrationSkillInstaller.RemoveSpecificSkillFilesAtProjectRoot(
                    temporaryRoot,
                    new[] { target },
                    CliConstants.V3_CLI_INVOCATION_MIGRATION_SKILL_NAME,
                    groupSkillsUnderUnityCliLoop: false,
                    ct: CancellationToken.None);

            Assert.That(result.IsSuccessful, Is.True);
            Assert.That(Directory.Exists(migrationSkillDir), Is.False);
            Assert.That(Directory.Exists(unrelatedSkillDir), Is.True);
        }

        [Test]
        public async Task RemoveSpecificSkillFilesAtProjectRoot_WithMultipleTargets_ReportsAllTargetsSucceeded()
        {
            // Tests that migration skill removal reports one successful result per requested target.
            string temporaryRoot = CreateTemporaryProjectRoot();
            ToolSkillSynchronizer.SkillTargetInfo codexTarget = new(
                "Codex CLI",
                ".codex",
                "--codex",
                hasSkillsDirectory: true,
                hasExistingSkills: false);
            ToolSkillSynchronizer.SkillTargetInfo claudeTarget = new(
                "Claude Code",
                ".claude",
                "--claude",
                hasSkillsDirectory: true,
                hasExistingSkills: false);
            string codexSkillDir = Path.Combine(
                temporaryRoot,
                ".codex",
                SkillInstallLayout.SkillsDirName,
                CliConstants.V3_CLI_INVOCATION_MIGRATION_SKILL_NAME);
            string claudeSkillDir = Path.Combine(
                temporaryRoot,
                ".claude",
                SkillInstallLayout.SkillsDirName,
                CliConstants.V3_CLI_INVOCATION_MIGRATION_SKILL_NAME);
            WriteSkillFile(codexSkillDir, "---\nname: v3-cli-invocation-migration\n---\n");
            WriteSkillFile(claudeSkillDir, "---\nname: v3-cli-invocation-migration\n---\n");

            ToolSkillSynchronizer.SkillInstallResult result =
                await V3MigrationSkillInstaller.RemoveSpecificSkillFilesAtProjectRoot(
                    temporaryRoot,
                    new[] { codexTarget, claudeTarget },
                    CliConstants.V3_CLI_INVOCATION_MIGRATION_SKILL_NAME,
                    groupSkillsUnderUnityCliLoop: false,
                    ct: CancellationToken.None);

            Assert.That(result.AttemptedTargets, Is.EqualTo(2));
            Assert.That(result.SucceededTargets, Is.EqualTo(2));
            Assert.That(result.IsSuccessful, Is.True);
            Assert.That(Directory.Exists(codexSkillDir), Is.False);
            Assert.That(Directory.Exists(claudeSkillDir), Is.False);
        }

        [Test]
        public async Task RemoveV3MigrationSkillFilesAtProjectRoot_WhenSkillExistsInBothLayouts_RemovesBothLayouts()
        {
            // Tests that temporary migration skill removal cleans up both supported install layouts.
            string temporaryRoot = CreateTemporaryProjectRoot();
            ToolSkillSynchronizer.SkillTargetInfo target = new(
                "Claude Code",
                ".claude",
                "--claude",
                hasSkillsDirectory: true,
                hasExistingSkills: false);
            string targetRoot = Path.Combine(temporaryRoot, ".claude");
            string flatMigrationSkillDir = SkillInstallLayout.GetInstalledSkillDirectoryPathForLayout(
                targetRoot,
                CliConstants.V3_CLI_INVOCATION_MIGRATION_SKILL_NAME,
                groupSkillsUnderUnityCliLoop: false);
            string groupedMigrationSkillDir = SkillInstallLayout.GetInstalledSkillDirectoryPathForLayout(
                targetRoot,
                CliConstants.V3_CLI_INVOCATION_MIGRATION_SKILL_NAME,
                groupSkillsUnderUnityCliLoop: true);
            string unrelatedSkillDir = Path.Combine(
                targetRoot,
                SkillInstallLayout.SkillsDirName,
                "uloop-compile");
            WriteSkillFile(flatMigrationSkillDir, "---\nname: v3-cli-invocation-migration\n---\n");
            WriteSkillFile(groupedMigrationSkillDir, "---\nname: v3-cli-invocation-migration\n---\n");
            WriteSkillFile(unrelatedSkillDir, "---\nname: uloop-compile\n---\n");

            ToolSkillSynchronizer.SkillInstallResult result =
                await V3MigrationSkillInstaller.RemoveV3MigrationSkillFilesAtProjectRoot(
                    temporaryRoot,
                    new[] { target },
                    groupSkillsUnderUnityCliLoop: false,
                    ct: CancellationToken.None);

            Assert.That(result.IsSuccessful, Is.True);
            Assert.That(Directory.Exists(flatMigrationSkillDir), Is.False);
            Assert.That(Directory.Exists(groupedMigrationSkillDir), Is.False);
            Assert.That(Directory.Exists(unrelatedSkillDir), Is.True);
        }

        private string CreateTemporaryProjectRoot()
        {
            string temporaryRoot = Path.Combine(
                Path.GetTempPath(),
                "ToolSkillSynchronizerTests",
                System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryRoot);
            _temporaryRoots = _temporaryRoots.Append(temporaryRoot).ToArray();
            return temporaryRoot;
        }

        private static void WriteSkillFile(string skillDir, string content = "---\nname: uloop-compile\n---\n")
        {
            Directory.CreateDirectory(skillDir);
            File.WriteAllText(Path.Combine(skillDir, SkillInstallLayout.SkillFileName), content);
        }

        private void RestoreToolSettingsFile()
        {
            if (_toolSettingsFileExisted)
            {
                File.WriteAllText(ToolSettingsFilePath, _toolSettingsFileContent);
                return;
            }

            if (File.Exists(ToolSettingsFilePath))
            {
                File.Delete(ToolSettingsFilePath);
            }
        }

        private static void CreateFakeSourceSkill(
            string projectRoot,
            string skillName,
            string toolDirectoryName,
            string additionalFileRelativePath,
            string additionalFileContent,
            bool isInternal = false,
            string sourceMetaFileRelativePath = null)
        {
            string skillDir = Path.Combine(
                projectRoot,
                "Packages",
                "com.example.fake",
                "Editor",
                toolDirectoryName,
                "Skill");
            Directory.CreateDirectory(skillDir);
            string internalLine = isInternal ? "internal: true\n" : string.Empty;
            File.WriteAllText(
                Path.Combine(skillDir, SkillInstallLayout.SkillFileName),
                $"---\nname: {skillName}\n{internalLine}---\n");
            File.WriteAllText(Path.Combine(skillDir, additionalFileRelativePath), additionalFileContent);
            if (!string.IsNullOrEmpty(sourceMetaFileRelativePath))
            {
                File.WriteAllText(Path.Combine(skillDir, sourceMetaFileRelativePath), "meta");
            }
        }

        private static string CreateFakeProjectLocalSkill(
            string projectRoot,
            string skillName,
            string toolDirectoryName)
        {
            string skillDir = Path.Combine(
                projectRoot,
                "Assets",
                "SampleFeature",
                "Editor",
                "McpExtensions",
                toolDirectoryName);
            Directory.CreateDirectory(skillDir);
            File.WriteAllText(
                Path.Combine(skillDir, SkillInstallLayout.SkillFileName),
                $"---\nname: {skillName}\n---\n");
            return skillDir;
        }

        private static void WriteManifestDependencies(string projectRoot, string dependenciesContent)
        {
            string packagesDir = Path.Combine(projectRoot, "Packages");
            Directory.CreateDirectory(packagesDir);
            File.WriteAllText(
                Path.Combine(packagesDir, "manifest.json"),
                "{\n  \"dependencies\": {\n" + dependenciesContent + "\n  }\n}");
        }

    }
}
