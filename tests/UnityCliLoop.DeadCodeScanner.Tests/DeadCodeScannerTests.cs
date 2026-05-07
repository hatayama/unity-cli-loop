using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityCliLoop.DeadCodeScanner;

namespace UnityCliLoop.DeadCodeScanner.Tests
{
    [TestFixture]
    public sealed class DeadCodeScannerTests
    {
        private string _rootPath = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _rootPath = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                $"dead-code-scanner-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_rootPath);
            CreateSampleRepository(_rootPath);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_rootPath))
            {
                Directory.Delete(_rootPath, recursive: true);
            }
        }

        // Verifies that default private scope reports private members and locals without surfacing public API candidates.
        [Test]
        public async Task ScanAsync_WhenUsingDefaultPrivateScope_ShouldReportPrivateMemberAndLocalFindings()
        {
            DeadCodeScanner scanner = new();
            ScanOptions options = ScanOptions.Default(_rootPath);

            System.Collections.Generic.IReadOnlyList<DeadCodeIssue> issues =
                await scanner.ScanAsync(options, CancellationToken.None);

            Assert.That(issues.Any(issue =>
                issue.Category == DeadCodeCategory.UnusedPrivateMember
                && issue.FullName.Contains("unusedField", StringComparison.Ordinal)), Is.True);
            Assert.That(issues.Any(issue =>
                issue.Category == DeadCodeCategory.UnusedPrivateMember
                && issue.FullName.Contains("UnusedPrivateMethod", StringComparison.Ordinal)), Is.True);
            Assert.That(issues.Any(issue =>
                issue.Category == DeadCodeCategory.UnusedLocal
                && issue.FullName.Contains("unusedLocal", StringComparison.Ordinal)), Is.True);
            Assert.That(issues.Any(issue =>
                issue.Category == DeadCodeCategory.PublicCandidate), Is.False);
        }

        // Verifies that public scope keeps unreferenced public symbols visible without treating them as direct deletion candidates.
        [Test]
        public async Task ScanAsync_WhenUsingPublicScope_ShouldReportPublicCandidates()
        {
            DeadCodeScanner scanner = new();
            ScanOptions options = new(
                _rootPath,
                ScanScope.Public,
                includeTypes: true,
                includeMembers: true,
                includeLocals: false,
                includeTestOnly: true,
                includeKept: false,
                ReportFormat.Table,
                failOnHighConfidence: false);

            System.Collections.Generic.IReadOnlyList<DeadCodeIssue> issues =
                await scanner.ScanAsync(options, CancellationToken.None);

            Assert.That(issues.Any(issue =>
                issue.Category == DeadCodeCategory.PublicCandidate
                && issue.FullName.Contains("UnreferencedPublicApi", StringComparison.Ordinal)), Is.True);
            Assert.That(issues.Any(issue =>
                issue.Category == DeadCodeCategory.PublicCandidate
                && issue.SymbolKind == "type"
                && issue.FullName.Contains("UsedProductionApi", StringComparison.Ordinal)), Is.False);
        }

        // Verifies that symbols referenced only from Assets are separated from production references.
        [Test]
        public async Task ScanAsync_WhenProductionSymbolIsOnlyUsedByAssets_ShouldReportTestOnly()
        {
            DeadCodeScanner scanner = new();
            ScanOptions options = new(
                _rootPath,
                ScanScope.Public,
                includeTypes: true,
                includeMembers: true,
                includeLocals: false,
                includeTestOnly: true,
                includeKept: false,
                ReportFormat.Table,
                failOnHighConfidence: false);

            System.Collections.Generic.IReadOnlyList<DeadCodeIssue> issues =
                await scanner.ScanAsync(options, CancellationToken.None);

            Assert.That(issues.Any(issue =>
                issue.Category == DeadCodeCategory.TestOnly
                && issue.FullName.Contains("TestOnlyFactory", StringComparison.Ordinal)), Is.True);
        }

        // Verifies that Unity or reflection entry points can be reported separately when requested.
        [Test]
        public async Task ScanAsync_WhenIncludingKeptSymbols_ShouldReportUnityToolAsKept()
        {
            DeadCodeScanner scanner = new();
            ScanOptions options = new(
                _rootPath,
                ScanScope.Public,
                includeTypes: true,
                includeMembers: true,
                includeLocals: false,
                includeTestOnly: true,
                includeKept: true,
                ReportFormat.Table,
                failOnHighConfidence: false);

            System.Collections.Generic.IReadOnlyList<DeadCodeIssue> issues =
                await scanner.ScanAsync(options, CancellationToken.None);

            Assert.That(issues.Any(issue =>
                issue.Category == DeadCodeCategory.KeptByUnityOrReflection
                && issue.FullName.Contains("SampleTool", StringComparison.Ordinal)), Is.True);
            Assert.That(issues.Any(issue =>
                issue.Category == DeadCodeCategory.KeptByUnityOrReflection
                && issue.FullName.Contains("RuntimeReset", StringComparison.Ordinal)), Is.True);
            Assert.That(issues.Any(issue =>
                issue.Category == DeadCodeCategory.KeptByUnityOrReflection
                && issue.SymbolKind == "type"
                && issue.FullName.Contains("UsedProductionApi", StringComparison.Ordinal)), Is.True);
        }

        private static void CreateSampleRepository(string rootPath)
        {
            string packageDirectory = Path.Combine(rootPath, "Packages", "src", "Editor", "Sample");
            string assetsDirectory = Path.Combine(rootPath, "Assets", "Tests");
            Directory.CreateDirectory(packageDirectory);
            Directory.CreateDirectory(assetsDirectory);

            WriteFile(
                Path.Combine(packageDirectory, "Sample.asmdef"),
                """
                {
                  "name": "Sample.Editor",
                  "references": [],
                  "includePlatforms": ["Editor"],
                  "versionDefines": []
                }
                """);
            WriteFile(
                Path.Combine(packageDirectory, "Sample.asmdef.meta"),
                """
                fileFormatVersion: 2
                guid: 11111111111111111111111111111111
                """);
            WriteFile(
                Path.Combine(packageDirectory, "SampleCode.cs"),
                """
                using System;

                namespace Sample
                {
                    public sealed class UnityCliLoopToolAttribute : Attribute
                    {
                    }

                    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
                    {
                    }

                    [UnityCliLoopTool]
                    public sealed class SampleTool
                    {
                    }

                    public sealed class UsedProductionApi
                    {
                        private int usedField;
                        private int unusedField;

                        public void Caller()
                        {
                            usedField++;
                            int unusedLocal = 1;
                            UsedPrivateMethod();
                        }

                        private void UsedPrivateMethod()
                        {
                        }

                        private void UnusedPrivateMethod()
                        {
                        }

                        [RuntimeInitializeOnLoadMethod]
                        private static void RuntimeReset()
                        {
                        }
                    }

                    public sealed class UnreferencedPublicApi
                    {
                    }

                    public sealed class TestOnlyFactory
                    {
                    }
                }
                """);
            WriteFile(
                Path.Combine(assetsDirectory, "SampleAssetUsage.cs"),
                """
                namespace SampleConsumer
                {
                    public sealed class SampleAssetUsage
                    {
                        public object Create()
                        {
                            return new Sample.TestOnlyFactory();
                        }
                    }
                }
                """);
        }

        private static void WriteFile(string path, string content)
        {
            File.WriteAllText(path, content.Replace("\r\n", "\n", StringComparison.Ordinal));
        }
    }
}
