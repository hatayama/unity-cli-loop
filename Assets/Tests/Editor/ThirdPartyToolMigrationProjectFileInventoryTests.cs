using System;
using System.IO;
using System.Runtime.InteropServices;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies migration project file inventory symlink/junction cycle guards.
    /// </summary>
    public sealed class ThirdPartyToolMigrationProjectFileInventoryTests
    {
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
    }
}
