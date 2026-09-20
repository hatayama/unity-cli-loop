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
    /// End-to-end EditMode coverage for the coordination point the sibling tools read the active
    /// added members through: a reload that adds a method and a field has to make both names
    /// readable there, because a tool outside this assembly has no other way to see them.
    /// </summary>
    public class HotReloadAddedMemberCoordinationE2ETests
    {
        private const string HostFileName = "HotReloadAddedMemberHost.cs";

        private const string HostExistingValueAnchor =
            "        public int ExistingValue()\n        {\n            return 1;\n        }\n";

        private const string HostExistingValueReplacement =
            "        private int _addedBadgeSeed = 4;\n\n"
            + "        private int AddedBadgeCount()\n        {\n"
            + "            return _addedBadgeSeed;\n        }\n\n"
            + "        public int ExistingValue()\n        {\n"
            + "            return AddedBadgeCount();\n        }\n";

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
        /// What: the coordination point answers with no added member before a reload, and after a
        /// reload that adds a method and a field it answers with the bare name of each, which is the
        /// spelling a compiler diagnostic quotes.
        /// </summary>
        [Test]
        public async Task DescribeActiveAddedMemberNames_AfterAReloadThatAddsMembers_HoldsTheirBareNames()
        {
            Assert.That(
                HotReloadAddedMemberCoordination.DescribeActiveAddedMemberNames,
                Is.Not.Null,
                "Precondition: installing the services wires the coordination point.");
            Assert.That(
                HotReloadAddedMemberCoordination.DescribeActiveAddedMemberNames(),
                Does.Not.Contain("AddedBadgeCount"),
                "Precondition: the scope starts with no added members.");

            string source = ReadFixture(HostFileName);
            Assert.That(
                source,
                Does.Contain(HostExistingValueAnchor),
                "Precondition: anchor must exist: " + HostExistingValueAnchor);
            source = source.Replace(
                HostExistingValueAnchor,
                HostExistingValueReplacement,
                StringComparison.Ordinal);

            string hostPath = FixturePath(HostFileName);
            HotReloadOrchestratorResult result = await HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                new[] { hostPath },
                contentPathOverride: null,
                CancellationToken.None,
                new Dictionary<string, string>
                {
                    [hostPath] = HotReloadTestSourceWriter.WriteEditedSource(
                        "AddedMemberCoordinationHost.cs",
                        source)
                });

            AssertKind(result, HotReloadMethodOutcomeKind.Added, "AddedBadgeCount");
            IReadOnlyList<string> names = HotReloadAddedMemberCoordination.DescribeActiveAddedMemberNames();
            Assert.That(names, Contains.Item("AddedBadgeCount"), FormatOutcomes(result));
            Assert.That(names, Contains.Item("_addedBadgeSeed"), FormatOutcomes(result));
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
