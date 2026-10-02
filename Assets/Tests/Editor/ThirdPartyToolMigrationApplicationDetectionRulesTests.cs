using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies legacy and current Application API detection used to decide migration scope.
    /// </summary>
    public sealed class ThirdPartyToolMigrationApplicationDetectionRulesTests
    {
        /// <summary>
        /// Verifies that a bare TimerDelay call in code is detected when bare migration is allowed.
        /// </summary>
        [Test]
        public void ContainsLegacyTimerDelayInvocation_WhenBareCallIsInCode_ReturnsTrue()
        {
            const string source = "await TimerDelay.Wait(1);";

            bool contains = ThirdPartyToolMigrationApplicationDetectionRules.ContainsLegacyTimerDelayInvocation(
                source,
                Array.Empty<string>(),
                true);

            Assert.That(contains, Is.True);
        }

        /// <summary>
        /// Verifies that a bare TimerDelay call that appears only inside a comment is not detected.
        /// </summary>
        [Test]
        public void ContainsLegacyTimerDelayInvocation_WhenCallIsOnlyInComment_ReturnsFalse()
        {
            const string source = "// await TimerDelay.Wait(1);\nclass C { }";

            bool contains = ThirdPartyToolMigrationApplicationDetectionRules.ContainsLegacyTimerDelayInvocation(
                source,
                Array.Empty<string>(),
                true);

            Assert.That(contains, Is.False);
        }

        /// <summary>
        /// Verifies that a legacy fully qualified Application type reference is detected even when bare migration is disabled.
        /// </summary>
        [Test]
        public void ContainsLegacyApplicationReference_WhenTypeIsLegacyQualified_ReturnsTrueWithoutBareMigration()
        {
            const string source = "io.github.hatayama.uLoopMCP.MainThreadSwitcher switcher;";

            bool contains = ThirdPartyToolMigrationApplicationDetectionRules.ContainsLegacyApplicationReference(
                source,
                false,
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>());

            Assert.That(contains, Is.True);
        }

        /// <summary>
        /// Verifies that an Application type referenced through a legacy namespace alias is detected even when bare migration is disabled.
        /// </summary>
        [Test]
        public void ContainsLegacyApplicationReference_WhenTypeUsesLegacyAlias_ReturnsTrueWithoutBareMigration()
        {
            const string source = "Legacy.MainThreadSwitcher switcher;";

            bool contains = ThirdPartyToolMigrationApplicationDetectionRules.ContainsLegacyApplicationReference(
                source,
                false,
                new[] { "Legacy" },
                Array.Empty<string>(),
                Array.Empty<string>());

            Assert.That(contains, Is.True);
        }

        /// <summary>
        /// Verifies that an Application type qualified with the current Application namespace is detected even when bare usage is disabled.
        /// </summary>
        [Test]
        public void ContainsCurrentApplicationReference_WhenTypeIsFullyQualified_ReturnsTrueWithoutBareUsage()
        {
            const string source = "io.github.hatayama.UnityCliLoop.Application.MainThreadSwitcher switcher;";

            bool contains = ThirdPartyToolMigrationApplicationDetectionRules.ContainsCurrentApplicationReference(
                source,
                false,
                Array.Empty<string>(),
                Array.Empty<string>());

            Assert.That(contains, Is.True);
        }
    }
}
