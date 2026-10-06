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

        // Verifies that repository-specific preprocessor symbols keep conditional package code visible to CA1502.
        [Test]
        public async Task AnalyzeAsync_WhenRepositorySymbolWrapsCode_ShouldAnalyzeConditionalBranch()
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
                && issue.Message.Contains("ConditionalTestFrameworkBranch", StringComparison.Ordinal)), Is.True);
        }

        // Verifies that a checkout placed below a .claude directory still has its sources analyzed.
        [Test]
        public async Task AnalyzeAsync_WhenRootSitsBelowClaudeDirectory_ShouldStillAnalyzeSources()
        {
            string rootPath = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                ".claude",
                "worktrees",
                $"code-complexity-{Guid.NewGuid():N}");
            try
            {
                Directory.CreateDirectory(rootPath);
                CreateSampleRepository(rootPath);
                CodeComplexityAnalyzerRunner runner = new();
                CodeComplexityOptions options = new(
                    rootPath,
                    maxComplexity: 1,
                    includeNonProduction: false,
                    ReportFormat.Table,
                    failOnExceeded: false);

                IReadOnlyList<CodeComplexityIssue> issues = await runner.AnalyzeAsync(options, CancellationToken.None);

                Assert.That(issues.Any(issue =>
                    issue.RuleId == "CA1502"
                    && issue.Message.Contains("ProductionBranch", StringComparison.Ordinal)), Is.True);
            }
            finally
            {
                // Only the per-test directory is removed: the work directory may already hold a real
                // .claude directory, which a recursive delete of .claude would wipe out.
                if (Directory.Exists(rootPath))
                {
                    Directory.Delete(rootPath, recursive: true);
                }
            }
        }

        // Verifies that a generated skill copy inside the scanned tree is skipped while the rest of the tree is analyzed.
        [Test]
        public async Task AnalyzeAsync_WhenGeneratedSkillCopyIsInsideScannedTree_ShouldIgnoreIt()
        {
            string skillCopyDirectory = Path.Combine(_rootPath, "Packages", "src", ".claude", "skills");
            Directory.CreateDirectory(skillCopyDirectory);
            WriteFile(
                Path.Combine(skillCopyDirectory, "Generated.cs"),
                """
                namespace SampleGenerated
                {
                    public sealed class GeneratedCode
                    {
                        public int GeneratedBranch(bool condition)
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
            CodeComplexityAnalyzerRunner runner = new();
            CodeComplexityOptions options = new(
                _rootPath,
                maxComplexity: 1,
                includeNonProduction: false,
                ReportFormat.Table,
                failOnExceeded: false);

            IReadOnlyList<CodeComplexityIssue> issues = await runner.AnalyzeAsync(options, CancellationToken.None);

            Assert.That(issues.Any(issue =>
                issue.Message.Contains("GeneratedBranch", StringComparison.Ordinal)), Is.False);
            Assert.That(issues.Any(issue =>
                issue.Message.Contains("ProductionBranch", StringComparison.Ordinal)), Is.True);
        }

        // Verifies that a root without production sources is rejected instead of being reported as clean.
        [Test]
        public void AnalyzeAsync_WhenNoProductionSourceExists_ShouldThrow()
        {
            DeleteProductionSources(_rootPath);
            CodeComplexityAnalyzerRunner runner = new();
            CodeComplexityOptions options = new(
                _rootPath,
                maxComplexity: 1,
                includeNonProduction: false,
                ReportFormat.Table,
                failOnExceeded: false);

            NoProductionSourceException? exception = Assert.ThrowsAsync<NoProductionSourceException>(
                async () => await runner.AnalyzeAsync(options, CancellationToken.None));

            Assert.That(exception?.Message, Does.Contain(Path.Combine(_rootPath, "Packages", "src")));
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

        // Verifies that invalid options return a clean validation failure instead of an unhandled exception.
        [Test]
        public void Main_WhenOptionIsUnknown_ShouldReturnValidationFailure()
        {
            int exitCode = Program.Main(new[]
            {
                "--unknown"
            });

            Assert.That(exitCode, Is.EqualTo(2));
        }

        // Verifies that oversized numeric input is rejected without overflowing.
        [Test]
        public void Main_WhenMaxComplexityOverflowsInt32_ShouldReturnValidationFailure()
        {
            int exitCode = Program.Main(new[]
            {
                "--root",
                _rootPath,
                "--max-complexity",
                "999999999999999999999999999999999999"
            });

            Assert.That(exitCode, Is.EqualTo(2));
        }

        // Verifies that empty root paths return validation failures before path resolution.
        [Test]
        public void Main_WhenRootPathIsEmpty_ShouldReturnValidationFailure()
        {
            int exitCode = Program.Main(new[]
            {
                "--root",
                string.Empty
            });

            Assert.That(exitCode, Is.EqualTo(2));
        }

        // Verifies that malformed root paths return validation failures before path resolution.
        [Test]
        public void Main_WhenRootPathContainsNullCharacter_ShouldReturnValidationFailure()
        {
            int exitCode = Program.Main(new[]
            {
                "--root",
                "\0"
            });

            Assert.That(exitCode, Is.EqualTo(2));
        }

        // Verifies that a root without production sources returns the validation failure code.
        [Test]
        public void Main_WhenNoProductionSourceExists_ShouldReturnValidationFailure()
        {
            DeleteProductionSources(_rootPath);

            int exitCode = Program.Main(new[]
            {
                "--root",
                _rootPath
            });

            Assert.That(exitCode, Is.EqualTo(2));
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

                    #if ULOOP_HAS_TEST_FRAMEWORK
                        public int ConditionalTestFrameworkBranch(bool condition)
                        {
                            if (condition)
                            {
                                return 1;
                            }

                            return 0;
                        }
                    #endif
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

        private static void DeleteProductionSources(string rootPath)
        {
            string packageSourcePath = Path.Combine(rootPath, "Packages", "src");
            foreach (string sourceFile in Directory.GetFiles(packageSourcePath, "*.cs", SearchOption.AllDirectories))
            {
                File.Delete(sourceFile);
            }
        }

        private static void WriteFile(string path, string content)
        {
            File.WriteAllText(path, content.Replace("\r\n", "\n", StringComparison.Ordinal));
        }
    }
}
