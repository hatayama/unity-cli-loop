using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies migration project file inventory symlink/junction cycle guards.
    /// </summary>
    public sealed class ThirdPartyToolMigrationProjectFileInventoryTests
    {
        [Test]
        public async Task CreateAsync_WhenAssetsContainsManyFiles_ReportsIncreasingProcessedItemCount()
        {
            // Verifies that the inventory walk reports actual scanned-file counts instead of always 0/0.
            string projectRoot = CreateProjectRoot();
            try
            {
                string vendorDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(vendorDirectory);
                for (int i = 0; i < 40; i++)
                {
                    File.WriteAllText(
                        Path.Combine(vendorDirectory, $"Tool{i}.cs"),
                        $"public sealed class Tool{i} {{}}");
                }

                List<ThirdPartyToolMigrationProgress> reports = new();
                RecordingInventoryProgress progress = new(reports);

                await ProjectFileInventory.CreateAsync(projectRoot, progress, CancellationToken.None);

                Assert.That(reports, Is.Not.Empty);
                Assert.That(reports[^1].ProcessedItemCount, Is.GreaterThan(0));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void Create_WhenAssetsContainsSelfReferentialSymbolicLink_CompletesWithoutLooping()
        {
            // Verifies a real directory symlink cycle is skipped and the inventory walk completes.
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.Ignore("Directory symlink creation requires elevated privileges on Windows.");
            }

            string projectRoot = CreateProjectRoot();
            try
            {
                string assetsDirectory = Path.Combine(projectRoot, "Assets");
                string vendorDirectory = Path.Combine(assetsDirectory, "VendorTools");
                string cycleLinkDirectory = Path.Combine(vendorDirectory, "Cycle");
                Directory.CreateDirectory(vendorDirectory);
                string includedPath = Path.Combine(vendorDirectory, "IncludedTool.cs");
                File.WriteAllText(includedPath, "public sealed class IncludedTool {}");
                CreateDirectorySymbolicLink(cycleLinkDirectory, vendorDirectory);

                ProjectFileInventory inventory = ProjectFileInventory.Create(projectRoot);

                Assert.That(inventory.CSharpFilePaths, Does.Contain(includedPath));
                Assert.That(
                    inventory.CSharpFilePaths,
                    Has.None.Matches<string>(path => path.IndexOf("Cycle", StringComparison.Ordinal) >= 0));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        private static string CreateProjectRoot()
        {
            string projectRoot = Path.Combine(
                Path.GetTempPath(),
                "UnityCliLoopMigrationInventoryTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(projectRoot, "Assets"));
            Directory.CreateDirectory(Path.Combine(projectRoot, "ProjectSettings"));
            return projectRoot;
        }

        private static void CreateDirectorySymbolicLink(string linkPath, string targetPath)
        {
            int result = Symlink(targetPath, linkPath);
            Assert.That(result, Is.EqualTo(0), $"symlink failed for {linkPath} -> {targetPath}");
        }

        [DllImport("libc", EntryPoint = "symlink", SetLastError = true)]
        private static extern int Symlink(string targetPath, string linkPath);

        private sealed class RecordingInventoryProgress : IProgress<ThirdPartyToolMigrationProgress>
        {
            private readonly List<ThirdPartyToolMigrationProgress> _reports;

            public RecordingInventoryProgress(List<ThirdPartyToolMigrationProgress> reports)
            {
                Assert.That(reports, Is.Not.Null);

                _reports = reports;
            }

            public void Report(ThirdPartyToolMigrationProgress value)
            {
                _reports.Add(value);
            }
        }
    }
}
