using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies the async assembly usage analysis stops at cancellation boundaries.
    /// </summary>
    public sealed class ThirdPartyToolMigrationAssemblyUsageAsyncAnalyzerTests
    {
        private const string CurrentScreenshotDtoSource =
            "using io.github.hatayama.UnityCliLoop.FirstPartyTools;\nclass C { ScreenshotResponse response; }";

        private string _projectRoot;
        private string _sourcePath;
        private List<string> _readPaths;

        [SetUp]
        public void SetUp()
        {
            _projectRoot = Path.Combine(Path.GetTempPath(), "uloop-test-" + Guid.NewGuid().ToString("N"));
            _sourcePath = Path.Combine(_projectRoot, "Assets", "VendorTools", "ScreenshotConsumer.cs");
            _readPaths = new List<string>();
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
        /// Verifies that a cancelled analysis returns an empty usage without reading any C# source.
        /// </summary>
        [Test]
        public void FindMigrationAssemblyUsageAsync_WhenTokenIsCancelled_ReadsNoSource()
        {
            Task<MigrationAssemblyUsage> task = Analyze(new NoOpProgress(), new CancellationToken(true));

            MigrationAssemblyUsage usage = GetCompletedResult(task);
            Assert.That(_readPaths, Is.Empty);
            Assert.That(usage.FirstPartyScreenshotReferenceAssemblyDirectories, Is.Empty);
        }

        /// <summary>
        /// Verifies that cancelling after the initial pass skips recording reference requirements.
        /// </summary>
        [Test]
        public void FindMigrationAssemblyUsageAsync_WhenCancelledAfterInitialPass_RecordsNoReferenceRequirements()
        {
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                CancelOnFirstProcessedItem progress = new CancelOnFirstProcessedItem(cancellation);

                Task<MigrationAssemblyUsage> task = Analyze(progress, cancellation.Token);

                MigrationAssemblyUsage usage = GetCompletedResult(task);
                Assert.That(progress.HasCancelled, Is.True);
                Assert.That(usage.FirstPartyScreenshotReferenceAssemblyDirectories, Is.Empty);
                Assert.That(usage.ToolContractsReferenceAssemblyDirectories, Is.Empty);
            }
        }

        private Task<MigrationAssemblyUsage> Analyze(IProgress<ThirdPartyToolMigrationProgress> progress, CancellationToken ct)
        {
            List<string> readPaths = _readPaths;
            ThirdPartyToolMigrationSourceFileCache cache = new ThirdPartyToolMigrationSourceFileCache(filePath =>
            {
                readPaths.Add(filePath);
                return CurrentScreenshotDtoSource;
            });
            return ThirdPartyToolMigrationAssemblyUsageAsyncAnalyzer.FindMigrationAssemblyUsageAsync(
                _projectRoot,
                new List<string> { _sourcePath },
                new List<string>(),
                new List<string>(),
                cache,
                new MigrationProgressCounter(2, progress),
                ct);
        }

        private static T GetCompletedResult<T>(Task<T> task)
        {
            Assert.That(task.IsCompleted, Is.True);
            return task.GetAwaiter().GetResult();
        }

        private sealed class NoOpProgress : IProgress<ThirdPartyToolMigrationProgress>
        {
            public void Report(ThirdPartyToolMigrationProgress value)
            {
            }
        }

        private sealed class CancelOnFirstProcessedItem : IProgress<ThirdPartyToolMigrationProgress>
        {
            private readonly CancellationTokenSource _cancellation;

            public CancelOnFirstProcessedItem(CancellationTokenSource cancellation)
            {
                _cancellation = cancellation;
            }

            public bool HasCancelled { get; private set; }

            public void Report(ThirdPartyToolMigrationProgress value)
            {
                if (value.ProcessedItemCount != 1)
                {
                    return;
                }

                HasCancelled = true;
                _cancellation.Cancel();
            }
        }
    }
}
