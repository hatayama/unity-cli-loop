using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// End-to-end EditMode coverage for edited bodies of compiled indexer accessors: the edited
    /// accessor runs its new body after the apply and its compiled body again after RevertAll, and
    /// accessors that were not edited keep their compiled body throughout.
    /// </summary>
    public class HotReloadIndexerAccessorE2ETests
    {
        private const string FixtureFileName = "HotReloadIndexerAccessorFixture.cs";

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
        /// What: an edited indexer getter returns the edited value after the apply and the compiled
        /// value after RevertAll.
        /// </summary>
        [Test]
        public async Task Run_EditedIndexerGetter_AppliesAndReverts()
        {
            HotReloadIndexerSingle target = new HotReloadIndexerSingle();
            Assert.That(target[5], Is.EqualTo(1105), "Precondition: compiled getter.");

            HotReloadOrchestratorResult result = await RunEditedAsync(
                "IndexerGetterE2E.cs",
                "Stored + i + 1100",
                "Stored + i + 1110");

            AssertPatched(result, "HotReloadIndexerSingle.get_Item(System.Int32)");
            Assert.That(target[5], Is.EqualTo(1115), "The patched getter must run.");

            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            Assert.That(target[5], Is.EqualTo(1105), "RevertAll must restore the compiled getter.");
        }

        /// <summary>
        /// What: an edited indexer setter stores the edited value after the apply and the compiled
        /// value after RevertAll.
        /// </summary>
        [Test]
        public async Task Run_EditedIndexerSetter_AppliesAndReverts()
        {
            HotReloadIndexerSingle target = new HotReloadIndexerSingle();
            target[2] = 3;
            Assert.That(target.Stored, Is.EqualTo(1155), "Precondition: compiled setter.");

            HotReloadOrchestratorResult result = await RunEditedAsync(
                "IndexerSetterE2E.cs",
                "value + i + 1150",
                "value + i + 1160");

            AssertPatched(result, "HotReloadIndexerSingle.set_Item(System.Int32,System.Int32)");
            target[2] = 3;
            Assert.That(target.Stored, Is.EqualTo(1165), "The patched setter must run.");

            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            target[2] = 3;
            Assert.That(target.Stored, Is.EqualTo(1155), "RevertAll must restore the compiled setter.");
        }

        /// <summary>
        /// What: with two indexer overloads, editing the string getter changes only that overload;
        /// the int overload keeps its compiled body, and RevertAll restores the string one.
        /// </summary>
        [Test]
        public async Task Run_EditedOverloadGetter_ChangesOnlyThatOverload()
        {
            HotReloadIndexerOverloads target = new HotReloadIndexerOverloads();
            Assert.That(target[4], Is.EqualTo(1204), "Precondition: compiled int overload.");
            Assert.That(target["abc"], Is.EqualTo(1253), "Precondition: compiled string overload.");

            HotReloadOrchestratorResult result = await RunEditedAsync(
                "IndexerOverloadE2E.cs",
                "s.Length + 1250",
                "s.Length + 1260");

            AssertPatched(result, "HotReloadIndexerOverloads.get_Item(System.String)");
            Assert.That(target["abc"], Is.EqualTo(1263), "The edited overload must run its new body.");
            Assert.That(target[4], Is.EqualTo(1204), "The other overload must keep its compiled body.");

            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            Assert.That(target["abc"], Is.EqualTo(1253), "RevertAll must restore the edited overload.");
            Assert.That(target[4], Is.EqualTo(1204));
        }

        /// <summary>
        /// What: an indexer renamed with [IndexerName] is patched under that name (get_Cell) and
        /// reverts.
        /// </summary>
        [Test]
        public async Task Run_EditedNamedIndexerGetter_AppliesAndReverts()
        {
            HotReloadIndexerNamed target = new HotReloadIndexerNamed();
            Assert.That(target[7], Is.EqualTo(1307), "Precondition: compiled getter.");

            HotReloadOrchestratorResult result = await RunEditedAsync(
                "IndexerNamedE2E.cs",
                "i + 1300",
                "i + 1310");

            AssertPatched(result, "HotReloadIndexerNamed.get_Cell(System.Int32)");
            Assert.That(target[7], Is.EqualTo(1317), "The patched getter must run.");

            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            Assert.That(target[7], Is.EqualTo(1307), "RevertAll must restore the compiled getter.");
        }

        private static async Task<HotReloadOrchestratorResult> RunEditedAsync(
            string editedFileName,
            string anchor,
            string replacement)
        {
            string fixturePath = Path.GetFullPath(
                Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", FixtureFileName));
            string onDisk = File.ReadAllText(fixturePath);
            Assert.That(
                onDisk.IndexOf(anchor, StringComparison.Ordinal),
                Is.EqualTo(onDisk.LastIndexOf(anchor, StringComparison.Ordinal)).And.GreaterThanOrEqualTo(0),
                "Precondition: the fixture must hold the anchor exactly once: " + anchor);
            string edited = onDisk.Replace(anchor, replacement, StringComparison.Ordinal);
            return await HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                new[] { fixturePath },
                contentPathOverride: null,
                CancellationToken.None,
                new Dictionary<string, string>
                {
                    [fixturePath] = HotReloadTestSourceWriter.WriteEditedSource(editedFileName, edited)
                });
        }

        // Exactly one Patched row, for the named accessor; no row is Failed or Skipped.
        private static void AssertPatched(HotReloadOrchestratorResult result, string methodFragment)
        {
            int patched = 0;
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                Assert.That(
                    outcome.Kind,
                    Is.EqualTo(HotReloadMethodOutcomeKind.Patched),
                    "Unexpected row.\n" + FormatOutcomes(result));
                if (outcome.Method.Contains(methodFragment))
                {
                    patched++;
                }
            }

            Assert.That(patched, Is.EqualTo(1), "Expected one Patched row for " + methodFragment + ".\n" + FormatOutcomes(result));
        }

        private static string FormatOutcomes(HotReloadOrchestratorResult result)
        {
            StringBuilder builder = new StringBuilder();
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                builder.Append(outcome.Kind).Append(' ').Append(outcome.Method)
                    .Append(" :: ").Append(outcome.Reason).Append('\n');
            }

            return builder.ToString();
        }
    }
}
