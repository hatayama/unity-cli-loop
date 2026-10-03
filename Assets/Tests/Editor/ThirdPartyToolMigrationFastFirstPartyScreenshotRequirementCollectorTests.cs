using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies the fast scan of first-party screenshot API usage and its assembly reference needs.
    /// </summary>
    public sealed class ThirdPartyToolMigrationFastFirstPartyScreenshotRequirementCollectorTests
    {
        private const string LegacyCaptureSource =
            "using io.github.hatayama.uLoopMCP;\n" +
            "using UnityEditor;\n" +
            "public sealed class ScreenshotTool\n" +
            "{\n" +
            "    public async System.Threading.Tasks.Task<UnityEngine.Texture2D> CaptureAsync(EditorWindow window, System.Threading.CancellationToken ct)\n" +
            "    {\n" +
            "        return await EditorWindowCaptureUtility.CaptureWindowAsync(window, 1.0f, ct);\n" +
            "    }\n" +
            "}";

        private const string CurrentScreenshotDtoSource =
            "using io.github.hatayama.UnityCliLoop.FirstPartyTools;\nclass C { ScreenshotResponse response; }";

        private string _projectRoot;
        private string _toolDirectory;
        private HashSet<string> _toolContractsDirectories;
        private HashSet<string> _screenshotDirectories;

        [SetUp]
        public void SetUp()
        {
            _projectRoot = Path.Combine(Path.GetTempPath(), "uloop-test-" + Guid.NewGuid().ToString("N"));
            _toolDirectory = Path.Combine(_projectRoot, "Assets", "VendorTools");
            Directory.CreateDirectory(_toolDirectory);
            _toolContractsDirectories = new HashSet<string>();
            _screenshotDirectories = new HashSet<string>();
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
        /// Verifies that a legacy EditorWindowCaptureUtility call is a migration target that needs only a ToolContracts reference.
        /// </summary>
        [Test]
        public void CollectFastFirstPartyScreenshotRequirementsAsync_WhenLegacyCaptureUtilityIsUsed_ReturnsTrueAndRequiresToolContracts()
        {
            Task<bool> task = Collect(WriteTool(LegacyCaptureSource), CancellationToken.None);

            Assert.That(GetCompletedResult(task), Is.True);
            Assert.That(_toolContractsDirectories, Is.EqualTo(new[] { _toolDirectory }));
            Assert.That(_screenshotDirectories, Is.Empty);
        }

        /// <summary>
        /// Verifies that a current first-party screenshot DTO requires both ToolContracts and screenshot assembly references.
        /// </summary>
        [Test]
        public void CollectFastFirstPartyScreenshotRequirementsAsync_WhenCurrentScreenshotDtoIsUsed_RequiresScreenshotReference()
        {
            Task<bool> task = Collect(WriteTool(CurrentScreenshotDtoSource), CancellationToken.None);

            Assert.That(GetCompletedResult(task), Is.True);
            Assert.That(_toolContractsDirectories, Is.EqualTo(new[] { _toolDirectory }));
            Assert.That(_screenshotDirectories, Is.EqualTo(new[] { _toolDirectory }));
        }

        /// <summary>
        /// Verifies that a cancelled scan reports no target and records no reference requirement.
        /// </summary>
        [Test]
        public void CollectFastFirstPartyScreenshotRequirementsAsync_WhenTokenIsCancelled_ReturnsFalseAndRecordsNothing()
        {
            Task<bool> task = Collect(WriteTool(LegacyCaptureSource), new CancellationToken(true));

            Assert.That(GetCompletedResult(task), Is.False);
            Assert.That(_toolContractsDirectories, Is.Empty);
            Assert.That(_screenshotDirectories, Is.Empty);
        }

        private string WriteTool(string source)
        {
            string filePath = Path.Combine(_toolDirectory, "ScreenshotTool.cs");
            File.WriteAllText(filePath, source);
            return filePath;
        }

        private Task<bool> Collect(string csharpFilePath, CancellationToken ct)
        {
            return ThirdPartyToolMigrationFastFirstPartyScreenshotRequirementCollector.CollectFastFirstPartyScreenshotRequirementsAsync(
                new List<string> { csharpFilePath },
                new List<string> { _toolDirectory },
                new List<AssemblyReferenceDirectory>(),
                _projectRoot,
                new HashSet<string>(),
                new Dictionary<string, HashSet<string>>(),
                new HashSet<string>(),
                new HashSet<string>(),
                new Dictionary<string, HashSet<string>>(),
                new Dictionary<string, HashSet<string>>(),
                _toolContractsDirectories,
                _screenshotDirectories,
                ct);
        }

        private static T GetCompletedResult<T>(Task<T> task)
        {
            Assert.That(task.IsCompleted, Is.True);
            return task.GetAwaiter().GetResult();
        }
    }
}
