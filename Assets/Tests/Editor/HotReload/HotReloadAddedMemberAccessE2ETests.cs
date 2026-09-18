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
    /// End-to-end EditMode coverage for added private members reached through shapes the
    /// compiled-member accessor delegates cannot express: a static property read and ref/out
    /// arguments. The reload must emit them and the patched runtime must return their values.
    /// </summary>
    public class HotReloadAddedMemberAccessE2ETests
    {
        private const string HostFileName = "HotReloadCrossFileAddedMemberHost.cs";
        private const string HostScaledBodyAnchor = "            return factor;\n";
        private const string HostValueAnchor = "        public int Value()";

        private HotReloadDomainTestScope _scope;

        [SetUp]
        public void SetUp()
        {
            _scope = new HotReloadDomainTestScope();
            HotReloadAutoRefreshHold.SyncToActiveChanges();
        }

        [TearDown]
        public void TearDown()
        {
            _scope.Dispose();
            HotReloadAutoRefreshHold.SyncToActiveChanges();
            VibeLogger.ClearMemoryLogs();
        }

        /// <summary>
        /// What: a compiled method whose new body calls added private methods that pass ref and
        /// out arguments and read an added private static property is patched, every added
        /// method is added rather than skipped, and the call returns the value those members
        /// compute.
        /// </summary>
        [Test]
        public async Task Run_AddedMethodsUsingRefOutAndAddedStaticProperty_ReturnTheirValue()
        {
            string source = ReadFixture(HostFileName);
            source = ReplaceInSource(
                source,
                HostScaledBodyAnchor,
                "            return AddedCompute(factor);\n");
            source = ReplaceInSource(
                source,
                HostValueAnchor,
                "        private static int AddedSeed\n        {\n            get { return 5; }\n        }\n\n"
                + "        private int AddedCompute(int factor)\n        {\n"
                + "            int value = factor;\n"
                + "            AddedBump(ref value);\n"
                + "            return AddedTryDouble(value, out int doubled) ? doubled : -1;\n        }\n\n"
                + "        private bool AddedTryDouble(int input, out int output)\n        {\n"
                + "            output = input * 2 + AddedSeed;\n"
                + "            return true;\n        }\n\n"
                + "        private void AddedBump(ref int value)\n        {\n"
                + "            value += 100;\n        }\n\n"
                + HostValueAnchor);

            string hostPath = FixturePath(HostFileName);
            HotReloadOrchestratorResult result = await HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                new[] { hostPath },
                contentPathOverride: null,
                CancellationToken.None,
                new Dictionary<string, string>
                {
                    [hostPath] = HotReloadTestSourceWriter.WriteEditedSource(
                        "AddedMemberAccessHost.cs",
                        source)
                });

            AssertNoKind(result, HotReloadMethodOutcomeKind.Failed);
            AssertNoKind(result, HotReloadMethodOutcomeKind.Skipped);
            AssertKind(result, HotReloadMethodOutcomeKind.Patched, "Scaled");
            AssertKind(result, HotReloadMethodOutcomeKind.Added, "AddedCompute");
            AssertKind(result, HotReloadMethodOutcomeKind.Added, "AddedTryDouble");
            AssertKind(result, HotReloadMethodOutcomeKind.Added, "AddedBump");
            // (3 + 100) * 2 + 5: the ref write, the out value and the static property all reach
            // the result, so a shim that dropped any of them returns something else.
            Assert.That(new HotReloadCrossFileAddedMemberHost().Scaled(3), Is.EqualTo(211), FormatOutcomes(result));
        }

        private static void AssertNoKind(HotReloadOrchestratorResult result, HotReloadMethodOutcomeKind kind)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                Assert.That(outcome.Kind, Is.Not.EqualTo(kind), "Unexpected " + kind + ".\n" + FormatOutcomes(result));
            }
        }

        private static void AssertKind(
            HotReloadOrchestratorResult result,
            HotReloadMethodOutcomeKind kind,
            string methodNamePart)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == kind && outcome.Method != null && outcome.Method.Contains(methodNamePart))
                {
                    return;
                }
            }

            Assert.Fail("Expected " + kind + " for " + methodNamePart + ".\n" + FormatOutcomes(result));
        }

        private static string FormatOutcomes(HotReloadOrchestratorResult result)
        {
            List<string> lines = new List<string>();
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                lines.Add(outcome.Kind + " " + outcome.Method + " :: " + outcome.Reason);
            }

            return string.Join("\n", lines);
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
    }
}
