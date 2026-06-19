using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using Newtonsoft.Json.Linq;

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
        public void ThirdPartyToolMigrationPreview_WhenInputFilePathsMutate_KeepsSnapshot()
        {
            // Verifies that preview file paths cannot be changed by mutating the constructor input array.
            string[] filePaths =
            {
                "Assets/VendorTools/HelloTool.cs"
            };
            ThirdPartyToolMigrationPreview preview = new(1, 1, filePaths);

            filePaths[0] = "Assets/Changed.cs";
            preview.FilePaths[0] = "Assets/ChangedAgain.cs";

            Assert.That(preview.FilePaths, Is.EqualTo(new[] { "Assets/VendorTools/HelloTool.cs" }));
        }

        [Test]
        public void ThirdPartyToolMigrationResult_WhenInputFilePathsMutate_KeepsSnapshot()
        {
            // Verifies that result file paths cannot be changed by mutating the constructor input array.
            string[] filePaths =
            {
                "Assets/VendorTools/HelloTool.cs"
            };
            ThirdPartyToolMigrationResult result = new(1, 1, filePaths);

            filePaths[0] = "Assets/Changed.cs";
            result.FilePaths[0] = "Assets/ChangedAgain.cs";

            Assert.That(result.FilePaths, Is.EqualTo(new[] { "Assets/VendorTools/HelloTool.cs" }));
        }

        [Test]
        public void ThirdPartyToolMigrationSourceFileCache_WhenFileIsReadTwice_UsesCachedSource()
        {
            // Verifies that async preview phases can reuse source text without repeated disk reads.
            int readCount = 0;
            ThirdPartyToolMigrationSourceFileCache cache = new(filePath =>
            {
                readCount++;
                return $"source:{filePath}";
            });

            string firstSource = cache.ReadAllText("Assets/VendorTools/HelloTool.cs");
            string secondSource = cache.ReadAllText("Assets/VendorTools/HelloTool.cs");

            Assert.That(firstSource, Is.EqualTo("source:Assets/VendorTools/HelloTool.cs"));
            Assert.That(secondSource, Is.EqualTo(firstSource));
            Assert.That(readCount, Is.EqualTo(1));
        }

        [Test]
        public void TryReadJsonObjectForMigration_WhenReadThrowsIOException_ReturnsFalse()
        {
            // Verifies that migration scans skip unreadable assembly JSON files.
            bool success = ThirdPartyToolMigrationFileService.TryReadJsonObjectForMigration(
                "Assets/VendorTools/VendorTools.Editor.asmdef",
                _ => throw new IOException("locked"),
                out JObject jsonObject);

            Assert.That(success, Is.False);
            Assert.That(jsonObject, Is.Null);
        }

        [Test]
        public void TryReadJsonObjectForMigration_WhenReadThrowsUnauthorizedAccessException_ReturnsFalse()
        {
            // Verifies that migration scans skip assembly JSON files blocked by file permissions.
            bool success = ThirdPartyToolMigrationFileService.TryReadJsonObjectForMigration(
                "Assets/VendorTools/VendorTools.Editor.asmdef",
                _ => throw new UnauthorizedAccessException("denied"),
                out JObject jsonObject);

            Assert.That(success, Is.False);
            Assert.That(jsonObject, Is.Null);
        }

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
        public void ApplyMigration_WhenLegacyToolAsmdefHasNoReferencesArray_AddsAsmdefReference()
        {
            // Verifies that valid minimal asmdefs compile after project-wide migration.
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
    ""name"": ""VendorTools.Editor""
}");

                ThirdPartyToolMigrationFileService service = new();
                ThirdPartyToolMigrationResult result = service.ApplyMigration(projectRoot);

                Assert.That(result.FileCount, Is.EqualTo(2));
                Assert.That(File.ReadAllText(toolPath), Does.Contain("UnityCliLoopTool<HelloSchema, HelloResponse>"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain(@"""references"": ["));
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
                Assert.That(File.ReadAllText(asmdefPath), Does.Not.Contain("GUID:5c4588558a3624eacbce0f50007cf1eb"));
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
                Assert.That(File.ReadAllText(asmdefPath), Does.Not.Contain("GUID:5c4588558a3624eacbce0f50007cf1eb"));
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
                Assert.That(File.ReadAllText(asmdefPath), Does.Not.Contain("GUID:5c4588558a3624eacbce0f50007cf1eb"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenLegacyRuntimeAsmdefNameExists_RewritesToRuntimeReference()
        {
            // Verifies that old name-based runtime references survive the runtime asmdef rename.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string asmdefPath = Path.Combine(toolDirectory, "VendorTools.asmdef");
                File.WriteAllText(asmdefPath, @"{
    ""name"": ""VendorTools"",
    ""references"": [
        ""uLoopMCP.Runtime""
    ]
}");

                ThirdPartyToolMigrationFileService service = new();
                ThirdPartyToolMigrationResult result = service.ApplyMigration(projectRoot);

                Assert.That(result.FileCount, Is.EqualTo(1));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:c956a21f824994ef087b6de566690b3d"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Not.Contain("uLoopMCP.Runtime"));
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
                Assert.That(File.ReadAllText(asmdefPath), Does.Not.Contain("GUID:5c4588558a3624eacbce0f50007cf1eb"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenCurrentToolContractsExistsWithLegacyAsmdefGuid_RewritesToToolContractsReference()
        {
            // Verifies that partially migrated tool implementations receive the ToolContracts asmdef reference.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string toolPath = Path.Combine(toolDirectory, "CurrentHelloTool.cs");
                string asmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                string toolSource = @"using io.github.hatayama.UnityCliLoop.ToolContracts;

[UnityCliLoopTool]
public sealed class CurrentHelloTool : UnityCliLoopTool<HelloSchema, HelloResponse>
{
}

public sealed class HelloSchema : UnityCliLoopToolSchema
{
}

public sealed class HelloResponse : UnityCliLoopToolResponse
{
}";
                File.WriteAllText(toolPath, toolSource);
                File.WriteAllText(asmdefPath, @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": [
        ""GUID:214998e563c124e8a88199b2dd1f522d""
    ]
}");

                ThirdPartyToolMigrationFileService service = new();
                ThirdPartyToolMigrationResult result = service.ApplyMigration(projectRoot);

                Assert.That(result.FileCount, Is.EqualTo(1));
                Assert.That(File.ReadAllText(toolPath), Is.EqualTo(toolSource));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:214998e563c124e8a88199b2dd1f522d"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenCurrentRegistrarExistsWithToolContractsAsmdefReference_AddsApplicationReference()
        {
            // Verifies that already-replaced asmdef refs still receive missing current assembly dependencies.
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
        ""GUID:fc3fd32eddbee40e39c2d76dc184957b""
    ]
}");

                ThirdPartyToolMigrationFileService service = new();
                ThirdPartyToolMigrationResult result = service.ApplyMigration(projectRoot);

                Assert.That(result.FileCount, Is.EqualTo(1));
                Assert.That(File.ReadAllText(registrationPath), Is.EqualTo(registrationSource));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:214998e563c124e8a88199b2dd1f522d"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Not.Contain("GUID:5c4588558a3624eacbce0f50007cf1eb"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenLegacyRegistrarReturnIsUsedWithoutExplicitToolInfo_AddsDomainReference()
        {
            // Verifies that registrar return types add the Domain dependency even without explicit ToolInfo usage.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string registrationPath = Path.Combine(toolDirectory, "ToolCountLabel.cs");
                string asmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                File.WriteAllText(registrationPath, @"using io.github.hatayama.uLoopMCP;

public static class ToolCountLabel
{
    public static string GetLabel()
    {
        return $""Tools: {CustomToolManager.GetRegisteredCustomTools().Length}"";
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
                Assert.That(File.ReadAllText(registrationPath), Does.Contain(
                    "io.github.hatayama.UnityCliLoop.Application.UnityCliLoopToolRegistrar.GetRegisteredCustomTools"));
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
        public void ApplyMigration_WhenLegacyDomainHelperExists_AddsDomainReference()
        {
            // Verifies that helpers moved to Domain migrate their source and asmdef dependency together.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string helperPath = Path.Combine(toolDirectory, "ToolHelper.cs");
                string asmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                File.WriteAllText(helperPath, @"using io.github.hatayama.uLoopMCP;

public static class ToolHelper
{
    public static ServiceResult<int> CreateResult()
    {
        return ServiceResult<int>.SuccessResult(1);
    }

    public static ToolSettingsCatalogItem[] GetCatalog()
    {
        return new ToolSettingsCatalogItem[0];
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
                Assert.That(File.ReadAllText(helperPath), Does.Contain(
                    "io.github.hatayama.UnityCliLoop.Domain.ServiceResult<int> CreateResult"));
                Assert.That(File.ReadAllText(helperPath), Does.Contain(
                    "io.github.hatayama.UnityCliLoop.Domain.ToolSettingsCatalogItem[] GetCatalog"));
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
        public void ApplyMigration_WhenCurrentDomainMetadataExistsWithLegacyAsmdefGuid_AddsDomainReference()
        {
            // Verifies that partially migrated metadata helpers receive the asmdef refs they already require.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string metadataPath = Path.Combine(toolDirectory, "ToolMetadataProvider.cs");
                string asmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                string metadataSource = @"using io.github.hatayama.UnityCliLoop.Domain;

public static class ToolMetadataProvider
{
    public static ToolInfo[] GetTools()
    {
        return new ToolInfo[0];
    }
}";
                File.WriteAllText(metadataPath, metadataSource);
                File.WriteAllText(asmdefPath, @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": [
        ""GUID:214998e563c124e8a88199b2dd1f522d""
    ]
}");

                ThirdPartyToolMigrationFileService service = new();
                ThirdPartyToolMigrationResult result = service.ApplyMigration(projectRoot);

                Assert.That(result.FileCount, Is.EqualTo(1));
                Assert.That(File.ReadAllText(metadataPath), Is.EqualTo(metadataSource));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:5c4588558a3624eacbce0f50007cf1eb"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:214998e563c124e8a88199b2dd1f522d"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenAssemblyUsesCurrentDomainGlobalUsingAndSplitMetadata_AddsDomainReference()
        {
            // Verifies that split V3 Domain metadata files receive their required asmdef references.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string globalUsingPath = Path.Combine(toolDirectory, "GlobalUsings.cs");
                string metadataPath = Path.Combine(toolDirectory, "ToolMetadataProvider.cs");
                string asmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                string globalUsingSource = "global using io.github.hatayama.UnityCliLoop.Domain;";
                string metadataSource = @"public static class ToolMetadataProvider
{
    public static ToolInfo[] GetTools()
    {
        return new ToolInfo[0];
    }
}";
                File.WriteAllText(globalUsingPath, globalUsingSource);
                File.WriteAllText(metadataPath, metadataSource);
                File.WriteAllText(asmdefPath, @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": [
        ""GUID:214998e563c124e8a88199b2dd1f522d""
    ]
}");

                ThirdPartyToolMigrationFileService service = new();
                ThirdPartyToolMigrationResult result = service.ApplyMigration(projectRoot);

                Assert.That(result.FileCount, Is.EqualTo(1));
                Assert.That(File.ReadAllText(globalUsingPath), Is.EqualTo(globalUsingSource));
                Assert.That(File.ReadAllText(metadataPath), Is.EqualTo(metadataSource));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:5c4588558a3624eacbce0f50007cf1eb"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:214998e563c124e8a88199b2dd1f522d"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenUnrelatedAsmdefJsonIsMalformedAndNoAsmrefs_AppliesAsmdefRepair()
        {
            // Verifies that unrelated malformed asmdefs do not block applying repairable asmdef changes.
            string projectRoot = CreateProjectRoot();
            try
            {
                string unrelatedDirectory = Path.Combine(projectRoot, "Assets", "Unrelated");
                Directory.CreateDirectory(unrelatedDirectory);
                File.WriteAllText(Path.Combine(unrelatedDirectory, "Broken.asmdef"), "{");

                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string toolPath = Path.Combine(toolDirectory, "CurrentHelloTool.cs");
                string asmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                File.WriteAllText(toolPath, @"using io.github.hatayama.UnityCliLoop.ToolContracts;

[UnityCliLoopTool]
public sealed class CurrentHelloTool : UnityCliLoopTool<HelloSchema, HelloResponse>
{
}

public sealed class HelloSchema : UnityCliLoopToolSchema
{
}

public sealed class HelloResponse : UnityCliLoopToolResponse
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

                Assert.That(result.FileCount, Is.EqualTo(1));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenUnrelatedAsmdefJsonIsMalformedAndAsmrefExists_AppliesAsmdefRepair()
        {
            // Verifies that malformed asmdefs do not block asmref-aware migration scans.
            string projectRoot = CreateProjectRoot();
            try
            {
                string unrelatedDirectory = Path.Combine(projectRoot, "Assets", "Unrelated");
                Directory.CreateDirectory(unrelatedDirectory);
                File.WriteAllText(Path.Combine(unrelatedDirectory, "Broken.asmdef"), "{");

                string asmrefDirectory = Path.Combine(projectRoot, "Assets", "VendorToolParts");
                Directory.CreateDirectory(asmrefDirectory);
                File.WriteAllText(Path.Combine(asmrefDirectory, "VendorTools.Editor.asmref"), @"{
    ""reference"": ""VendorTools.Editor""
}");

                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string toolPath = Path.Combine(toolDirectory, "CurrentHelloTool.cs");
                string asmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                File.WriteAllText(toolPath, @"using io.github.hatayama.UnityCliLoop.ToolContracts;

[UnityCliLoopTool]
public sealed class CurrentHelloTool : UnityCliLoopTool<HelloSchema, HelloResponse>
{
}

public sealed class HelloSchema : UnityCliLoopToolSchema
{
}

public sealed class HelloResponse : UnityCliLoopToolResponse
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

                Assert.That(result.FileCount, Is.EqualTo(1));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenUnrelatedAsmrefJsonIsMalformed_AppliesAsmdefRepair()
        {
            // Verifies that malformed asmrefs do not block migration scans for valid assemblies.
            string projectRoot = CreateProjectRoot();
            try
            {
                string unrelatedDirectory = Path.Combine(projectRoot, "Assets", "Unrelated");
                Directory.CreateDirectory(unrelatedDirectory);
                File.WriteAllText(Path.Combine(unrelatedDirectory, "Broken.asmref"), "{");

                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string toolPath = Path.Combine(toolDirectory, "CurrentHelloTool.cs");
                string asmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                File.WriteAllText(toolPath, @"using io.github.hatayama.UnityCliLoop.ToolContracts;

[UnityCliLoopTool]
public sealed class CurrentHelloTool : UnityCliLoopTool<HelloSchema, HelloResponse>
{
}

public sealed class HelloSchema : UnityCliLoopToolSchema
{
}

public sealed class HelloResponse : UnityCliLoopToolResponse
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

                Assert.That(result.FileCount, Is.EqualTo(1));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
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
        public void ApplyMigration_WhenAssemblyUsesGlobalLegacyUsing_RewritesSplitScreenshotHelpersAndAsmdef()
        {
            // Verifies that split files using old screenshot helpers receive the required V3 first-party assembly reference.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string globalUsingPath = Path.Combine(toolDirectory, "GlobalUsings.cs");
                string helperPath = Path.Combine(toolDirectory, "ScreenshotHelper.cs");
                string asmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                File.WriteAllText(globalUsingPath, "global using io.github.hatayama.uLoopMCP;");
                File.WriteAllText(helperPath, @"using UnityEditor;

public sealed class ScreenshotHelper
{
    public EditorWindow[] FindWindows() => EditorWindowCaptureUtility.FindWindowsByName(""Game"", WindowMatchMode.exact);
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
                Assert.That(File.ReadAllText(helperPath), Does.Contain(
                    "io.github.hatayama.UnityCliLoop.FirstPartyTools.EditorWindowCaptureUtility.FindWindowsByName"));
                Assert.That(File.ReadAllText(helperPath), Does.Contain(
                    "io.github.hatayama.UnityCliLoop.FirstPartyTools.WindowMatchMode.exact"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:a0bdbd2c5705643fbb9aef9fac8fd46a"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenCurrentToolContractsFileUsesLegacyScreenshotCapture_AddsScreenshotAsmdefReference()
        {
            // Verifies that partially migrated files finish screenshot helper migration and add the missing assembly reference.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string toolPath = Path.Combine(toolDirectory, "CaptureTool.cs");
                string asmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                File.WriteAllText(toolPath, @"using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using io.github.hatayama.UnityCliLoop.ToolContracts;

public sealed class CaptureTool : UnityCliLoopTool<CaptureSchema, CaptureResponse>
{
    public override string ToolName => ""capture"";

    protected override async Task<CaptureResponse> ExecuteAsync(CaptureSchema parameters, CancellationToken ct)
    {
        Texture2D texture = await EditorWindowCaptureUtility.CaptureWindowAsync(parameters.Window, 1.0f, ct);
        return new CaptureResponse(texture != null);
    }
}

public sealed class CaptureSchema : UnityCliLoopToolSchema
{
    public EditorWindow Window { get; set; }
}

public sealed class CaptureResponse : UnityCliLoopToolResponse
{
    public bool Success { get; }
    public CaptureResponse(bool success) { Success = success; }
}");
                File.WriteAllText(asmdefPath, @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": [
        ""GUID:fc3fd32eddbee40e39c2d76dc184957b""
    ]
}");

                ThirdPartyToolMigrationFileService service = new();
                ThirdPartyToolMigrationResult result = service.ApplyMigration(projectRoot);

                Assert.That(result.FileCount, Is.EqualTo(2));
                Assert.That(File.ReadAllText(toolPath), Does.Contain(
                    "io.github.hatayama.UnityCliLoop.FirstPartyTools.EditorWindowCaptureUtility.CaptureWindowAsync"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:a0bdbd2c5705643fbb9aef9fac8fd46a"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenCurrentToolContractsFileUsesLegacyMainThreadSwitcher_AddsApplicationReference()
        {
            // Verifies that partially migrated files finish main-thread switcher migration and add the Application asmdef reference.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string toolPath = Path.Combine(toolDirectory, "MainThreadTool.cs");
                string asmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                File.WriteAllText(toolPath, @"using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.UnityCliLoop.ToolContracts;

public sealed class MainThreadTool : UnityCliLoopTool<MainThreadSchema, MainThreadResponse>
{
    protected override async Task<MainThreadResponse> ExecuteAsync(MainThreadSchema parameters, CancellationToken ct)
    {
        await MainThreadSwitcher.SwitchToMainThread(PlayerLoopTiming.Update, ct);
        return new MainThreadResponse();
    }
}

public sealed class MainThreadSchema : UnityCliLoopToolSchema
{
}

public sealed class MainThreadResponse : UnityCliLoopToolResponse
{
}");
                File.WriteAllText(asmdefPath, @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": [
        ""GUID:fc3fd32eddbee40e39c2d76dc184957b""
    ]
}");

                ThirdPartyToolMigrationFileService service = new();
                ThirdPartyToolMigrationResult result = service.ApplyMigration(projectRoot);

                Assert.That(result.FileCount, Is.EqualTo(2));
                Assert.That(File.ReadAllText(toolPath), Does.Contain(
                    "io.github.hatayama.UnityCliLoop.Application.MainThreadSwitcher.SwitchToMainThread(ct)"));
                Assert.That(File.ReadAllText(toolPath), Does.Not.Contain("PlayerLoopTiming"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:214998e563c124e8a88199b2dd1f522d"));
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
        public void ApplyMigration_WhenAssemblyUsesGlobalLegacyUsing_RewritesSplitDomainHelpers()
        {
            // Verifies that split Domain helper files receive source and asmdef migration through global usings.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string globalUsingPath = Path.Combine(toolDirectory, "GlobalUsings.cs");
                string helperPath = Path.Combine(toolDirectory, "ToolHelper.cs");
                string asmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                File.WriteAllText(globalUsingPath, "global using io.github.hatayama.uLoopMCP;");
                File.WriteAllText(
                    helperPath,
                    "public static class ToolHelper { public static ServiceResult<int> Create() => null; }");
                File.WriteAllText(asmdefPath, @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": [
        ""GUID:214998e563c124e8a88199b2dd1f522d""
    ]
}");

                ThirdPartyToolMigrationFileService service = new();
                ThirdPartyToolMigrationResult result = service.ApplyMigration(projectRoot);

                Assert.That(result.FileCount, Is.EqualTo(3));
                Assert.That(File.ReadAllText(helperPath), Does.Contain(
                    "io.github.hatayama.UnityCliLoop.Domain.ServiceResult<int> Create"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:5c4588558a3624eacbce0f50007cf1eb"));
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
        public void ApplyMigration_WhenAssemblyUsesGlobalLegacyToolInfoAlias_RewritesSplitAliasConstructors()
        {
            // Verifies that global type aliases provide constructor migration context to sibling files.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string globalUsingPath = Path.Combine(toolDirectory, "GlobalUsings.cs");
                string metadataPath = Path.Combine(toolDirectory, "ToolMetadataProvider.cs");
                string asmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                File.WriteAllText(
                    globalUsingPath,
                    "global using LegacyToolInfo = io.github.hatayama.uLoopMCP.ToolInfo;");
                File.WriteAllText(
                    metadataPath,
                    @"using io.github.hatayama.UnityCliLoop.ToolContracts;

public static class ToolMetadataProvider
{
    public static LegacyToolInfo Create(ToolParameterSchema schema)
    {
        return new LegacyToolInfo(""hello"", ""description"", schema);
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
                Assert.That(File.ReadAllText(globalUsingPath), Does.Contain(
                    "global using LegacyToolInfo = io.github.hatayama.UnityCliLoop.Domain.ToolInfo;"));
                Assert.That(File.ReadAllText(metadataPath), Does.Contain(
                    "new io.github.hatayama.UnityCliLoop.Domain.ToolInfo(\"hello\", schema)"));
                Assert.That(File.ReadAllText(metadataPath), Does.Not.Contain("\"description\", schema"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenAssemblyUsesGlobalLegacyToolInfoAlias_KeepsUnrelatedBareTypes()
        {
            // Verifies that ToolInfo type aliases do not grant full legacy namespace context to sibling files.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string globalUsingPath = Path.Combine(toolDirectory, "GlobalUsings.cs");
                string unrelatedPath = Path.Combine(toolDirectory, "UnrelatedMetadata.cs");
                string asmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                string unrelatedSource = "public sealed class UnrelatedMetadata { public BaseToolSchema Schema; }";
                File.WriteAllText(
                    globalUsingPath,
                    "global using LegacyToolInfo = io.github.hatayama.uLoopMCP.ToolInfo;");
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
                Assert.That(File.ReadAllText(globalUsingPath), Does.Contain(
                    "global using LegacyToolInfo = io.github.hatayama.UnityCliLoop.Domain.ToolInfo;"));
                Assert.That(File.ReadAllText(unrelatedPath), Is.EqualTo(unrelatedSource));
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
        public void ApplyMigration_WhenNoAsmdefCurrentToolContractsFileUsesLegacyScreenshotCapture_RewritesSource()
        {
            // Verifies that predefined editor assemblies finish partially migrated screenshot helpers.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "Editor", "CustomTools");
                Directory.CreateDirectory(toolDirectory);
                string toolPath = Path.Combine(toolDirectory, "CaptureTool.cs");
                File.WriteAllText(toolPath, @"using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using io.github.hatayama.UnityCliLoop.ToolContracts;

public sealed class CaptureTool : UnityCliLoopTool<ScreenshotSchema, ScreenshotResponse>
{
    public async Task<Texture2D> CaptureAsync(EditorWindow window, CancellationToken ct)
    {
        Texture2D texture = await EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, ct);
        return texture;
    }
}");

                ThirdPartyToolMigrationFileService service = new();
                ThirdPartyToolMigrationResult result = service.ApplyMigration(projectRoot);
                string migratedSource = File.ReadAllText(toolPath);

                Assert.That(result.FileCount, Is.EqualTo(1));
                Assert.That(migratedSource, Does.Contain(
                    "UnityCliLoopTool<io.github.hatayama.UnityCliLoop.FirstPartyTools.ScreenshotSchema, io.github.hatayama.UnityCliLoop.FirstPartyTools.ScreenshotResponse>"));
                Assert.That(migratedSource, Does.Contain(
                    "io.github.hatayama.UnityCliLoop.FirstPartyTools.EditorWindowCaptureUtility.CaptureWindowAsync"));
                Assert.That(migratedSource, Does.Not.Contain("await EditorWindowCaptureUtility.CaptureWindowAsync"));
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
                Assert.That(File.ReadAllText(asmdefPath), Does.Not.Contain("GUID:5c4588558a3624eacbce0f50007cf1eb"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void PreviewMigration_WhenUnrelatedCustomToolManagerExists_KeepsAsmdef()
        {
            // Verifies that project-owned type names do not trigger migration dependencies by themselves.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string sourcePath = Path.Combine(toolDirectory, "CustomToolManager.cs");
                string asmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                string source = "public sealed class CustomToolManager {}";
                string asmdefSource = @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": []
}";
                File.WriteAllText(sourcePath, source);
                File.WriteAllText(asmdefPath, asmdefSource);

                ThirdPartyToolMigrationFileService service = new();
                ThirdPartyToolMigrationPreview preview = service.PreviewMigration(projectRoot);
                ThirdPartyToolMigrationResult result = service.ApplyMigration(projectRoot);

                Assert.That(preview.HasTargets, Is.False);
                Assert.That(result.FileCount, Is.EqualTo(0));
                Assert.That(File.ReadAllText(sourcePath), Is.EqualTo(source));
                Assert.That(File.ReadAllText(asmdefPath), Is.EqualTo(asmdefSource));
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

        [Test]
        public async Task HasMigrationTargetsAsync_WhenLegacyToolExistsUnderAssets_ReturnsTrue()
        {
            // Verifies that startup detection can find V2 custom tools without building a migration preview.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                File.WriteAllText(
                    Path.Combine(toolDirectory, "HelloTool.cs"),
                    "using io.github.hatayama.uLoopMCP; [McpTool] public sealed class HelloTool {}");

                ThirdPartyToolMigrationFileService service = new();

                bool hasTargets = await service.HasMigrationTargetsAsync(projectRoot, CancellationToken.None);

                Assert.That(hasTargets, Is.True);
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public async Task HasMigrationTargetsAsync_WhenLegacyAsmdefNameExistsUnderAssets_ReturnsTrue()
        {
            // Verifies that startup detection catches old assembly names without relying on source files.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                File.WriteAllText(
                    Path.Combine(toolDirectory, "VendorTools.Editor.asmdef"),
                    @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": [
        ""uLoopMCP.Editor""
    ]
}");

                ThirdPartyToolMigrationFileService service = new();

                bool hasTargets = await service.HasMigrationTargetsAsync(projectRoot, CancellationToken.None);

                Assert.That(hasTargets, Is.True);
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public async Task HasMigrationTargetsAsync_WhenCurrentToolContractsExistsWithLegacyAsmdefGuid_ReturnsTrue()
        {
            // Verifies that startup detection catches partially migrated tools that still need asmdef repair.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                File.WriteAllText(
                    Path.Combine(toolDirectory, "CurrentHelloTool.cs"),
                    @"using io.github.hatayama.UnityCliLoop.ToolContracts;

[UnityCliLoopTool]
public sealed class CurrentHelloTool : UnityCliLoopTool<HelloSchema, HelloResponse>
{
}

public sealed class HelloSchema : UnityCliLoopToolSchema
{
}

public sealed class HelloResponse : UnityCliLoopToolResponse
{
}");
                File.WriteAllText(
                    Path.Combine(toolDirectory, "VendorTools.Editor.asmdef"),
                    @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": [
        ""GUID:214998e563c124e8a88199b2dd1f522d""
    ]
}");

                ThirdPartyToolMigrationFileService service = new();

                bool hasTargets = await service.HasMigrationTargetsAsync(projectRoot, CancellationToken.None);

                Assert.That(hasTargets, Is.True);
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public async Task HasMigrationTargetsAsync_WhenUnrelatedAsmdefJsonIsMalformed_StillDetectsAsmdefRepair()
        {
            // Verifies that unrelated malformed asmdefs do not block startup detection for repairable tools.
            string projectRoot = CreateProjectRoot();
            try
            {
                string unrelatedDirectory = Path.Combine(projectRoot, "Assets", "Unrelated");
                Directory.CreateDirectory(unrelatedDirectory);
                File.WriteAllText(Path.Combine(unrelatedDirectory, "Broken.asmdef"), "{");

                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                File.WriteAllText(
                    Path.Combine(toolDirectory, "CurrentHelloTool.cs"),
                    @"using io.github.hatayama.UnityCliLoop.ToolContracts;

[UnityCliLoopTool]
public sealed class CurrentHelloTool : UnityCliLoopTool<HelloSchema, HelloResponse>
{
}

public sealed class HelloSchema : UnityCliLoopToolSchema
{
}

public sealed class HelloResponse : UnityCliLoopToolResponse
{
}");
                File.WriteAllText(
                    Path.Combine(toolDirectory, "VendorTools.Editor.asmdef"),
                    @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": [
        ""GUID:214998e563c124e8a88199b2dd1f522d""
    ]
}");

                ThirdPartyToolMigrationFileService service = new();

                bool hasTargets = await service.HasMigrationTargetsAsync(projectRoot, CancellationToken.None);

                Assert.That(hasTargets, Is.True);
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public async Task HasMigrationTargetsAsync_WhenAssemblyUsesCurrentDomainGlobalUsingAndSplitMetadata_ReturnsTrue()
        {
            // Verifies that startup detection treats current Domain global usings as assembly-scoped.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                File.WriteAllText(
                    Path.Combine(toolDirectory, "GlobalUsings.cs"),
                    "global using io.github.hatayama.UnityCliLoop.Domain;");
                File.WriteAllText(
                    Path.Combine(toolDirectory, "ToolMetadataProvider.cs"),
                    @"public static class ToolMetadataProvider
{
    public static ToolInfo[] GetTools()
    {
        return new ToolInfo[0];
    }
}");
                File.WriteAllText(
                    Path.Combine(toolDirectory, "VendorTools.Editor.asmdef"),
                    @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": [
        ""GUID:214998e563c124e8a88199b2dd1f522d""
    ]
}");

                ThirdPartyToolMigrationFileService service = new();

                bool hasTargets = await service.HasMigrationTargetsAsync(projectRoot, CancellationToken.None);

                Assert.That(hasTargets, Is.True);
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public async Task HasMigrationTargetsAsync_WhenCurrentApplicationGuidReferenceExists_ReturnsFalse()
        {
            // Verifies that current V3 Application references do not trigger the startup migration prompt.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                File.WriteAllText(
                    Path.Combine(toolDirectory, "CurrentManualToolRegistration.cs"),
                    @"using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.ToolContracts;

public static class CurrentManualToolRegistration
{
    public static void Register(IUnityCliLoopTool tool)
    {
        UnityCliLoopToolRegistrar.RegisterCustomTool(tool);
    }
}");
                File.WriteAllText(
                    Path.Combine(toolDirectory, "VendorTools.Editor.asmdef"),
                    @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": [
        ""GUID:214998e563c124e8a88199b2dd1f522d"",
        ""GUID:fc3fd32eddbee40e39c2d76dc184957b""
    ]
}");

                ThirdPartyToolMigrationFileService service = new();

                bool hasTargets = await service.HasMigrationTargetsAsync(projectRoot, CancellationToken.None);

                Assert.That(hasTargets, Is.False);
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public async Task HasMigrationTargetsAsync_WhenCurrentRegistrarReturnMissingDomainReference_ReturnsTrue()
        {
            // Verifies that startup detection catches registrar return calls that still need Domain asmdef refs.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                File.WriteAllText(
                    Path.Combine(toolDirectory, "ToolCountLabel.cs"),
                    @"using io.github.hatayama.UnityCliLoop.Application;

public static class ToolCountLabel
{
    public static string GetLabel()
    {
        return $""Tools: {UnityCliLoopToolRegistrar.GetRegisteredCustomTools().Length}"";
    }
}");
                File.WriteAllText(
                    Path.Combine(toolDirectory, "VendorTools.Editor.asmdef"),
                    @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": [
        ""GUID:214998e563c124e8a88199b2dd1f522d"",
        ""GUID:fc3fd32eddbee40e39c2d76dc184957b""
    ]
}");

                ThirdPartyToolMigrationFileService service = new();

                bool hasTargets = await service.HasMigrationTargetsAsync(projectRoot, CancellationToken.None);

                Assert.That(hasTargets, Is.True);
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public async Task HasMigrationTargetsAsync_WhenLegacyToolExistsUnderPackagesDirectory_ReturnsFalse()
        {
            // Verifies that startup detection keeps Package Manager contents outside migration scope.
            string projectRoot = CreateProjectRoot();
            try
            {
                string packageDirectory = Path.Combine(
                    projectRoot,
                    "Packages",
                    "com.example.legacy-tool",
                    "Editor");
                Directory.CreateDirectory(packageDirectory);
                File.WriteAllText(
                    Path.Combine(packageDirectory, "PackageTool.cs"),
                    "using io.github.hatayama.uLoopMCP; [McpTool] public sealed class PackageTool {}");

                ThirdPartyToolMigrationFileService service = new();

                bool hasTargets = await service.HasMigrationTargetsAsync(projectRoot, CancellationToken.None);

                Assert.That(hasTargets, Is.False);
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void PreviewMigration_WhenCacheIsInvalidated_RefreshesChangedProject()
        {
            // Verifies that repeated setup wizard previews can reuse scans and refresh after invalidation.
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
                ThirdPartyToolMigrationPreview firstPreview = service.PreviewMigration(projectRoot);
                File.WriteAllText(toolPath, "public sealed class HelloTool {}");
                ThirdPartyToolMigrationPreview cachedPreview = service.PreviewMigration(projectRoot);
                service.InvalidatePreviewCache();
                ThirdPartyToolMigrationPreview refreshedPreview = service.PreviewMigration(projectRoot);

                Assert.That(firstPreview.HasTargets, Is.True);
                Assert.That(cachedPreview.FileCount, Is.EqualTo(firstPreview.FileCount));
                Assert.That(refreshedPreview.HasTargets, Is.False);
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public async Task PreviewMigrationAsync_WhenProjectChangesAfterCachedPreview_RefreshesCurrentProject()
        {
            // Verifies that migration wizard Refresh reads the current files instead of reusing a stale preview.
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
                Progress<ThirdPartyToolMigrationProgress> progress = new();
                ThirdPartyToolMigrationPreview firstPreview =
                    await service.PreviewMigrationAsync(projectRoot, progress, CancellationToken.None);
                File.WriteAllText(toolPath, "public sealed class HelloTool {}");

                ThirdPartyToolMigrationPreview refreshedPreview =
                    await service.PreviewMigrationAsync(projectRoot, progress, CancellationToken.None);

                Assert.That(firstPreview.HasTargets, Is.True);
                Assert.That(refreshedPreview.HasTargets, Is.False);
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public async Task PreviewMigrationAsync_WhenUnrelatedAsmdefJsonIsMalformedAndNoAsmrefs_PreviewsAsmdefRepair()
        {
            // Verifies that unrelated malformed asmdefs do not block previewing repairable asmdef changes.
            string projectRoot = CreateProjectRoot();
            try
            {
                string unrelatedDirectory = Path.Combine(projectRoot, "Assets", "Unrelated");
                Directory.CreateDirectory(unrelatedDirectory);
                File.WriteAllText(Path.Combine(unrelatedDirectory, "Broken.asmdef"), "{");

                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                File.WriteAllText(
                    Path.Combine(toolDirectory, "CurrentHelloTool.cs"),
                    @"using io.github.hatayama.UnityCliLoop.ToolContracts;

[UnityCliLoopTool]
public sealed class CurrentHelloTool : UnityCliLoopTool<HelloSchema, HelloResponse>
{
}

public sealed class HelloSchema : UnityCliLoopToolSchema
{
}

public sealed class HelloResponse : UnityCliLoopToolResponse
{
}");
                File.WriteAllText(
                    Path.Combine(toolDirectory, "VendorTools.Editor.asmdef"),
                    @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": [
        ""GUID:214998e563c124e8a88199b2dd1f522d""
    ]
}");

                ThirdPartyToolMigrationFileService service = new();
                Progress<ThirdPartyToolMigrationProgress> progress = new();

                ThirdPartyToolMigrationPreview preview =
                    await service.PreviewMigrationAsync(projectRoot, progress, CancellationToken.None);

                Assert.That(preview.HasTargets, Is.True);
                Assert.That(preview.FileCount, Is.EqualTo(1));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public async Task PreviewMigrationAsync_WhenUnrelatedAsmrefJsonIsMalformed_PreviewsAsmdefRepair()
        {
            // Verifies that malformed asmrefs do not block async preview scans for repairable assemblies.
            string projectRoot = CreateProjectRoot();
            try
            {
                string unrelatedDirectory = Path.Combine(projectRoot, "Assets", "Unrelated");
                Directory.CreateDirectory(unrelatedDirectory);
                File.WriteAllText(Path.Combine(unrelatedDirectory, "Broken.asmref"), "{");

                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                File.WriteAllText(
                    Path.Combine(toolDirectory, "CurrentHelloTool.cs"),
                    @"using io.github.hatayama.UnityCliLoop.ToolContracts;

[UnityCliLoopTool]
public sealed class CurrentHelloTool : UnityCliLoopTool<HelloSchema, HelloResponse>
{
}

public sealed class HelloSchema : UnityCliLoopToolSchema
{
}

public sealed class HelloResponse : UnityCliLoopToolResponse
{
}");
                File.WriteAllText(
                    Path.Combine(toolDirectory, "VendorTools.Editor.asmdef"),
                    @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": [
        ""GUID:214998e563c124e8a88199b2dd1f522d""
    ]
}");

                ThirdPartyToolMigrationFileService service = new();
                Progress<ThirdPartyToolMigrationProgress> progress = new();

                ThirdPartyToolMigrationPreview preview =
                    await service.PreviewMigrationAsync(projectRoot, progress, CancellationToken.None);

                Assert.That(preview.HasTargets, Is.True);
                Assert.That(preview.FileCount, Is.EqualTo(1));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void PreviewMigration_WhenCurrentApplicationGuidReferenceExists_KeepsAsmdef()
        {
            // Verifies that V3 Application references are not reported as legacy asmdef migration.
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
                string asmdefSource = @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": [
        ""GUID:214998e563c124e8a88199b2dd1f522d"",
        ""GUID:fc3fd32eddbee40e39c2d76dc184957b""
    ]
}";
                File.WriteAllText(registrationPath, registrationSource);
                File.WriteAllText(asmdefPath, asmdefSource);

                ThirdPartyToolMigrationFileService service = new();
                ThirdPartyToolMigrationPreview preview = service.PreviewMigration(projectRoot);

                Assert.That(preview.HasTargets, Is.False);
                Assert.That(File.ReadAllText(registrationPath), Is.EqualTo(registrationSource));
                Assert.That(File.ReadAllText(asmdefPath), Is.EqualTo(asmdefSource));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public async Task PreviewMigrationAsync_WhenProjectContainsManyFiles_ReportsIncrementalProgress()
        {
            // Verifies that setup wizard previews report progress before the full scan finishes.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                for (int i = 0; i < 48; i++)
                {
                    File.WriteAllText(
                        Path.Combine(toolDirectory, $"Plain{i}.cs"),
                        $"public sealed class Plain{i} {{}}");
                }

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

                List<ThirdPartyToolMigrationProgress> reports = new();
                RecordingMigrationProgress progress = new(reports);
                ThirdPartyToolMigrationFileService service = new();

                ThirdPartyToolMigrationPreview preview =
                    await service.PreviewMigrationAsync(projectRoot, progress, CancellationToken.None);

                Assert.That(preview.HasTargets, Is.True);
                Assert.That(reports.Count, Is.GreaterThan(1));
                Assert.That(
                    reports.Any(report =>
                        report.TotalItemCount > 0 &&
                        report.ProcessedItemCount > 0 &&
                        report.ProcessedItemCount < report.TotalItemCount),
                    Is.True);
                ThirdPartyToolMigrationProgress lastReport = reports[reports.Count - 1];
                Assert.That(lastReport.ProcessedItemCount, Is.EqualTo(lastReport.TotalItemCount));
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

        private sealed class RecordingMigrationProgress : IProgress<ThirdPartyToolMigrationProgress>
        {
            private readonly List<ThirdPartyToolMigrationProgress> _reports;

            public RecordingMigrationProgress(List<ThirdPartyToolMigrationProgress> reports)
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
