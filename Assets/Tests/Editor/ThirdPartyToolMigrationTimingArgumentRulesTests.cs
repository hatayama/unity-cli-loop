using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies how timing migration recognizes and removes legacy PlayerLoopTiming arguments and parameters.
    /// </summary>
    public sealed class ThirdPartyToolMigrationTimingArgumentRulesTests
    {
        /// <summary>
        /// Verifies a named legacy timing argument is removed even when it is not at the removed parameter position.
        /// </summary>
        [Test]
        public void RemoveLegacyPlayerLoopTimingCallerArguments_WhenTimingIsPassedByName_RemovesNamedArgument()
        {
            RemovedLegacyPlayerLoopTimingParameter[] removedParameters =
            {
                new RemovedLegacyPlayerLoopTimingParameter(1, "timing")
            };

            (string[] arguments, bool changed) =
                ThirdPartyToolMigrationTimingArgumentRules.RemoveLegacyPlayerLoopTimingCallerArguments(
                    new[] { "timing: PlayerLoopTiming.Update", "value" },
                    removedParameters,
                    Array.Empty<string>());

            Assert.That(changed, Is.True);
            Assert.That(arguments, Is.EqualTo(new[] { "value" }));
        }

        /// <summary>
        /// Verifies a fully qualified legacy PlayerLoopTiming parameter is matched and its name is returned.
        /// </summary>
        [Test]
        public void ReadLegacyPlayerLoopTimingParameter_WhenTypeIsFullyQualified_ReturnsParameterName()
        {
            (bool isMatch, string parameterName) =
                ThirdPartyToolMigrationTimingArgumentRules.ReadLegacyPlayerLoopTimingParameter(
                    "io.github.hatayama.uLoopMCP.PlayerLoopTiming timing",
                    Array.Empty<string>(),
                    false);

            Assert.That(isMatch, Is.True);
            Assert.That(parameterName, Is.EqualTo("timing"));
        }

        /// <summary>
        /// Verifies a legacy PlayerLoopTiming parameter qualified by a legacy namespace alias is matched.
        /// </summary>
        [Test]
        public void ReadLegacyPlayerLoopTimingParameter_WhenTypeIsAliasQualified_ReturnsParameterName()
        {
            (bool isMatch, string parameterName) =
                ThirdPartyToolMigrationTimingArgumentRules.ReadLegacyPlayerLoopTimingParameter(
                    "Legacy.PlayerLoopTiming loop",
                    new[] { "Legacy" },
                    false);

            Assert.That(isMatch, Is.True);
            Assert.That(parameterName, Is.EqualTo("loop"));
        }

        /// <summary>
        /// Verifies a named first argument is not treated as a positional MainThreadSwitcher timing argument.
        /// </summary>
        [Test]
        public void IsLegacyMainThreadSwitcherPositionalTimingArgument_WhenFirstArgumentIsNamed_ReturnsFalse()
        {
            bool result =
                ThirdPartyToolMigrationTimingArgumentRules.IsLegacyMainThreadSwitcherPositionalTimingArgument(
                    new[] { "frames: 2", "ct" },
                    0);

            Assert.That(result, Is.False);
        }

        /// <summary>
        /// Verifies identifier-like expression detection accepts identifiers and rejects other text.
        /// </summary>
        [TestCase("", false)]
        [TestCase("1value", false)]
        [TestCase("first-second", false)]
        [TestCase("_value1", true)]
        public void IsIdentifierLikeExpression_WhenGivenText_ReturnsWhetherItIsAnIdentifier(
            string argument,
            bool expected)
        {
            bool result = ThirdPartyToolMigrationTimingArgumentRules.IsIdentifierLikeExpression(argument);

            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// Verifies a PlayerLoopTiming member accessed through a legacy namespace alias is a legacy timing argument.
        /// </summary>
        [Test]
        public void IsLegacyPlayerLoopTimingArgument_WhenAccessedThroughAlias_ReturnsTrue()
        {
            bool result = ThirdPartyToolMigrationTimingArgumentRules.IsLegacyPlayerLoopTimingArgument(
                " Legacy.PlayerLoopTiming.Update ",
                new[] { "Other", "Legacy" });

            Assert.That(result, Is.True);
        }
    }
}
