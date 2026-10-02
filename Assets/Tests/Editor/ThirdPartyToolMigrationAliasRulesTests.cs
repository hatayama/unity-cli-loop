using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies namespace alias discovery used by the migration rules.
    /// </summary>
    public sealed class ThirdPartyToolMigrationAliasRulesTests
    {
        /// <summary>
        /// Verifies that a legacy namespace alias declared inside a comment is ignored while a real alias directive is returned.
        /// </summary>
        [Test]
        public void GetLegacyNamespaceAliases_WhenOneAliasDirectiveIsInComment_ReturnsOnlyCodeAlias()
        {
            const string source =
                "// using Commented = io.github.hatayama.uLoopMCP;\nusing Legacy = io.github.hatayama.uLoopMCP;";

            string[] aliases = ThirdPartyToolMigrationAliasRules.GetLegacyNamespaceAliases(source);

            Assert.That(aliases, Is.EqualTo(new[] { "Legacy" }));
        }
    }
}
