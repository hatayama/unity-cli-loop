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
                failOnHighConfidence: false,
                maxPublicCandidates: -1);

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
                failOnHighConfidence: false,
                maxPublicCandidates: -1);

            System.Collections.Generic.IReadOnlyList<DeadCodeIssue> issues =
                await scanner.ScanAsync(options, CancellationToken.None);

            Assert.That(issues.Any(issue =>
                issue.Category == DeadCodeCategory.TestOnly
                && issue.FullName.Contains("TestOnlyFactory", StringComparison.Ordinal)), Is.True);
        }

        // Verifies that default-scope unused private findings are treated as high-confidence deletion candidates for the CI gate.
        [Test]
        public async Task ScanAsync_WhenUsingDefaultPrivateScope_ShouldReportHighConfidenceDeletionCandidates()
        {
            DeadCodeScanner scanner = new();
            ScanOptions options = ScanOptions.Default(_rootPath);

            System.Collections.Generic.IReadOnlyList<DeadCodeIssue> issues =
                await scanner.ScanAsync(options, CancellationToken.None);

            Assert.That(issues.Any(issue => issue.IsHighConfidenceDeletionCandidate()), Is.True);
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
                failOnHighConfidence: false,
                maxPublicCandidates: -1);

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

        /// <summary>
        /// Verifies that an internal production member referenced through InternalsVisibleTo from an
        /// Assets asmdef is classified as TestOnly instead of PublicCandidate.
        /// </summary>
        [Test]
        public async Task ScanAsync_WhenInternalMemberIsReferencedViaAssetsAsmdefInternalsVisibleTo_ShouldReportTestOnly()
        {
            DeadCodeScanner scanner = new();
            ScanOptions options = CreatePublicScopeOptions(_rootPath, includeKept: false);

            System.Collections.Generic.IReadOnlyList<DeadCodeIssue> issues =
                await scanner.ScanAsync(options, CancellationToken.None);

            Assert.That(issues.Any(issue =>
                issue.Category == DeadCodeCategory.TestOnly
                && issue.FullName.Contains("CreateForTesting", StringComparison.Ordinal)), Is.True);
            Assert.That(issues.Any(issue =>
                issue.Category == DeadCodeCategory.PublicCandidate
                && issue.FullName.Contains("CreateForTesting", StringComparison.Ordinal)), Is.False);
        }

        /// <summary>
        /// Verifies that Packages/src/Runtime asmdefs are loaded as production and scanned for
        /// unreferenced public symbols.
        /// </summary>
        [Test]
        public async Task ScanAsync_WhenRuntimeAsmdefDefinesUnreferencedPublicType_ShouldReportPublicCandidate()
        {
            DeadCodeScanner scanner = new();
            ScanOptions options = CreatePublicScopeOptions(_rootPath, includeKept: false);

            System.Collections.Generic.IReadOnlyList<DeadCodeIssue> issues =
                await scanner.ScanAsync(options, CancellationToken.None);

            Assert.That(issues.Any(issue =>
                issue.Category == DeadCodeCategory.PublicCandidate
                && issue.SymbolKind == "type"
                && issue.FullName.Contains("UnreferencedRuntimeApi", StringComparison.Ordinal)
                && issue.AssemblyName == "Sample.Runtime"), Is.True);
        }

        /// <summary>
        /// Verifies that a Runtime internal member referenced from an Editor assembly through
        /// InternalsVisibleTo is not reported as any finding.
        /// </summary>
        [Test]
        public async Task ScanAsync_WhenRuntimeInternalMemberIsReferencedFromEditorViaInternalsVisibleTo_ShouldNotReportFinding()
        {
            DeadCodeScanner scanner = new();
            ScanOptions options = CreatePublicScopeOptions(_rootPath, includeKept: false);

            System.Collections.Generic.IReadOnlyList<DeadCodeIssue> issues =
                await scanner.ScanAsync(options, CancellationToken.None);

            Assert.That(issues.Any(issue =>
                issue.FullName.Contains("RuntimeInternalUsedByEditor", StringComparison.Ordinal)), Is.False);
            Assert.That(issues.Any(issue =>
                issue.Category == DeadCodeCategory.PublicCandidate
                && issue.FullName.Contains("RuntimeInternalUsedByEditor", StringComparison.Ordinal)), Is.False);
            Assert.That(issues.Any(issue =>
                issue.Category == DeadCodeCategory.TestOnly
                && issue.FullName.Contains("RuntimeInternalUsedByEditor", StringComparison.Ordinal)), Is.False);
        }

        /// <summary>
        /// Verifies that compiler-bound awaiter members are kept as KeptByUnityOrReflection
        /// and are not reported as PublicCandidate.
        /// </summary>
        [Test]
        public async Task ScanAsync_WhenAwaitPatternMembersHaveNoDirectReferences_ShouldReportKeptAndNotPublicCandidate()
        {
            DeadCodeScanner scanner = new();
            ScanOptions options = CreatePublicScopeOptions(_rootPath, includeKept: true);

            System.Collections.Generic.IReadOnlyList<DeadCodeIssue> issues =
                await scanner.ScanAsync(options, CancellationToken.None);

            Assert.That(issues.Any(issue =>
                issue.Category == DeadCodeCategory.KeptByUnityOrReflection
                && issue.FullName.Contains("SampleAwaitable.Awaiter.IsCompleted", StringComparison.Ordinal)), Is.True);
            Assert.That(issues.Any(issue =>
                issue.Category == DeadCodeCategory.KeptByUnityOrReflection
                && issue.FullName.Contains("SampleAwaitable.Awaiter.GetResult", StringComparison.Ordinal)), Is.True);
            Assert.That(issues.Any(issue =>
                issue.Category == DeadCodeCategory.KeptByUnityOrReflection
                && issue.FullName.Contains("SampleAwaitable.GetAwaiter", StringComparison.Ordinal)), Is.True);
            Assert.That(issues.Any(issue =>
                issue.Category == DeadCodeCategory.PublicCandidate
                && issue.FullName.Contains("SampleAwaitable.Awaiter.IsCompleted", StringComparison.Ordinal)), Is.False);
            Assert.That(issues.Any(issue =>
                issue.Category == DeadCodeCategory.PublicCandidate
                && issue.FullName.Contains("SampleAwaitable.Awaiter.GetResult", StringComparison.Ordinal)), Is.False);
            Assert.That(issues.Any(issue =>
                issue.Category == DeadCodeCategory.PublicCandidate
                && issue.FullName.Contains("SampleAwaitable.GetAwaiter", StringComparison.Ordinal)), Is.False);
        }

        /// <summary>
        /// Verifies that the IsExternalInit polyfill type is kept as KeptByUnityOrReflection
        /// and is not reported as PublicCandidate.
        /// </summary>
        [Test]
        public async Task ScanAsync_WhenIsExternalInitPolyfillHasNoDirectReferences_ShouldReportKeptAndNotPublicCandidate()
        {
            DeadCodeScanner scanner = new();
            ScanOptions options = CreatePublicScopeOptions(_rootPath, includeKept: true);

            System.Collections.Generic.IReadOnlyList<DeadCodeIssue> issues =
                await scanner.ScanAsync(options, CancellationToken.None);

            Assert.That(issues.Any(issue =>
                issue.Category == DeadCodeCategory.KeptByUnityOrReflection
                && issue.FullName.Contains("IsExternalInit", StringComparison.Ordinal)), Is.True);
            Assert.That(issues.Any(issue =>
                issue.Category == DeadCodeCategory.PublicCandidate
                && issue.FullName.Contains("IsExternalInit", StringComparison.Ordinal)), Is.False);
        }

        /// <summary>
        /// Verifies that Newtonsoft ShouldSerialize{Property} methods are kept as
        /// KeptByUnityOrReflection and are not reported as PublicCandidate.
        /// </summary>
        [Test]
        public async Task ScanAsync_WhenShouldSerializeMethodHasNoDirectReferences_ShouldReportKeptAndNotPublicCandidate()
        {
            DeadCodeScanner scanner = new();
            ScanOptions options = CreatePublicScopeOptions(_rootPath, includeKept: true);

            System.Collections.Generic.IReadOnlyList<DeadCodeIssue> issues =
                await scanner.ScanAsync(options, CancellationToken.None);

            Assert.That(issues.Any(issue =>
                issue.Category == DeadCodeCategory.KeptByUnityOrReflection
                && issue.FullName.Contains("ShouldSerializeOptionalNote", StringComparison.Ordinal)), Is.True);
            Assert.That(issues.Any(issue =>
                issue.Category == DeadCodeCategory.PublicCandidate
                && issue.FullName.Contains("ShouldSerializeOptionalNote", StringComparison.Ordinal)), Is.False);
        }

        /// <summary>
        /// Verifies that a ShouldSerialize* method without a matching property/field is not kept.
        /// </summary>
        [Test]
        public async Task ScanAsync_WhenShouldSerializeMethodHasNoMatchingMember_ShouldNotReportKept()
        {
            DeadCodeScanner scanner = new();
            ScanOptions options = CreatePublicScopeOptions(_rootPath, includeKept: true);

            System.Collections.Generic.IReadOnlyList<DeadCodeIssue> issues =
                await scanner.ScanAsync(options, CancellationToken.None);

            Assert.That(issues.Any(issue =>
                issue.Category == DeadCodeCategory.KeptByUnityOrReflection
                && issue.FullName.Contains("ShouldSerializeMissingMember", StringComparison.Ordinal)), Is.False);
            Assert.That(issues.Any(issue =>
                issue.Category == DeadCodeCategory.PublicCandidate
                && issue.FullName.Contains("ShouldSerializeMissingMember", StringComparison.Ordinal)), Is.True);
        }

        private static ScanOptions CreatePublicScopeOptions(string rootPath, bool includeKept)
        {
            return new ScanOptions(
                rootPath,
                ScanScope.Public,
                includeTypes: true,
                includeMembers: true,
                includeLocals: false,
                includeTestOnly: true,
                includeKept,
                ReportFormat.Table,
                failOnHighConfidence: false,
                maxPublicCandidates: -1);
        }

        private static void CreateSampleRepository(string rootPath)
        {
            string packageDirectory = Path.Combine(rootPath, "Packages", "src", "Editor", "Sample");
            string runtimeDirectory = Path.Combine(rootPath, "Packages", "src", "Runtime", "SampleRuntime");
            string assetsDirectory = Path.Combine(rootPath, "Assets", "Tests");
            string assetsTestAsmdefDirectory = Path.Combine(assetsDirectory, "Editor");
            Directory.CreateDirectory(packageDirectory);
            Directory.CreateDirectory(runtimeDirectory);
            Directory.CreateDirectory(assetsDirectory);
            Directory.CreateDirectory(assetsTestAsmdefDirectory);

            WriteFile(
                Path.Combine(packageDirectory, "Sample.asmdef"),
                """
                {
                  "name": "Sample.Editor",
                  "references": ["GUID:33333333333333333333333333333333"],
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
                using System.Runtime.CompilerServices;

                [assembly: InternalsVisibleTo("Sample.Tests")]

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
                            usedField += Sample.Runtime.RuntimeInternalApi.RuntimeInternalUsedByEditor();
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

                    public sealed class JsonOptionalPayload
                    {
                        public string OptionalNote { get; set; } = string.Empty;

                        public bool ShouldSerializeOptionalNote()
                        {
                            return !string.IsNullOrEmpty(OptionalNote);
                        }

                        public bool ShouldSerializeMissingMember()
                        {
                            return false;
                        }
                    }

                    public sealed class TestOnlyFactory
                    {
                    }

                    public sealed class InternalVisibleApi
                    {
                        internal static int CreateForTesting()
                        {
                            return 1;
                        }
                    }

                    public struct SampleAwaitable
                    {
                        public Awaiter GetAwaiter() => new();

                        public struct Awaiter : INotifyCompletion
                        {
                            public bool IsCompleted => true;

                            public void GetResult()
                            {
                            }

                            public void OnCompleted(Action continuation)
                            {
                                continuation();
                            }
                        }
                    }
                }

                namespace System.Runtime.CompilerServices
                {
                    public sealed class IsExternalInit
                    {
                    }
                }
                """);
            WriteFile(
                Path.Combine(runtimeDirectory, "Sample.Runtime.asmdef"),
                """
                {
                  "name": "Sample.Runtime",
                  "references": [],
                  "includePlatforms": [],
                  "versionDefines": []
                }
                """);
            WriteFile(
                Path.Combine(runtimeDirectory, "Sample.Runtime.asmdef.meta"),
                """
                fileFormatVersion: 2
                guid: 33333333333333333333333333333333
                """);
            WriteFile(
                Path.Combine(runtimeDirectory, "RuntimeCode.cs"),
                """
                using System.Runtime.CompilerServices;

                [assembly: InternalsVisibleTo("Sample.Editor")]

                namespace Sample.Runtime
                {
                    public sealed class UnreferencedRuntimeApi
                    {
                    }

                    public static class RuntimeInternalApi
                    {
                        internal static int RuntimeInternalUsedByEditor()
                        {
                            return 1;
                        }
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
            WriteFile(
                Path.Combine(assetsTestAsmdefDirectory, "Sample.Tests.asmdef"),
                """
                {
                  "name": "Sample.Tests",
                  "references": ["Sample.Editor"],
                  "includePlatforms": ["Editor"],
                  "versionDefines": []
                }
                """);
            WriteFile(
                Path.Combine(assetsTestAsmdefDirectory, "Sample.Tests.asmdef.meta"),
                """
                fileFormatVersion: 2
                guid: 22222222222222222222222222222222
                """);
            WriteFile(
                Path.Combine(assetsTestAsmdefDirectory, "SampleInternalUsage.cs"),
                """
                namespace SampleConsumer
                {
                    public sealed class SampleInternalUsage
                    {
                        public int Create()
                        {
                            return Sample.InternalVisibleApi.CreateForTesting();
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
