using System.Collections.Generic;
using System.Linq;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies the bookkeeping helpers of the cross-file PlayerLoopTiming migration planner.
    /// </summary>
    public sealed class ThirdPartyToolMigrationCrossFileTimingMigrationPlannerTests
    {
        private const string AssemblyDirectoryKey = "<PROJECT_ROOT>/Assets/VendorTools";

        /// <summary>
        /// Verifies that assemblies whose removed-signature lists are all empty report no pending signatures.
        /// </summary>
        [Test]
        public void HasRemovedPlayerLoopTimingSignatures_WhenEveryListIsEmpty_ReturnsFalse()
        {
            Dictionary<string, List<RemovedLegacyPlayerLoopTimingSignature>> signaturesByDirectory =
                new Dictionary<string, List<RemovedLegacyPlayerLoopTimingSignature>>
                {
                    { AssemblyDirectoryKey, new List<RemovedLegacyPlayerLoopTimingSignature>() }
                };

            bool hasSignatures =
                ThirdPartyToolMigrationCrossFileTimingMigrationPlanner.HasRemovedPlayerLoopTimingSignatures(
                    signaturesByDirectory);

            Assert.That(hasSignatures, Is.False);
        }

        /// <summary>
        /// Verifies that a file without a pending change falls back to the provided on-disk content.
        /// </summary>
        [Test]
        public void GetPendingMigrationFileContent_WhenFileHasNoPendingChange_ReturnsFallbackContent()
        {
            List<MigrationFileChange> changes = new List<MigrationFileChange>
            {
                new MigrationFileChange("<PROJECT_ROOT>/Assets/VendorTools/Other.cs", "pending")
            };

            string content = ThirdPartyToolMigrationCrossFileTimingMigrationPlanner.GetPendingMigrationFileContent(
                "<PROJECT_ROOT>/Assets/VendorTools/Tool.cs",
                changes,
                "on-disk");

            Assert.That(content, Is.EqualTo("on-disk"));
        }

        /// <summary>
        /// Verifies that the names recorded for an assembly directory are returned for that directory.
        /// </summary>
        [Test]
        public void GetAssemblyScopedNameArray_WhenDirectoryHasNames_ReturnsThem()
        {
            Dictionary<string, string[]> namesByDirectory = new Dictionary<string, string[]>
            {
                { AssemblyDirectoryKey, new[] { "LegacyAlias" } }
            };

            string[] names = ThirdPartyToolMigrationCrossFileTimingMigrationPlanner.GetAssemblyScopedNameArray(
                namesByDirectory,
                AssemblyDirectoryKey);

            Assert.That(names, Is.EqualTo(new[] { "LegacyAlias" }));
        }

        /// <summary>
        /// Verifies that upserting a change for a new file appends it after the existing changes.
        /// </summary>
        [Test]
        public void UpsertMigrationFileChange_WhenFileHasNoChange_AppendsNewChange()
        {
            List<MigrationFileChange> changes = new List<MigrationFileChange>
            {
                new MigrationFileChange("<PROJECT_ROOT>/Assets/VendorTools/Other.cs", "other")
            };

            ThirdPartyToolMigrationCrossFileTimingMigrationPlanner.UpsertMigrationFileChange(
                changes,
                "<PROJECT_ROOT>/Assets/VendorTools/Tool.cs",
                "migrated");

            Assert.That(
                changes.Select(change => change.FilePath),
                Is.EqualTo(new[] { "<PROJECT_ROOT>/Assets/VendorTools/Other.cs", "<PROJECT_ROOT>/Assets/VendorTools/Tool.cs" }));
            Assert.That(changes[1].Content, Is.EqualTo("migrated"));
        }
    }
}
