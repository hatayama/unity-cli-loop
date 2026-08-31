using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies local type shadowing is not rewritten by contract/domain/toolinfo renames.
    /// </summary>
    public sealed class ThirdPartyToolMigrationLocalTypeShadowingTests
    {
        [Test]
        public void MigrateCSharpSource_WhenProjectDefinesSecuritySettings_KeepsProjectTypeUsages()
        {
            // Verifies a project-owned SecuritySettings type is not rewritten to the V3 security setting type.
            string source = @"using io.github.hatayama.uLoopMCP;

public sealed class SecuritySettings
{
    public bool Enabled { get; set; }
}

public sealed class SettingsFactory
{
    public SecuritySettings Create()
    {
        return new SecuritySettings();
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Content, Does.Contain("public sealed class SecuritySettings"));
            Assert.That(result.Content, Does.Contain("public SecuritySettings Create()"));
            Assert.That(result.Content, Does.Contain("return new SecuritySettings();"));
            Assert.That(result.Content, Does.Not.Contain("UnityCliLoopSecuritySetting"));
        }

        [Test]
        public void MigrateCSharpSource_WhenProjectDefinesServiceResult_KeepsProjectTypeUsages()
        {
            // Verifies a project-owned ServiceResult type is not rewritten to the ToolContracts type.
            string source = @"using io.github.hatayama.uLoopMCP;

public sealed class ServiceResult
{
    public bool Ok { get; set; }
}

public sealed class ResultFactory
{
    public ServiceResult Create()
    {
        return new ServiceResult();
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Content, Does.Contain("public sealed class ServiceResult"));
            Assert.That(result.Content, Does.Contain("public ServiceResult Create()"));
            Assert.That(result.Content, Does.Contain("return new ServiceResult();"));
            Assert.That(
                result.Content,
                Does.Not.Contain("io.github.hatayama.UnityCliLoop.ToolContracts.ServiceResult"));
        }

        [Test]
        public void MigrateCSharpSource_WhenProjectDefinesToolInfo_KeepsProjectTypeUsages()
        {
            // Verifies a project-owned ToolInfo type is not rewritten to the ToolContracts type.
            string source = @"using io.github.hatayama.uLoopMCP;

public sealed class ToolInfo
{
    public string Name { get; set; }
}

public sealed class ToolInfoFactory
{
    public ToolInfo Create()
    {
        return new ToolInfo();
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSource(source);

            Assert.That(result.Content, Does.Contain("public sealed class ToolInfo"));
            Assert.That(result.Content, Does.Contain("public ToolInfo Create()"));
            Assert.That(result.Content, Does.Contain("return new ToolInfo();"));
            Assert.That(
                result.Content,
                Does.Not.Contain("io.github.hatayama.UnityCliLoop.ToolContracts.ToolInfo"));
        }

        [Test]
        public void MigrateCSharpSourceForLegacyAssembly_WhenAssemblyDeclaresSecuritySettings_KeepsProjectTypeUsages()
        {
            // Verifies sibling-file ownership of SecuritySettings also blocks bare reference rewrites.
            string source = @"using io.github.hatayama.uLoopMCP;

public sealed class SettingsFactory
{
    public SecuritySettings Create()
    {
        return new SecuritySettings();
    }
}";

            ThirdPartyToolMigrationContentResult result =
                ThirdPartyToolMigrationRules.MigrateCSharpSourceForLegacyAssembly(
                    source,
                    hasLegacyAssemblySource: true,
                    hasAssemblyScopedCurrentToolContractsUsing: false,
                    hasAssemblyScopedCurrentApplicationUsing: false,
                    hasAssemblyScopedCurrentDomainUsing: false,
                    hasAssemblyScopedCurrentFirstPartyToolsUsing: false,
                    legacyAssemblyAliases: System.Array.Empty<string>(),
                    legacyAssemblyToolInfoAliases: System.Array.Empty<string>(),
                    currentApplicationAssemblyAliases: System.Array.Empty<string>(),
                    currentDomainAssemblyAliases: System.Array.Empty<string>(),
                    currentFirstPartyToolsAssemblyAliases: System.Array.Empty<string>(),
                    assemblyDeclaredTypeNames: new[] { "SecuritySettings" });

            Assert.That(result.Content, Does.Contain("public SecuritySettings Create()"));
            Assert.That(result.Content, Does.Contain("return new SecuritySettings();"));
            Assert.That(result.Content, Does.Not.Contain("UnityCliLoopSecuritySetting"));
        }
    }
}
