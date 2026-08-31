using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies CLI Version Comparer behavior.
    /// </summary>
    public class CliVersionComparerTests
    {
        private const string SharedCompareCasesPath = "cli/common/version/compare_cases.json";

        [Test]
        public void TryCompareCliVersions_WhenSharedContractCasesLoaded_MatchesContract()
        {
            // Verifies that C# comparison behavior matches the shared cross-language contract table.
            CompareCaseCatalog catalog = ReadCompareCaseCatalog();

            foreach (CompareCase testCase in catalog.Cases)
            {
                bool ok = CliVersionComparer.TryCompareCliVersions(
                    testCase.Left,
                    testCase.Right,
                    out int comparison);

                Assert.That(ok, Is.EqualTo(testCase.Ok), testCase.Name);
                Assert.That(comparison, Is.EqualTo(testCase.Comparison), testCase.Name);
            }
        }

        [TestCase("3.0.0-beta.0", "3.0.0-beta.0", true)]
        [TestCase("3.0.0-beta.1", "3.0.0-beta.0", true)]
        [TestCase("3.0.0", "3.0.0-beta.0", true)]
        [TestCase("v3.0.0-beta.0", "3.0.0-beta.0", true)]
        [TestCase("3.0.0-beta.0", "3.0.0-beta.1", false)]
        [TestCase("3.0.0-beta.0", "3.0.0", false)]
        public void IsVersionGreaterThanOrEqual_ReturnsExpectedResult(
            string installedVersion,
            string requiredVersion,
            bool expected)
        {
            // Verifies greater-than-or-equal comparison for CLI setup compatibility checks.
            bool result = CliVersionComparer.IsVersionGreaterThanOrEqual(
                installedVersion,
                requiredVersion);

            Assert.That(result, Is.EqualTo(expected));
        }

        [TestCase("3.0.0-beta.0", "3.0.0-beta.1", true)]
        [TestCase("3.0.0-beta.0", "3.0.0", true)]
        [TestCase("3.0.0", "3.0.0-beta.0", false)]
        public void IsVersionLessThan_ReturnsExpectedResult(
            string leftVersion,
            string rightVersion,
            bool expected)
        {
            // Verifies less-than comparison for CLI downgrade and prerequisite checks.
            bool result = CliVersionComparer.IsVersionLessThan(leftVersion, rightVersion);

            Assert.That(result, Is.EqualTo(expected));
        }

        [TestCase("3.0.0-beta.2", "3.0.0-beta.1", true)]
        [TestCase("3.0.0-beta.1", "3.0.0-beta.1", false)]
        [TestCase("3.0.0-beta.0", "3.0.0-beta.1", false)]
        public void IsVersionGreaterThan_ReturnsExpectedResult(
            string leftVersion,
            string rightVersion,
            bool expected)
        {
            // Verifies strict greater-than comparison for CLI setup downgrade detection.
            bool result = CliVersionComparer.IsVersionGreaterThan(leftVersion, rightVersion);

            Assert.That(result, Is.EqualTo(expected));
        }

        [TestCase("3.0.0-beta.1", "3.0.0-beta.1", true)]
        [TestCase("v3.0.0-beta.1", "3.0.0-beta.1", true)]
        [TestCase("3.0.0-beta.2", "3.0.0-beta.1", false)]
        public void IsVersionEqual_ReturnsExpectedResult(
            string leftVersion,
            string rightVersion,
            bool expected)
        {
            // Verifies semantic equality comparison for CLI setup exact-match detection.
            bool result = CliVersionComparer.IsVersionEqual(leftVersion, rightVersion);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void IsVersionGreaterThanOrEqual_WhenVersionIsInvalid_ReturnsFalse()
        {
            // Verifies malformed CLI versions fail closed during setup compatibility checks.
            bool result = CliVersionComparer.IsVersionGreaterThanOrEqual(
                "3.0.0-beta.0",
                "not-a-version");

            Assert.That(result, Is.False);
        }

        private static CompareCaseCatalog ReadCompareCaseCatalog()
        {
            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            string json = File.ReadAllText(Path.Combine(projectRoot, SharedCompareCasesPath));
            CompareCaseCatalog catalog = JsonConvert.DeserializeObject<CompareCaseCatalog>(json);
            Assert.That(catalog, Is.Not.Null, "Shared compare cases must parse.");
            Assert.That(catalog.Cases, Is.Not.Empty, "Shared compare cases must not be empty.");
            return catalog;
        }

        private sealed class CompareCaseCatalog
        {
            public List<CompareCase> Cases { get; set; } = new List<CompareCase>();
        }

        private sealed class CompareCase
        {
            public string Name { get; set; }
            public string Left { get; set; }
            public string Right { get; set; }
            public bool Ok { get; set; }
            public int Comparison { get; set; }
        }
    }
}
