using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace io.github.hatayama.uLoopMCP
{
    [TestFixture]
    public class ToolSkillSynchronizerTests
    {
        private string _projectRoot;
        private string[] _nonExistentDirsBefore;
        private string[] _temporaryRoots;

        [SetUp]
        public void SetUp()
        {
            _projectRoot = UnityMcpPathResolver.GetProjectRoot();

            _nonExistentDirsBefore = ToolSkillSynchronizer.SkillTargetDirs
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
        }

        [Test]
        public async Task InstallSkillFiles_DoesNotCreateNonExistentTargetDirectories()
        {
            // Arrange: record which target directories don't exist
            UnityEngine.Debug.Assert(_nonExistentDirsBefore.Length > 0,
                "At least one target directory should not exist for this test to be meaningful");

            // Act
            await ToolSkillSynchronizer.InstallSkillFiles();

            // Assert: directories that didn't exist before should still not exist
            foreach (string dir in _nonExistentDirsBefore)
            {
                string fullPath = Path.Combine(_projectRoot, dir);
                Assert.IsFalse(Directory.Exists(fullPath),
                    $"Directory '{dir}' should not be created by InstallSkillFiles when '{dir}' did not exist");
            }
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
                    groupSkillsUnderUnityCliLoop: true);

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
                    groupSkillsUnderUnityCliLoop: false);

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
                    groupSkillsUnderUnityCliLoop: false);

            string installedSkillDir = Path.Combine(
                skillsRoot,
                "uloop-fake-skill");

            Assert.That(result.IsSuccessful, Is.True);
            Assert.That(File.Exists(Path.Combine(installedSkillDir, SkillInstallLayout.SkillFileName)), Is.True);
            Assert.That(Directory.Exists(managedSkillsRoot), Is.False);
        }

        [Test]
        public async Task InstallSkillFilesAtProjectRoot_WhenFlatLayoutRequested_RemovesUnexpectedManagedSkillDirectories()
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
            WriteSkillFile(Path.Combine(managedSkillsRoot, "uloop-stale-skill"));

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
                    groupSkillsUnderUnityCliLoop: false);

            string installedSkillDir = Path.Combine(
                skillsRoot,
                "uloop-fake-skill");

            Assert.That(result.IsSuccessful, Is.True);
            Assert.That(File.Exists(Path.Combine(installedSkillDir, SkillInstallLayout.SkillFileName)), Is.True);
            Assert.That(Directory.Exists(managedSkillsRoot), Is.False);
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
                    groupSkillsUnderUnityCliLoop: false);

            Assert.That(result.IsSuccessful, Is.True);
            Assert.That(Directory.Exists(managedSkillsRoot), Is.False);
        }

        [Test]
        public void DetectTargets_DoesNotIncludeTargetsWithOnlyParentDirectory()
        {
            // Arrange
            string temporaryRoot = CreateTemporaryProjectRoot();
            foreach (string dir in ToolSkillSynchronizer.SkillTargetDirs)
            {
                Directory.CreateDirectory(Path.Combine(temporaryRoot, dir));
            }

            // Act
            string[] detectedTargetDirs = ToolSkillSynchronizer.DetectTargets(temporaryRoot, requireSkillsDirectory: true)
                .Select(target => target.DirName)
                .ToArray();

            // Assert
            foreach (string dir in ToolSkillSynchronizer.SkillTargetDirs)
            {
                Assert.IsFalse(detectedTargetDirs.Contains(dir),
                    $"Target '{dir}' should not be detected when only the parent directory exists");
            }
        }

        [Test]
        public void DetectTargets_WhenParentDirectoryExists_ReportsTargetAsNotOptedIn()
        {
            string temporaryRoot = CreateTemporaryProjectRoot();
            foreach (string dir in ToolSkillSynchronizer.SkillTargetDirs)
            {
                Directory.CreateDirectory(Path.Combine(temporaryRoot, dir));
            }

            ToolSkillSynchronizer.SkillTargetInfo[] detectedTargets = ToolSkillSynchronizer.DetectTargets(
                    temporaryRoot,
                    requireSkillsDirectory: false)
                .ToArray();

            Assert.AreEqual(ToolSkillSynchronizer.SkillTargetDirs.Length, detectedTargets.Length);
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
            foreach (string dir in ToolSkillSynchronizer.SkillTargetDirs)
            {
                Directory.CreateDirectory(Path.Combine(temporaryRoot, dir, SkillInstallLayout.SkillsDirName));
            }

            // Act
            ToolSkillSynchronizer.SkillTargetInfo[] detectedTargets = ToolSkillSynchronizer.DetectTargets(temporaryRoot, requireSkillsDirectory: true)
                .ToArray();

            // Assert
            Assert.AreEqual(ToolSkillSynchronizer.SkillTargetDirs.Length, detectedTargets.Length,
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

            foreach (string dir in ToolSkillSynchronizer.SkillTargetDirs)
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
            foreach (string dir in ToolSkillSynchronizer.SkillTargetDirs)
            {
                string targetRoot = Path.Combine(temporaryRoot, dir);
                WriteSkillFile(Path.Combine(
                    targetRoot,
                    SkillInstallLayout.SkillsDirName,
                    SkillInstallLayout.ManagedSkillsDirName,
                    "uloop-compile"));
            }

            ToolSkillSynchronizer.SkillTargetInfo[] detectedTargets = ToolSkillSynchronizer.DetectTargets(
                    temporaryRoot,
                    requireSkillsDirectory: true)
                .ToArray();

            Assert.AreEqual(ToolSkillSynchronizer.SkillTargetDirs.Length, detectedTargets.Length);
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
            foreach (string dir in ToolSkillSynchronizer.SkillTargetDirs)
            {
                string targetRoot = Path.Combine(temporaryRoot, dir);
                WriteSkillFile(Path.Combine(targetRoot, SkillInstallLayout.SkillsDirName, "uloop-compile"));
            }

            ToolSkillSynchronizer.SkillTargetInfo[] detectedTargets = ToolSkillSynchronizer.DetectTargets(
                    temporaryRoot,
                    requireSkillsDirectory: true,
                    groupSkillsUnderUnityCliLoop: true)
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

            ToolSkillSynchronizer.SkillTargetInfo[] detectedTargets = ToolSkillSynchronizer.DetectTargets(
                    temporaryRoot,
                    requireSkillsDirectory: true,
                    groupSkillsUnderUnityCliLoop: true)
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
            foreach (string dir in ToolSkillSynchronizer.SkillTargetDirs)
            {
                string targetRoot = Path.Combine(temporaryRoot, dir);
                WriteSkillFile(Path.Combine(
                    targetRoot,
                    SkillInstallLayout.SkillsDirName,
                    SkillInstallLayout.ManagedSkillsDirName,
                    "uloop-compile"));
            }

            ToolSkillSynchronizer.SkillTargetInfo[] detectedTargets = ToolSkillSynchronizer.DetectTargets(
                    temporaryRoot,
                    requireSkillsDirectory: true,
                    groupSkillsUnderUnityCliLoop: false)
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
            foreach (string dir in ToolSkillSynchronizer.SkillTargetDirs)
            {
                string targetRoot = Path.Combine(temporaryRoot, dir);
                WriteSkillFile(
                    Path.Combine(targetRoot, SkillInstallLayout.SkillsDirName, "acme-third-party"),
                    "---\nname: acme-third-party\ntoolName: acme-third-party\n---\n");
            }

            ToolSkillSynchronizer.SkillTargetInfo[] detectedTargets = ToolSkillSynchronizer.DetectTargets(
                    temporaryRoot,
                    requireSkillsDirectory: true)
                .ToArray();

            Assert.AreEqual(ToolSkillSynchronizer.SkillTargetDirs.Length, detectedTargets.Length);
            foreach (ToolSkillSynchronizer.SkillTargetInfo target in detectedTargets)
            {
                Assert.IsTrue(target.HasExistingSkills,
                    $"Target '{target.DirName}' should treat legacy third-party skills as installed");
            }
        }

        [Test]
        public void DetectTargets_WhenOnlyManualLegacySkillsExist_DoesNotReportInstalled()
        {
            string temporaryRoot = CreateTemporaryProjectRoot();
            foreach (string dir in ToolSkillSynchronizer.SkillTargetDirs)
            {
                string targetRoot = Path.Combine(temporaryRoot, dir);
                WriteSkillFile(
                    Path.Combine(targetRoot, SkillInstallLayout.SkillsDirName, "find-orphaned-meta"),
                    "---\nname: find-orphaned-meta\n---\n");
            }

            ToolSkillSynchronizer.SkillTargetInfo[] detectedTargets = ToolSkillSynchronizer.DetectTargets(
                    temporaryRoot,
                    requireSkillsDirectory: true)
                .ToArray();

            Assert.AreEqual(ToolSkillSynchronizer.SkillTargetDirs.Length, detectedTargets.Length);
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

            ToolSkillSynchronizer.SkillTargetInfo[] detectedTargets = ToolSkillSynchronizer.DetectTargets(
                    temporaryRoot,
                    requireSkillsDirectory: true,
                    groupSkillsUnderUnityCliLoop: true)
                .ToArray();

            Assert.That(detectedTargets.Length, Is.EqualTo(1));
            Assert.That(detectedTargets[0].InstallState, Is.EqualTo(SkillInstallState.Installed));
        }

        [Test]
        public void AreSkillsInstalled_ReturnsTrueForManagedLegacyAndNamespacedSkillsOnly()
        {
            string temporaryRoot = CreateTemporaryProjectRoot();

            string manualTargetRoot = Path.Combine(temporaryRoot, ".claude");
            WriteSkillFile(
                Path.Combine(manualTargetRoot, SkillInstallLayout.SkillsDirName, "find-orphaned-meta"),
                "---\nname: find-orphaned-meta\n---\n");
            Assert.IsFalse(CliInstallationDetector.AreSkillsInstalled(temporaryRoot, ".claude"),
                "Manual local skills should not be treated as installed uLoop skills");

            string legacyTargetRoot = Path.Combine(temporaryRoot, ".codex");
            WriteSkillFile(
                Path.Combine(legacyTargetRoot, SkillInstallLayout.SkillsDirName, "acme-third-party"),
                "---\nname: acme-third-party\ntoolName: acme-third-party\n---\n");
            Assert.IsTrue(CliInstallationDetector.AreSkillsInstalled(temporaryRoot, ".codex"),
                "Legacy third-party managed skills should be detected");

            string managedTargetRoot = Path.Combine(temporaryRoot, ".agents");
            WriteSkillFile(Path.Combine(
                managedTargetRoot,
                SkillInstallLayout.SkillsDirName,
                SkillInstallLayout.ManagedSkillsDirName,
                "uloop-compile"));
            Assert.IsTrue(CliInstallationDetector.AreSkillsInstalled(temporaryRoot, ".agents"),
                "Namespaced managed skills should be detected");
        }

        [Test]
        public void AreSkillsInstalled_WhenLayoutSpecified_MatchesOnlySelectedLayout()
        {
            string temporaryRoot = CreateTemporaryProjectRoot();

            string flatTargetRoot = Path.Combine(temporaryRoot, ".claude");
            WriteSkillFile(Path.Combine(flatTargetRoot, SkillInstallLayout.SkillsDirName, "uloop-compile"));
            Assert.IsTrue(CliInstallationDetector.AreSkillsInstalled(temporaryRoot, ".claude", false));
            Assert.IsFalse(CliInstallationDetector.AreSkillsInstalled(temporaryRoot, ".claude", true));

            string groupedTargetRoot = Path.Combine(temporaryRoot, ".codex");
            WriteSkillFile(Path.Combine(
                groupedTargetRoot,
                SkillInstallLayout.SkillsDirName,
                SkillInstallLayout.ManagedSkillsDirName,
                "uloop-compile"));
            Assert.IsTrue(CliInstallationDetector.AreSkillsInstalled(temporaryRoot, ".codex", true));
            Assert.IsFalse(CliInstallationDetector.AreSkillsInstalled(temporaryRoot, ".codex", false));
        }

        [Test]
        public void AreSkillsInstalled_WhenLegacyManagedDirectoryIsEmpty_StillDetectsFlatLayout()
        {
            string temporaryRoot = CreateTemporaryProjectRoot();
            string targetRoot = Path.Combine(temporaryRoot, ".cursor");
            Directory.CreateDirectory(Path.Combine(targetRoot, SkillInstallLayout.SkillsDirName, "uloop-compile"));

            Assert.IsTrue(CliInstallationDetector.AreSkillsInstalled(temporaryRoot, ".cursor", false));
            Assert.IsFalse(CliInstallationDetector.AreSkillsInstalled(temporaryRoot, ".cursor", true));
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

            ToolSkillSynchronizer.SkillTargetInfo[] detectedTargets = ToolSkillSynchronizer.DetectTargets(
                    temporaryRoot,
                    requireSkillsDirectory: true,
                    groupSkillsUnderUnityCliLoop: true)
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

            ToolSkillSynchronizer.SkillTargetInfo[] detectedTargets = ToolSkillSynchronizer.DetectTargets(
                    temporaryRoot,
                    requireSkillsDirectory: true,
                    groupSkillsUnderUnityCliLoop: true)
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

            ToolSkillSynchronizer.SkillTargetInfo[] detectedTargets = ToolSkillSynchronizer.DetectTargets(
                    temporaryRoot,
                    requireSkillsDirectory: true,
                    groupSkillsUnderUnityCliLoop: true)
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

            ToolSkillSynchronizer.SkillTargetInfo[] detectedTargets = ToolSkillSynchronizer.DetectTargets(
                    temporaryRoot,
                    requireSkillsDirectory: true,
                    groupSkillsUnderUnityCliLoop: true)
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

            ToolSkillSynchronizer.SkillTargetInfo[] detectedTargets = ToolSkillSynchronizer.DetectTargets(
                    temporaryRoot,
                    requireSkillsDirectory: true,
                    groupSkillsUnderUnityCliLoop: true)
                .ToArray();

            Assert.That(detectedTargets.Length, Is.EqualTo(1));
            Assert.That(detectedTargets[0].InstallState, Is.EqualTo(SkillInstallState.Outdated));
            Assert.That(detectedTargets[0].HasExistingSkills, Is.True);
        }

        [Test]
        public void DetectTargets_WhenUnexpectedManagedSkillDirectoryExists_ReportsOutdated()
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

            string staleSkillDir = Path.Combine(
                targetRoot,
                SkillInstallLayout.SkillsDirName,
                SkillInstallLayout.ManagedSkillsDirName,
                "uloop-stale-skill");
            WriteSkillFile(staleSkillDir, "---\nname: uloop-stale-skill\n---\n");

            ToolSkillSynchronizer.SkillTargetInfo[] detectedTargets = ToolSkillSynchronizer.DetectTargets(
                    temporaryRoot,
                    requireSkillsDirectory: true,
                    groupSkillsUnderUnityCliLoop: true)
                .ToArray();

            Assert.That(detectedTargets.Length, Is.EqualTo(1));
            Assert.That(detectedTargets[0].InstallState, Is.EqualTo(SkillInstallState.Outdated));
        }

        [Test]
        public async Task InstallSkillFilesAtProjectRoot_WhenUnexpectedManagedSkillDirectoryExists_RemovesIt()
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

            string staleSkillDir = Path.Combine(
                targetRoot,
                SkillInstallLayout.SkillsDirName,
                SkillInstallLayout.ManagedSkillsDirName,
                "uloop-stale-skill");
            WriteSkillFile(staleSkillDir, "---\nname: uloop-stale-skill\n---\n");

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
                    groupSkillsUnderUnityCliLoop: true);

            string installedSkillDir = Path.Combine(
                targetRoot,
                SkillInstallLayout.SkillsDirName,
                SkillInstallLayout.ManagedSkillsDirName,
                "uloop-public-skill");

            Assert.That(result.IsSuccessful, Is.True);
            Assert.That(Directory.Exists(staleSkillDir), Is.False);
            Assert.That(File.Exists(Path.Combine(installedSkillDir, SkillInstallLayout.SkillFileName)), Is.True);
            Assert.That(File.ReadAllText(Path.Combine(installedSkillDir, "reference.md")), Is.EqualTo("reference"));
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

            ToolSkillSynchronizer.SkillTargetInfo[] detectedTargets = ToolSkillSynchronizer.DetectTargets(
                    temporaryRoot,
                    requireSkillsDirectory: true,
                    groupSkillsUnderUnityCliLoop: true)
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

            ToolSkillSynchronizer.SkillTargetInfo[] detectedTargets = ToolSkillSynchronizer.DetectTargets(
                    temporaryRoot,
                    requireSkillsDirectory: true,
                    groupSkillsUnderUnityCliLoop: true)
                .ToArray();

            Assert.That(detectedTargets.Length, Is.EqualTo(1));
            Assert.That(detectedTargets[0].InstallState, Is.EqualTo(SkillInstallState.Missing));
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
