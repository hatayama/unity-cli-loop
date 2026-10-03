using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies asmdef reference migration deduplication.
    /// </summary>
    public sealed class ThirdPartyToolMigrationAsmdefReferenceRulesTests
    {
        /// <summary>
        /// Verifies that an existing ToolContracts name reference is dropped when the migrated legacy reference already adds the same assembly by GUID.
        /// </summary>
        [Test]
        public void MigrateAsmdefReferences_WhenMigratedLegacyReferenceDuplicatesExistingReference_KeepsSingleReference()
        {
            string[] references = { "uLoopMCP.Editor", "UnityCLILoop.ToolContracts" };

            ThirdPartyToolMigrationAsmdefReferenceMigrationResult result =
                ThirdPartyToolMigrationAsmdefReferenceRules.MigrateAsmdefReferences(
                    references,
                    true,
                    false,
                    false,
                    false,
                    false);

            Assert.That(result.References, Is.EqualTo(new[] { "GUID:fc3fd32eddbee40e39c2d76dc184957b" }));
            Assert.That(result.ReplacementCount, Is.EqualTo(1));
        }
    }
}
