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
        public void MigrationProjectFingerprint_WhenCandidateFileChanges_DoesNotMatch()
        {
            // Verifies that cached migration plans are rejected after candidate source changes.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string toolPath = Path.Combine(toolDirectory, "HelloTool.cs");
                File.WriteAllText(toolPath, "public sealed class HelloTool {}");
                ProjectFileInventory firstInventory = ProjectFileInventory.Create(projectRoot);
                MigrationProjectFingerprint fingerprint =
                    MigrationProjectFingerprint.CaptureFromInventory(firstInventory);

                File.WriteAllText(toolPath, "public sealed class ChangedHelloTool { public int Value; }");
                ProjectFileInventory changedInventory = ProjectFileInventory.Create(projectRoot);

                Assert.That(fingerprint.Matches(changedInventory), Is.False);
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void MigrationProjectFingerprint_WhenCandidateFileContentChangesWithoutMetadataChange_DoesNotMatch()
        {
            // Verifies that cached migration plans are rejected after same-size same-timestamp source changes.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string toolPath = Path.Combine(toolDirectory, "HelloTool.cs");
                string originalSource = "public sealed class AlphaTool {}";
                string changedSource = "public sealed class BravoTool {}";
                DateTime originalLastWriteTimeUtc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                File.WriteAllText(toolPath, originalSource);
                File.SetLastWriteTimeUtc(toolPath, originalLastWriteTimeUtc);
                ProjectFileInventory firstInventory = ProjectFileInventory.Create(projectRoot);
                MigrationProjectFingerprint fingerprint =
                    MigrationProjectFingerprint.CaptureFromInventory(firstInventory);

                File.WriteAllText(toolPath, changedSource);
                File.SetLastWriteTimeUtc(toolPath, originalLastWriteTimeUtc);
                ProjectFileInventory changedInventory = ProjectFileInventory.Create(projectRoot);

                Assert.That(changedSource.Length, Is.EqualTo(originalSource.Length));
                Assert.That(fingerprint.Matches(changedInventory), Is.False);
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void MigrationProjectFingerprint_WhenCandidateFileIsAdded_DoesNotMatch()
        {
            // Verifies that cached migration plans are rejected after candidate files are added.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                File.WriteAllText(
                    Path.Combine(toolDirectory, "HelloTool.cs"),
                    "public sealed class HelloTool {}");
                ProjectFileInventory firstInventory = ProjectFileInventory.Create(projectRoot);
                MigrationProjectFingerprint fingerprint =
                    MigrationProjectFingerprint.CaptureFromInventory(firstInventory);

                File.WriteAllText(
                    Path.Combine(toolDirectory, "AddedTool.cs"),
                    "public sealed class AddedTool {}");
                ProjectFileInventory changedInventory = ProjectFileInventory.Create(projectRoot);

                Assert.That(fingerprint.Matches(changedInventory), Is.False);
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void MigrationProjectFingerprint_WhenAsmdefMetaChanges_DoesNotMatch()
        {
            // Verifies that cached migration plans are rejected after asmref GUID resolution changes.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string asmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                string metaPath = asmdefPath + ".meta";
                File.WriteAllText(
                    asmdefPath,
                    @"{ ""name"": ""VendorTools.Editor"", ""references"": [] }");
                File.WriteAllText(metaPath, "guid: 11111111111111111111111111111111");
                ProjectFileInventory firstInventory = ProjectFileInventory.Create(projectRoot);
                MigrationProjectFingerprint fingerprint =
                    MigrationProjectFingerprint.CaptureFromInventory(firstInventory);

                File.WriteAllText(metaPath, "guid: 22222222222222222222222222222222");
                File.SetLastWriteTimeUtc(metaPath, DateTime.UtcNow.AddMinutes(1));
                ProjectFileInventory changedInventory = ProjectFileInventory.Create(projectRoot);

                Assert.That(fingerprint.Matches(changedInventory), Is.False);
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void MigrationProjectFingerprint_WhenAssemblySidecarFileIsLocked_CaptureDoesNotThrow()
        {
            // Verifies that locked assembly sidecar files do not abort migration preview caching.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string asmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                string metaPath = asmdefPath + ".meta";
                string asmrefPath = Path.Combine(toolDirectory, "VendorTools.asmref");
                File.WriteAllText(
                    asmdefPath,
                    @"{ ""name"": ""VendorTools.Editor"", ""references"": [] }");
                File.WriteAllText(metaPath, "guid: 11111111111111111111111111111111");
                File.WriteAllText(
                    asmrefPath,
                    @"{ ""reference"": ""VendorTools.Editor"" }");
                ProjectFileInventory inventory = ProjectFileInventory.Create(projectRoot);

                using FileStream lockedMeta = new(metaPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                using FileStream lockedAsmref = new(asmrefPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

                Assert.DoesNotThrow(() => MigrationProjectFingerprint.CaptureFromInventory(inventory));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void PreflightScanner_WhenSourceHasNoMigrationMarkers_ReturnsNoTargets()
        {
            // Verifies that startup preflight can skip full scans when no migration marker text exists.
            MigrationTargetPreflightResult result =
                ThirdPartyToolMigrationPreflightScanner.InspectSourceText(
                    "public sealed class PlainTool {}",
                    ".cs");

            Assert.That(result, Is.EqualTo(MigrationTargetPreflightResult.NoTargets));
        }

        [Test]
        public void PreflightScanner_WhenLegacyToolSourceExists_ReturnsHasTargets()
        {
            // Verifies that startup preflight detects direct legacy custom tool source immediately.
            MigrationTargetPreflightResult result =
                ThirdPartyToolMigrationPreflightScanner.InspectSourceText(
                    "using io.github.hatayama.uLoopMCP; [McpTool] public sealed class HelloTool {}",
                    ".cs");

            Assert.That(result, Is.EqualTo(MigrationTargetPreflightResult.HasTargets));
        }

        [Test]
        public void PreflightScanner_WhenLegacyNamespaceIsOnlyComment_ReturnsNeedsFullScan()
        {
            // Verifies that startup preflight does not report targets from marker text inside comments.
            MigrationTargetPreflightResult result =
                ThirdPartyToolMigrationPreflightScanner.InspectSourceText(
                    "// using io.github.hatayama.uLoopMCP;",
                    ".cs");

            Assert.That(result, Is.EqualTo(MigrationTargetPreflightResult.NeedsFullScan));
        }

        [Test]
        public void PreflightScanner_WhenLegacyAsmdefReferenceExists_ReturnsHasTargets()
        {
            // Verifies that startup preflight detects legacy asmdef references without building an inventory.
            MigrationTargetPreflightResult result =
                ThirdPartyToolMigrationPreflightScanner.InspectSourceText(
                    @"{ ""references"": [ ""uLoopMCP.Editor"" ] }",
                    ".asmdef");

            Assert.That(result, Is.EqualTo(MigrationTargetPreflightResult.HasTargets));
        }

        [Test]
        public void PreflightScanner_WhenLegacyAsmdefReferenceIsMalformed_ReturnsNeedsFullScan()
        {
            // Verifies that startup preflight defers malformed legacy asmdefs to the full scanner.
            MigrationTargetPreflightResult result =
                ThirdPartyToolMigrationPreflightScanner.InspectSourceText(
                    @"{ ""references"": [ ""uLoopMCP.Editor"" ",
                    ".asmdef");

            Assert.That(result, Is.EqualTo(MigrationTargetPreflightResult.NeedsFullScan));
        }

        [Test]
        public async Task PreflightScanner_WhenAmbiguousFileExistsWithDirectTarget_ReturnsHasTargets()
        {
            // Verifies that an ambiguous marker does not force full scan before later direct targets are checked.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                File.WriteAllText(
                    Path.Combine(toolDirectory, "0_Ambiguous.cs"),
                    "// using io.github.hatayama.uLoopMCP;");
                File.WriteAllText(
                    Path.Combine(toolDirectory, "1_HelloTool.cs"),
                    "using io.github.hatayama.uLoopMCP; [McpTool] public sealed class HelloTool {}");

                MigrationTargetPreflightResult result =
                    await ThirdPartyToolMigrationPreflightScanner.FindMigrationTargetAsync(
                        projectRoot,
                        CancellationToken.None);

                Assert.That(result, Is.EqualTo(MigrationTargetPreflightResult.HasTargets));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
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
        public void GetRelativePathSegments_WhenPathContainsParentSegments_NormalizesBeforeSplitting()
        {
            // Verifies that path ancestry checks use canonical paths before deriving Unity-relative segments.
            string projectRoot = Path.Combine(Path.GetTempPath(), "UnityCliLoopPathRoot");
            string filePath = Path.Combine(
                projectRoot,
                "..",
                "UnityCliLoopPathRoot",
                "Assets",
                "VendorTools",
                "HelloTool.cs");

            string[] segments = ThirdPartyToolMigrationAssemblyReferenceResolver.GetRelativePathSegments(
                filePath,
                projectRoot);

            Assert.That(segments, Is.EqualTo(new[] { "Assets", "VendorTools", "HelloTool.cs" }));
        }

        [Test]
        public void IsSameOrChildPath_WhenChildContainsParentSegments_NormalizesBeforeComparison()
        {
            // Verifies that assembly-directory ancestry checks are based on canonical paths.
            string projectRoot = Path.Combine(Path.GetTempPath(), "UnityCliLoopPathRoot");
            string parentPath = Path.Combine(projectRoot, "Assets", "VendorTools");
            string childPath = Path.Combine(
                projectRoot,
                "Assets",
                "Other",
                "..",
                "VendorTools",
                "Nested");

            bool isSameOrChild = ThirdPartyToolMigrationAssemblyReferenceResolver.IsSameOrChildPath(
                childPath,
                parentPath);

            Assert.That(isSameOrChild, Is.True);
        }

        [Test]
        public void IsSameOrChildPath_WhenEitherPathIsEmpty_ThrowsArgumentException()
        {
            // Verifies that empty paths fail fast instead of normalizing to the process directory.
            string nonEmptyPath = Path.GetTempPath();

            ArgumentException childException = Assert.Throws<ArgumentException>(
                () => ThirdPartyToolMigrationAssemblyReferenceResolver.IsSameOrChildPath(string.Empty, nonEmptyPath));
            ArgumentException parentException = Assert.Throws<ArgumentException>(
                () => ThirdPartyToolMigrationAssemblyReferenceResolver.IsSameOrChildPath(nonEmptyPath, string.Empty));

            Assert.That(childException.ParamName, Is.EqualTo("childPath"));
            Assert.That(parentException.ParamName, Is.EqualTo("parentPath"));
        }

        [Test]
        public void ReadAsmdefGuidReferenceFromMetaFile_WhenMetaReadThrowsIOException_ReturnsEmpty()
        {
            // Verifies that unreadable .meta files do not abort assembly reference discovery.
            string guidReference =
                ThirdPartyToolMigrationAssemblyReferenceResolver.ReadAsmdefGuidReferenceFromMetaFile(
                    "Assets/VendorTools/VendorTools.Editor.asmdef.meta",
                    _ => throw new IOException("locked"));

            Assert.That(guidReference, Is.Empty);
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
        public void ApplyMigration_WhenManualRegistrationExists_RewritesToToolContractsReference()
        {
            // Verifies that migrated manual registration source uses the public ToolContracts registrar assembly.
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
                    "io.github.hatayama.UnityCliLoop.ToolContracts.UnityCliLoopToolRegistrar.RegisterCustomTool"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Not.Contain("GUID:214998e563c124e8a88199b2dd1f522d"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Not.Contain("GUID:5c4588558a3624eacbce0f50007cf1eb"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenCurrentManualRegistrationExists_RewritesApplicationNamespace()
        {
            // Verifies that mixed assemblies move current registrar dependencies to ToolContracts while legacy tools migrate.
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

                Assert.That(result.FileCount, Is.EqualTo(3));
                Assert.That(File.ReadAllText(toolPath), Does.Contain("UnityCliLoopTool<HelloSchema, HelloResponse>"));
                Assert.That(File.ReadAllText(registrationPath), Does.Contain(
                    "using io.github.hatayama.UnityCliLoop.ToolContracts;"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Not.Contain("GUID:214998e563c124e8a88199b2dd1f522d"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Not.Contain("GUID:5c4588558a3624eacbce0f50007cf1eb"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenCurrentManualRegistrationExistsWithLegacyAsmdefName_RewritesApplicationNamespace()
        {
            // Verifies that partially migrated registrar code moves to ToolContracts and receives its asmdef ref.
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

                Assert.That(result.FileCount, Is.EqualTo(2));
                Assert.That(File.ReadAllText(registrationPath), Does.Contain(
                    "using io.github.hatayama.UnityCliLoop.ToolContracts;"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Not.Contain("GUID:214998e563c124e8a88199b2dd1f522d"));
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
            // Verifies that partially migrated GUID refs add ToolContracts while source moves off Application.
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

                Assert.That(result.FileCount, Is.EqualTo(2));
                Assert.That(File.ReadAllText(registrationPath), Does.Contain(
                    "using io.github.hatayama.UnityCliLoop.ToolContracts;"));
                Assert.That(File.ReadAllText(registrationPath), Does.Not.Contain(
                    "using io.github.hatayama.UnityCliLoop.Application;"));
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
        public void ApplyMigration_WhenLocalUIElementInfoWasAlreadyQualified_PreservesExplicitFirstPartyReference()
        {
            // Verifies that project-wide migration keeps explicit first-party DTO references.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string toolPath = Path.Combine(toolDirectory, "CurrentElementTool.cs");
                File.WriteAllText(toolPath, @"using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using io.github.hatayama.UnityCliLoop.ToolContracts;

public sealed class CurrentElementTool : UnityCliLoopTool<ElementSchema, ElementResponse>
{
    public async Task<Texture2D> CaptureAsync(EditorWindow window, CancellationToken ct)
    {
        return await EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, ct);
    }

    private List<io.github.hatayama.UnityCliLoop.ToolContracts.UIElementInfo> BuildFirstPartyElements()
    {
        List<io.github.hatayama.UnityCliLoop.ToolContracts.UIElementInfo> elements = new();
        elements.Add(CreateFirstPartyElement());
        return elements;
    }

    private io.github.hatayama.UnityCliLoop.ToolContracts.UIElementInfo CreateFirstPartyElement()
    {
        return new io.github.hatayama.UnityCliLoop.ToolContracts.UIElementInfo();
    }

    private UIElementInfo CreateProjectElement()
    {
        return new UIElementInfo();
    }
}

public sealed class ElementSchema : UnityCliLoopToolSchema
{
}

public sealed class ElementResponse : UnityCliLoopToolResponse
{
}

public sealed class UIElementInfo
{
}");

                ThirdPartyToolMigrationFileService service = new();
                ThirdPartyToolMigrationPreview preview = service.PreviewMigration(projectRoot);
                ThirdPartyToolMigrationResult result = service.ApplyMigration(projectRoot);
                string migratedSource = File.ReadAllText(toolPath);

                Assert.That(preview.FileCount, Is.EqualTo(1));
                Assert.That(result.FileCount, Is.EqualTo(1));
                Assert.That(migratedSource, Does.Contain(
                    "private List<io.github.hatayama.UnityCliLoop.ToolContracts.UIElementInfo> BuildFirstPartyElements()"));
                Assert.That(migratedSource, Does.Contain(
                    "private io.github.hatayama.UnityCliLoop.ToolContracts.UIElementInfo CreateFirstPartyElement()"));
                Assert.That(migratedSource, Does.Contain(
                    "new io.github.hatayama.UnityCliLoop.ToolContracts.UIElementInfo()"));
                Assert.That(migratedSource, Does.Contain("private UIElementInfo CreateProjectElement()"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenQualifiedFirstPartyDtoSharesLocalName_UsesToolContractsReference()
        {
            // Verifies that unambiguous first-party DTO references add the public ToolContracts asmdef dependency.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string toolPath = Path.Combine(toolDirectory, "CurrentElementTool.cs");
                string asmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                string toolSource = @"public sealed class CurrentElementTool
{
    private io.github.hatayama.UnityCliLoop.ToolContracts.UIElementInfo CreateFirstPartyElement()
    {
        return new io.github.hatayama.UnityCliLoop.ToolContracts.UIElementInfo();
    }

    private UIElementInfo CreateProjectElement()
    {
        return new UIElementInfo();
    }
}

public sealed class UIElementInfo
{
}";
                File.WriteAllText(toolPath, toolSource);
                File.WriteAllText(asmdefPath, @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": []
}");

                ThirdPartyToolMigrationFileService service = new();
                ThirdPartyToolMigrationResult result = service.ApplyMigration(projectRoot);

                Assert.That(result.FileCount, Is.EqualTo(1));
                Assert.That(File.ReadAllText(toolPath), Is.EqualTo(toolSource));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Not.Contain("GUID:a0bdbd2c5705643fbb9aef9fac8fd46a"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenCurrentRegistrarExistsWithToolContractsAsmdefReference_RewritesApplicationNamespace()
        {
            // Verifies that already-replaced asmdef refs keep using the public ToolContracts assembly.
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
                Assert.That(File.ReadAllText(registrationPath), Does.Contain(
                    "using io.github.hatayama.UnityCliLoop.ToolContracts;"));
                Assert.That(File.ReadAllText(registrationPath), Does.Not.Contain(
                    "using io.github.hatayama.UnityCliLoop.Application;"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Not.Contain("GUID:214998e563c124e8a88199b2dd1f522d"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Not.Contain("GUID:5c4588558a3624eacbce0f50007cf1eb"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenLegacyRegistrarReturnIsUsedWithoutExplicitToolInfo_UsesToolContractsReference()
        {
            // Verifies that registrar return types use ToolContracts without explicit ToolInfo usage.
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
                    "io.github.hatayama.UnityCliLoop.ToolContracts.UnityCliLoopToolRegistrar.GetRegisteredCustomTools"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Not.Contain("GUID:214998e563c124e8a88199b2dd1f522d"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Not.Contain("GUID:5c4588558a3624eacbce0f50007cf1eb"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenDomainMetadataExistsWithoutManualRegistration_UsesToolContractsReference()
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
                    "io.github.hatayama.UnityCliLoop.ToolContracts.ToolInfo[]"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Not.Contain("GUID:5c4588558a3624eacbce0f50007cf1eb"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Not.Contain("GUID:214998e563c124e8a88199b2dd1f522d"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenLegacyDomainHelperExists_UsesToolContractsReference()
        {
            // Verifies that helpers moved to ToolContracts migrate their source and asmdef dependency together.
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
                    "io.github.hatayama.UnityCliLoop.ToolContracts.ServiceResult<int> CreateResult"));
                Assert.That(File.ReadAllText(helperPath), Does.Contain(
                    "io.github.hatayama.UnityCliLoop.ToolContracts.ToolSettingsCatalogItem[] GetCatalog"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Not.Contain("GUID:5c4588558a3624eacbce0f50007cf1eb"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Not.Contain("GUID:214998e563c124e8a88199b2dd1f522d"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenCurrentDomainMetadataExistsWithLegacyAsmdefGuid_RewritesDomainNamespace()
        {
            // Verifies that partially migrated metadata helpers move to ToolContracts and receive its asmdef ref.
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

                Assert.That(result.FileCount, Is.EqualTo(2));
                Assert.That(File.ReadAllText(metadataPath), Does.Contain(
                    "using io.github.hatayama.UnityCliLoop.ToolContracts;"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Not.Contain("GUID:5c4588558a3624eacbce0f50007cf1eb"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:214998e563c124e8a88199b2dd1f522d"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenAssemblyUsesCurrentDomainGlobalUsingAndSplitMetadata_RewritesDomainNamespace()
        {
            // Verifies that split V3 Domain metadata files move to ToolContracts and receive their asmdef reference.
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

                Assert.That(result.FileCount, Is.EqualTo(2));
                Assert.That(File.ReadAllText(globalUsingPath), Does.Contain(
                    "global using io.github.hatayama.UnityCliLoop.ToolContracts;"));
                Assert.That(File.ReadAllText(metadataPath), Is.EqualTo(metadataSource));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Not.Contain("GUID:5c4588558a3624eacbce0f50007cf1eb"));
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
            // Verifies that split files using old screenshot helpers receive the required ToolContracts assembly reference.
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
                    "io.github.hatayama.UnityCliLoop.ToolContracts.EditorWindowCaptureUtility.FindWindowsByName"));
                Assert.That(File.ReadAllText(helperPath), Does.Contain(
                    "io.github.hatayama.UnityCliLoop.ToolContracts.WindowMatchMode.exact"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Not.Contain("GUID:a0bdbd2c5705643fbb9aef9fac8fd46a"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenCurrentToolContractsFileUsesLegacyScreenshotCapture_KeepsToolContractsReference()
        {
            // Verifies that partially migrated files finish screenshot helper migration using ToolContracts only.
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

                Assert.That(result.FileCount, Is.EqualTo(1));
                Assert.That(File.ReadAllText(toolPath), Does.Contain(
                    "io.github.hatayama.UnityCliLoop.ToolContracts.EditorWindowCaptureUtility.CaptureWindowAsync"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Not.Contain("GUID:a0bdbd2c5705643fbb9aef9fac8fd46a"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenCurrentToolContractsFileUsesLegacyMainThreadSwitcher_KeepsToolContractsReference()
        {
            // Verifies that partially migrated files finish main-thread switcher migration using ToolContracts only.
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

                Assert.That(result.FileCount, Is.EqualTo(1));
                Assert.That(File.ReadAllText(toolPath), Does.Contain(
                    "io.github.hatayama.UnityCliLoop.ToolContracts.MainThreadSwitcher.SwitchToMainThread(ct)"));
                Assert.That(File.ReadAllText(toolPath), Does.Not.Contain("PlayerLoopTiming"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Not.Contain("GUID:214998e563c124e8a88199b2dd1f522d"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenFileScopedLegacyUsingUsesMainThreadSwitcher_AddsToolContractsReference()
        {
            // Verifies that regular legacy usings add the ToolContracts asmdef reference for migrated main-thread calls.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string toolPath = Path.Combine(toolDirectory, "MainThreadTool.cs");
                string asmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                File.WriteAllText(toolPath, @"using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

public sealed class MainThreadTool
{
    public async Task RunAsync(CancellationToken ct)
    {
        await MainThreadSwitcher.SwitchToMainThread(PlayerLoopTiming.Update, ct);
    }
}");
                File.WriteAllText(asmdefPath, @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": []
}");

                ThirdPartyToolMigrationFileService service = new();
                ThirdPartyToolMigrationResult result = service.ApplyMigration(projectRoot);
                string migratedSource = File.ReadAllText(toolPath);
                string migratedAsmdef = File.ReadAllText(asmdefPath);

                Assert.That(result.FilePaths.Contains(toolPath), Is.True);
                Assert.That(result.FilePaths.Contains(asmdefPath), Is.True);
                Assert.That(migratedSource, Does.Contain(
                    "io.github.hatayama.UnityCliLoop.ToolContracts.MainThreadSwitcher.SwitchToMainThread(ct)"));
                Assert.That(migratedAsmdef, Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
                Assert.That(migratedAsmdef, Does.Not.Contain("GUID:214998e563c124e8a88199b2dd1f522d"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenLegacyUsingUsesBareFirstPartyScreenshotDto_AddsToolContractsReference()
        {
            // Verifies that regular legacy usings that migrate screenshot DTOs repair asmdef references.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string toolPath = Path.Combine(toolDirectory, "ElementTool.cs");
                string asmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                File.WriteAllText(toolPath, @"using io.github.hatayama.uLoopMCP;

public sealed class ElementTool
{
    public UIElementInfo CreateElement()
    {
        return new UIElementInfo();
    }
}");
                File.WriteAllText(asmdefPath, @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": []
}");

                ThirdPartyToolMigrationFileService service = new();
                ThirdPartyToolMigrationResult result = service.ApplyMigration(projectRoot);
                string migratedSource = File.ReadAllText(toolPath);
                string migratedAsmdef = File.ReadAllText(asmdefPath);

                Assert.That(result.FilePaths.Contains(toolPath), Is.True);
                Assert.That(result.FilePaths.Contains(asmdefPath), Is.True);
                Assert.That(migratedSource, Does.Contain(
                    "io.github.hatayama.UnityCliLoop.ToolContracts.UIElementInfo"));
                Assert.That(migratedAsmdef, Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
                Assert.That(migratedAsmdef, Does.Not.Contain("GUID:a0bdbd2c5705643fbb9aef9fac8fd46a"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenAssemblyUsesCurrentApplicationGlobalUsing_RewritesApplicationNamespace()
        {
            // Verifies that split files relying on a current Application global using move to ToolContracts.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string globalUsingPath = Path.Combine(toolDirectory, "GlobalUsings.cs");
                string mainThreadPath = Path.Combine(toolDirectory, "MainThreadUsage.cs");
                string asmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                File.WriteAllText(globalUsingPath, "global using io.github.hatayama.UnityCliLoop.Application;");
                File.WriteAllText(mainThreadPath, @"using System.Threading;
using System.Threading.Tasks;

public sealed class MainThreadUsage
{
    public async Task RunAsync(CancellationToken ct)
    {
        await MainThreadSwitcher.SwitchToMainThread(ct);
    }
}");
                File.WriteAllText(asmdefPath, @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": [
        ""GUID:fc3fd32eddbee40e39c2d76dc184957b""
    ]
}");

                ThirdPartyToolMigrationFileService service = new();
                ThirdPartyToolMigrationResult result = service.ApplyMigration(projectRoot);

                Assert.That(result.FileCount, Is.EqualTo(1));
                Assert.That(File.ReadAllText(globalUsingPath), Does.Contain(
                    "global using io.github.hatayama.UnityCliLoop.ToolContracts;"));
                Assert.That(File.ReadAllText(mainThreadPath), Does.Contain(
                    "await MainThreadSwitcher.SwitchToMainThread(ct);"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Not.Contain("GUID:214998e563c124e8a88199b2dd1f522d"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenAssemblyUsesCurrentApplicationGlobalUsing_RewritesBareMainThreadSwitcherTiming()
        {
            // Verifies that split files relying on a current Application global using drop removed timing arguments.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string globalUsingPath = Path.Combine(toolDirectory, "GlobalUsings.cs");
                string mainThreadPath = Path.Combine(toolDirectory, "MainThreadUsage.cs");
                string asmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                File.WriteAllText(globalUsingPath, "global using io.github.hatayama.UnityCliLoop.Application;");
                File.WriteAllText(mainThreadPath, @"using System.Threading;
using System.Threading.Tasks;

public sealed class MainThreadUsage
{
    public async Task RunAsync(CancellationToken ct)
    {
        await MainThreadSwitcher.SwitchToMainThread(PlayerLoopTiming.Update, ct);
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
                string migratedSource = File.ReadAllText(mainThreadPath);

                Assert.That(result.FilePaths.Contains(mainThreadPath), Is.True);
                Assert.That(migratedSource, Does.Contain(
                    "await MainThreadSwitcher.SwitchToMainThread(ct);"));
                Assert.That(migratedSource, Does.Not.Contain("PlayerLoopTiming"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenAssemblyUsesCurrentApplicationGlobalAlias_RewritesQualifiedMainThreadSwitcherTiming()
        {
            // Verifies that split files relying on a current Application global alias drop removed timing arguments.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string globalUsingPath = Path.Combine(toolDirectory, "GlobalUsings.cs");
                string mainThreadPath = Path.Combine(toolDirectory, "MainThreadUsage.cs");
                string asmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                File.WriteAllText(globalUsingPath, "global using App = io.github.hatayama.UnityCliLoop.Application;");
                File.WriteAllText(mainThreadPath, @"using System.Threading;
using System.Threading.Tasks;

public sealed class MainThreadUsage
{
    public async Task RunAsync(CancellationToken ct)
    {
        await App.MainThreadSwitcher.SwitchToMainThread(PlayerLoopTiming.Update, ct);
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
                string migratedSource = File.ReadAllText(mainThreadPath);

                Assert.That(result.FilePaths.Contains(globalUsingPath), Is.True);
                Assert.That(result.FilePaths.Contains(mainThreadPath), Is.True);
                Assert.That(File.ReadAllText(globalUsingPath), Does.Contain(
                    "global using App = io.github.hatayama.UnityCliLoop.ToolContracts;"));
                Assert.That(migratedSource, Does.Contain(
                    "await App.MainThreadSwitcher.SwitchToMainThread(ct);"));
                Assert.That(migratedSource, Does.Not.Contain("PlayerLoopTiming"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenLegacyTimingWrapperCallerIsSplit_RewritesCallerArguments()
        {
            // Verifies that timing wrapper signature changes update callers in other source files.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string wrapperPath = Path.Combine(toolDirectory, "MainThreadWrapper.cs");
                string callerPath = Path.Combine(toolDirectory, "MainThreadCaller.cs");
                File.WriteAllText(wrapperPath, @"using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

public sealed class MainThreadWrapper
{
    public async Task RunAsync(PlayerLoopTiming loop, CancellationToken ct)
    {
        await MainThreadSwitcher.SwitchToMainThread(loop, ct);
    }
}");
                File.WriteAllText(callerPath, @"using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

public sealed class MainThreadCaller
{
    public Task CallAsync(MainThreadWrapper wrapper, CancellationToken ct)
    {
        return wrapper.RunAsync(PlayerLoopTiming.Update, ct);
    }
}");

                ThirdPartyToolMigrationFileService service = new();
                ThirdPartyToolMigrationResult result = service.ApplyMigration(projectRoot);
                string migratedWrapperSource = File.ReadAllText(wrapperPath);
                string migratedCallerSource = File.ReadAllText(callerPath);

                Assert.That(result.FileCount, Is.EqualTo(2));
                Assert.That(migratedWrapperSource, Does.Contain("public async Task RunAsync(CancellationToken ct)"));
                Assert.That(migratedCallerSource, Does.Contain("return wrapper.RunAsync(ct);"));
                Assert.That(migratedWrapperSource, Does.Not.Contain("PlayerLoopTiming"));
                Assert.That(migratedCallerSource, Does.Not.Contain("PlayerLoopTiming"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenLegacyTimingWrapperCallerIsInAnotherAssembly_RewritesCallerArguments()
        {
            // Verifies that public timing wrapper signature changes update callers in other asmdef assemblies.
            string projectRoot = CreateProjectRoot();
            try
            {
                string helperDirectory = Path.Combine(projectRoot, "Assets", "VendorHelpers");
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(helperDirectory);
                Directory.CreateDirectory(toolDirectory);
                string wrapperPath = Path.Combine(helperDirectory, "MainThreadWrapper.cs");
                string callerPath = Path.Combine(toolDirectory, "MainThreadCaller.cs");
                string helperAsmdefPath = Path.Combine(helperDirectory, "VendorHelpers.Editor.asmdef");
                string toolAsmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                File.WriteAllText(wrapperPath, @"using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

public sealed class MainThreadWrapper
{
    public async Task RunAsync(PlayerLoopTiming loop, CancellationToken ct)
    {
        await MainThreadSwitcher.SwitchToMainThread(loop, ct);
    }
}");
                File.WriteAllText(callerPath, @"using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

public sealed class MainThreadCaller
{
    public Task CallAsync(MainThreadWrapper wrapper, CancellationToken ct)
    {
        return wrapper.RunAsync(PlayerLoopTiming.Update, ct);
    }
}");
                File.WriteAllText(helperAsmdefPath, @"{
    ""name"": ""VendorHelpers.Editor"",
    ""references"": []
}");
                File.WriteAllText(toolAsmdefPath, @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": [
        ""VendorHelpers.Editor""
    ]
}");

                ThirdPartyToolMigrationFileService service = new();
                ThirdPartyToolMigrationResult result = service.ApplyMigration(projectRoot);
                string migratedWrapperSource = File.ReadAllText(wrapperPath);
                string migratedCallerSource = File.ReadAllText(callerPath);

                Assert.That(result.FilePaths.Contains(wrapperPath), Is.True);
                Assert.That(result.FilePaths.Contains(callerPath), Is.True);
                Assert.That(migratedWrapperSource, Does.Contain("public async Task RunAsync(CancellationToken ct)"));
                Assert.That(migratedCallerSource, Does.Contain("return wrapper.RunAsync(ct);"));
                Assert.That(migratedCallerSource, Does.Not.Contain("PlayerLoopTiming"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenUnrelatedAssemblyHasSameTimingWrapper_KeepsUnreachableCallerArguments()
        {
            // Verifies that cross-file timing rewrites do not cross asmdef boundaries without a reference.
            string projectRoot = CreateProjectRoot();
            try
            {
                string helperDirectory = Path.Combine(projectRoot, "Assets", "VendorHelpers");
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                string unrelatedDirectory = Path.Combine(projectRoot, "Assets", "UnrelatedTools");
                Directory.CreateDirectory(helperDirectory);
                Directory.CreateDirectory(toolDirectory);
                Directory.CreateDirectory(unrelatedDirectory);
                string helperWrapperPath = Path.Combine(helperDirectory, "MainThreadWrapper.cs");
                string toolCallerPath = Path.Combine(toolDirectory, "MainThreadCaller.cs");
                string unrelatedWrapperPath = Path.Combine(unrelatedDirectory, "MainThreadWrapper.cs");
                string unrelatedCallerPath = Path.Combine(unrelatedDirectory, "MainThreadCaller.cs");
                string helperAsmdefPath = Path.Combine(helperDirectory, "VendorHelpers.Editor.asmdef");
                string toolAsmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                string unrelatedAsmdefPath = Path.Combine(unrelatedDirectory, "UnrelatedTools.Editor.asmdef");
                File.WriteAllText(helperWrapperPath, @"using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

public sealed class MainThreadWrapper
{
    public async Task RunAsync(PlayerLoopTiming loop, CancellationToken ct)
    {
        await MainThreadSwitcher.SwitchToMainThread(loop, ct);
    }
}");
                File.WriteAllText(toolCallerPath, @"using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

public sealed class MainThreadCaller
{
    public Task CallAsync(MainThreadWrapper wrapper, CancellationToken ct)
    {
        return wrapper.RunAsync(PlayerLoopTiming.Update, ct);
    }
}");
                File.WriteAllText(unrelatedWrapperPath, @"using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

public sealed class MainThreadWrapper
{
    public Task RunAsync(PlayerLoopTiming loop, CancellationToken ct)
    {
        return Task.CompletedTask;
    }
}");
                File.WriteAllText(unrelatedCallerPath, @"using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

public sealed class MainThreadCaller
{
    public Task CallAsync(MainThreadWrapper wrapper, CancellationToken ct)
    {
        return wrapper.RunAsync(PlayerLoopTiming.Update, ct);
    }
}");
                File.WriteAllText(helperAsmdefPath, @"{
    ""name"": ""VendorHelpers.Editor"",
    ""references"": []
}");
                File.WriteAllText(toolAsmdefPath, @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": [
        ""VendorHelpers.Editor""
    ]
}");
                File.WriteAllText(unrelatedAsmdefPath, @"{
    ""name"": ""UnrelatedTools.Editor"",
    ""references"": []
}");

                ThirdPartyToolMigrationFileService service = new();
                ThirdPartyToolMigrationResult result = service.ApplyMigration(projectRoot);
                string migratedToolCallerSource = File.ReadAllText(toolCallerPath);
                string migratedUnrelatedWrapperSource = File.ReadAllText(unrelatedWrapperPath);
                string migratedUnrelatedCallerSource = File.ReadAllText(unrelatedCallerPath);

                Assert.That(result.FilePaths.Contains(toolCallerPath), Is.True);
                Assert.That(migratedToolCallerSource, Does.Contain("return wrapper.RunAsync(ct);"));
                Assert.That(migratedUnrelatedWrapperSource, Does.Contain(
                    "public Task RunAsync(PlayerLoopTiming loop, CancellationToken ct)"));
                Assert.That(migratedUnrelatedCallerSource, Does.Contain(
                    "return wrapper.RunAsync(PlayerLoopTiming.Update, ct);"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenCrossFileTimingWrapperCallChain_RevisitsStaleOuterParameter()
        {
            // Verifies that cross-file wrapper rewrites also remove newly stale timing parameters.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string innerPath = Path.Combine(toolDirectory, "InnerWrapper.cs");
                string outerPath = Path.Combine(toolDirectory, "OuterWrapper.cs");
                string callerPath = Path.Combine(toolDirectory, "MainThreadCaller.cs");
                File.WriteAllText(innerPath, @"using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

public sealed class InnerWrapper
{
    public async Task InnerAsync(PlayerLoopTiming loop, CancellationToken ct)
    {
        await MainThreadSwitcher.SwitchToMainThread(loop, ct);
    }
}");
                File.WriteAllText(outerPath, @"using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

public sealed class OuterWrapper
{
    public Task OuterAsync(InnerWrapper inner, PlayerLoopTiming loop, CancellationToken ct)
    {
        return inner.InnerAsync(loop, ct);
    }
}");
                File.WriteAllText(callerPath, @"using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

public sealed class MainThreadCaller
{
    public Task CallAsync(OuterWrapper outer, InnerWrapper inner, CancellationToken ct)
    {
        return outer.OuterAsync(inner, PlayerLoopTiming.Update, ct);
    }
}");

                ThirdPartyToolMigrationFileService service = new();
                ThirdPartyToolMigrationResult result = service.ApplyMigration(projectRoot);
                string migratedInnerSource = File.ReadAllText(innerPath);
                string migratedOuterSource = File.ReadAllText(outerPath);
                string migratedCallerSource = File.ReadAllText(callerPath);

                Assert.That(result.FilePaths.Contains(innerPath), Is.True);
                Assert.That(result.FilePaths.Contains(outerPath), Is.True);
                Assert.That(result.FilePaths.Contains(callerPath), Is.True);
                Assert.That(migratedInnerSource, Does.Contain("public async Task InnerAsync(CancellationToken ct)"));
                Assert.That(migratedOuterSource, Does.Contain(
                    "public Task OuterAsync(InnerWrapper inner, CancellationToken ct)"));
                Assert.That(migratedOuterSource, Does.Contain("return inner.InnerAsync(ct);"));
                Assert.That(migratedCallerSource, Does.Contain("return outer.OuterAsync(inner, ct);"));
                Assert.That(migratedOuterSource, Does.Not.Contain("PlayerLoopTiming"));
                Assert.That(migratedCallerSource, Does.Not.Contain("PlayerLoopTiming"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenAssemblyUsesCurrentToolContractsGlobalUsing_RewritesSplitPartialHelpers()
        {
            // Verifies that split files relying on a current ToolContracts global using finish partial migration.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string globalUsingPath = Path.Combine(toolDirectory, "GlobalUsings.cs");
                string toolPath = Path.Combine(toolDirectory, "PartialTool.cs");
                string asmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                File.WriteAllText(globalUsingPath, "global using io.github.hatayama.UnityCliLoop.ToolContracts;");
                File.WriteAllText(toolPath, @"using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

public sealed class PartialTool : UnityCliLoopTool<PartialSchema, PartialResponse>
{
    public async Task<Texture2D> CaptureAsync(EditorWindow window, CancellationToken ct)
    {
        await EditorDelay.DelayFrame(2, ct);
        await TimerDelay.Wait(10, cancellationToken: ct);
        await MainThreadSwitcher.SwitchToMainThread(PlayerLoopTiming.Update, ct);
        Texture2D texture = await EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, ct);
        return texture;
    }
}

public sealed class PartialSchema : UnityCliLoopToolSchema
{
}

public sealed class PartialResponse : UnityCliLoopToolResponse
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
                string migratedSource = File.ReadAllText(toolPath);

                Assert.That(result.FileCount, Is.EqualTo(1));
                Assert.That(migratedSource, Does.Contain(
                    "EditorFrameWaiter.WaitFramesOrTimeoutAsync(2, UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS, ct)"));
                Assert.That(migratedSource, Does.Contain("TimerDelay.Wait(10, ct: ct)"));
                Assert.That(migratedSource, Does.Contain(
                    "io.github.hatayama.UnityCliLoop.ToolContracts.MainThreadSwitcher.SwitchToMainThread(ct)"));
                Assert.That(migratedSource, Does.Contain(
                    "io.github.hatayama.UnityCliLoop.ToolContracts.EditorWindowCaptureUtility.CaptureWindowAsync"));
                Assert.That(migratedSource, Does.Not.Contain("EditorDelay"));
                Assert.That(migratedSource, Does.Not.Contain("PlayerLoopTiming"));
                Assert.That(migratedSource, Does.Not.Contain("cancellationToken:"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Not.Contain("GUID:214998e563c124e8a88199b2dd1f522d"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Not.Contain("GUID:a0bdbd2c5705643fbb9aef9fac8fd46a"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenCurrentFirstPartyToolsCaptureNeedsTimeout_AddsToolContractsReference()
        {
            // Verifies that migrated capture timeout constants have a direct ToolContracts asmdef reference.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string toolPath = Path.Combine(toolDirectory, "CurrentScreenshotTool.cs");
                string asmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                File.WriteAllText(toolPath, @"using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;

public sealed class CurrentScreenshotTool
{
    public async Task<Texture2D> CaptureAsync(EditorWindow window, CancellationToken ct)
    {
        return await EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, ct);
    }
}");
                File.WriteAllText(asmdefPath, @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": []
}");

                ThirdPartyToolMigrationFileService service = new();
                ThirdPartyToolMigrationResult result = service.ApplyMigration(projectRoot);
                string migratedSource = File.ReadAllText(toolPath);
                string migratedAsmdef = File.ReadAllText(asmdefPath);

                Assert.That(result.FileCount, Is.EqualTo(2));
                Assert.That(migratedSource, Does.Contain(
                    "io.github.hatayama.UnityCliLoop.ToolContracts.UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS"));
                Assert.That(migratedAsmdef, Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
                Assert.That(migratedAsmdef, Does.Not.Contain("GUID:a0bdbd2c5705643fbb9aef9fac8fd46a"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenAssemblyUsesCurrentFirstPartyToolsGlobalUsing_RewritesSplitCapture()
        {
            // Verifies that split files relying on a current FirstPartyTools global using finish capture migration.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string globalUsingPath = Path.Combine(toolDirectory, "GlobalUsings.cs");
                string toolPath = Path.Combine(toolDirectory, "CurrentScreenshotTool.cs");
                string asmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                File.WriteAllText(globalUsingPath, "global using io.github.hatayama.UnityCliLoop.FirstPartyTools;");
                File.WriteAllText(toolPath, @"using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

public sealed class CurrentScreenshotTool
{
    public async Task<Texture2D> CaptureAsync(EditorWindow window, CancellationToken ct)
    {
        return await EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, ct);
    }
}");
                File.WriteAllText(asmdefPath, @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": []
}");

                ThirdPartyToolMigrationFileService service = new();
                ThirdPartyToolMigrationResult result = service.ApplyMigration(projectRoot);
                string migratedSource = File.ReadAllText(toolPath);
                string migratedAsmdef = File.ReadAllText(asmdefPath);

                Assert.That(result.FileCount, Is.EqualTo(3));
                Assert.That(File.ReadAllText(globalUsingPath), Does.Contain(
                    "global using io.github.hatayama.UnityCliLoop.ToolContracts;"));
                Assert.That(migratedSource, Does.Contain(
                    "io.github.hatayama.UnityCliLoop.ToolContracts.EditorWindowCaptureUtility.CaptureWindowAsync"));
                Assert.That(migratedSource, Does.Contain(
                    "io.github.hatayama.UnityCliLoop.ToolContracts.UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS"));
                Assert.That(migratedSource, Does.Not.Contain("return await EditorWindowCaptureUtility.CaptureWindowAsync"));
                Assert.That(migratedAsmdef, Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
                Assert.That(migratedAsmdef, Does.Not.Contain("GUID:a0bdbd2c5705643fbb9aef9fac8fd46a"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenAssemblyUsesCurrentFirstPartyToolsGlobalAlias_RewritesQualifiedSplitCapture()
        {
            // Verifies that split files relying on a current FirstPartyTools global alias finish capture migration.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string globalUsingPath = Path.Combine(toolDirectory, "GlobalUsings.cs");
                string toolPath = Path.Combine(toolDirectory, "CurrentScreenshotTool.cs");
                string asmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                File.WriteAllText(globalUsingPath, "global using Fpt = io.github.hatayama.UnityCliLoop.FirstPartyTools;");
                File.WriteAllText(toolPath, @"using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

public sealed class CurrentScreenshotTool
{
    public async Task<Texture2D> CaptureAsync(EditorWindow window, CancellationToken ct)
    {
        return await Fpt.EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, ct);
    }
}");
                File.WriteAllText(asmdefPath, @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": [
        ""GUID:fc3fd32eddbee40e39c2d76dc184957b"",
        ""GUID:a0bdbd2c5705643fbb9aef9fac8fd46a""
    ]
}");

                ThirdPartyToolMigrationFileService service = new();
                ThirdPartyToolMigrationResult result = service.ApplyMigration(projectRoot);
                string migratedSource = File.ReadAllText(toolPath);

                Assert.That(result.FileCount, Is.EqualTo(2));
                Assert.That(result.FilePaths.Contains(toolPath), Is.True);
                Assert.That(result.FilePaths.Contains(globalUsingPath), Is.True);
                Assert.That(File.ReadAllText(globalUsingPath), Does.Contain(
                    "global using Fpt = io.github.hatayama.UnityCliLoop.ToolContracts;"));
                Assert.That(migratedSource, Does.Contain(
                    "io.github.hatayama.UnityCliLoop.ToolContracts.EditorWindowCaptureUtility.CaptureWindowAsync"));
                Assert.That(migratedSource, Does.Contain(
                    "io.github.hatayama.UnityCliLoop.ToolContracts.UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS"));
                Assert.That(migratedSource, Does.Not.Contain(
                    "return await Fpt.EditorWindowCaptureUtility.CaptureWindowAsync"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenCurrentFirstPartyToolsGlobalUsingOnlyNeedsReference_RewritesFirstPartyNamespace()
        {
            // Verifies that a current FirstPartyTools global using by itself moves to ToolContracts.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string globalUsingPath = Path.Combine(toolDirectory, "GlobalUsings.cs");
                string asmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                File.WriteAllText(globalUsingPath, "global using io.github.hatayama.UnityCliLoop.FirstPartyTools;");
                File.WriteAllText(asmdefPath, @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": []
}");

                ThirdPartyToolMigrationFileService service = new();
                ThirdPartyToolMigrationResult result = service.ApplyMigration(projectRoot);
                string migratedAsmdef = File.ReadAllText(asmdefPath);

                Assert.That(result.FileCount, Is.EqualTo(2));
                Assert.That(result.FilePaths.Contains(asmdefPath), Is.True);
                Assert.That(result.FilePaths.Contains(globalUsingPath), Is.True);
                Assert.That(migratedAsmdef, Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
                Assert.That(migratedAsmdef, Does.Not.Contain("GUID:a0bdbd2c5705643fbb9aef9fac8fd46a"));
                Assert.That(File.ReadAllText(globalUsingPath), Does.Contain(
                    "global using io.github.hatayama.UnityCliLoop.ToolContracts;"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public void ApplyMigration_WhenCurrentFirstPartyToolsAliasOnlyNeedsReference_RewritesFirstPartyNamespace()
        {
            // Verifies that a current FirstPartyTools namespace alias by itself moves to ToolContracts.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string aliasUsingPath = Path.Combine(toolDirectory, "AliasUsing.cs");
                string asmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                File.WriteAllText(aliasUsingPath, "using Fpt = io.github.hatayama.UnityCliLoop.FirstPartyTools;");
                File.WriteAllText(asmdefPath, @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": []
}");

                ThirdPartyToolMigrationFileService service = new();
                ThirdPartyToolMigrationResult result = service.ApplyMigration(projectRoot);
                string migratedAsmdef = File.ReadAllText(asmdefPath);

                Assert.That(result.FileCount, Is.EqualTo(2));
                Assert.That(result.FilePaths.Contains(asmdefPath), Is.True);
                Assert.That(result.FilePaths.Contains(aliasUsingPath), Is.True);
                Assert.That(migratedAsmdef, Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
                Assert.That(migratedAsmdef, Does.Not.Contain("GUID:a0bdbd2c5705643fbb9aef9fac8fd46a"));
                Assert.That(File.ReadAllText(aliasUsingPath), Does.Contain(
                    "using Fpt = io.github.hatayama.UnityCliLoop.ToolContracts;"));
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
                    "io.github.hatayama.UnityCliLoop.ToolContracts.ServiceResult<int> Create"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Not.Contain("GUID:5c4588558a3624eacbce0f50007cf1eb"));
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
                    "global using LegacyToolInfo = io.github.hatayama.UnityCliLoop.ToolContracts.ToolInfo;"));
                Assert.That(File.ReadAllText(metadataPath), Does.Contain(
                    "new io.github.hatayama.UnityCliLoop.ToolContracts.ToolInfo(\"hello\", schema)"));
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
                    "global using LegacyToolInfo = io.github.hatayama.UnityCliLoop.ToolContracts.ToolInfo;"));
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
        public void ApplyMigration_WhenSplitProjectScreenshotDtosExist_KeepsProjectDtoReferences()
        {
            // Verifies that assembly-level DTO declarations protect split custom tool references from first-party rewrites.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string toolPath = Path.Combine(toolDirectory, "CaptureTool.cs");
                string dtoPath = Path.Combine(toolDirectory, "CaptureDtos.cs");
                string asmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                File.WriteAllText(toolPath, @"using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using io.github.hatayama.UnityCliLoop.ToolContracts;

public sealed class CaptureTool : UnityCliLoopTool<ScreenshotSchema, ScreenshotResponse>
{
    public async Task<ScreenshotResponse> ExecuteAsync(ScreenshotSchema parameters, CancellationToken ct)
    {
        Texture2D texture = await EditorWindowCaptureUtility.CaptureWindowAsync(parameters.Window, 1.0f, ct);
        List<UIElementInfo> elements = new();
        return new ScreenshotResponse { Elements = elements };
    }
}");
                File.WriteAllText(dtoPath, @"using System.Collections.Generic;
using UnityEditor;
using io.github.hatayama.UnityCliLoop.ToolContracts;

public sealed class ScreenshotSchema : UnityCliLoopToolSchema
{
    public EditorWindow Window { get; set; }
}

public sealed class ScreenshotResponse : UnityCliLoopToolResponse
{
    public List<UIElementInfo> Elements { get; set; } = new List<UIElementInfo>();
}

public sealed class UIElementInfo
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
                string migratedToolSource = File.ReadAllText(toolPath);

                Assert.That(result.FileCount, Is.EqualTo(1));
                Assert.That(migratedToolSource, Does.Contain("UnityCliLoopTool<ScreenshotSchema, ScreenshotResponse>"));
                Assert.That(migratedToolSource, Does.Contain("Task<ScreenshotResponse> ExecuteAsync"));
                Assert.That(migratedToolSource, Does.Contain("List<UIElementInfo> elements"));
                Assert.That(migratedToolSource, Does.Contain(
                    "io.github.hatayama.UnityCliLoop.ToolContracts.EditorWindowCaptureUtility.CaptureWindowAsync"));
                Assert.That(migratedToolSource, Does.Not.Contain(
                    "io.github.hatayama.UnityCliLoop.ToolContracts.ScreenshotSchema"));
                Assert.That(migratedToolSource, Does.Not.Contain(
                    "io.github.hatayama.UnityCliLoop.ToolContracts.ScreenshotResponse"));
                Assert.That(migratedToolSource, Does.Not.Contain(
                    "io.github.hatayama.UnityCliLoop.ToolContracts.UIElementInfo"));
                Assert.That(File.ReadAllText(dtoPath), Does.Not.Contain(
                    "io.github.hatayama.UnityCliLoop.FirstPartyTools"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Not.Contain("GUID:a0bdbd2c5705643fbb9aef9fac8fd46a"));
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
                    "UnityCliLoopTool<io.github.hatayama.UnityCliLoop.ToolContracts.ScreenshotSchema, io.github.hatayama.UnityCliLoop.ToolContracts.ScreenshotResponse>"));
                Assert.That(migratedSource, Does.Contain(
                    "io.github.hatayama.UnityCliLoop.ToolContracts.EditorWindowCaptureUtility.CaptureWindowAsync"));
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
        public void ApplyMigration_WhenAssemblyUsesGlobalLegacyUsingAndSplitManualRegistration_AddsToolContractsReference()
        {
            // Verifies that manual registration files relying on assembly-level legacy detection get ToolContracts refs.
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
                    "io.github.hatayama.UnityCliLoop.ToolContracts.UnityCliLoopToolRegistrar.RegisterCustomTool"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Not.Contain("GUID:214998e563c124e8a88199b2dd1f522d"));
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
        public async Task HasMigrationTargetsAsync_WhenLegacyGlobalUsingAndBareToolAttributeAreSplit_ReturnsTrue()
        {
            // Verifies that startup detection treats legacy global usings as assembly-scoped.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                File.WriteAllText(
                    Path.Combine(toolDirectory, "GlobalUsings.cs"),
                    "global using io.github.hatayama.uLoopMCP;");
                File.WriteAllText(
                    Path.Combine(toolDirectory, "BareAttributeTool.cs"),
                    @"[McpTool]
public sealed class BareAttributeTool
{
}");
                File.WriteAllText(
                    Path.Combine(toolDirectory, "VendorTools.Editor.asmdef"),
                    @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": []
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
        public async Task HasMigrationTargetsAsync_WhenCurrentFirstPartyToolsGlobalUsingOnlyNeedsReference_ReturnsTrue()
        {
            // Verifies that preview detection reports a missing screenshot reference for current FirstPartyTools global using.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                File.WriteAllText(
                    Path.Combine(toolDirectory, "GlobalUsings.cs"),
                    "global using io.github.hatayama.UnityCliLoop.FirstPartyTools;");
                File.WriteAllText(
                    Path.Combine(toolDirectory, "VendorTools.Editor.asmdef"),
                    @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": []
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
        public async Task HasMigrationTargetsAsync_WhenCurrentFirstPartyToolsAliasOnlyNeedsReference_ReturnsTrue()
        {
            // Verifies that preview detection reports a missing screenshot reference for current FirstPartyTools alias using.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                File.WriteAllText(
                    Path.Combine(toolDirectory, "AliasUsing.cs"),
                    "using Fpt = io.github.hatayama.UnityCliLoop.FirstPartyTools;");
                File.WriteAllText(
                    Path.Combine(toolDirectory, "VendorTools.Editor.asmdef"),
                    @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": []
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
        public async Task HasMigrationTargetsAsync_WhenAssemblyUsesCurrentApplicationGlobalAlias_ReturnsTrue()
        {
            // Verifies that preview detection treats current Application global aliases as assembly-scoped.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                File.WriteAllText(
                    Path.Combine(toolDirectory, "GlobalUsings.cs"),
                    "global using App = io.github.hatayama.UnityCliLoop.Application;");
                File.WriteAllText(
                    Path.Combine(toolDirectory, "MainThreadUsage.cs"),
                    @"using System.Threading;
using System.Threading.Tasks;

public sealed class MainThreadUsage
{
    public async Task RunAsync(CancellationToken ct)
    {
        await App.MainThreadSwitcher.SwitchToMainThread(PlayerLoopTiming.Update, ct);
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
        public async Task HasMigrationTargetsAsync_WhenAssemblyUsesCurrentFirstPartyToolsGlobalAlias_ReturnsTrue()
        {
            // Verifies that preview detection treats current FirstPartyTools global aliases as assembly-scoped.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                File.WriteAllText(
                    Path.Combine(toolDirectory, "GlobalUsings.cs"),
                    "global using Fpt = io.github.hatayama.UnityCliLoop.FirstPartyTools;");
                File.WriteAllText(
                    Path.Combine(toolDirectory, "CurrentScreenshotTool.cs"),
                    @"using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

public sealed class CurrentScreenshotTool
{
    public async Task<Texture2D> CaptureAsync(EditorWindow window, CancellationToken ct)
    {
        return await Fpt.EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, ct);
    }
}");
                File.WriteAllText(
                    Path.Combine(toolDirectory, "VendorTools.Editor.asmdef"),
                    @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": [
        ""GUID:fc3fd32eddbee40e39c2d76dc184957b"",
        ""GUID:a0bdbd2c5705643fbb9aef9fac8fd46a""
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
        public async Task HasMigrationTargetsAsync_WhenAssemblyUsesCurrentToolContractsGlobalUsingAndSplitPartialHelpers_ReturnsTrue()
        {
            // Verifies that startup detection treats current ToolContracts global usings as assembly-scoped.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                File.WriteAllText(
                    Path.Combine(toolDirectory, "GlobalUsings.cs"),
                    "global using io.github.hatayama.UnityCliLoop.ToolContracts;");
                File.WriteAllText(
                    Path.Combine(toolDirectory, "PartialTool.cs"),
                    @"using System.Threading;
using System.Threading.Tasks;

public sealed class PartialTool : UnityCliLoopTool<PartialSchema, PartialResponse>
{
    public async Task RunAsync(CancellationToken ct)
    {
        await EditorDelay.DelayFrame(2, ct);
        await MainThreadSwitcher.SwitchToMainThread(PlayerLoopTiming.Update, ct);
    }
}

public sealed class PartialSchema : UnityCliLoopToolSchema
{
}

public sealed class PartialResponse : UnityCliLoopToolResponse
{
}");
                File.WriteAllText(
                    Path.Combine(toolDirectory, "VendorTools.Editor.asmdef"),
                    @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": [
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
        public async Task HasMigrationTargetsAsync_WhenCurrentToolContractsGlobalUsingOnlyNeedsHelperRewrite_ReturnsTrue()
        {
            // Verifies that startup detection reports source-only helper migrations after asmdef refs are already fixed.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                File.WriteAllText(
                    Path.Combine(toolDirectory, "GlobalUsings.cs"),
                    "global using io.github.hatayama.UnityCliLoop.ToolContracts;");
                File.WriteAllText(
                    Path.Combine(toolDirectory, "PartialTool.cs"),
                    @"using System.Threading;
using System.Threading.Tasks;

public sealed class PartialTool
{
    public async Task RunAsync(CancellationToken ct)
    {
        await MainThreadSwitcher.SwitchToMainThread(PlayerLoopTiming.Update, ct);
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
        public async Task HasMigrationTargetsAsync_WhenAssemblyDeclaresMainThreadSwitcherForCurrentToolContractsFile_ReturnsFalse()
        {
            // Verifies that startup detection uses assembly-scoped type declarations before adding V3 references.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                File.WriteAllText(
                    Path.Combine(toolDirectory, "AUsage.cs"),
                    @"using System.Threading.Tasks;
using io.github.hatayama.UnityCliLoop.ToolContracts;

public sealed class MainThreadUsage
{
    public Task RunAsync()
    {
        return MainThreadSwitcher.SwitchToMainThread(PlayerLoopTiming.Update);
    }
}");
                File.WriteAllText(
                    Path.Combine(toolDirectory, "ZMainThreadSwitcher.cs"),
                    @"using System.Threading.Tasks;

public enum PlayerLoopTiming
{
    Update
}

public static class MainThreadSwitcher
{
    public static Task SwitchToMainThread(PlayerLoopTiming timing)
    {
        return Task.CompletedTask;
    }
}");
                File.WriteAllText(
                    Path.Combine(toolDirectory, "VendorTools.Editor.asmdef"),
                    @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": [
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
        public async Task HasMigrationTargetsAsync_WhenCurrentSplitScreenshotDtosDoNotUseScreenshotApis_ReturnsFalse()
        {
            // Verifies that startup detection protects split local DTOs before screenshot reference checks.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                File.WriteAllText(
                    Path.Combine(toolDirectory, "ATool.cs"),
                    @"using io.github.hatayama.UnityCliLoop.ToolContracts;

public sealed class CurrentScreenshotTool : UnityCliLoopTool<ScreenshotSchema, ScreenshotResponse>
{
}");
                File.WriteAllText(
                    Path.Combine(toolDirectory, "ZDtos.cs"),
                    @"using io.github.hatayama.UnityCliLoop.ToolContracts;

public sealed class ScreenshotSchema : UnityCliLoopToolSchema
{
}

public sealed class ScreenshotResponse : UnityCliLoopToolResponse
{
}");
                File.WriteAllText(
                    Path.Combine(toolDirectory, "VendorTools.Editor.asmdef"),
                    @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": [
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
        public async Task HasMigrationTargetsAsync_WhenCurrentRenderingCaptureNeedsDiscard_ReturnsTrue()
        {
            // Verifies that startup detection reports current rendering capture deconstruction rewrites.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                File.WriteAllText(
                    Path.Combine(toolDirectory, "RenderingCapture.cs"),
                    @"using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

public sealed class RenderingCapture
{
    public async Task CaptureAsync(CancellationToken ct)
    {
        Texture2D texture = null;
        int yOffset = 0;
        (texture, yOffset) = await EditorWindowCaptureUtility.CaptureGameRenderingAsync(
            1.0f,
            UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS,
            ct);
    }
}");
                File.WriteAllText(
                    Path.Combine(toolDirectory, "VendorTools.Editor.asmdef"),
                    @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": [
        ""GUID:fc3fd32eddbee40e39c2d76dc184957b"",
        ""GUID:a0bdbd2c5705643fbb9aef9fac8fd46a""
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
        public async Task HasMigrationTargetsAsync_WhenCurrentToolContractsFileUsesLegacyScreenshotCaptureWithoutAsmdef_ReturnsTrue()
        {
            // Verifies that startup detection reports source-only screenshot signature migrations.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                File.WriteAllText(
                    Path.Combine(toolDirectory, "ScreenshotTool.cs"),
                    @"using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using io.github.hatayama.UnityCliLoop.ToolContracts;

public sealed class ScreenshotTool : UnityCliLoopTool<ScreenshotSchema, ScreenshotResponse>
{
    public async Task<Texture2D> CaptureAsync(EditorWindow window, CancellationToken ct)
    {
        return await EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, ct);
    }
}

public sealed class ScreenshotSchema : UnityCliLoopToolSchema
{
}

public sealed class ScreenshotResponse : UnityCliLoopToolResponse
{
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
        public async Task HasMigrationTargetsAsync_WhenCurrentToolContractsFileUsesLegacyScreenshotCaptureAndReferencesExist_ReturnsTrue()
        {
            // Verifies that startup detection is not hidden by already-correct asmdef references.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                File.WriteAllText(
                    Path.Combine(toolDirectory, "ScreenshotTool.cs"),
                    @"using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using io.github.hatayama.UnityCliLoop.ToolContracts;

public sealed class ScreenshotTool : UnityCliLoopTool<ScreenshotSchema, ScreenshotResponse>
{
    public async Task<Texture2D> CaptureAsync(EditorWindow window, CancellationToken ct)
    {
        return await EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, ct);
    }
}

public sealed class ScreenshotSchema : UnityCliLoopToolSchema
{
}

public sealed class ScreenshotResponse : UnityCliLoopToolResponse
{
}");
                File.WriteAllText(
                    Path.Combine(toolDirectory, "VendorTools.Editor.asmdef"),
                    @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": [
        ""GUID:fc3fd32eddbee40e39c2d76dc184957b"",
        ""GUID:a0bdbd2c5705643fbb9aef9fac8fd46a""
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
        public async Task HasMigrationTargetsAsync_WhenCurrentApplicationGuidReferenceExists_ReturnsTrue()
        {
            // Verifies that current V3 Application namespace usage still triggers source migration.
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

                Assert.That(hasTargets, Is.True);
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
        public async Task ApplyMigrationAsync_WhenProjectChangesAfterPreview_RebuildsPlan()
        {
            // Verifies that cached preview plans are not applied after project files change.
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
                ThirdPartyToolMigrationPreview preview =
                    await service.PreviewMigrationAsync(projectRoot, progress, CancellationToken.None);
                File.WriteAllText(toolPath, "public sealed class HelloTool {}");
                File.WriteAllText(asmdefPath, @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": []
}");

                ThirdPartyToolMigrationResult result =
                    await service.ApplyMigrationAsync(projectRoot, progress, CancellationToken.None);

                Assert.That(preview.HasTargets, Is.True);
                Assert.That(result.FileCount, Is.EqualTo(0));
                Assert.That(File.ReadAllText(toolPath), Is.EqualTo("public sealed class HelloTool {}"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Not.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public async Task ApplyMigrationAsync_WhenProjectChangesDuringPreview_RebuildsPlan()
        {
            // Verifies that cached preview plans are not applied after files change during plan creation.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string toolPath = Path.Combine(toolDirectory, "HelloTool.cs");
                string editedToolSource = "public sealed class EditedTool {}";
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
                FileChangingProgress progress = new(() =>
                {
                    File.WriteAllText(toolPath, editedToolSource);
                    File.SetLastWriteTimeUtc(toolPath, DateTime.UtcNow.AddMinutes(1));
                });
                ThirdPartyToolMigrationPreview preview =
                    await service.PreviewMigrationAsync(projectRoot, progress, CancellationToken.None);

                ThirdPartyToolMigrationResult result =
                    await service.ApplyMigrationAsync(projectRoot, progress, CancellationToken.None);

                Assert.That(preview.HasTargets, Is.True);
                Assert.That(result.FilePaths, Does.Not.Contain(toolPath));
                Assert.That(File.ReadAllText(toolPath), Is.EqualTo(editedToolSource));
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
        public void PreviewMigration_WhenCurrentApplicationGuidReferenceExists_ReportsSourceMigration()
        {
            // Verifies that V3 Application namespace usage is reported as source migration.
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

                Assert.That(preview.HasTargets, Is.True);
                Assert.That(preview.FileCount, Is.EqualTo(1));
                Assert.That(preview.FilePaths.Contains(registrationPath), Is.True);
                Assert.That(preview.FilePaths.Contains(asmdefPath), Is.False);
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

        [Test]
        public async Task ApplyMigrationAsync_WhenProjectContainsManyFiles_ReportsProgressAndWritesChanges()
        {
            // Verifies that setup wizard migration applies file writes after reporting incremental progress.
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

                ThirdPartyToolMigrationResult result =
                    await service.ApplyMigrationAsync(projectRoot, progress, CancellationToken.None);

                Assert.That(result.FileCount, Is.EqualTo(2));
                Assert.That(File.ReadAllText(toolPath), Does.Contain("UnityCliLoopTool<HelloSchema, HelloResponse>"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
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

        [Test]
        public async Task ApplyMigrationAsync_WhenFileScopedLegacyUsingUsesMainThreadSwitcher_AddsToolContractsReference()
        {
            // Verifies that async migration also adds ToolContracts asmdef references for regular legacy usings.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string toolPath = Path.Combine(toolDirectory, "MainThreadTool.cs");
                string asmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                File.WriteAllText(toolPath, @"using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;

public sealed class MainThreadTool
{
    public async Task RunAsync(CancellationToken ct)
    {
        await MainThreadSwitcher.SwitchToMainThread(PlayerLoopTiming.Update, ct);
    }
}");
                File.WriteAllText(asmdefPath, @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": []
}");

                ThirdPartyToolMigrationFileService service = new();
                ThirdPartyToolMigrationResult result =
                    await service.ApplyMigrationAsync(
                        projectRoot,
                        new Progress<ThirdPartyToolMigrationProgress>(),
                        CancellationToken.None);
                string migratedSource = File.ReadAllText(toolPath);
                string migratedAsmdef = File.ReadAllText(asmdefPath);

                Assert.That(result.FilePaths.Contains(toolPath), Is.True);
                Assert.That(result.FilePaths.Contains(asmdefPath), Is.True);
                Assert.That(migratedSource, Does.Contain(
                    "io.github.hatayama.UnityCliLoop.ToolContracts.MainThreadSwitcher.SwitchToMainThread(ct)"));
                Assert.That(migratedAsmdef, Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
                Assert.That(migratedAsmdef, Does.Not.Contain("GUID:214998e563c124e8a88199b2dd1f522d"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public async Task ApplyMigrationAsync_WhenAssemblyUsesCurrentApplicationGlobalAlias_RewritesQualifiedMainThreadSwitcherTiming()
        {
            // Verifies that async migration uses current Application global aliases from sibling files.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string globalUsingPath = Path.Combine(toolDirectory, "GlobalUsings.cs");
                string mainThreadPath = Path.Combine(toolDirectory, "MainThreadUsage.cs");
                string asmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                File.WriteAllText(globalUsingPath, "global using App = io.github.hatayama.UnityCliLoop.Application;");
                File.WriteAllText(mainThreadPath, @"using System.Threading;
using System.Threading.Tasks;

public sealed class MainThreadUsage
{
    public async Task RunAsync(CancellationToken ct)
    {
        await App.MainThreadSwitcher.SwitchToMainThread(PlayerLoopTiming.Update, ct);
    }
}");
                File.WriteAllText(asmdefPath, @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": [
        ""GUID:214998e563c124e8a88199b2dd1f522d""
    ]
}");

                ThirdPartyToolMigrationFileService service = new();
                ThirdPartyToolMigrationResult result =
                    await service.ApplyMigrationAsync(
                        projectRoot,
                        new Progress<ThirdPartyToolMigrationProgress>(),
                        CancellationToken.None);
                string migratedSource = File.ReadAllText(mainThreadPath);

                Assert.That(result.FilePaths.Contains(globalUsingPath), Is.True);
                Assert.That(result.FilePaths.Contains(mainThreadPath), Is.True);
                Assert.That(File.ReadAllText(globalUsingPath), Does.Contain(
                    "global using App = io.github.hatayama.UnityCliLoop.ToolContracts;"));
                Assert.That(migratedSource, Does.Contain(
                    "await App.MainThreadSwitcher.SwitchToMainThread(ct);"));
                Assert.That(migratedSource, Does.Not.Contain("PlayerLoopTiming"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public async Task ApplyMigrationAsync_WhenCurrentFirstPartyToolsGlobalUsingOnlyNeedsReference_RewritesFirstPartyNamespace()
        {
            // Verifies that async migration rewrites current FirstPartyTools global using to ToolContracts.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string globalUsingPath = Path.Combine(toolDirectory, "GlobalUsings.cs");
                string asmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                File.WriteAllText(globalUsingPath, "global using io.github.hatayama.UnityCliLoop.FirstPartyTools;");
                File.WriteAllText(asmdefPath, @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": []
}");

                ThirdPartyToolMigrationFileService service = new();
                ThirdPartyToolMigrationResult result =
                    await service.ApplyMigrationAsync(
                        projectRoot,
                        new Progress<ThirdPartyToolMigrationProgress>(),
                        CancellationToken.None);
                string migratedAsmdef = File.ReadAllText(asmdefPath);

                Assert.That(result.FileCount, Is.EqualTo(2));
                Assert.That(result.FilePaths.Contains(asmdefPath), Is.True);
                Assert.That(result.FilePaths.Contains(globalUsingPath), Is.True);
                Assert.That(migratedAsmdef, Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
                Assert.That(migratedAsmdef, Does.Not.Contain("GUID:a0bdbd2c5705643fbb9aef9fac8fd46a"));
                Assert.That(File.ReadAllText(globalUsingPath), Does.Contain(
                    "global using io.github.hatayama.UnityCliLoop.ToolContracts;"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public async Task ApplyMigrationAsync_WhenAssemblyUsesCurrentFirstPartyToolsGlobalAlias_RewritesQualifiedSplitCapture()
        {
            // Verifies that async migration uses current FirstPartyTools global aliases from sibling files.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string globalUsingPath = Path.Combine(toolDirectory, "GlobalUsings.cs");
                string toolPath = Path.Combine(toolDirectory, "CurrentScreenshotTool.cs");
                string asmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                File.WriteAllText(globalUsingPath, "global using Fpt = io.github.hatayama.UnityCliLoop.FirstPartyTools;");
                File.WriteAllText(toolPath, @"using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

public sealed class CurrentScreenshotTool
{
    public async Task<Texture2D> CaptureAsync(EditorWindow window, CancellationToken ct)
    {
        return await Fpt.EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, ct);
    }
}");
                File.WriteAllText(asmdefPath, @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": [
        ""GUID:fc3fd32eddbee40e39c2d76dc184957b"",
        ""GUID:a0bdbd2c5705643fbb9aef9fac8fd46a""
    ]
}");

                ThirdPartyToolMigrationFileService service = new();
                ThirdPartyToolMigrationResult result =
                    await service.ApplyMigrationAsync(
                        projectRoot,
                        new Progress<ThirdPartyToolMigrationProgress>(),
                        CancellationToken.None);
                string migratedSource = File.ReadAllText(toolPath);

                Assert.That(result.FileCount, Is.EqualTo(2));
                Assert.That(result.FilePaths.Contains(toolPath), Is.True);
                Assert.That(result.FilePaths.Contains(globalUsingPath), Is.True);
                Assert.That(File.ReadAllText(globalUsingPath), Does.Contain(
                    "global using Fpt = io.github.hatayama.UnityCliLoop.ToolContracts;"));
                Assert.That(migratedSource, Does.Contain(
                    "io.github.hatayama.UnityCliLoop.ToolContracts.EditorWindowCaptureUtility.CaptureWindowAsync"));
                Assert.That(migratedSource, Does.Contain(
                    "io.github.hatayama.UnityCliLoop.ToolContracts.UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS"));
                Assert.That(migratedSource, Does.Not.Contain(
                    "return await Fpt.EditorWindowCaptureUtility.CaptureWindowAsync"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public async Task ApplyMigrationAsync_WhenCurrentFirstPartyToolsAliasOnlyNeedsReference_RewritesFirstPartyNamespace()
        {
            // Verifies that async migration rewrites current FirstPartyTools alias using to ToolContracts.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string aliasUsingPath = Path.Combine(toolDirectory, "AliasUsing.cs");
                string asmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                File.WriteAllText(aliasUsingPath, "using Fpt = io.github.hatayama.UnityCliLoop.FirstPartyTools;");
                File.WriteAllText(asmdefPath, @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": []
}");

                ThirdPartyToolMigrationFileService service = new();
                ThirdPartyToolMigrationResult result =
                    await service.ApplyMigrationAsync(
                        projectRoot,
                        new Progress<ThirdPartyToolMigrationProgress>(),
                        CancellationToken.None);
                string migratedAsmdef = File.ReadAllText(asmdefPath);

                Assert.That(result.FileCount, Is.EqualTo(2));
                Assert.That(result.FilePaths.Contains(asmdefPath), Is.True);
                Assert.That(result.FilePaths.Contains(aliasUsingPath), Is.True);
                Assert.That(migratedAsmdef, Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
                Assert.That(migratedAsmdef, Does.Not.Contain("GUID:a0bdbd2c5705643fbb9aef9fac8fd46a"));
                Assert.That(File.ReadAllText(aliasUsingPath), Does.Contain(
                    "using Fpt = io.github.hatayama.UnityCliLoop.ToolContracts;"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public async Task ApplyMigrationAsync_WhenCanceledAfterPlanCompletes_SkipsWritesBeforeApply()
        {
            // Verifies that cancellation before the first file write leaves project files untouched.
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

                using CancellationTokenSource cts = new();
                CancelOnCompleteMigrationProgress progress = new(cts);
                ThirdPartyToolMigrationFileService service = new();

                ThirdPartyToolMigrationResult result =
                    await service.ApplyMigrationAsync(projectRoot, progress, cts.Token);

                Assert.That(result.FileCount, Is.EqualTo(0));
                Assert.That(cts.IsCancellationRequested, Is.True);
                Assert.That(File.ReadAllText(toolPath), Does.Contain("AbstractUnityTool<HelloSchema, HelloResponse>"));
                Assert.That(File.ReadAllText(asmdefPath), Does.Not.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
            }
            finally
            {
                Directory.Delete(projectRoot, recursive: true);
            }
        }

        [Test]
        public async Task ApplyMigrationAsync_WhenCurrentSplitScreenshotDtosDoNotUseScreenshotApis_KeepsAsmdef()
        {
            // Verifies that async migration waits for assembly-level DTO discovery before screenshot reference checks.
            string projectRoot = CreateProjectRoot();
            try
            {
                string toolDirectory = Path.Combine(projectRoot, "Assets", "VendorTools");
                Directory.CreateDirectory(toolDirectory);
                string toolPath = Path.Combine(toolDirectory, "ATool.cs");
                string dtoPath = Path.Combine(toolDirectory, "ZDtos.cs");
                string asmdefPath = Path.Combine(toolDirectory, "VendorTools.Editor.asmdef");
                string asmdefSource = @"{
    ""name"": ""VendorTools.Editor"",
    ""references"": [
        ""GUID:fc3fd32eddbee40e39c2d76dc184957b""
    ]
}";
                File.WriteAllText(toolPath, @"using io.github.hatayama.UnityCliLoop.ToolContracts;

public sealed class CurrentScreenshotTool : UnityCliLoopTool<ScreenshotSchema, ScreenshotResponse>
{
}");
                File.WriteAllText(dtoPath, @"using io.github.hatayama.UnityCliLoop.ToolContracts;

public sealed class ScreenshotSchema : UnityCliLoopToolSchema
{
}

public sealed class ScreenshotResponse : UnityCliLoopToolResponse
{
}");
                File.WriteAllText(asmdefPath, asmdefSource);

                ThirdPartyToolMigrationFileService service = new();
                Progress<ThirdPartyToolMigrationProgress> progress = new();

                ThirdPartyToolMigrationResult result =
                    await service.ApplyMigrationAsync(projectRoot, progress, CancellationToken.None);

                Assert.That(result.FileCount, Is.EqualTo(0));
                Assert.That(File.ReadAllText(asmdefPath), Is.EqualTo(asmdefSource));
                Assert.That(File.ReadAllText(toolPath), Does.Contain("UnityCliLoopTool<ScreenshotSchema, ScreenshotResponse>"));
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

        private sealed class CancelOnCompleteMigrationProgress : IProgress<ThirdPartyToolMigrationProgress>
        {
            private readonly CancellationTokenSource _cts;

            public CancelOnCompleteMigrationProgress(CancellationTokenSource cts)
            {
                Assert.That(cts, Is.Not.Null);

                _cts = cts;
            }

            public void Report(ThirdPartyToolMigrationProgress value)
            {
                if (value.TotalItemCount <= 0 || value.ProcessedItemCount < value.TotalItemCount)
                {
                    return;
                }

                _cts.Cancel();
            }
        }

        private sealed class FileChangingProgress : IProgress<ThirdPartyToolMigrationProgress>
        {
            private readonly Action _changeProject;
            private bool _hasChangedProject;

            public FileChangingProgress(Action changeProject)
            {
                Assert.That(changeProject, Is.Not.Null);

                _changeProject = changeProject;
            }

            public void Report(ThirdPartyToolMigrationProgress value)
            {
                if (_hasChangedProject ||
                    value.TotalItemCount == 0 ||
                    value.ProcessedItemCount < 1)
                {
                    return;
                }

                _hasChangedProject = true;
                _changeProject();
            }
        }

    }
}
