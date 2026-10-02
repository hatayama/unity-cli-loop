using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies legacy registrar and current Domain type detection used to decide migration scope.
    /// </summary>
    public sealed class ThirdPartyToolMigrationDomainDetectionRulesTests
    {
        /// <summary>
        /// Verifies that a fully qualified legacy CustomToolManager reference is detected even when bare migration is disabled.
        /// </summary>
        [Test]
        public void ContainsLegacyRegistrarReference_WhenReferenceIsFullyQualified_ReturnsTrueWithoutBareMigration()
        {
            const string source = "io.github.hatayama.uLoopMCP.CustomToolManager.RegisterCustomTool(tool);";

            bool contains = ThirdPartyToolMigrationDomainDetectionRules.ContainsLegacyRegistrarReference(
                source,
                false,
                Array.Empty<string>());

            Assert.That(contains, Is.True);
        }

        /// <summary>
        /// Verifies that a CustomToolManager reference through a legacy namespace alias is detected even when bare migration is disabled.
        /// </summary>
        [Test]
        public void ContainsLegacyRegistrarReference_WhenReferenceUsesLegacyAlias_ReturnsTrueWithoutBareMigration()
        {
            const string source = "Legacy.CustomToolManager.RegisterCustomTool(tool);";

            bool contains = ThirdPartyToolMigrationDomainDetectionRules.ContainsLegacyRegistrarReference(
                source,
                false,
                new[] { "Legacy" });

            Assert.That(contains, Is.True);
        }

        /// <summary>
        /// Verifies that a bare CustomToolManager mention inside a comment is not detected as a migratable reference.
        /// </summary>
        [Test]
        public void ContainsLegacyRegistrarReference_WhenBareReferenceIsOnlyInComment_ReturnsFalse()
        {
            const string source = "// CustomToolManager.RegisterCustomTool(tool);\nclass C { }";

            bool contains = ThirdPartyToolMigrationDomainDetectionRules.ContainsLegacyRegistrarReference(
                source,
                true,
                Array.Empty<string>());

            Assert.That(contains, Is.False);
        }

        /// <summary>
        /// Verifies that a fully qualified legacy GetRegisteredCustomTools call is detected even when bare migration is disabled.
        /// </summary>
        [Test]
        public void ContainsLegacyRegistrarDomainReturnReference_WhenCallIsFullyQualified_ReturnsTrueWithoutBareMigration()
        {
            const string source = "var tools = io.github.hatayama.uLoopMCP.CustomToolManager.GetRegisteredCustomTools();";

            bool contains = ThirdPartyToolMigrationDomainDetectionRules.ContainsLegacyRegistrarDomainReturnReference(
                source,
                false,
                Array.Empty<string>());

            Assert.That(contains, Is.True);
        }

        /// <summary>
        /// Verifies that a GetRegisteredCustomTools call through a legacy namespace alias is detected even when bare migration is disabled.
        /// </summary>
        [Test]
        public void ContainsLegacyRegistrarDomainReturnReference_WhenCallUsesLegacyAlias_ReturnsTrueWithoutBareMigration()
        {
            const string source = "var tools = Legacy.CustomToolManager.GetRegisteredCustomTools();";

            bool contains = ThirdPartyToolMigrationDomainDetectionRules.ContainsLegacyRegistrarDomainReturnReference(
                source,
                false,
                new[] { "Legacy" });

            Assert.That(contains, Is.True);
        }

        /// <summary>
        /// Verifies that a bare GetRegisteredCustomTools call inside a comment is not detected as a migratable reference.
        /// </summary>
        [Test]
        public void ContainsLegacyRegistrarDomainReturnReference_WhenBareCallIsOnlyInComment_ReturnsFalse()
        {
            const string source = "// var tools = CustomToolManager.GetRegisteredCustomTools();\nclass C { }";

            bool contains = ThirdPartyToolMigrationDomainDetectionRules.ContainsLegacyRegistrarDomainReturnReference(
                source,
                true,
                Array.Empty<string>());

            Assert.That(contains, Is.False);
        }

        /// <summary>
        /// Verifies that a type qualified with the current Domain namespace is detected even when bare current Domain types are not allowed.
        /// </summary>
        [Test]
        public void ContainsCurrentDomainHelperApiForAssembly_WhenTypeIsFullyQualified_ReturnsTrueWithoutBareUsage()
        {
            const string source = "io.github.hatayama.UnityCliLoop.Domain.ServiceResult result;";

            bool contains = ThirdPartyToolMigrationDomainDetectionRules.ContainsCurrentDomainHelperApiForAssembly(
                source,
                false);

            Assert.That(contains, Is.True);
        }

        /// <summary>
        /// Verifies that a bare current Domain type name is detected only when bare current Domain types are allowed.
        /// </summary>
        [Test]
        public void ContainsCurrentDomainHelperApiForAssembly_WhenTypeIsBare_ReturnsWhetherBareUsageIsAllowed()
        {
            const string source = "ServiceResult result;";

            bool containsWithBareUsage =
                ThirdPartyToolMigrationDomainDetectionRules.ContainsCurrentDomainHelperApiForAssembly(source, true);
            bool containsWithoutBareUsage =
                ThirdPartyToolMigrationDomainDetectionRules.ContainsCurrentDomainHelperApiForAssembly(source, false);

            Assert.That(containsWithBareUsage, Is.True);
            Assert.That(containsWithoutBareUsage, Is.False);
        }

        /// <summary>
        /// Verifies that a type name occurring only inside a comment is not reported as a legacy assembly-scoped reference.
        /// </summary>
        [Test]
        public void ContainsLegacyAssemblyScopedTypeName_WhenNameIsOnlyInComment_ReturnsFalse()
        {
            const string source = "// ToolInfo info;\nclass C { }";
            ThirdPartyToolMigrationParsingRules.CodeTextMask codeTextMask =
                ThirdPartyToolMigrationParsingRules.CodeTextMask.CreateUncached(source);

            bool contains = ThirdPartyToolMigrationDomainDetectionRules.ContainsLegacyAssemblyScopedTypeName(
                source,
                codeTextMask,
                "ToolInfo");

            Assert.That(contains, Is.False);
        }
    }
}
