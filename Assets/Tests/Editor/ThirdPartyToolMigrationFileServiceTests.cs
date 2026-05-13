using System;
using System.IO;
using System.Linq;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies project-wide V3 third-party tool migration file behavior.
    /// </summary>
    public sealed class ThirdPartyToolMigrationFileServiceTests
    {
        [Test]
        public void ApplyMigration_WhenLegacyToolAssemblyExists_RewritesSourceAndAsmdef()
        {
            // Verifies that project-wide migration rewrites both custom tool source and its asmdef reference.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string toolPath = Path.Combine(toolDirectory, "HelloTool.cs");
                string asmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                File.WriteAllText(toolPath, @"using io.github.hatayama.uLoopMCP;

[McpTool]
public sealed class HelloTool : AbstractUnityTool<HelloSchema, HelloResponse>
{
}

public sealed class HelloSchema : BaseToolSchema
{
}

public sealed class HelloResponse : BaseToolResponse
{
}");
                File.WriteAllText(asmdefPath, @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": [
        ""GUID:214998e563c124e8a88199b2dd1f522d""
    ]
}");

                ThirdPartyToolMigrationFileService service = new();
                ThirdPartyToolMigrationPreview preview = service.PreviewMigration(projectRoot);
                ThirdPartyToolMigrationResult result = service.ApplyMigration(projectRoot);

                Assert.That(preview.FileCount, Is.EqualTo(2));
                Assert.That(preview.FilePaths.Contains(toolPath), Is.True);
                Assert.That(preview.FilePaths.Contains(asmdefPath), Is.True);
                Assert.That(result.FileCount, Is.EqualTo(2));
                Assert.That(File.ReadAllText(toolPath), Does.Contain("UnityCliLoopTool<HelloSchema, HelloResponse>"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenManualRegistrationExists_AddsApplicationReference()
        {
            // Verifies that migrated manual registration source keeps access to the V3 registrar assembly.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string toolPath = Path.Combine(toolDirectory, "ManualToolRegistration.cs");
                string asmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                File.WriteAllText(toolPath, @"using io.github.hatayama.uLoopMCP;

public static class ManualToolRegistration
{
    public static void Register(IUnityTool tool)
    {
        CustomToolManager.RegisterCustomTool(tool);
    }
}");
                File.WriteAllText(asmdefPath, @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": [
        ""GUID:214998e563c124e8a88199b2dd1f522d""
    ]
}");

                ThirdPartyToolMigrationFileService service = new();
                ThirdPartyToolMigrationResult result = service.ApplyMigration(projectRoot);

                Assert.That(result.FileCount, Is.EqualTo(2));
                Assert.That(File.ReadAllText(toolPath), Does.Contain(
                    "io.github.hatayama.UnityCliLoop.Application.UnityCliLoopToolRegistrar.RegisterCustomTool"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:214998e563c124e8a88199b2dd1f522d"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void PreviewMigration_WhenLegacyToolExistsUnderExcludedDirectory_IgnoresFile()
        {
            // Verifies that generated Unity folders are not scanned for third-party tools.
            string projectRoot = CreateProjectRoot();
            try
            {
                string generatedDirectory = Path.Combine(projectRoot, "Library");
                Directory.CreateDirectory(generatedDirectory);
                File.WriteAllText(
                    Path.Combine(generatedDirectory, "GeneratedTool.cs"),
                    "using io.github.hatayama.uLoopMCP; [McpTool] public sealed class GeneratedTool {}");

                ThirdPartyToolMigrationFileService service = new();
                ThirdPartyToolMigrationPreview preview = service.PreviewMigration(projectRoot);

                Assert.That(preview.HasTargets, Is.False);
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
                "UnityCliLoopTests",
                "ThirdPartyToolMigration",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(projectRoot);
            Directory.CreateDirectory(Path.Combine(projectRoot, "ProjectSettings"));
            Directory.CreateDirectory(Path.Combine(projectRoot, "Assets"));
            return projectRoot;
        }
    }
}
