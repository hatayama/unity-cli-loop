using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies compile-error-message token matching for V2 legacy API detection.
    /// </summary>
    public sealed class ThirdPartyToolMigrationDetectionRulesTests
    {
        [Test]
        public void ContainsLegacyApiToken_WhenMessageContainsLegacyNamespaceSegment_ReturnsTrue()
        {
            // Verifies that a CS0234 message shaped like Roslyn's actual segment-only report is detected.
            const string message =
                "Assets/Editor/MyTool.cs(3,25): error CS0234: The type or namespace name 'uLoopMCP' " +
                "does not exist in the namespace 'io.github.hatayama' (are you missing an assembly reference?)";

            Assert.That(ThirdPartyToolMigrationDetectionRules.ContainsLegacyApiToken(message), Is.True);
        }

        [Test]
        public void ContainsLegacyApiToken_WhenMessageContainsLegacyAssemblyName_ReturnsTrue()
        {
            // Verifies that asmdef-reference-shaped errors referencing the legacy assembly name are detected.
            const string message = "error: Assembly 'uLoopMCP.Editor' could not be resolved.";

            Assert.That(ThirdPartyToolMigrationDetectionRules.ContainsLegacyApiToken(message), Is.True);
        }

        [Test]
        public void ContainsLegacyApiToken_WhenMessageContainsRenamedLegacyTypeName_ReturnsTrue()
        {
            // Verifies that CS0246-shaped errors for a renamed legacy base type are detected without depending on the error code.
            const string message = "Assets/Editor/MyTool.cs(5,14): error CS0246: The type or namespace name " +
                "'AbstractUnityTool' could not be found (are you missing a using directive or an assembly reference?)";

            Assert.That(ThirdPartyToolMigrationDetectionRules.ContainsLegacyApiToken(message), Is.True);
        }

        [Test]
        public void ContainsLegacyApiToken_WhenMessageContainsAttributeSuffixStrippedForm_ReturnsTrue()
        {
            // Verifies that Roslyn's attribute-usage error, which reports 'McpTool' without the 'Attribute' suffix, is still detected.
            const string message = "Assets/Editor/MyTool.cs(2,6): error CS0246: The type or namespace name " +
                "'McpTool' could not be found (are you missing a using directive or an assembly reference?)";

            Assert.That(ThirdPartyToolMigrationDetectionRules.ContainsLegacyApiToken(message), Is.True);
        }

        [Test]
        public void ContainsLegacyApiToken_WhenTokenIsSubstringOfUnrelatedIdentifier_ReturnsFalse()
        {
            // Verifies identifier-boundary matching: 'IUnityTool' must not match inside 'IUnityToolbarButton'.
            const string message = "Assets/Editor/MyTool.cs(9,10): error CS0246: The type or namespace name " +
                "'IUnityToolbarButton' could not be found (are you missing a using directive or an assembly reference?)";

            Assert.That(ThirdPartyToolMigrationDetectionRules.ContainsLegacyApiToken(message), Is.False);
        }

        [Test]
        public void ContainsLegacyApiToken_WhenMessageIsUnrelatedV3CompileError_ReturnsFalse()
        {
            // Verifies that ordinary V3 compile errors unrelated to legacy migration do not match.
            const string message = "Assets/Editor/MyTool.cs(1,1): error CS0103: The name 'undefinedSymbol' " +
                "does not exist in the current context";

            Assert.That(ThirdPartyToolMigrationDetectionRules.ContainsLegacyApiToken(message), Is.False);
        }

        [Test]
        public void ContainsLegacyApiToken_WhenMessageReferencesTypeSharedByLegacyAndCurrentNames_ReturnsFalse()
        {
            // Verifies that names unchanged between V2 and V3 (e.g. ServiceResult) are excluded from the token set,
            // since matching them would false-positive on unrelated V3-only compile errors.
            const string message = "Assets/Editor/MyTool.cs(4,9): error CS0246: The type or namespace name " +
                "'ServiceResult' could not be found (are you missing a using directive or an assembly reference?)";

            Assert.That(ThirdPartyToolMigrationDetectionRules.ContainsLegacyApiToken(message), Is.False);
        }
    }
}
