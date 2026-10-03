using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies asmdef migration leaves asmdefs with an unexpected references shape untouched.
    /// </summary>
    public sealed class ThirdPartyToolMigrationAsmdefRulesTests
    {
        /// <summary>
        /// Verifies that an asmdef whose references value is not an array is returned unchanged with no replacements.
        /// </summary>
        [Test]
        public void MigrateAsmdefSource_WhenReferencesIsNotArray_ReturnsSourceUnchanged()
        {
            string source = "{ \"name\": \"VendorTools.Editor\", \"references\": \"uLoopMCP.Editor\" }";

            ThirdPartyToolMigrationContentResult result = ThirdPartyToolMigrationAsmdefRules.MigrateAsmdefSource(
                source,
                true,
                true,
                false,
                false,
                false);

            Assert.That(result.Content, Is.EqualTo(source));
            Assert.That(result.ReplacementCount, Is.EqualTo(0));
        }
    }
}
