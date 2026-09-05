using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// End-to-end EditMode coverage for reloading files whose types live in the global namespace,
    /// where the shim assembly has to host them under a synthesized namespace.
    /// </summary>
    public class HotReloadGlobalNamespaceShimE2ETests
    {
        private const string HostFileName = "HotReloadGlobalNamespaceHost.cs";
        private const string CallerFileName = "HotReloadGlobalNamespaceCaller.cs";
        private const string HostValueAnchor = "    public int Value()";
        private const string CallerCallBodyAnchor = "return host.Value();";

        [SetUp]
        public void SetUp()
        {
            HotReloadPatcher.RevertAll();
            HotReloadAutoRefreshHold.Sync(HotReloadPatcher.ActiveChangeCount);
        }

        [TearDown]
        public void TearDown()
        {
            HotReloadPatcher.RevertAll();
            HotReloadAutoRefreshHold.Sync(HotReloadPatcher.ActiveChangeCount);
            VibeLogger.ClearMemoryLogs();
        }

        /// <summary>
        /// What: a body edited in a global-namespace file binds to a method added in another
        /// global-namespace file of the same reload, both files are applied, and the patched
        /// caller returns the value the added method produces.
        /// </summary>
        [Test]
        public async Task Run_GlobalNamespaceCallerUsesMethodAddedInOtherFile_AppliesBothAndUpdatesRuntime()
        {
            string hostPath = FixturePath(HostFileName);
            string callerPath = FixturePath(CallerFileName);
            string editedHost = InsertHostMember(
                "    public int Added()\n    {\n        return 41;\n    }\n\n");
            string editedCaller = ReplaceInSource(
                ReadFixture(CallerFileName),
                CallerCallBodyAnchor,
                "return host.Added() + 1;");

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { hostPath, callerPath },
                contentPathOverride: null,
                CancellationToken.None,
                new Dictionary<string, string>
                {
                    [hostPath] = HotReloadTestSourceWriter.WriteEditedSource(
                        "GlobalNamespaceShimHost.cs",
                        editedHost),
                    [callerPath] = HotReloadTestSourceWriter.WriteEditedSource(
                        "GlobalNamespaceShimCaller.cs",
                        editedCaller)
                });

            AssertNoFailure(result);
            FindOutcome(result, HotReloadMethodOutcomeKind.Added, "Added");
            FindOutcome(result, HotReloadMethodOutcomeKind.Patched, "Call");
            Assert.That(
                new HotReloadGlobalNamespaceCaller().Call(new HotReloadGlobalNamespaceHost()),
                Is.EqualTo(42));
        }

        private static string InsertHostMember(string memberText)
        {
            string source = ReadFixture(HostFileName);
            Assert.That(source, Does.Contain(HostValueAnchor), "Precondition: host anchor must exist.");
            return source.Replace(HostValueAnchor, memberText + HostValueAnchor, StringComparison.Ordinal);
        }

        private static string ReplaceInSource(string source, string anchor, string replacement)
        {
            Assert.That(source, Does.Contain(anchor), "Precondition: anchor must exist: " + anchor);
            return source.Replace(anchor, replacement, StringComparison.Ordinal);
        }

        private static string ReadFixture(string fileName)
        {
            return File.ReadAllText(FixturePath(fileName));
        }

        private static string FixturePath(string fileName)
        {
            string path = Path.GetFullPath(
                Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", fileName));
            Assert.That(File.Exists(path), Is.True, "Fixture missing: " + path);
            return path;
        }

        private static void AssertNoFailure(HotReloadOrchestratorResult result)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                Assert.That(
                    outcome.Kind,
                    Is.Not.EqualTo(HotReloadMethodOutcomeKind.Failed),
                    "Unexpected failure.\n" + FormatOutcomes(result));
            }
        }

        private static HotReloadMethodOutcome FindOutcome(
            HotReloadOrchestratorResult result,
            HotReloadMethodOutcomeKind kind,
            string methodNamePart)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == kind && outcome.Method != null && outcome.Method.Contains(methodNamePart))
                {
                    return outcome;
                }
            }

            Assert.Fail("Expected " + kind + " for " + methodNamePart + ".\n" + FormatOutcomes(result));
            return null;
        }

        private static string FormatOutcomes(HotReloadOrchestratorResult result)
        {
            List<string> lines = new List<string>();
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                lines.Add(outcome.Kind + " " + outcome.Method + " @" + outcome.FilePath + " :: " + outcome.Reason);
            }

            return string.Join("\n", lines);
        }
    }
}
