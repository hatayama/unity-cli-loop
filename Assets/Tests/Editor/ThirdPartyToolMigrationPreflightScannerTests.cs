using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies the cheap migration preflight scan over project files and source text.
    /// </summary>
    public sealed class ThirdPartyToolMigrationPreflightScannerTests
    {
        private const string LegacyToolSource =
            "using io.github.hatayama.uLoopMCP; [McpTool] public sealed class HelloTool {}";

        private string _projectRoot;

        [SetUp]
        public void SetUp()
        {
            _projectRoot = Path.Combine(Path.GetTempPath(), "uloop-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_projectRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_projectRoot))
            {
                Directory.Delete(_projectRoot, true);
            }
        }

        /// <summary>
        /// Verifies that a project without an Assets folder reports no targets instead of walking a missing directory.
        /// </summary>
        [Test]
        public void FindMigrationTargetAsync_WhenAssetsDirectoryIsMissing_ReturnsNoTargets()
        {
            Task<MigrationTargetPreflightResult> task =
                ThirdPartyToolMigrationPreflightScanner.FindMigrationTargetAsync(_projectRoot, CancellationToken.None);

            Assert.That(GetCompletedResult(task), Is.EqualTo(MigrationTargetPreflightResult.NoTargets));
        }

        /// <summary>
        /// Verifies that a cancelled preflight stops before inspecting files and reports no targets.
        /// </summary>
        [Test]
        public void FindMigrationTargetAsync_WhenTokenIsCancelled_ReturnsNoTargets()
        {
            WriteProjectFile(Path.Combine("Assets", "VendorTools", "HelloTool.cs"), LegacyToolSource);

            Task<MigrationTargetPreflightResult> task =
                ThirdPartyToolMigrationPreflightScanner.FindMigrationTargetAsync(_projectRoot, new CancellationToken(true));

            Assert.That(GetCompletedResult(task), Is.EqualTo(MigrationTargetPreflightResult.NoTargets));
        }

        /// <summary>
        /// Verifies that legacy tools inside a Unity-ignored trailing-tilde folder are not reported as targets.
        /// </summary>
        [Test]
        public void FindMigrationTargetAsync_WhenLegacyToolIsUnderExcludedDirectory_ReturnsNoTargets()
        {
            WriteProjectFile(Path.Combine("Assets", "Samples~", "HelloTool.cs"), LegacyToolSource);

            Task<MigrationTargetPreflightResult> task =
                ThirdPartyToolMigrationPreflightScanner.FindMigrationTargetAsync(_projectRoot, CancellationToken.None);

            Assert.That(GetCompletedResult(task), Is.EqualTo(MigrationTargetPreflightResult.NoTargets));
        }

        /// <summary>
        /// Verifies that source text with an extension other than .cs or .asmdef is never a migration target.
        /// </summary>
        [Test]
        public void InspectSourceText_WhenExtensionIsNotInspected_ReturnsNoTargets()
        {
            MigrationTargetPreflightResult result =
                ThirdPartyToolMigrationPreflightScanner.InspectSourceText(LegacyToolSource, ".txt");

            Assert.That(result, Is.EqualTo(MigrationTargetPreflightResult.NoTargets));
        }

        /// <summary>
        /// Verifies that a source mentioning only a renamed contract type in a comment is deferred to the full scan.
        /// </summary>
        [Test]
        public void InspectSourceText_WhenOnlyTypeReplacementMarkerExists_ReturnsNeedsFullScan()
        {
            MigrationTargetPreflightResult result =
                ThirdPartyToolMigrationPreflightScanner.InspectSourceText("// BaseToolSchema", ".cs");

            Assert.That(result, Is.EqualTo(MigrationTargetPreflightResult.NeedsFullScan));
        }

        /// <summary>
        /// Verifies that an asmdef whose legacy name text has no references array is deferred to the full scan.
        /// </summary>
        [Test]
        public void InspectSourceText_WhenAsmdefMentionsLegacyNameWithoutReferences_ReturnsNeedsFullScan()
        {
            MigrationTargetPreflightResult result =
                ThirdPartyToolMigrationPreflightScanner.InspectSourceText(
                    @"{ ""name"": ""uLoopMCP.Editor.Extensions"" }",
                    ".asmdef");

            Assert.That(result, Is.EqualTo(MigrationTargetPreflightResult.NeedsFullScan));
        }

        /// <summary>
        /// Verifies that an asmdef whose references do not include a legacy assembly is deferred to the full scan.
        /// </summary>
        [Test]
        public void InspectSourceText_WhenAsmdefReferencesDoNotIncludeLegacyAssembly_ReturnsNeedsFullScan()
        {
            MigrationTargetPreflightResult result =
                ThirdPartyToolMigrationPreflightScanner.InspectSourceText(
                    @"{ ""name"": ""uLoopMCP.Editor.Extensions"", ""references"": [ ""VendorTools.Editor"" ] }",
                    ".asmdef");

            Assert.That(result, Is.EqualTo(MigrationTargetPreflightResult.NeedsFullScan));
        }

        private void WriteProjectFile(string relativePath, string content)
        {
            string filePath = Path.Combine(_projectRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            File.WriteAllText(filePath, content);
        }

        private static T GetCompletedResult<T>(Task<T> task)
        {
            Assert.That(task.IsCompleted, Is.True);
            return task.GetAwaiter().GetResult();
        }
    }
}
