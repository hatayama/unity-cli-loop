using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies single-file current Application API detection.
    /// </summary>
    public sealed class ThirdPartyToolMigrationApiDetectionRulesTests
    {
        /// <summary>
        /// Verifies that a bare Application type is detected when the file imports the current Application namespace.
        /// </summary>
        [Test]
        public void ContainsCurrentApplicationApi_WhenBareTypeFollowsApplicationUsing_ReturnsTrue()
        {
            const string source =
                "using io.github.hatayama.UnityCliLoop.Application;\nclass C { MainThreadSwitcher switcher; }";

            bool contains = ThirdPartyToolMigrationApiDetectionRules.ContainsCurrentApplicationApi(source);

            Assert.That(contains, Is.True);
        }

        /// <summary>
        /// Verifies that a bare Application type name is not detected when the file never references the current Application namespace.
        /// </summary>
        [Test]
        public void ContainsCurrentApplicationApi_WhenBareTypeHasNoApplicationNamespace_ReturnsFalse()
        {
            const string source = "class C { MainThreadSwitcher switcher; }";

            bool contains = ThirdPartyToolMigrationApiDetectionRules.ContainsCurrentApplicationApi(source);

            Assert.That(contains, Is.False);
        }
    }
}
