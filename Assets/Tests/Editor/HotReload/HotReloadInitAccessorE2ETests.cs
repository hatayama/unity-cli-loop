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
    /// End-to-end EditMode coverage for edited bodies of compiled init accessors: an object
    /// initializer runs the edited body after the apply and the compiled body after RevertAll, and an
    /// init body that assigns a readonly field stays Skipped and keeps running its compiled body.
    /// </summary>
    public class HotReloadInitAccessorE2ETests
    {
        private const string FixtureFileName = "HotReloadInitAccessorFixture.cs";

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
        /// What: an edited class init accessor runs through an object initializer after the apply and
        /// runs its compiled body again after RevertAll.
        /// </summary>
        [Test]
        public async Task Run_EditedClassInitAccessor_AppliesThroughObjectInitializerAndReverts()
        {
            Assert.That(new HotReloadInitClass { X = 5 }.X, Is.EqualTo(1705), "Precondition: compiled init.");

            HotReloadOrchestratorResult result = await RunEditedAsync("ClassInitE2E.cs", "value + 1700", "value + 1710");

            AssertOutcome(result, HotReloadMethodOutcomeKind.Patched, "HotReloadInitClass.set_X(System.Int32)");
            Assert.That(new HotReloadInitClass { X = 5 }.X, Is.EqualTo(1715), "The patched init must run.");

            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            Assert.That(new HotReloadInitClass { X = 5 }.X, Is.EqualTo(1705), "RevertAll must restore the compiled init.");
        }

        /// <summary>
        /// What: an edited record init accessor runs through an object initializer and a with
        /// expression after the apply, and runs its compiled body again after RevertAll.
        /// </summary>
        [Test]
        public async Task Run_EditedRecordInitAccessor_AppliesThroughInitializerAndWithAndReverts()
        {
            HotReloadInitRecord original = new HotReloadInitRecord(1) { B = 5 };
            Assert.That(original.B, Is.EqualTo(1805), "Precondition: compiled init.");

            HotReloadOrchestratorResult result = await RunEditedAsync("RecordInitE2E.cs", "value + 1800", "value + 1810");

            AssertOutcome(result, HotReloadMethodOutcomeKind.Patched, "HotReloadInitRecord.set_B(System.Int32)");
            Assert.That(new HotReloadInitRecord(1) { B = 5 }.B, Is.EqualTo(1815), "The patched init must run.");
            Assert.That((original with { B = 6 }).B, Is.EqualTo(1816), "A with expression runs the patched init too.");

            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            Assert.That(new HotReloadInitRecord(1) { B = 5 }.B, Is.EqualTo(1805), "RevertAll must restore the compiled init.");
        }

        /// <summary>
        /// What: an edited init body that assigns a readonly field is Skipped with the init-only
        /// member reason, no row is Failed, and the object initializer keeps running the compiled body.
        /// </summary>
        [Test]
        public async Task Run_EditedInitAssigningReadonlyField_IsSkippedAndKeepsTheCompiledBody()
        {
            Assert.That(new HotReloadInitWrites { AssignReadonly = 5 }.LockedValue, Is.EqualTo(2105), "Precondition: compiled init.");

            HotReloadOrchestratorResult result = await RunEditedAsync("RefusedInitE2E.cs", "value + 2100", "value + 2110");

            AssertOutcome(result, HotReloadMethodOutcomeKind.Skipped, "HotReloadInitWrites.set_AssignReadonly(System.Int32)");
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                Assert.That(outcome.Kind, Is.Not.EqualTo(HotReloadMethodOutcomeKind.Failed), FormatOutcomes(result));
            }

            Assert.That(
                FindOutcome(result, "HotReloadInitWrites.set_AssignReadonly(").Reason,
                Does.Contain("Init accessors that assign a readonly field or an init-only property are skipped"));
            Assert.That(new HotReloadInitWrites { AssignReadonly = 5 }.LockedValue, Is.EqualTo(2105), "The skipped init keeps its compiled body.");
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

        private static void AssertOutcome(
            HotReloadOrchestratorResult result,
            HotReloadMethodOutcomeKind kind,
            string methodFragment)
        {
            HotReloadMethodOutcome outcome = FindOutcome(result, methodFragment);
            Assert.That(outcome?.Kind, Is.EqualTo(kind), "Expected " + kind + " for " + methodFragment + ".\n" + FormatOutcomes(result));
        }

        private static HotReloadMethodOutcome FindOutcome(HotReloadOrchestratorResult result, string methodFragment)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Method != null && outcome.Method.Contains(methodFragment))
                {
                    return outcome;
                }
            }

            return null;
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
