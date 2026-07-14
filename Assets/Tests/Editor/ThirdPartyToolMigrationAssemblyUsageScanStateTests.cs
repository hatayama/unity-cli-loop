using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Characterization tests that pin the current scan-state aggregation behavior before Extract Class.
    /// </summary>
    public sealed class ThirdPartyToolMigrationAssemblyUsageScanStateTests
    {
        private const string ProjectRoot = "/Project";
        private const string AssemblyDirectory = "/Project/Assets/VendorTools";
        private const string SourceFilePath = "/Project/Assets/VendorTools/Foo.cs";

        private static ThirdPartyToolMigrationAssemblyUsageScanState CreateScanState()
        {
            return new ThirdPartyToolMigrationAssemblyUsageScanState(
                ProjectRoot,
                new List<string> { AssemblyDirectory },
                new List<AssemblyReferenceDirectory>());
        }

        [Test]
        public void Constructor_WhenCreated_ExposesGivenAsmdefAndAssemblyReferenceDirectories()
        {
            // Verifies that scan-state construction stores caller-provided directory lists without copying.
            List<string> asmdefDirectories = new() { AssemblyDirectory };
            List<AssemblyReferenceDirectory> assemblyReferenceDirectories = new()
            {
                new AssemblyReferenceDirectory("/Project/Assets/VendorTools/Sub", AssemblyDirectory)
            };

            ThirdPartyToolMigrationAssemblyUsageScanState scanState = new(
                ProjectRoot,
                asmdefDirectories,
                assemblyReferenceDirectories);

            Assert.That(scanState.AsmdefDirectories, Is.SameAs(asmdefDirectories));
            Assert.That(scanState.AssemblyReferenceDirectories, Is.SameAs(assemblyReferenceDirectories));
        }

        [Test]
        public void RecordInitialSourceFacts_WhenSourceHasNoMigrationCandidateText_ReturnsFalseAndRecordsNothing()
        {
            // Verifies that unrelated source text leaves every aggregation set untouched.
            ThirdPartyToolMigrationAssemblyUsageScanState scanState = CreateScanState();

            bool recorded = scanState.RecordInitialSourceFacts(
                "public class Foo { }",
                SourceFilePath);

            Assert.That(recorded, Is.False);
            Assert.That(scanState.LegacyAssemblyDirectories, Is.Empty);
            Assert.That(scanState.AssemblyDeclaredTypeNamesByDirectory, Is.Empty);
        }

        [Test]
        public void RecordInitialSourceFacts_WhenSourceContainsLegacyNamespaceUsage_RecordsLegacyDirectoryAndDeclaredTypeNames()
        {
            // Verifies that legacy namespace text marks the owning assembly as legacy and captures declared types.
            ThirdPartyToolMigrationAssemblyUsageScanState scanState = CreateScanState();

            bool recorded = scanState.RecordInitialSourceFacts(
                "using io.github.hatayama.uLoopMCP;\npublic class Foo { }\n",
                SourceFilePath);

            Assert.That(recorded, Is.True);
            Assert.That(scanState.LegacyAssemblyDirectories, Is.EquivalentTo(new[] { AssemblyDirectory }));
            Assert.That(
                scanState.AssemblyDeclaredTypeNamesByDirectory[AssemblyDirectory],
                Is.EquivalentTo(new[] { "Foo" }));
        }

        [Test]
        public void RecordInitialSourceFacts_WhenSourceHasGlobalUsingAliasForLegacyNamespace_RecordsAssemblyScopedAliasAndDirectory()
        {
            // Verifies that a global-using alias for the legacy namespace is captured per assembly directory.
            ThirdPartyToolMigrationAssemblyUsageScanState scanState = CreateScanState();

            scanState.RecordInitialSourceFacts(
                "global using LegacyAlias = io.github.hatayama.uLoopMCP;\n",
                SourceFilePath);

            Assert.That(scanState.AssemblyScopedLegacyDirectories, Is.EquivalentTo(new[] { AssemblyDirectory }));
            Assert.That(
                scanState.AssemblyScopedLegacyAliasesByDirectory[AssemblyDirectory],
                Is.EquivalentTo(new[] { "LegacyAlias" }));
        }

        [Test]
        public void RecordInitialSourceFacts_WhenSourceHasGlobalUsingForCurrentDomainNamespace_RecordsDomainScopeAndReference()
        {
            // Verifies that a global using for the current domain namespace scopes the assembly and adds a reference.
            ThirdPartyToolMigrationAssemblyUsageScanState scanState = CreateScanState();

            scanState.RecordInitialSourceFacts(
                "global using io.github.hatayama.UnityCliLoop.Domain;\n",
                SourceFilePath);

            Assert.That(
                scanState.AssemblyScopedCurrentDomainDirectories,
                Is.EquivalentTo(new[] { AssemblyDirectory }));
            Assert.That(
                scanState.DomainReferenceAssemblyDirectories,
                Is.EquivalentTo(new[] { AssemblyDirectory }));
        }

        [Test]
        public void RecordInitialSourceFacts_WhenSourceHasGlobalUsingForCurrentApplicationNamespace_RecordsApplicationScopeAndReference()
        {
            // Verifies that a global using for the current application namespace scopes the assembly and adds a reference.
            ThirdPartyToolMigrationAssemblyUsageScanState scanState = CreateScanState();

            scanState.RecordInitialSourceFacts(
                "global using io.github.hatayama.UnityCliLoop.Application;\n",
                SourceFilePath);

            Assert.That(
                scanState.AssemblyScopedCurrentApplicationDirectories,
                Is.EquivalentTo(new[] { AssemblyDirectory }));
            Assert.That(
                scanState.ApplicationReferenceAssemblyDirectories,
                Is.EquivalentTo(new[] { AssemblyDirectory }));
        }

        [Test]
        public void RecordInitialSourceFacts_WhenSourceHasGlobalUsingForCurrentFirstPartyToolsNamespace_RecordsFirstPartyScopeAndReference()
        {
            // Verifies that a global using for the first-party tools namespace scopes the assembly and adds a reference.
            ThirdPartyToolMigrationAssemblyUsageScanState scanState = CreateScanState();

            scanState.RecordInitialSourceFacts(
                "global using io.github.hatayama.UnityCliLoop.FirstPartyTools;\n",
                SourceFilePath);

            Assert.That(
                scanState.AssemblyScopedCurrentFirstPartyToolsDirectories,
                Is.EquivalentTo(new[] { AssemblyDirectory }));
            Assert.That(
                scanState.FirstPartyScreenshotReferenceAssemblyDirectories,
                Is.EquivalentTo(new[] { AssemblyDirectory }));
        }

        [Test]
        public void RecordInitialSourceFacts_WhenSourceContainsBareCustomToolManager_ReturnsTrueWithoutToolContractsReference()
        {
            // why: CustomToolManager is a migration-candidate fragment, but bare registrar API recording requires legacy assembly/namespace context
            ThirdPartyToolMigrationAssemblyUsageScanState scanState = CreateScanState();

            bool recorded = scanState.RecordInitialSourceFacts(
                "public void Foo() { CustomToolManager.Register(); }",
                SourceFilePath);

            Assert.That(recorded, Is.True);
            Assert.That(scanState.ToolContractsReferenceAssemblyDirectories, Is.Empty);
        }

        [Test]
        public void RecordTargetScanInitialSourceFacts_WhenSourceContainsLegacyRegistrarApi_DoesNotRecordToolContractsReference()
        {
            // Verifies that the fast target scan skips registrar-API-based tool-contracts requirement recording.
            ThirdPartyToolMigrationAssemblyUsageScanState scanState = CreateScanState();

            scanState.RecordTargetScanInitialSourceFacts(
                "public void Foo() { CustomToolManager.Register(); }",
                SourceFilePath);

            Assert.That(scanState.ToolContractsReferenceAssemblyDirectories, Is.Empty);
        }

        [Test]
        public void RecordInitialSourceFacts_WhenSourceContainsCurrentDomainMetadataApi_RecordsDomainAndToolContractsReferences()
        {
            // Verifies that the full initial scan records both domain and tool-contracts references for domain metadata usage.
            ThirdPartyToolMigrationAssemblyUsageScanState scanState = CreateScanState();

            scanState.RecordInitialSourceFacts(
                "object info = io.github.hatayama.UnityCliLoop.Domain.ToolInfo.Empty;",
                SourceFilePath);

            Assert.That(
                scanState.DomainReferenceAssemblyDirectories,
                Is.EquivalentTo(new[] { AssemblyDirectory }));
            Assert.That(
                scanState.ToolContractsReferenceAssemblyDirectories,
                Is.EquivalentTo(new[] { AssemblyDirectory }));
        }

        [Test]
        public void RecordTargetScanInitialSourceFacts_WhenSourceContainsCurrentDomainMetadataApi_RecordsNeitherDomainNorToolContractsReferences()
        {
            // Verifies that the fast target scan skips domain-metadata-based reference recording entirely.
            ThirdPartyToolMigrationAssemblyUsageScanState scanState = CreateScanState();

            scanState.RecordTargetScanInitialSourceFacts(
                "object info = io.github.hatayama.UnityCliLoop.Domain.ToolInfo.Empty;",
                SourceFilePath);

            Assert.That(scanState.DomainReferenceAssemblyDirectories, Is.Empty);
            Assert.That(scanState.ToolContractsReferenceAssemblyDirectories, Is.Empty);
        }

        [Test]
        public void RecordInitialSourceFacts_WhenSourceContainsCurrentRegistrarApi_CreateUsageBridgesRegistrarIntoApplicationReference()
        {
            // Verifies that CreateUsage folds registrar-owning assemblies into the application reference set.
            ThirdPartyToolMigrationAssemblyUsageScanState scanState = CreateScanState();
            scanState.RecordInitialSourceFacts(
                "public void Foo() { UnityCliLoopToolRegistrar.Register(); }",
                SourceFilePath);

            MigrationAssemblyUsage usage = scanState.CreateUsage();

            Assert.That(
                usage.ApplicationReferenceAssemblyDirectories,
                Is.EquivalentTo(new[] { AssemblyDirectory }));
        }

        [Test]
        public void RecordInitialSourceFacts_WhenSourceContainsCurrentRegistrarApi_CreateReferenceRequirementUsageDoesNotBridgeRegistrar()
        {
            // Verifies that CreateReferenceRequirementUsage does not fold registrar-owning assemblies into the application set.
            ThirdPartyToolMigrationAssemblyUsageScanState scanState = CreateScanState();
            scanState.RecordInitialSourceFacts(
                "public void Foo() { UnityCliLoopToolRegistrar.Register(); }",
                SourceFilePath);

            MigrationAssemblyUsage usage = scanState.CreateReferenceRequirementUsage();

            Assert.That(usage.ApplicationReferenceAssemblyDirectories, Is.Empty);
        }

        [Test]
        public void RecordReferenceRequirements_WhenSourceHasNoMigrationCandidateText_RecordsNothing()
        {
            // Verifies that reference-requirement scanning ignores source without any migration candidate text.
            ThirdPartyToolMigrationAssemblyUsageScanState scanState = CreateScanState();

            scanState.RecordReferenceRequirements("public class Foo { }", SourceFilePath);

            Assert.That(scanState.ToolContractsReferenceAssemblyDirectories, Is.Empty);
            Assert.That(scanState.HasReferenceRequirements, Is.False);
        }

        [Test]
        public void RecordReferenceRequirements_WhenSourceContainsCurrentRegistrarDomainReturnApi_RecordsToolContractsReference()
        {
            // Verifies that a current registrar domain-return call is recorded as a tool-contracts requirement.
            ThirdPartyToolMigrationAssemblyUsageScanState scanState = CreateScanState();

            scanState.RecordReferenceRequirements(
                "var tools = UnityCliLoopToolRegistrar.GetRegisteredCustomTools();",
                SourceFilePath);

            Assert.That(
                scanState.ToolContractsReferenceAssemblyDirectories,
                Is.EquivalentTo(new[] { AssemblyDirectory }));
            Assert.That(scanState.HasReferenceRequirements, Is.True);
        }

        [Test]
        public void CreateUsage_WhenNothingRecorded_ReturnsEmptyUsageWithGivenDirectories()
        {
            // Verifies that CreateUsage on a fresh scan state yields an otherwise-empty usage snapshot.
            ThirdPartyToolMigrationAssemblyUsageScanState scanState = CreateScanState();

            MigrationAssemblyUsage usage = scanState.CreateUsage();

            Assert.That(usage.AsmdefDirectories, Is.EquivalentTo(new[] { AssemblyDirectory }));
            Assert.That(usage.LegacyAssemblyDirectories, Is.Empty);
            Assert.That(usage.ToolContractsReferenceAssemblyDirectories, Is.Empty);
        }
    }
}
