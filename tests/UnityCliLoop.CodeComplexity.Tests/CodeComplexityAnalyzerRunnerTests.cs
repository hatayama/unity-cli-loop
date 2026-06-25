using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityCliLoop.CodeComplexity;

namespace UnityCliLoop.CodeComplexity.Tests
{
    [TestFixture]
    public sealed class CodeComplexityAnalyzerRunnerTests
    {
        private string _rootPath = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _rootPath = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                $"code-complexity-{Guid.NewGuid():N}");
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

        // Verifies that the runner reports Microsoft's CA1502 diagnostic when a method exceeds the configured threshold.
        [Test]
        public async Task AnalyzeAsync_WhenMethodExceedsThreshold_ShouldReportCA1502()
        {
            CodeComplexityAnalyzerRunner runner = new();
            CodeComplexityOptions options = new(
                _rootPath,
                maxComplexity: 1,
                includeNonProduction: false,
                ReportFormat.Table,
                failOnExceeded: false);

            IReadOnlyList<CodeComplexityIssue> issues = await runner.AnalyzeAsync(options, CancellationToken.None);

            Assert.That(issues.Any(issue =>
                issue.RuleId == "CA1502"
                && issue.Message.Contains("ProductionBranch", StringComparison.Ordinal)), Is.True);
            Assert.That(issues.All(issue => !Path.IsPathRooted(issue.FilePath)), Is.True);
        }

        // Verifies that non-production sources stay out of the default package analysis.
        [Test]
        public async Task AnalyzeAsync_WhenNonProductionIsExcluded_ShouldIgnoreAssetsSources()
        {
            CodeComplexityAnalyzerRunner runner = new();
            CodeComplexityOptions options = new(
                _rootPath,
                maxComplexity: 1,
                includeNonProduction: false,
                ReportFormat.Table,
                failOnExceeded: false);

            IReadOnlyList<CodeComplexityIssue> issues = await runner.AnalyzeAsync(options, CancellationToken.None);

            Assert.That(issues.Any(issue =>
                issue.Message.Contains("AssetBranch", StringComparison.Ordinal)), Is.False);
        }

        // Verifies that non-production analysis can be enabled for advisory local checks.
        [Test]
        public async Task AnalyzeAsync_WhenNonProductionIsIncluded_ShouldReportAssetsSources()
        {
            CodeComplexityAnalyzerRunner runner = new();
            CodeComplexityOptions options = new(
                _rootPath,
                maxComplexity: 1,
                includeNonProduction: true,
                ReportFormat.Table,
                failOnExceeded: false);

            IReadOnlyList<CodeComplexityIssue> issues = await runner.AnalyzeAsync(options, CancellationToken.None);

            Assert.That(issues.Any(issue =>
                issue.RuleId == "CA1502"
                && issue.Message.Contains("AssetBranch", StringComparison.Ordinal)), Is.True);
        }

        // Verifies that advisory mode keeps the command successful when CA1502 diagnostics are present.
        [Test]
        public void Main_WhenFailOnExceededIsFalse_ShouldReturnSuccessForFindings()
        {
            int exitCode = Program.Main(new[]
            {
                "--root",
                _rootPath,
                "--max-complexity",
                "1",
                "--fail-on-exceeded",
                "false"
            });

            Assert.That(exitCode, Is.EqualTo(0));
        }

        // Verifies that blocking mode returns a failing exit code when CA1502 diagnostics are present.
        [Test]
        public void Main_WhenFailOnExceededIsTrue_ShouldReturnFailureForFindings()
        {
            int exitCode = Program.Main(new[]
            {
                "--root",
                _rootPath,
                "--max-complexity",
                "1",
                "--fail-on-exceeded",
                "true"
            });

            Assert.That(exitCode, Is.EqualTo(1));
        }

        private static void CreateSampleRepository(string rootPath)
        {
            string packageDirectory = Path.Combine(rootPath, "Packages", "src", "Editor", "Sample");
            string assetsDirectory = Path.Combine(rootPath, "Assets", "Tests");
            Directory.CreateDirectory(packageDirectory);
            Directory.CreateDirectory(assetsDirectory);

            WriteFile(
                Path.Combine(packageDirectory, "SampleCode.cs"),
                """
                namespace Sample
                {
                    public sealed class ProductionCode
                    {
                        public int ProductionBranch(bool condition)
                        {
                            if (condition)
                            {
                                return 1;
                            }

                            return 0;
                        }
                    }
                }
                """);
            WriteFile(
                Path.Combine(assetsDirectory, "AssetCode.cs"),
                """
                namespace SampleAssets
                {
                    public sealed class AssetCode
                    {
                        public int AssetBranch(bool condition)
                        {
                            if (condition)
                            {
                                return 1;
                            }

                            return 0;
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
