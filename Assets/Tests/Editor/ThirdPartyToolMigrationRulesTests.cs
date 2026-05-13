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
        public void MigrateCSharpSource_WhenLegacyToolAttributeHasSupportedArguments_PreservesSupportedArguments()
        {
            // Verifies that migration drops removed metadata without changing supported tool visibility metadata.
            string source =
                "[McpTool(Description = \"hello\", DisplayDevelopmentOnly = true)] public sealed class HelloTool {}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("[UnityCliLoopTool(DisplayDevelopmentOnly = true)]"));
            Assert.That(result.Content, Does.Not.Contain("Description"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyToolAttributeHasSecurityArgument_RewritesSecurityArgument()
        {
            // Verifies that supported security metadata keeps compiling after the enum rename.
            string source =
                "[McpTool(RequiredSecuritySetting = SecuritySettings.None)] public sealed class HelloTool {}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "[UnityCliLoopTool(RequiredSecuritySetting = UnityCliLoopSecuritySetting.None)]"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyToolAttributeSharesAttributeList_RewritesOnlyLegacyToolEntry()
        {
            // Verifies that valid C# attribute lists migrate the tool attribute without dropping sibling attributes.
            string source = "[McpTool(Description = \"hello\"), System.Obsolete] public sealed class HelloTool {}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("[UnityCliLoopTool, System.Obsolete]"));
            Assert.That(result.Content, Does.Not.Contain("McpTool"));
            Assert.That(result.Content, Does.Not.Contain("Description"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyToolDescriptionContainsBracket_RewritesAttribute()
        {
            // Verifies that description text cannot terminate the attribute-list scan early.
            string source = "[McpTool(Description = \"Use [foo]\")] public sealed class HelloTool {}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("[UnityCliLoopTool]"));
            Assert.That(result.Content, Does.Not.Contain("McpTool"));
            Assert.That(result.Content, Does.Not.Contain("Description"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyToolAttributeIsQualified_RewritesQualifiedAttribute()
        {
            // Verifies that tools without a namespace import still receive a compilable V3 attribute.
            string source =
                "[io.github.hatayama.uLoopMCP.McpToolAttribute(Description = \"hello\")] public sealed class HelloTool {}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain(
                "[io.github.hatayama.UnityCliLoop.ToolContracts.UnityCliLoopTool]"));
            Assert.That(result.Content, Does.Not.Contain("McpTool"));
            Assert.That(result.Content, Does.Not.Contain("Description"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyPublicHelpersAreUsed_RewritesHelperTypes()
        {
            // Verifies that migrated custom tools keep compiling when they override helper-driven schema behavior.
            string source = @"using io.github.hatayama.uLoopMCP;

public sealed class HelloTool
{
    public ToolParameterSchema ParameterSchema => ToolParameterSchemaGenerator.FromDto<HelloSchema>();

    private void Fail()
    {
        throw new ParameterValidationException(""bad"");
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("UnityCliLoopToolParameterSchemaGenerator"));
            Assert.That(result.Content, Does.Contain("UnityCliLoopToolParameterValidationException"));
            Assert.That(result.Content, Does.Not.Match(@"\bToolParameterSchemaGenerator\b"));
            Assert.That(result.Content, Does.Not.Match(@"\bParameterValidationException\b"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyRegistrarMetadataIsUsed_RewritesDomainMetadataType()
        {
            // Verifies that explicit registrar metadata declarations keep compiling after namespace migration.
            string source = @"using io.github.hatayama.uLoopMCP;

public static class ManualToolRegistration
{
    public static ToolInfo[] GetTools()
    {
        return CustomToolManager.GetRegisteredCustomTools();
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("io.github.hatayama.UnityCliLoop.Domain.ToolInfo[]"));
            Assert.That(result.Content, Does.Contain(
                "io.github.hatayama.UnityCliLoop.Application.UnityCliLoopToolRegistrar.GetRegisteredCustomTools"));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyApiExistsOnlyInComment_KeepsContent()
        {
            // Verifies that migration does not rewrite inert documentation comments inside C# files.
            string source = "// AbstractUnityTool should not be rewritten here";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.False);
            Assert.That(result.Content, Is.EqualTo(source));
        }

        [Test]
        public void MigrateCSharpSource_WhenLegacyApiExistsOnlyInStringLiteral_KeepsContent()
        {
            // Verifies that migration does not rewrite test fixture strings or examples inside C# files.
            string source = "public const string Example = \"IUnityTool\";";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.False);
            Assert.That(result.Content, Is.EqualTo(source));
        }

        [Test]
        public void MigrateCSharpSource_WhenGenericLegacyNameExistsWithoutLegacyMarker_KeepsContent()
        {
            // Verifies that unrelated project types are not migrated just because their names resemble old API names.
            string source = "public sealed class CustomToolManager { public SecuritySettings Settings; }";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Changed, Is.False);
            Assert.That(result.Content, Is.EqualTo(source));
        }

        [Test]
        public void MigrateCSharpSourceForLegacyAssembly_WhenFileReliesOnGlobalUsing_RewritesContractTypes()
        {
            // Verifies that files split away from a legacy global using still migrate inside the same assembly.
            string source = "public sealed class HelloSchema : BaseToolSchema {}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSourceForLegacyAssembly(
                    source,
                    hasLegacyAssemblySource: true);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("UnityCliLoopToolSchema"));
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
                ThirdPartyToolMigrationRules.MigrateAsmdefSource(
                    source,
                    hasLegacyCSharpSource: false,
                    requiresApplicationReference: false);

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
                ThirdPartyToolMigrationRules.MigrateAsmdefSource(
                    source,
                    hasLegacyCSharpSource: false,
                    requiresApplicationReference: false);

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
                ThirdPartyToolMigrationRules.MigrateAsmdefSource(
                    source,
                    hasLegacyCSharpSource: true,
                    requiresApplicationReference: false);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
            Assert.That(result.Content, Does.Not.Contain("GUID:214998e563c124e8a88199b2dd1f522d"));
        }

        [Test]
        public void MigrateAsmdefSource_WhenManualRegistrationIsUsed_KeepsApplicationReference()
        {
            // Verifies that migrated manual registration code can reference the V3 registrar assembly.
            string source = @"{
    ""name"": ""MyCompany.Tools.Editor"",
    ""references"": [
        ""GUID:214998e563c124e8a88199b2dd1f522d""
    ]
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateAsmdefSource(
                    source,
                    hasLegacyCSharpSource: true,
                    requiresApplicationReference: true);

            Assert.That(result.Changed, Is.True);
            Assert.That(result.Content, Does.Contain("GUID:fc3fd32eddbee40e39c2d76dc184957b"));
            Assert.That(result.Content, Does.Contain("GUID:214998e563c124e8a88199b2dd1f522d"));
            Assert.That(result.Content, Does.Contain("GUID:5c4588558a3624eacbce0f50007cf1eb"));
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
        public void ContainsLegacyCSharpApi_WhenLegacyToolApiExistsOnlyInStringLiteral_ReturnsFalse()
        {
            // Verifies that inert fixture text does not trigger project migration UI.
            string source = "public const string Example = \"[McpTool]\";";

            bool containsLegacyApi = ThirdPartyToolMigrationRules.ContainsLegacyCSharpApi(source);

            Assert.That(containsLegacyApi, Is.False);
        }

        [Test]
        public void ContainsLegacyCSharpApi_WhenLegacyToolApiExistsOnlyInComment_ReturnsFalse()
        {
            // Verifies that comments do not trigger project migration UI.
            string source = "// CustomToolManager";

            bool containsLegacyApi = ThirdPartyToolMigrationRules.ContainsLegacyCSharpApi(source);

            Assert.That(containsLegacyApi, Is.False);
        }

        [Test]
        public void ContainsLegacyCSharpApi_WhenGenericLegacyNameExistsWithoutLegacyMarker_ReturnsFalse()
        {
            // Verifies that unrelated source names do not trigger project migration UI.
            string source = "public sealed class CustomToolManager {}";

            bool containsLegacyApi = ThirdPartyToolMigrationRules.ContainsLegacyCSharpApi(source);

            Assert.That(containsLegacyApi, Is.False);
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
