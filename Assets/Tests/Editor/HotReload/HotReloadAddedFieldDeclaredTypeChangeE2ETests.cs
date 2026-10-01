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
    /// End-to-end EditMode coverage for the warning a run gives when it re-declares an added field
    /// a previous reload added with another type: the store replaces a value the new type cannot
    /// hold, and nothing else in the response says the value is gone.
    /// </summary>
    public class HotReloadAddedFieldDeclaredTypeChangeE2ETests
    {
        private const string FixtureFileName = "HotReloadAddedFieldApplyFixture.cs";
        private const string ReadAddedOriginal =
            "        public int ReadAdded()\n        {\n            return 0;\n        }";
        private const string DeclaredTypeChangedToken = "with a different type";

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
        /// What: re-declaring an added field with another type warns and names the field, and the
        /// patched reader sees the new type's initial value rather than the old one.
        /// </summary>
        [Test]
        public async Task Run_AddedFieldDeclaredTypeChanged_WarnsNamingTheField()
        {
            await RunAsync(WithAddedMember("private int _addedCount = 3;", "return _addedCount;"));
            Assert.That(new HotReloadAddedFieldApplyFixture().ReadAdded(), Is.EqualTo(3));

            HotReloadOrchestratorResult second = await RunAsync(
                WithAddedMember("private string _addedCount = \"ab\";", "return _addedCount.Length;"));

            Assert.That(
                second.Warnings ?? new List<string>(),
                Has.Some.Contains(DeclaredTypeChangedToken)
                    .And.Some.Contains(typeof(HotReloadAddedFieldApplyFixture).FullName + "._addedCount"),
                FormatOutcomes(second));
        }

        /// <summary>
        /// What: a later run that keeps the added field's type and edits only the reader does not
        /// warn, so the warning is not given for every re-declaration.
        /// </summary>
        [Test]
        public async Task Run_AddedFieldDeclaredTypeKept_DoesNotWarn()
        {
            await RunAsync(WithAddedMember("private int _addedCount = 3;", "return _addedCount;"));

            HotReloadOrchestratorResult second = await RunAsync(
                WithAddedMember("private int _addedCount = 3;", "return _addedCount + 1;"));

            Assert.That(new HotReloadAddedFieldApplyFixture().ReadAdded(), Is.EqualTo(4), FormatOutcomes(second));
            Assert.That(
                second.Warnings ?? new List<string>(),
                Has.None.Contains(DeclaredTypeChangedToken),
                FormatOutcomes(second));
        }

        private static string WithAddedMember(string addedMember, string readAddedBody)
        {
            string onDisk = File.ReadAllText(FixturePath());
            Assert.That(onDisk, Does.Contain(ReadAddedOriginal), "Precondition: ReadAdded anchor must exist.");
            return onDisk.Replace(
                ReadAddedOriginal,
                "        " + addedMember + "\n\n"
                + "        public int ReadAdded()\n        {\n            " + readAddedBody + "\n        }");
        }

        private static Task<HotReloadOrchestratorResult> RunAsync(string edited)
        {
            return HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                new[] { FixturePath() },
                HotReloadTestSourceWriter.WriteEditedSource("AddedFieldDeclaredTypeChangeE2E.cs", edited),
                CancellationToken.None);
        }

        private static string FixturePath()
        {
            string path = Path.GetFullPath(
                Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", FixtureFileName));
            Assert.That(File.Exists(path), Is.True, "Fixture missing: " + path);
            return path;
        }

        private static string FormatOutcomes(HotReloadOrchestratorResult result)
        {
            List<string> lines = new List<string>();
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                lines.Add(outcome.Kind + " " + outcome.Method + " @" + outcome.FilePath + " :: " + outcome.Reason);
            }

            lines.AddRange(result.Warnings ?? new List<string>());
            return string.Join("\n", lines);
        }
    }
}
