using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies detection of legacy EditorWindowCaptureUtility calls and of legacy or current first-party screenshot type references.
    /// </summary>
    public sealed class ThirdPartyToolMigrationScreenshotDetectionRulesTests
    {
        private const string LegacyCaptureUtility = "io.github.hatayama.uLoopMCP.EditorWindowCaptureUtility";

        /// <summary>
        /// Verifies that a non-awaited legacy CaptureWindowAsync call is detected.
        /// </summary>
        [Test]
        public void ContainsLegacyEditorWindowCaptureUtilityCall_WhenCaptureWindowIsNotAwaited_ReturnsTrue()
        {
            string source = "Task capture = " + LegacyCaptureUtility + ".CaptureWindowAsync(window, 1.0f, ct);\n";

            bool result = ContainsCall(source);

            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Verifies that a legacy CaptureWindowAsync call that only appears inside a comment is not detected.
        /// </summary>
        [Test]
        public void ContainsLegacyEditorWindowCaptureUtilityCall_WhenCallIsCommentedOut_ReturnsFalse()
        {
            string source = "// return await " + LegacyCaptureUtility + ".CaptureWindowAsync(window, 1.0f, ct);\n";

            bool result = ContainsCall(source);

            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Verifies that a non-awaited legacy CaptureWindowAsync call with a migratable argument list is reported as needing migration.
        /// </summary>
        [Test]
        public void ContainsLegacyEditorWindowCaptureUtilityMigration_WhenCaptureWindowIsNotAwaited_ReturnsTrue()
        {
            string source = "Task capture = " + LegacyCaptureUtility + ".CaptureWindowAsync(window, 1.0f, ct);\n";

            bool result = ContainsMigration(source);

            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Verifies that a CaptureWindowAsync call qualified with an alias that is not a known legacy or current alias is not reported as needing migration.
        /// </summary>
        [Test]
        public void ContainsLegacyEditorWindowCaptureUtilityMigration_WhenAliasIsUnknown_ReturnsFalse()
        {
            string source = "return await Other.EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, ct);\n";

            bool result = ContainsMigration(source);

            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Verifies that a CaptureWindowAsync call qualified with a known legacy namespace alias is reported as needing migration.
        /// </summary>
        [Test]
        public void ContainsLegacyEditorWindowCaptureUtilityMigration_WhenAliasIsKnownLegacyAlias_ReturnsTrue()
        {
            string source = "return await Legacy.EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, ct);\n";

            bool result = ThirdPartyToolMigrationScreenshotDetectionRules.ContainsLegacyEditorWindowCaptureUtilityMigration(
                source,
                new[] { "Legacy" },
                Array.Empty<string>(),
                canMigrateBareLegacyEditorWindowCaptureUtility: false,
                assemblyDeclaredTypeNames: Array.Empty<string>(),
                requiresTimeoutArgumentMigration: true);

            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Verifies that a legacy CaptureWindowAsync call without a closing parenthesis is not reported as needing migration.
        /// </summary>
        [Test]
        public void ContainsLegacyEditorWindowCaptureUtilityMigration_WhenCallIsUnterminated_ReturnsFalse()
        {
            string source = "return await " + LegacyCaptureUtility + ".CaptureWindowAsync(window, 1.0f, ct";

            bool result = ContainsMigration(source);

            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Verifies that a legacy CaptureWindowAsync call whose arguments cannot be migrated is not reported as needing migration.
        /// </summary>
        [Test]
        public void ContainsLegacyEditorWindowCaptureUtilityMigration_WhenArgumentsCannotBeMigrated_ReturnsFalse()
        {
            string source = "return await " + LegacyCaptureUtility + ".CaptureWindowAsync(window, 1.0f);\n";

            bool result = ContainsMigration(source);

            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Verifies that a legacy-namespace-qualified screenshot type is detected without any alias or bare-name permission.
        /// </summary>
        [Test]
        public void ContainsLegacyFirstPartyScreenshotReference_WhenTypeIsLegacyNamespaceQualified_ReturnsTrue()
        {
            string source = "object mode = io.github.hatayama.uLoopMCP.WindowMatchMode.Exact;\n";

            bool result = ThirdPartyToolMigrationScreenshotDetectionRules.ContainsLegacyFirstPartyScreenshotReference(
                source,
                canMigrateBareLegacyFirstPartyScreenshotApi: false,
                aliases: Array.Empty<string>(),
                assemblyDeclaredTypeNames: Array.Empty<string>());

            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Verifies that a screenshot type qualified with a legacy namespace alias is detected.
        /// </summary>
        [Test]
        public void ContainsLegacyFirstPartyScreenshotReference_WhenTypeIsAliasQualified_ReturnsTrue()
        {
            string source = "object mode = Legacy.CaptureMode.GameView;\n";

            bool result = ThirdPartyToolMigrationScreenshotDetectionRules.ContainsLegacyFirstPartyScreenshotReference(
                source,
                canMigrateBareLegacyFirstPartyScreenshotApi: false,
                aliases: new[] { "Legacy" },
                assemblyDeclaredTypeNames: Array.Empty<string>());

            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Verifies that a screenshot type qualified with the current FirstPartyTools namespace is detected as a current reference.
        /// </summary>
        [Test]
        public void ContainsCurrentFirstPartyScreenshotReference_WhenTypeIsCurrentNamespaceQualified_ReturnsTrue()
        {
            string source =
                "string[] names = io.github.hatayama.UnityCliLoop.FirstPartyTools.EditorWindowCaptureUtility.GetOpenWindowNames();\n";

            bool result = ThirdPartyToolMigrationScreenshotDetectionRules.ContainsCurrentFirstPartyScreenshotReference(
                source,
                canUseBareCurrentFirstPartyScreenshotType: false,
                currentFirstPartyToolsNamespaceAliases: Array.Empty<string>(),
                assemblyDeclaredTypeNames: Array.Empty<string>());

            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Verifies that a screenshot contract type qualified with the current FirstPartyTools namespace is detected.
        /// </summary>
        [Test]
        public void ContainsCurrentFirstPartyScreenshotContractReference_WhenTypeIsCurrentNamespaceQualified_ReturnsTrue()
        {
            string source = "object mode = io.github.hatayama.UnityCliLoop.FirstPartyTools.CaptureMode.GameView;\n";

            bool result = ContainsContractReference(source, canUseBareCurrentFirstPartyScreenshotType: false);

            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Verifies that a bare screenshot contract type is detected when bare current FirstPartyTools types are in scope.
        /// </summary>
        [Test]
        public void ContainsCurrentFirstPartyScreenshotContractReference_WhenBareTypeIsInScope_ReturnsTrue()
        {
            string source = "object mode = WindowMatchMode.Exact;\n";

            bool result = ContainsContractReference(source, canUseBareCurrentFirstPartyScreenshotType: true);

            Assert.That(result, Is.True);
        }

        /// <summary>
        /// Verifies that a bare screenshot contract type is not detected when bare current FirstPartyTools types are not in scope.
        /// </summary>
        [Test]
        public void ContainsCurrentFirstPartyScreenshotContractReference_WhenBareTypeIsNotInScope_ReturnsFalse()
        {
            string source = "object mode = WindowMatchMode.Exact;\n";

            bool result = ContainsContractReference(source, canUseBareCurrentFirstPartyScreenshotType: false);

            Assert.That(result, Is.False);
        }

        private static bool ContainsCall(string source)
        {
            return ThirdPartyToolMigrationScreenshotDetectionRules.ContainsLegacyEditorWindowCaptureUtilityCall(
                source,
                Array.Empty<string>(),
                Array.Empty<string>(),
                canMigrateBareLegacyEditorWindowCaptureUtility: false,
                assemblyDeclaredTypeNames: Array.Empty<string>());
        }

        private static bool ContainsMigration(string source)
        {
            return ThirdPartyToolMigrationScreenshotDetectionRules.ContainsLegacyEditorWindowCaptureUtilityMigration(
                source,
                Array.Empty<string>(),
                Array.Empty<string>(),
                canMigrateBareLegacyEditorWindowCaptureUtility: false,
                assemblyDeclaredTypeNames: Array.Empty<string>(),
                requiresTimeoutArgumentMigration: true);
        }

        private static bool ContainsContractReference(string source, bool canUseBareCurrentFirstPartyScreenshotType)
        {
            return ThirdPartyToolMigrationScreenshotDetectionRules.ContainsCurrentFirstPartyScreenshotContractReference(
                source,
                canUseBareCurrentFirstPartyScreenshotType,
                Array.Empty<string>(),
                Array.Empty<string>());
        }
    }
}
