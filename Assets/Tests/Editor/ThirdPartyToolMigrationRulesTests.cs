using System.Linq;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies V3 third-party tool migration rewrite rules.
    /// </summary>
    public sealed class ThirdPartyToolMigrationRulesTests
    {
        [Test]
        public void MigrateCSharpSource_WhenLegacyToolApiIsUsed_RewritesToV3Contracts()
        {
            // Verifies that V2 custom tool source is rewritten to the V3 public contract names.
            string source = @"using io.github.hatayama.uLoopMCP;

namespace Samples
{
    [McpTool(Description = ""hello"")]
    public sealed class HelloTool : AbstractUnityTool<HelloSchema, HelloResponse>
    {
    }

    public sealed class HelloSchema : BaseToolSchema
    {
    }

    public sealed class HelloResponse : BaseToolResponse
    {
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("using io.github.hatayama.UnityCliLoop.ToolContracts;"));
            Assert.That(result.Content, Does.Contain("[UnityCliLoopTool]"));
            Assert.That(result.Content, Does.Contain("UnityCliLoopTool<HelloSchema, HelloResponse>"));
            Assert.That(result.Content, Does.Contain("UnityCliLoopToolSchema"));
            Assert.That(result.Content, Does.Contain("UnityCliLoopToolResponse"));
            Assert.That(result.Content, Does.Not.Contain("uLoopMCP"));
            Assert.That(result.ReplacementCount, Is.EqualTo(5));
        }

        [Test]
        public void MigrateCSharpSource_WhenNoLegacyToolApiIsUsed_KeepsContent()
        {
            // Verifies that unrelated C# files are not rewritten.
            string source = "public sealed class PlainClass {}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.False);
            Assert.That(result.Content, Is.EqualTo(source));
            Assert.That(result.ReplacementCount, Is.EqualTo(0));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyToolAttributeSuffixHasArguments_DropsUnsupportedArguments()
        {
            // Verifies that old attribute suffix syntax does not keep removed V3 attribute arguments.
            string source = "[McpToolAttribute(Description = \"hello\")] public sealed class HelloTool {}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("[UnityCliLoopTool]"));
            Assert.That(result.Content, Does.Not.Contain("Description"));
            Assert.That(result.Content, Does.Not.Contain("UnityCliLoopToolAttribute("));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyNamespaceLikeTextExists_KeepsContent()
        {
            // Verifies that namespace migration treats dots as literal characters.
            string source = "using ioXgithubYhatayamaZUnityCliLoop;";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.False);
            Assert.That(result.Content, Is.EqualTo(source));
        }

        [Test]
        public void MigrateAsmdefSource_WhenLegacyReferenceIsUsed_RewritesToToolContractsGuid()
        {
            // Verifies that a custom tool asmdef points at the V3 ToolContracts assembly.
            string source = @"{
    ""name"": ""MyCompany.Tools.Editor"",
    ""references"": [
        ""uLoopMCP.Editor""
    ]
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateAsmdefSource(source, hasLegacyCSharpSource: false);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
            Assert.That(result.Content, Does.Not.Contain("uLoopMCP.Editor"));
            Assert.That(result.ReplacementCount, Is.EqualTo(1));
        }

        [Test]
        public void MigrateAsmdefSource_WhenOnlyLegacyGuidIsUsedWithoutLegacySource_KeepsContent()
        {
            // Verifies that package-owned Application references are not rewritten by GUID alone.
            string source = @"{
    ""name"": ""MyCompany.Tools.Editor"",
    ""references"": [
        ""GUID:214998e563c124e8a88199b2dd1f522d""
    ]
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateAsmdefSource(source, hasLegacyCSharpSource: false);

            Assert.That(result.Changed, Is.False);
            Assert.That(result.Content, Is.EqualTo(source));
        }

        [Test]
        public void MigrateAsmdefSource_WhenLegacyGuidIsUsedWithLegacySource_RewritesToToolContractsGuid()
        {
            // Verifies that old custom tool assemblies with GUID references are migrated when source confirms old API usage.
            string source = @"{
    ""name"": ""MyCompany.Tools.Editor"",
    ""references"": [
        ""GUID:214998e563c124e8a88199b2dd1f522d""
    ]
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateAsmdefSource(source, hasLegacyCSharpSource: true);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
            Assert.That(result.Content, Does.Not.Contain("GUID:214998e563c124e8a88199b2dd1f522d"));
        }

        [Test]
        public void ContainsLegacyCSharpApi_WhenLegacyToolApiExists_ReturnsTrue()
        {
            // Verifies that migration detection is based on public custom tool API usage.
            string source = "[McpTool] public sealed class HelloTool : AbstractUnityTool<HelloSchema, HelloResponse> {}";

            bool containsLegacyApi = ThirdPartyToolMigrationRules.ContainsLegacyCSharpApi(source);

            Assert.That(containsLegacyApi, Is.True);
        }

        [Test]
        public void ContainsLegacyCSharpApi_WhenOnlyCurrentApiExists_ReturnsFalse()
        {
            // Verifies that already migrated V3 tools are not detected again.
            string source = "[UnityCliLoopTool] public sealed class HelloTool : UnityCliLoopTool<HelloSchema, HelloResponse> {}";

            bool containsLegacyApi = ThirdPartyToolMigrationRules.ContainsLegacyCSharpApi(source);

            Assert.That(containsLegacyApi, Is.False);
        }

        [Test]
        public void GetExcludedDirectoryNames_IncludesUnityGeneratedDirectories()
        {
            // Verifies that generated Unity folders are skipped during project-wide scans.
            string[] names = ThirdPartyToolMigrationRules.GetExcludedDirectoryNames();

            Assert.That(names.Contains("Library"), Is.True);
            Assert.That(names.Contains("Temp"), Is.True);
            Assert.That(names.Contains(".git"), Is.True);
        }
    }
}
