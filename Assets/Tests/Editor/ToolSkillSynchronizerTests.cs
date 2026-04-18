using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace io.github.hatayama.uLoopMCP
{
    [TestFixture]
    public class ToolSkillSynchronizerTests
    {
        private const string SkillsDirName = "skills";

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
                    string skillsPath = Path.Combine(fullPath, SkillsDirName);
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
                Directory.CreateDirectory(Path.Combine(temporaryRoot, dir, SkillsDirName));
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
            // Arrange
            string testToolName = "compile";

            // Act
            ToolSkillSynchronizer.RemoveSkillFiles(testToolName);

            // Assert: directories that didn't exist before should still not exist
            foreach (string dir in _nonExistentDirsBefore)
            {
                string fullPath = Path.Combine(_projectRoot, dir);
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
    }
}
