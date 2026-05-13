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
        public void ApplyMigration_WhenCurrentManualRegistrationExists_PreservesApplicationReference()
        {
            // Verifies that mixed assemblies keep current registrar dependencies while legacy tools migrate.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string toolPath = Path.Combine(toolDirectory, "HelloTool.cs");
                string registrationPath = Path.Combine(toolDirectory, "CurrentManualToolRegistration.cs");
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
                File.WriteAllText(registrationPath, @"using io.github.hatayama.UnityCliLoop.Application;

public static class CurrentManualToolRegistration
{
    public static void Register(object tool)
    {
        UnityCliLoopToolRegistrar.RegisterCustomTool(tool);
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
                Assert.That(File.ReadAllText(toolPath), Does.Contain("UnityCliLoopTool<HelloSchema, HelloResponse>"));
                Assert.That(File.ReadAllText(registrationPath), Does.Contain("UnityCliLoopToolRegistrar"));
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
        public void ApplyMigration_WhenCurrentManualRegistrationExistsWithLegacyAsmdefName_AddsApplicationReference()
        {
            // Verifies that partially migrated registrar code receives the asmdef refs it already requires.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string registrationPath = Path.Combine(toolDirectory, "CurrentManualToolRegistration.cs");
                string asmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                string registrationSource = @"using io.github.hatayama.UnityCliLoop.Application;

public static class CurrentManualToolRegistration
{
    public static void Register(object tool)
    {
        UnityCliLoopToolRegistrar.RegisterCustomTool(tool);
    }
}";
                File.WriteAllText(registrationPath, registrationSource);
                File.WriteAllText(asmdefPath, @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": [
        ""uLoopMCP.Editor""
    ]
}");

                ThirdPartyToolMigrationFileService service = new();
                ThirdPartyToolMigrationResult result = service.ApplyMigration(projectRoot);

                Assert.That(result.FileCount, Is.EqualTo(1));
                Assert.That(File.ReadAllText(registrationPath), Is.EqualTo(registrationSource));
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
        public void ApplyMigration_WhenCurrentManualRegistrationExistsWithLegacyAsmdefGuid_AddsContractReferences()
        {
            // Verifies that partially migrated GUID refs are expanded to the assemblies current registrar APIs expose.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string registrationPath = Path.Combine(toolDirectory, "CurrentManualToolRegistration.cs");
                string asmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                string registrationSource = @"using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.ToolContracts;

public static class CurrentManualToolRegistration
{
    public static void Register(IUnityCliLoopTool tool)
    {
        UnityCliLoopToolRegistrar.RegisterCustomTool(tool);
    }
}";
                File.WriteAllText(registrationPath, registrationSource);
                File.WriteAllText(asmdefPath, @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": [
        ""GUID:214998e563c124e8a88199b2dd1f522d""
    ]
}");

                ThirdPartyToolMigrationFileService service = new();
                ThirdPartyToolMigrationResult result = service.ApplyMigration(projectRoot);

                Assert.That(result.FileCount, Is.EqualTo(1));
                Assert.That(File.ReadAllText(registrationPath), Is.EqualTo(registrationSource));
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
        public void ApplyMigration_WhenAssemblyUsesGlobalLegacyUsing_KeepsUnrelatedFiles()
        {
            // Verifies that assembly-level migration does not rename unrelated project types.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string globalUsingPath = Path.Combine(toolDirectory, "GlobalUsings.cs");
                string unrelatedPath = Path.Combine(toolDirectory, "BaseToolSchema.cs");
                string asmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                string unrelatedSource = "public sealed class BaseToolSchema {}";
                File.WriteAllText(globalUsingPath, "global using io.github.hatayama.uLoopMCP;");
                File.WriteAllText(unrelatedPath, unrelatedSource);
                File.WriteAllText(asmdefPath, @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": [
        ""GUID:214998e563c124e8a88199b2dd1f522d""
    ]
}");

                ThirdPartyToolMigrationFileService service = new();
                ThirdPartyToolMigrationResult result = service.ApplyMigration(projectRoot);

                Assert.That(result.FileCount, Is.EqualTo(2));
                Assert.That(File.ReadAllText(globalUsingPath), Does.Contain("io.github.hatayama.UnityCliLoop.ToolContracts"));
                Assert.That(File.ReadAllText(unrelatedPath), Is.EqualTo(unrelatedSource));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenAssemblyUsesFileScopedLegacyUsing_KeepsUnrelatedBareTypes()
        {
            // Verifies that file-scoped imports do not grant legacy type context to sibling files.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string toolPath = Path.Combine(toolDirectory, "HelloTool.cs");
                string unrelatedPath = Path.Combine(toolDirectory, "UnrelatedMetadata.cs");
                string asmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                string unrelatedSource =
                    "public sealed class UnrelatedMetadata { public BaseToolResponse Response; }";
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
                File.WriteAllText(unrelatedPath, unrelatedSource);
                File.WriteAllText(asmdefPath, @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": [
        ""GUID:214998e563c124e8a88199b2dd1f522d""
    ]
}");

                ThirdPartyToolMigrationFileService service = new();
                ThirdPartyToolMigrationResult result = service.ApplyMigration(projectRoot);

                Assert.That(result.FileCount, Is.EqualTo(2));
                Assert.That(File.ReadAllText(toolPath), Does.Contain("UnityCliLoopTool<HelloSchema, HelloResponse>"));
                Assert.That(File.ReadAllText(unrelatedPath), Is.EqualTo(unrelatedSource));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenAssemblyUsesGlobalLegacyUsing_RewritesGenericSplitContractFiles()
        {
            // Verifies that collection-shaped legacy type references migrate with their assembly.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string globalUsingPath = Path.Combine(toolDirectory, "GlobalUsings.cs");
                string listPath = Path.Combine(toolDirectory, "ToolList.cs");
                string asmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                File.WriteAllText(globalUsingPath, "global using io.github.hatayama.uLoopMCP;");
                File.WriteAllText(
                    listPath,
                    "public sealed class ToolList { public System.Collections.Generic.List<IUnityTool> Tools; }");
                File.WriteAllText(asmdefPath, @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": [
        ""GUID:214998e563c124e8a88199b2dd1f522d""
    ]
}");

                ThirdPartyToolMigrationFileService service = new();
                ThirdPartyToolMigrationResult result = service.ApplyMigration(projectRoot);

                Assert.That(result.FileCount, Is.EqualTo(3));
                Assert.That(File.ReadAllText(listPath), Does.Contain(
                    "System.Collections.Generic.List<IUnityCliLoopTool>"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenAssemblyUsesGlobalLegacyUsing_RewritesSplitReturnContractTypes()
        {
            // Verifies that helper return types relying on global legacy usings migrate with their assembly.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string globalUsingPath = Path.Combine(toolDirectory, "GlobalUsings.cs");
                string responseFactoryPath = Path.Combine(toolDirectory, "ResponseFactory.cs");
                string asmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                File.WriteAllText(globalUsingPath, "global using io.github.hatayama.uLoopMCP;");
                File.WriteAllText(
                    responseFactoryPath,
                    "public static class ResponseFactory { public static BaseToolResponse Create() => null; }");
                File.WriteAllText(asmdefPath, @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": [
        ""GUID:214998e563c124e8a88199b2dd1f522d""
    ]
}");

                ThirdPartyToolMigrationFileService service = new();
                ThirdPartyToolMigrationResult result = service.ApplyMigration(projectRoot);

                Assert.That(result.FileCount, Is.EqualTo(3));
                Assert.That(File.ReadAllText(responseFactoryPath), Does.Contain("UnityCliLoopToolResponse Create"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenAssemblyUsesGlobalLegacyUsing_RewritesSplitBareToolAttributes()
        {
            // Verifies that attribute-only files relying on global legacy usings migrate with their assembly.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string globalUsingPath = Path.Combine(toolDirectory, "GlobalUsings.cs");
                string toolAttributePath = Path.Combine(toolDirectory, "HelloTool.Attribute.cs");
                string asmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                File.WriteAllText(globalUsingPath, "global using io.github.hatayama.uLoopMCP;");
                File.WriteAllText(
                    toolAttributePath,
                    "[McpTool]\npublic sealed partial class HelloTool\n{\n}");
                File.WriteAllText(asmdefPath, @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": [
        ""GUID:214998e563c124e8a88199b2dd1f522d""
    ]
}");

                ThirdPartyToolMigrationFileService service = new();
                ThirdPartyToolMigrationResult result = service.ApplyMigration(projectRoot);

                Assert.That(result.FileCount, Is.EqualTo(3));
                Assert.That(File.ReadAllText(toolAttributePath), Does.Contain("[UnityCliLoopTool]"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenAssemblyUsesGlobalLegacyAlias_RewritesSplitAliasQualifiedFiles()
        {
            // Verifies that global namespace aliases provide legacy context to sibling files.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string globalUsingPath = Path.Combine(toolDirectory, "GlobalUsings.cs");
                string toolAttributePath = Path.Combine(toolDirectory, "HelloTool.Attribute.cs");
                string asmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                File.WriteAllText(globalUsingPath, "global using Old = io.github.hatayama.uLoopMCP;");
                File.WriteAllText(
                    toolAttributePath,
                    "[Old.McpTool]\npublic sealed partial class HelloTool\n{\n    private Old.IUnityTool tool;\n}");
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
                Assert.That(File.ReadAllText(toolAttributePath), Does.Contain(
                    "io.github.hatayama.UnityCliLoop.ToolContracts.UnityCliLoopTool"));
                Assert.That(File.ReadAllText(toolAttributePath), Does.Contain("Old.IUnityCliLoopTool"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenAssemblyUsesGlobalLegacyAlias_RewritesSplitAliasQualifiedContractTypes()
        {
            // Verifies that alias-qualified legacy contract types migrate in sibling files.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string globalUsingPath = Path.Combine(toolDirectory, "GlobalUsings.cs");
                string responseFactoryPath = Path.Combine(toolDirectory, "ResponseFactory.cs");
                string asmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                File.WriteAllText(globalUsingPath, "global using Old = io.github.hatayama.uLoopMCP;");
                File.WriteAllText(
                    responseFactoryPath,
                    "public static class ResponseFactory { public static Old.BaseToolResponse Create() => null; }");
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
                Assert.That(File.ReadAllText(responseFactoryPath), Does.Contain("Old.UnityCliLoopToolResponse Create"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenLegacyToolExistsUnderAsmref_RewritesReferencedAsmdef()
        {
            // Verifies that asmref folders mark the referenced asmdef as the migrated assembly.
            string projectRoot = CreateProjectRoot();
            try
            {
                string asmdefDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                string asmrefDirectory = Path.Combine(projectRoot, "Assets", "VendorToolParts");
                Directory.CreateDirectory(asmdefDirectory);
                Directory.CreateDirectory(asmrefDirectory);
                string toolPath = Path.Combine(asmrefDirectory, "HelloTool.cs");
                string asmrefPath = Path.Combine(asmrefDirectory, "VendorTools.Editor.asmref");
                string asmdefPath = Path.Combine(asmdefDirectory, "VendorTools.Editor.asmdef");
                File.WriteAllText(toolPath, @"using io.github.hatayama.uLoopMCP;

[McpTool]
public sealed class HelloTool : AbstractUnityTool<HelloSchema, HelloResponse>
{
}");
                File.WriteAllText(asmrefPath, @"{
    ""reference"": ""VendorTools.Editor""
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
                Assert.That(File.ReadAllText(toolPath), Does.Contain("UnityCliLoopTool<HelloSchema, HelloResponse>"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Not.Contain("GUID:214998e563c124e8a88199b2dd1f522d"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenNestedAsmrefOverridesAncestorAsmdef_RewritesReferencedAsmdef()
        {
            // Verifies that nested asmref folders use their referenced assembly instead of an ancestor asmdef.
            string projectRoot = CreateProjectRoot();
            try
            {
                string ancestorDirectory = Path.Combine(projectRoot, "Assets", "OuterAssembly");
                string asmrefDirectory = Path.Combine(ancestorDirectory, "VendorToolParts");
                string targetDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(asmrefDirectory);
                Directory.CreateDirectory(targetDirectory);
                string ancestorAsmdefPath = Path.Combine(ancestorDirectory, "OuterAssembly.Editor.asmdef");
                string toolPath = Path.Combine(asmrefDirectory, "HelloTool.cs");
                string asmrefPath = Path.Combine(asmrefDirectory, "VendorTools.Editor.asmref");
                string targetAsmdefPath = Path.Combine(targetDirectory, "VendorTools.Editor.asmdef");
                File.WriteAllText(ancestorAsmdefPath, @"{
    ""name"": ""OuterAssembly.Editor"",
    ""references"": []
}");
                File.WriteAllText(toolPath, @"using io.github.hatayama.uLoopMCP;

[McpTool]
public sealed class HelloTool : AbstractUnityTool<HelloSchema, HelloResponse>
{
}");
                File.WriteAllText(asmrefPath, @"{
    ""reference"": ""VendorTools.Editor""
}");
                File.WriteAllText(targetAsmdefPath, @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": [
        ""GUID:214998e563c124e8a88199b2dd1f522d""
    ]
}");

                ThirdPartyToolMigrationFileService service = new();
                ThirdPartyToolMigrationResult result = service.ApplyMigration(projectRoot);

                Assert.That(result.FileCount, Is.EqualTo(2));
                Assert.That(File.ReadAllText(toolPath), Does.Contain("UnityCliLoopTool<HelloSchema, HelloResponse>"));
                Assert.That(File.ReadAllText(targetAsmdefPath), Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
                Assert.That(File.ReadAllText(ancestorAsmdefPath), Does.Not.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
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
        public void ApplyMigration_WhenNoAsmdefFirstPassRuntimeUsesGlobalLegacyUsing_KeepsRegularRuntimeFiles()
        {
            // Verifies that Unity predefined firstpass runtime scripts do not migrate regular runtime siblings.
            string projectRoot = CreateProjectRoot();
            try
            {
                string firstPassDirectory = Path.Combine(projectRoot, "Assets", "Plugins");
                string runtimeDirectory = Path.Combine(projectRoot, "Assets", "Scripts");
                Directory.CreateDirectory(firstPassDirectory);
                Directory.CreateDirectory(runtimeDirectory);
                string globalUsingPath = Path.Combine(firstPassDirectory, "GlobalUsings.cs");
                string runtimePath = Path.Combine(runtimeDirectory, "UnrelatedMetadata.cs");
                string runtimeSource =
                    "public sealed class UnrelatedMetadata { public BaseToolResponse Response; }";
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
        public void ApplyMigration_WhenNoAsmdefFirstPassEditorUsesGlobalLegacyUsing_KeepsRegularEditorFiles()
        {
            // Verifies that Unity predefined firstpass editor scripts do not migrate regular editor siblings.
            string projectRoot = CreateProjectRoot();
            try
            {
                string firstPassEditorDirectory = Path.Combine(projectRoot, "Assets", "Plugins", "Editor");
                string editorDirectory = Path.Combine(projectRoot, "Assets", "Editor");
                Directory.CreateDirectory(firstPassEditorDirectory);
                Directory.CreateDirectory(editorDirectory);
                string globalUsingPath = Path.Combine(firstPassEditorDirectory, "GlobalUsings.cs");
                string editorPath = Path.Combine(editorDirectory, "UnrelatedMetadata.cs");
                string editorSource =
                    "public sealed class UnrelatedMetadata { public BaseToolResponse Response; }";
                File.WriteAllText(globalUsingPath, "global using io.github.hatayama.uLoopMCP;");
                File.WriteAllText(editorPath, editorSource);

                ThirdPartyToolMigrationFileService service = new();
                ThirdPartyToolMigrationResult result = service.ApplyMigration(projectRoot);

                Assert.That(result.FileCount, Is.EqualTo(1));
                Assert.That(File.ReadAllText(globalUsingPath), Does.Contain("io.github.hatayama.UnityCliLoop.ToolContracts"));
                Assert.That(File.ReadAllText(editorPath), Is.EqualTo(editorSource));
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

        [Test]
        public void PreviewMigration_WhenLegacyToolExistsOutsideAssets_IgnoresFile()
        {
            // Verifies that repository tooling outside Unity source roots is not migrated.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolsDirectory = Path.Combine(projectRoot, "tools");
                Directory.CreateDirectory(toolsDirectory);
                File.WriteAllText(
                    Path.Combine(toolsDirectory, "LegacyToolFixture.cs"),
                    "using io.github.hatayama.uLoopMCP; [McpTool] public sealed class LegacyToolFixture {}");

                ThirdPartyToolMigrationFileService service = new();
                ThirdPartyToolMigrationPreview preview = service.PreviewMigration(projectRoot);

                Assert.That(preview.HasTargets, Is.False);
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenLegacyToolExistsUnderAssetsPackagesDirectory_RewritesAssetsFiles()
        {
            // Verifies that only Unity's project-root Packages directory is excluded from migration scans.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "Packages", "VendorTools");
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
                ThirdPartyToolMigrationResult result = service.ApplyMigration(projectRoot);

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
        public void ApplyMigration_WhenLegacyToolExistsUnderPackagesDirectory_KeepsPackageFiles()
        {
            // Verifies that Package Manager and embedded package contents are not rewritten in place.
            string projectRoot = CreateProjectRoot();
            try
            {
                string packageDirectory = Path.Combine(
                    projectRoot,
                    "Packages",
                    "com.example.legacy-tool",
                    "Editor");
                Directory.CreateDirectory(packageDirectory);
                string toolPath = Path.Combine(packageDirectory, "PackageTool.cs");
                string asmdefPath = Path.Combine(packageDirectory, "LegacyPackageTool.Editor.asmdef");
                string toolSource = @"using io.github.hatayama.uLoopMCP;

[McpTool]
public sealed class PackageTool : AbstractUnityTool<PackageToolSchema, PackageToolResponse>
{
}

public sealed class PackageToolSchema : BaseToolSchema
{
}

public sealed class PackageToolResponse : BaseToolResponse
{
}";
                string asmdefSource = @"{
    ""name"": ""LegacyPackageTool.Editor"",
    ""references"": [
        ""GUID:214998e563c124e8a88199b2dd1f522d""
    ]
}";
                File.WriteAllText(toolPath, toolSource);
                File.WriteAllText(asmdefPath, asmdefSource);

                ThirdPartyToolMigrationFileService service = new();
                ThirdPartyToolMigrationPreview preview = service.PreviewMigration(projectRoot);
                ThirdPartyToolMigrationResult result = service.ApplyMigration(projectRoot);

                Assert.That(preview.HasTargets, Is.False);
                Assert.That(result.FileCount, Is.EqualTo(0));
                Assert.That(File.ReadAllText(toolPath), Is.EqualTo(toolSource));
                Assert.That(File.ReadAllText(asmdefPath), Is.EqualTo(asmdefSource));
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
