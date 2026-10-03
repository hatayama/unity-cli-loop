using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies alias-qualified tool contract detection and EditorDelay call detection.
    /// </summary>
    public sealed class ThirdPartyToolMigrationToolContractDetectionRulesTests
    {
        /// <summary>
        /// Verifies that a legacy Domain type referenced through a legacy assembly alias is detected.
        /// </summary>
        [Test]
        public void ContainsLegacyAliasQualifiedAssemblyScopedApi_WhenDomainTypeUsesAlias_ReturnsTrue()
        {
            const string source = "Legacy.ServiceResult result;";

            bool contains = ThirdPartyToolMigrationToolContractDetectionRules.ContainsLegacyAliasQualifiedAssemblyScopedApi(
                source,
                new[] { "Legacy" });

            Assert.That(contains, Is.True);
        }

        /// <summary>
        /// Verifies that CustomToolManager referenced through a legacy assembly alias is detected.
        /// </summary>
        [Test]
        public void ContainsLegacyAliasQualifiedAssemblyScopedApi_WhenRegistrarUsesAlias_ReturnsTrue()
        {
            const string source = "Legacy.CustomToolManager.RegisterCustomTool(tool);";

            bool contains = ThirdPartyToolMigrationToolContractDetectionRules.ContainsLegacyAliasQualifiedAssemblyScopedApi(
                source,
                new[] { "Legacy" });

            Assert.That(contains, Is.True);
        }

        /// <summary>
        /// Verifies that a bare EditorDelay.DelayFrame call that appears only inside a comment is not detected.
        /// </summary>
        [Test]
        public void ContainsLegacyEditorDelayFrameCall_WhenCallIsOnlyInComment_ReturnsFalse()
        {
            const string source = "// await EditorDelay.DelayFrame(1);\nclass C { }";

            bool contains = ThirdPartyToolMigrationToolContractDetectionRules.ContainsLegacyEditorDelayFrameCall(
                source,
                Array.Empty<string>(),
                true);

            Assert.That(contains, Is.False);
        }
    }
}
