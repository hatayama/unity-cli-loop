using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityCliLoop.CodeComplexity;

namespace UnityCliLoop.CodeComplexity.Tests
{
    [TestFixture]
    public sealed class ConfigureAwaitGuardTests
    {
        // Verifies Task-like awaits without ConfigureAwait(false) are reported while custom
        // awaitables (intentional main-thread hops) are excluded by return type.
        [Test]
        public void FindMissingConfigureAwait_WhenSampleHasTaskAwaitWithoutConfigureAwait_ReportsViolation()
        {
            string rootPath = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                $"configure-await-{Guid.NewGuid():N}");
            Directory.CreateDirectory(rootPath);
            try
            {
                string toolsDir = Path.Combine(rootPath, "Packages", "src", "Editor", "FirstPartyTools", "Sample");
                string contractsDir = Path.Combine(rootPath, "Packages", "src", "Editor", "ToolContracts");
                Directory.CreateDirectory(toolsDir);
                Directory.CreateDirectory(contractsDir);
                File.WriteAllText(
                    Path.Combine(contractsDir, "MainThreadSwitcher.cs"),
                    """
                    using System;
                    using System.Runtime.CompilerServices;
                    using System.Threading;
                    namespace io.github.hatayama.UnityCliLoop.ToolContracts
                    {
                        public static class MainThreadSwitcher
                        {
                            public static SwitchToMainThreadAwaitable SwitchToMainThread(CancellationToken ct = default)
                                => new SwitchToMainThreadAwaitable(ct);
                        }
                        public struct SwitchToMainThreadAwaitable
                        {
                            public SwitchToMainThreadAwaitable(CancellationToken cancellationToken) { }
                            public Awaiter GetAwaiter() => new Awaiter();
                            public struct Awaiter : INotifyCompletion
                            {
                                public bool IsCompleted => true;
                                public void GetResult() { }
                                public void OnCompleted(Action continuation) => continuation();
                            }
                        }
                    }
                    """);
                File.WriteAllText(
                    Path.Combine(toolsDir, "SampleTool.cs"),
                    """
                    using System.Threading.Tasks;
                    using io.github.hatayama.UnityCliLoop.ToolContracts;
                    namespace Sample
                    {
                        public static class SampleTool
                        {
                            public static async Task RunAsync()
                            {
                                await Task.Delay(1);
                                await MainThreadSwitcher.SwitchToMainThread();
                                await Task.CompletedTask;
                            }
                        }
                    }
                    """);

                ConfigureAwaitGuard guard = new();
                IReadOnlyList<ConfigureAwaitViolation> violations = guard.FindMissingConfigureAwait(rootPath);

                Assert.That(violations.Count, Is.EqualTo(2));
                Assert.That(violations.All(violation =>
                    violation.AwaitExpression.Contains("Task.Delay", StringComparison.Ordinal)
                    || violation.AwaitExpression.Contains("Task.CompletedTask", StringComparison.Ordinal)), Is.True);
                Assert.That(violations.Any(violation =>
                    violation.AwaitExpression.Contains("SwitchToMainThread", StringComparison.Ordinal)), Is.False);
            }
            finally
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }

        // Verifies FirstPartyTools Task-like awaits all use ConfigureAwait(false) so paused
        // Play Mode cannot hang tool continuations captured on Unity's SynchronizationContext.
        [Test]
        public void FindMissingConfigureAwait_ForRepositoryFirstPartyTools_HasNoViolations()
        {
            string repositoryRoot = FindRepositoryRoot();
            ConfigureAwaitGuard guard = new();
            IReadOnlyList<ConfigureAwaitViolation> violations = guard.FindMissingConfigureAwait(repositoryRoot);

            if (violations.Count == 0)
            {
                return;
            }

            StringBuilder message = new();
            message.AppendLine($"Found {violations.Count} Task-like await(s) missing ConfigureAwait(false):");
            foreach (ConfigureAwaitViolation violation in violations.Take(40))
            {
                message.AppendLine($"{violation.FilePath}:{violation.Line}: {violation.AwaitExpression}");
            }

            Assert.Fail(message.ToString());
        }

        private static string FindRepositoryRoot()
        {
            string? directory = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrEmpty(directory))
            {
                if (File.Exists(Path.Combine(directory, "Packages", "src", "package.json"))
                    || File.Exists(Path.Combine(directory, "Packages", "manifest.json")))
                {
                    return directory;
                }

                directory = Directory.GetParent(directory)?.FullName;
            }

            Assert.Fail("Failed to locate repository root from test directory.");
            return string.Empty;
        }
    }
}
