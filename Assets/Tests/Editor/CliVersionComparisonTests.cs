using System;
using NUnit.Framework;

namespace io.github.hatayama.uLoopMCP.Tests.Editor
{
    public class CliVersionComparisonTests
    {
        [TestCase("2.1.0", "2.1.0", 0)]
        [TestCase("v2.1.0", "2.1.0", 0)]
        [TestCase("2.1.0", "3.0.0-beta.0", -1)]
        [TestCase("3.0.0-beta.0", "2.1.0", 1)]
        [TestCase("3.0.0-beta.0", "3.0.0", -1)]
        [TestCase("3.0.0-beta.1", "3.0.0-beta.0", 1)]
        [TestCase("3.0.0-alpha.10", "3.0.0-alpha.2", 1)]
        public void TryCompare_WhenVersionUsesSemVer_ReturnsExpectedOrder(
            string leftVersion,
            string rightVersion,
            int expectedSign)
        {
            bool parsed = CliVersionComparison.TryCompare(leftVersion, rightVersion, out int comparison);

            Assert.That(parsed, Is.True);
            Assert.That(Math.Sign(comparison), Is.EqualTo(expectedSign));
        }

        [TestCase("invalid", "2.1.0")]
        [TestCase("2.1.0", "invalid")]
        [TestCase("2.1", "2.1.0")]
        [TestCase("3.0.0-alpha..1", "3.0.0")]
        [TestCase("3.0.0-alpha_1", "3.0.0")]
        [TestCase("3.0.0-01", "3.0.0")]
        public void TryCompare_WhenVersionCannotParse_ReturnsFalse(string leftVersion, string rightVersion)
        {
            bool parsed = CliVersionComparison.TryCompare(leftVersion, rightVersion, out int comparison);

            Assert.That(parsed, Is.False);
            Assert.That(comparison, Is.EqualTo(0));
        }

        [TestCase("2.1.0", "2.1.0", true)]
        [TestCase("v2.1.0", "2.1.0", true)]
        [TestCase("3.0.0-beta.0", "3.0.0-beta.0", true)]
        [TestCase("3.0.0-beta.0", "3.0.0", false)]
        [TestCase("invalid", "2.1.0", false)]
        public void IsSameVersion_ReturnsExpectedValue(
            string leftVersion,
            string rightVersion,
            bool expected)
        {
            bool isSame = CliVersionComparison.IsSameVersion(leftVersion, rightVersion);

            Assert.That(isSame, Is.EqualTo(expected));
        }
    }
}
