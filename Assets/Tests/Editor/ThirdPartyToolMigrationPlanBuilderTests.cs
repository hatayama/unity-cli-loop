using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies async migration plan building honours cancellation while planning files.
    /// </summary>
    public sealed class ThirdPartyToolMigrationPlanBuilderTests
    {
        private const string LegacyAsmdefReferences = "\"references\": [ \"uLoopMCP.Editor\" ]";

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
        /// Verifies that cancelling after the first asmdef is planned returns an empty plan instead of a partial one.
        /// </summary>
        [Test]
        public void CreateAsync_WhenCancelledWhileProcessingAsmdefs_ReturnsEmptyPlan()
        {
            WriteAsmdef("VendorA", "{ \"name\": \"VendorA.Editor\", " + LegacyAsmdefReferences + " }");
            WriteAsmdef("VendorB", "{ \"name\": \"VendorB.Editor\", " + LegacyAsmdefReferences + " }");
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                // Without asmrefs, the first counted work item is the first asmdef of the final planning loop.
                CancelAtProgress progress = new CancelAtProgress(cancellation, 1, 4);

                Task<MigrationPlan> task = ThirdPartyToolMigrationPlanBuilder.CreateAsync(
                    _projectRoot,
                    progress,
                    cancellation.Token);

                Assert.That(task.IsCompleted, Is.True);
                MigrationPlan plan = task.GetAwaiter().GetResult();
                Assert.That(progress.HasCancelled, Is.True);
                Assert.That(plan.Changes, Is.Empty);
                Assert.That(plan.ReplacementCount, Is.EqualTo(0));
            }
        }

        /// <summary>
        /// Verifies that cancelling while the last C# file is planned returns an empty plan instead of its rewrite.
        /// </summary>
        [Test]
        public void CreateAsync_WhenCancelledAfterLastCSharpFile_ReturnsEmptyPlan()
        {
            string toolDirectory = Path.Combine(_projectRoot, "Assets", "VendorTools");
            Directory.CreateDirectory(toolDirectory);
            File.WriteAllText(
                Path.Combine(toolDirectory, "HelloTool.cs"),
                "using io.github.hatayama.uLoopMCP; [McpTool] public sealed class HelloTool {}");
            using (CancellationTokenSource cancellation = new CancellationTokenSource())
            {
                // One C# file is three work items; the third is reported by the C# planning loop.
                CancelAtProgress progress = new CancelAtProgress(cancellation, 3, 3);

                Task<MigrationPlan> task = ThirdPartyToolMigrationPlanBuilder.CreateAsync(
                    _projectRoot,
                    progress,
                    cancellation.Token);

                Assert.That(task.IsCompleted, Is.True);
                MigrationPlan plan = task.GetAwaiter().GetResult();
                Assert.That(progress.HasCancelled, Is.True);
                Assert.That(plan.Changes, Is.Empty);
                Assert.That(plan.ReplacementCount, Is.EqualTo(0));
            }
        }

        private void WriteAsmdef(string directoryName, string content)
        {
            string directory = Path.Combine(_projectRoot, "Assets", directoryName);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, directoryName + ".Editor.asmdef"), content);
        }

        private sealed class CancelAtProgress : IProgress<ThirdPartyToolMigrationProgress>
        {
            private readonly CancellationTokenSource _cancellation;
            private readonly int _processedItemCount;
            private readonly int _totalItemCount;

            public CancelAtProgress(CancellationTokenSource cancellation, int processedItemCount, int totalItemCount)
            {
                _cancellation = cancellation;
                _processedItemCount = processedItemCount;
                _totalItemCount = totalItemCount;
            }

            public bool HasCancelled { get; private set; }

            public void Report(ThirdPartyToolMigrationProgress value)
            {
                if (value.TotalItemCount != _totalItemCount || value.ProcessedItemCount != _processedItemCount)
                {
                    return;
                }

                HasCancelled = true;
                _cancellation.Cancel();
            }
        }
    }
}
