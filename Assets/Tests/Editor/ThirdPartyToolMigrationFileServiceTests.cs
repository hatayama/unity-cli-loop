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
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:5c4588558a3624eacbce0f50007cf1eb"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenDomainMetadataExistsWithoutManualRegistration_AddsDomainReference()
        {
            // Verifies that ToolInfo-only metadata helpers migrate their asmdef dependency.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string metadataPath = Path.Combine(toolDirectory, "ToolMetadataProvider.cs");
                string asmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                File.WriteAllText(metadataPath, @"using io.github.hatayama.uLoopMCP;

public static class ToolMetadataProvider
{
    public static ToolInfo[] GetTools()
    {
        return new ToolInfo[0];
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
                Assert.That(File.ReadAllText(metadataPath), Does.Contain(
                    "io.github.hatayama.UnityCliLoop.Domain.ToolInfo[]"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:5c4588558a3624eacbce0f50007cf1eb"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Not.Contain("GUID:214998e563c124e8a88199b2dd1f522d"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenAssemblyUsesGlobalLegacyUsing_RewritesSplitContractFiles()
        {
            // Verifies that schema files relying on global legacy usings migrate with their assembly.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string globalUsingPath = Path.Combine(toolDirectory, "GlobalUsings.cs");
                string schemaPath = Path.Combine(toolDirectory, "HelloSchema.cs");
                string asmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                File.WriteAllText(globalUsingPath, "global using io.github.hatayama.uLoopMCP;");
                File.WriteAllText(schemaPath, "public sealed class HelloSchema : BaseToolSchema {}");
                File.WriteAllText(asmdefPath, @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": [
        ""GUID:214998e563c124e8a88199b2dd1f522d""
    ]
}");

                ThirdPartyToolMigrationFileService service = new();
                ThirdPartyToolMigrationResult result = service.ApplyMigration(projectRoot);

                Assert.That(result.FileCount, Is.EqualTo(3));
                Assert.That(File.ReadAllText(globalUsingPath), Does.Contain("io.github.hatayama.UnityCliLoop.ToolContracts"));
                Assert.That(File.ReadAllText(schemaPath), Does.Contain("UnityCliLoopToolSchema"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenNoAsmdefAssemblyUsesGlobalLegacyUsing_RewritesSplitContractFiles()
        {
            // Verifies that predefined assemblies get the same assembly-level migration as asmdef assemblies.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "Editor", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string globalUsingPath = Path.Combine(toolDirectory, "GlobalUsings.cs");
                string schemaPath = Path.Combine(toolDirectory, "HelloSchema.cs");
                File.WriteAllText(globalUsingPath, "global using io.github.hatayama.uLoopMCP;");
                File.WriteAllText(schemaPath, "public sealed class HelloSchema : BaseToolSchema {}");

                ThirdPartyToolMigrationFileService service = new();
                ThirdPartyToolMigrationResult result = service.ApplyMigration(projectRoot);

                Assert.That(result.FileCount, Is.EqualTo(2));
                Assert.That(File.ReadAllText(globalUsingPath), Does.Contain("io.github.hatayama.UnityCliLoop.ToolContracts"));
                Assert.That(File.ReadAllText(schemaPath), Does.Contain("UnityCliLoopToolSchema"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenNoAsmdefEditorAssemblyUsesGlobalLegacyUsing_KeepsRuntimeFiles()
        {
            // Verifies that predefined editor migration does not rewrite the separate runtime assembly.
            string projectRoot = CreateProjectRoot();
            try
            {
                string editorDirectory = Path.Combine(projectRoot, "Assets", "Editor", "VendorTools");
                string runtimeDirectory = Path.Combine(projectRoot, "Assets", "Scripts");
                Directory.CreateDirectory(editorDirectory);
                Directory.CreateDirectory(runtimeDirectory);
                string globalUsingPath = Path.Combine(editorDirectory, "GlobalUsings.cs");
                string runtimePath = Path.Combine(runtimeDirectory, "BaseToolSchema.cs");
                string runtimeSource = "public sealed class BaseToolSchema {}";
                File.WriteAllText(globalUsingPath, "global using io.github.hatayama.uLoopMCP;");
                File.WriteAllText(runtimePath, runtimeSource);

                ThirdPartyToolMigrationFileService service = new();
                ThirdPartyToolMigrationResult result = service.ApplyMigration(projectRoot);

                Assert.That(result.FileCount, Is.EqualTo(1));
                Assert.That(File.ReadAllText(globalUsingPath), Does.Contain("io.github.hatayama.UnityCliLoop.ToolContracts"));
                Assert.That(File.ReadAllText(runtimePath), Is.EqualTo(runtimeSource));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenAssemblyUsesGlobalLegacyUsingAndSplitManualRegistration_AddsApplicationReference()
        {
            // Verifies that manual registration files relying on assembly-level legacy detection get required refs.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string globalUsingPath = Path.Combine(toolDirectory, "GlobalUsings.cs");
                string registrationPath = Path.Combine(toolDirectory, "ManualToolRegistration.cs");
                string asmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                File.WriteAllText(globalUsingPath, "global using io.github.hatayama.uLoopMCP;");
                File.WriteAllText(registrationPath, @"public static class ManualToolRegistration
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

                Assert.That(result.FileCount, Is.EqualTo(3));
                Assert.That(File.ReadAllText(registrationPath), Does.Contain(
                    "io.github.hatayama.UnityCliLoop.Application.UnityCliLoopToolRegistrar.RegisterCustomTool"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:214998e563c124e8a88199b2dd1f522d"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:5c4588558a3624eacbce0f50007cf1eb"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenUserSidecarFilesExist_PreservesSidecarFiles()
        {
            // Verifies that project-wide source rewrites do not treat user sidecars as migration scratch files.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string toolPath = Path.Combine(toolDirectory, "HelloTool.cs");
                string tempSidecarPath = toolPath + ".tmp";
                string backupSidecarPath = toolPath + ".bak";
                File.WriteAllText(toolPath, @"using io.github.hatayama.uLoopMCP;

[McpTool]
public sealed class HelloTool : AbstractUnityTool<HelloSchema, HelloResponse>
{
}");
                File.WriteAllText(tempSidecarPath, "user temp sidecar");
                File.WriteAllText(backupSidecarPath, "user backup sidecar");

                ThirdPartyToolMigrationFileService service = new();
                ThirdPartyToolMigrationResult result = service.ApplyMigration(projectRoot);

                Assert.That(result.FileCount, Is.EqualTo(1));
                Assert.That(File.ReadAllText(toolPath), Does.Contain("UnityCliLoopTool<HelloSchema, HelloResponse>"));
                Assert.That(File.ReadAllText(tempSidecarPath), Is.EqualTo("user temp sidecar"));
                Assert.That(File.ReadAllText(backupSidecarPath), Is.EqualTo("user backup sidecar"));
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
