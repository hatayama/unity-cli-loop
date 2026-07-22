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
    /// Verifies the scope-limited migration inventory/plan/preview path used by compile-error-driven
    /// detection, which must only scan the assemblies containing matched files instead of the whole project.
    /// </summary>
    public sealed class ThirdPartyToolMigrationScopedPreviewTests
    {
        [Test]
        public async Task CreateFromDirectoriesAsync_WhenGivenOneOfTwoToolDirectories_OnlyCollectsFilesUnderThatDirectory()
        {
            // Verifies that scoped inventory creation ignores files outside the given scope directories.
            string projectRoot = CreateProjectRoot();
            try
            {
                string scopedToolDirectory = Path.Combine(projectRoot, "Assets", "ScopedTool");
                string otherToolDirectory = Path.Combine(projectRoot, "Assets", "OtherTool");
                Directory.CreateDirectory(scopedToolDirectory);
                Directory.CreateDirectory(otherToolDirectory);
                string scopedFilePath = Path.Combine(scopedToolDirectory, "ScopedFile.cs");
                string otherFilePath = Path.Combine(otherToolDirectory, "OtherFile.cs");
                File.WriteAllText(scopedFilePath, "public sealed class ScopedFile {}");
                File.WriteAllText(otherFilePath, "public sealed class OtherFile {}");

                ProjectFileInventory inventory = await ProjectFileInventory.CreateFromDirectoriesAsync(
                    new List<string> { scopedToolDirectory },
                    projectRoot,
                    new Progress<ThirdPartyToolMigrationProgress>(),
                    CancellationToken.None);

                Assert.That(inventory.CSharpFilePaths, Does.Contain(scopedFilePath));
                Assert.That(inventory.CSharpFilePaths, Has.No.Member(otherFilePath));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public async Task CreateFromDirectoriesAsync_WhenGivenMultipleScopeDirectories_MergesResultsFromBoth()
        {
            // Verifies that scoped inventory creation merges files from every given scope directory.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolADirectory = Path.Combine(projectRoot, "Assets", "ToolA");
                string toolBDirectory = Path.Combine(projectRoot, "Assets", "ToolB");
                Directory.CreateDirectory(toolADirectory);
                Directory.CreateDirectory(toolBDirectory);
                string toolAFilePath = Path.Combine(toolADirectory, "ToolAFile.cs");
                string toolBFilePath = Path.Combine(toolBDirectory, "ToolBFile.cs");
                File.WriteAllText(toolAFilePath, "public sealed class ToolAFile {}");
                File.WriteAllText(toolBFilePath, "public sealed class ToolBFile {}");

                ProjectFileInventory inventory = await ProjectFileInventory.CreateFromDirectoriesAsync(
                    new List<string> { toolADirectory, toolBDirectory },
                    projectRoot,
                    new Progress<ThirdPartyToolMigrationProgress>(),
                    CancellationToken.None);

                Assert.That(inventory.CSharpFilePaths, Does.Contain(toolAFilePath));
                Assert.That(inventory.CSharpFilePaths, Does.Contain(toolBFilePath));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public async Task CreateFromDirectoriesAsync_WhenScopeDirectoryDoesNotExist_SkipsItWithoutThrowing()
        {
            // Verifies that a stale/removed scope directory is skipped rather than causing a scan failure.
            string projectRoot = CreateProjectRoot();
            try
            {
                string missingDirectory = Path.Combine(projectRoot, "Assets", "MissingTool");

                ProjectFileInventory inventory = await ProjectFileInventory.CreateFromDirectoriesAsync(
                    new List<string> { missingDirectory },
                    projectRoot,
                    new Progress<ThirdPartyToolMigrationProgress>(),
                    CancellationToken.None);

                Assert.That(inventory.CSharpFilePaths, Is.Empty);
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public async Task PreviewMigrationInScopeAsync_WhenLegacyToolIsOutsideScope_DoesNotReportItAsATarget()
        {
            // Verifies that PreviewMigrationInScopeAsync only inspects files under the given scope
            // directories, so a legacy tool outside the scope is invisible to the scoped preview.
            string projectRoot = CreateProjectRoot();
            try
            {
                string scopedToolDirectory = Path.Combine(projectRoot, "Assets", "ScopedTool");
                string outOfScopeToolDirectory = Path.Combine(projectRoot, "Assets", "OutOfScopeTool");
                Directory.CreateDirectory(scopedToolDirectory);
                Directory.CreateDirectory(outOfScopeToolDirectory);
                File.WriteAllText(
                    Path.Combine(scopedToolDirectory, "InScopeTool.cs"),
                    "public sealed class InScopeTool {}");
                File.WriteAllText(
                    Path.Combine(outOfScopeToolDirectory, "OutOfScopeTool.cs"),
                    LegacyToolSource);

                ThirdPartyToolMigrationFileService service = new();
                ThirdPartyToolMigrationPreview preview = await service.PreviewMigrationInScopeAsync(
                    projectRoot,
                    new List<string> { scopedToolDirectory },
                    new Progress<ThirdPartyToolMigrationProgress>(),
                    CancellationToken.None);

                Assert.That(preview.HasTargets, Is.False);
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public async Task PreviewMigrationInScopeAsync_WhenLegacyToolIsInsideScope_ReportsItAsATarget()
        {
            // Verifies that PreviewMigrationInScopeAsync detects and reports a legacy tool file that
            // is under one of the given scope directories.
            string projectRoot = CreateProjectRoot();
            try
            {
                string scopedToolDirectory = Path.Combine(projectRoot, "Assets", "ScopedTool");
                Directory.CreateDirectory(scopedToolDirectory);
                string legacyToolPath = Path.Combine(scopedToolDirectory, "LegacyTool.cs");
                File.WriteAllText(legacyToolPath, LegacyToolSource);

                ThirdPartyToolMigrationFileService service = new();
                ThirdPartyToolMigrationPreview preview = await service.PreviewMigrationInScopeAsync(
                    projectRoot,
                    new List<string> { scopedToolDirectory },
                    new Progress<ThirdPartyToolMigrationProgress>(),
                    CancellationToken.None);

                Assert.That(preview.HasTargets, Is.True);
                Assert.That(preview.FilePaths, Does.Contain(legacyToolPath));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        private const string LegacyToolSource = @"using io.github.hatayama.uLoopMCP;

[McpTool]
public sealed class LegacyTool : AbstractUnityTool<LegacySchema, LegacyResponse>
{
}

public sealed class LegacySchema : BaseToolSchema
{
}

public sealed class LegacyResponse : BaseToolResponse
{
}";

        private static string CreateProjectRoot()
        {
            string projectRoot = Path.Combine(
                Path.GetTempPath(),
                "UnityCliLoopTests",
                "ThirdPartyToolMigrationScopedPreview",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(projectRoot);
            Directory.CreateDirectory(Path.Combine(projectRoot, "ProjectSettings"));
            Directory.CreateDirectory(Path.Combine(projectRoot, "Assets"));
            return projectRoot;
        }
    }
}
