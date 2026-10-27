using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies how the skill install root is saved, read back, and resolved to a directory.
    /// </summary>
    [TestFixture]
    public sealed class SkillInstallRootResolverTests
    {
        private string _temporaryRoot;

        [SetUp]
        public void SetUpTemporaryRoot()
        {
            _temporaryRoot = Path.Combine(Path.GetTempPath(), "uloop-install-root-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_temporaryRoot);
        }

        [TearDown]
        public void TearDownTemporaryRoot()
        {
            if (Directory.Exists(_temporaryRoot))
            {
                Directory.Delete(_temporaryRoot, true);
            }
        }

        /// <summary>
        /// Verifies that a project without saved settings installs at the Unity project root.
        /// </summary>
        [Test]
        public void GetSelection_WhenNothingIsSaved_SelectsUnityProjectRoot()
        {
            string projectRoot = CreateNestedProject(withGitDirectory: true);

            SkillInstallRootSelection selection = new SkillInstallRootResolver().GetSelection(projectRoot);

            Assert.That(selection.SelectedRoot, Is.EqualTo(SkillInstallRoot.UnityProjectRoot));
            Assert.That(selection.SelectedRootPath, Is.EqualTo(Path.GetFullPath(projectRoot)));
        }

        /// <summary>
        /// Verifies that a saved Git repository root is read back and resolves to the repository directory.
        /// </summary>
        [Test]
        public void SaveInstallRoot_WhenGitRepositoryRootIsSaved_ResolvesToRepositoryRoot()
        {
            string projectRoot = CreateNestedProject(withGitDirectory: true);
            SkillInstallRootResolver resolver = new();

            resolver.SaveInstallRoot(projectRoot, SkillInstallRoot.GitRepositoryRoot);
            SkillInstallRootSelection selection = resolver.GetSelection(projectRoot);

            Assert.That(selection.SelectedRoot, Is.EqualTo(SkillInstallRoot.GitRepositoryRoot));
            Assert.That(selection.IsSelectedRootAvailable, Is.True);
            Assert.That(selection.SelectedRootPath, Is.EqualTo(Path.GetFullPath(_temporaryRoot)));
        }

        /// <summary>
        /// Verifies that the saved file uses the installRoot spelling the uloop CLI reads.
        /// </summary>
        [Test]
        public void SaveInstallRoot_WritesTheValueTheCliReads()
        {
            string projectRoot = CreateNestedProject(withGitDirectory: true);

            new SkillInstallRootResolver().SaveInstallRoot(projectRoot, SkillInstallRoot.GitRepositoryRoot);

            string json = File.ReadAllText(Path.Combine(projectRoot, ".uloop", "settings.skills.json"));
            Assert.That(json.Replace(" ", string.Empty), Does.Contain("\"installRoot\":\"git\""));
        }

        /// <summary>
        /// Verifies that a saved Git repository root is reported unavailable when no repository contains the project.
        /// </summary>
        [Test]
        public void GetSelection_WhenGitRootIsSavedOutsideRepository_ReportsUnavailable()
        {
            string projectRoot = CreateNestedProject(withGitDirectory: false);
            SkillInstallRootResolver resolver = new();
            resolver.SaveInstallRoot(projectRoot, SkillInstallRoot.GitRepositoryRoot);

            SkillInstallRootSelection selection = resolver.GetSelection(projectRoot);

            Assert.That(selection.IsGitRepositoryRootAvailable, Is.False);
            Assert.That(selection.IsSelectedRootAvailable, Is.False);
            Assert.That(selection.SelectedRootPath, Is.Null);
        }

        /// <summary>
        /// Verifies that a .git file, as used by worktrees and submodules, marks the repository root.
        /// </summary>
        [Test]
        public void FindGitRepositoryRoot_WhenAncestorHasGitFile_ReturnsThatAncestor()
        {
            string projectRoot = CreateNestedProject(withGitDirectory: false);
            File.WriteAllText(Path.Combine(_temporaryRoot, ".git"), "gitdir: elsewhere\n");

            string gitRoot = SkillInstallRootResolver.FindGitRepositoryRoot(projectRoot);

            Assert.That(gitRoot, Is.EqualTo(Path.GetFullPath(_temporaryRoot)));
        }

        /// <summary>
        /// Verifies that the search starts at the Unity project root itself.
        /// </summary>
        [Test]
        public void FindGitRepositoryRoot_WhenProjectRootHoldsGit_ReturnsProjectRoot()
        {
            string projectRoot = CreateNestedProject(withGitDirectory: true);
            Directory.CreateDirectory(Path.Combine(projectRoot, ".git"));

            string gitRoot = SkillInstallRootResolver.FindGitRepositoryRoot(projectRoot);

            Assert.That(gitRoot, Is.EqualTo(Path.GetFullPath(projectRoot)));
        }

        /// <summary>
        /// Verifies that detection with a saved Git repository root reports targets found under the repository root.
        /// </summary>
        [Test]
        public void DetectSkillTargets_WhenGitRootIsSaved_DetectsTargetsUnderRepositoryRoot()
        {
            string projectRoot = CreateNestedProject(withGitDirectory: true);
            Directory.CreateDirectory(Path.Combine(_temporaryRoot, ".codex"));
            Directory.CreateDirectory(Path.Combine(projectRoot, ".agent"));
            ToolSkillSetupService service = new(new NoDisabledToolsPort());
            service.SaveSkillInstallRoot(projectRoot, SkillInstallRoot.GitRepositoryRoot);

            List<SkillSetupTargetInfo> targets = service.DetectSkillTargetsForLayoutAtProjectRoot(
                projectRoot,
                groupSkillsUnderUnityCliLoop: false);

            Assert.That(targets.Count, Is.EqualTo(1));
            Assert.That(targets[0].DirName, Is.EqualTo(".codex"));
        }

        /// <summary>
        /// Verifies that detection reports no targets when the saved Git repository root does not exist.
        /// </summary>
        [Test]
        public void DetectSkillTargets_WhenGitRootIsUnavailable_ReportsNoTargets()
        {
            string projectRoot = CreateNestedProject(withGitDirectory: false);
            Directory.CreateDirectory(Path.Combine(projectRoot, ".codex"));
            ToolSkillSetupService service = new(new NoDisabledToolsPort());
            service.SaveSkillInstallRoot(projectRoot, SkillInstallRoot.GitRepositoryRoot);

            List<SkillSetupTargetInfo> targets = service.DetectSkillTargetsForLayoutFastAtProjectRoot(
                projectRoot,
                groupSkillsUnderUnityCliLoop: false);

            Assert.That(targets, Is.Empty);
        }

        /// <summary>
        /// Verifies that installing the migration skill into an unavailable Git repository root fails without writing files.
        /// </summary>
        [Test]
        public void InstallV3MigrationSkillFiles_WhenGitRootIsUnavailable_ThrowsWithoutWriting()
        {
            string projectRoot = CreateNestedProject(withGitDirectory: false);
            ToolSkillSetupService service = new(new NoDisabledToolsPort());
            service.SaveSkillInstallRoot(projectRoot, SkillInstallRoot.GitRepositoryRoot);
            SkillSetupTargetInfo target = new(
                "Claude Code",
                ".claude",
                "--claude",
                hasSkillsDirectory: true,
                hasExistingSkills: false);

            Assert.ThrowsAsync<InvalidOperationException>(() => service.InstallV3MigrationSkillFilesAsync(
                projectRoot,
                new List<SkillSetupTargetInfo> { target },
                groupSkillsUnderUnityCliLoop: false,
                CancellationToken.None));
            Assert.That(Directory.Exists(Path.Combine(projectRoot, ".claude")), Is.False);
        }

        /// <summary>
        /// Verifies that the migration skill is installed under the Git repository root when it is saved.
        /// </summary>
        [Test]
        public async Task InstallV3MigrationSkillFiles_WhenGitRootIsSaved_InstallsUnderRepositoryRoot()
        {
            string projectRoot = CreateNestedProject(withGitDirectory: true);
            ToolSkillSetupService service = new(new NoDisabledToolsPort());
            service.SaveSkillInstallRoot(projectRoot, SkillInstallRoot.GitRepositoryRoot);
            SkillSetupTargetInfo target = new(
                "Claude Code",
                ".claude",
                "--claude",
                hasSkillsDirectory: true,
                hasExistingSkills: false);

            await service.InstallV3MigrationSkillFilesAsync(
                projectRoot,
                new List<SkillSetupTargetInfo> { target },
                groupSkillsUnderUnityCliLoop: false,
                CancellationToken.None);

            Assert.That(
                File.Exists(Path.Combine(_temporaryRoot, ".claude", "skills", "v3-cli-invocation-migration", "SKILL.md")),
                Is.True);
            Assert.That(Directory.Exists(Path.Combine(projectRoot, ".claude")), Is.False);
        }

        // Builds <temporaryRoot>/unity as the Unity project, optionally with .git at the temporary root.
        private string CreateNestedProject(bool withGitDirectory)
        {
            if (withGitDirectory)
            {
                Directory.CreateDirectory(Path.Combine(_temporaryRoot, ".git"));
            }

            string projectRoot = Path.Combine(_temporaryRoot, "unity");
            Directory.CreateDirectory(projectRoot);
            return projectRoot;
        }

        private sealed class NoDisabledToolsPort : IToolSettingsPort
        {
            public bool IsToolEnabled(string toolName) => true;

            public void SetToolEnabled(string toolName, bool enabled)
            {
            }

            public string[] GetDisabledTools() => Array.Empty<string>();

            public void InvalidateCache()
            {
            }
        }
    }
}
