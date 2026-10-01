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
    /// End-to-end EditMode coverage for an added field whose initializer is a lambda, which the
    /// store runs on first access like any other initializer it can emit.
    /// </summary>
    public class HotReloadAddedFieldLambdaInitializerE2ETests
    {
        private const string FixtureFileName = "HotReloadAddedFieldApplyFixture.cs";
        private const string ReadAddedOriginal =
            "        public int ReadAdded()\n        {\n            return 0;\n        }";

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
        /// What: an added field initialized with a lambda that names only its own parameter
        /// applies, and the patched reader calls the stored delegate.
        /// </summary>
        [Test]
        public async Task Run_AddedFieldWithLambdaInitializer_PatchedReaderCallsIt()
        {
            string onDisk = File.ReadAllText(FixturePath());
            Assert.That(onDisk, Does.Contain(ReadAddedOriginal), "Precondition: ReadAdded anchor must exist.");
            string edited = onDisk.Replace(
                ReadAddedOriginal,
                "        private System.Func<int, int> _addedStep = v => v + 1;\n\n"
                + "        public int ReadAdded()\n        {\n            return _addedStep(4);\n        }");

            HotReloadOrchestratorResult result = await HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                new[] { FixturePath() },
                HotReloadTestSourceWriter.WriteEditedSource("AddedFieldLambdaInitializerE2E.cs", edited),
                CancellationToken.None);

            Assert.That(new HotReloadAddedFieldApplyFixture().ReadAdded(), Is.EqualTo(5), FormatOutcomes(result));
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
