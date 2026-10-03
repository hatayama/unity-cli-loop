using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies legacy Domain type name rewrites.
    /// </summary>
    public sealed class ThirdPartyToolMigrationTypeReplacementRulesTests
    {
        /// <summary>
        /// Verifies that a legacy fully qualified Domain type is rewritten to the ToolContracts namespace.
        /// </summary>
        [Test]
        public void ReplaceLegacyDomainTypeNamesInCode_WhenTypeIsLegacyQualified_RewritesToToolContracts()
        {
            const string source = "io.github.hatayama.uLoopMCP.ServiceResult result;";
            int replacementCount = 0;

            string content = ThirdPartyToolMigrationTypeReplacementRules.ReplaceLegacyDomainTypeNamesInCode(
                source,
                Array.Empty<string>(),
                Array.Empty<string>(),
                ref replacementCount);

            Assert.That(content, Is.EqualTo("io.github.hatayama.UnityCliLoop.ToolContracts.ServiceResult result;"));
            Assert.That(replacementCount, Is.EqualTo(1));
        }
    }
}
