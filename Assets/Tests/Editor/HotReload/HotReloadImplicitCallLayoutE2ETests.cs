using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// End-to-end coverage for calls the compiler generates into an internal method of a type in a
    /// file the run does not pass: a lambda making one is skipped and keeps its compiled body, the
    /// method's own statements are patched, and a construct that binds to nothing still fails.
    /// </summary>
    public class HotReloadImplicitCallLayoutE2ETests
    {
        private const string FixtureFileName = "HotReloadImplicitCallLayoutFixtures.cs";

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
        }

        /// <summary>
        /// What: a lambda whose foreach goes through an internal GetEnumerator of a type in a file
        /// the run does not pass is Skipped, no row fails, and the method keeps its compiled value.
        /// </summary>
        [Test]
        public async Task Run_ClosureLoopingThroughUnpassedGetEnumerator_IsSkippedAndKeepsTheCompiledBody()
        {
            HotReloadImplicitCallLayoutHost host = new HotReloadImplicitCallLayoutHost();
            Assert.That(host.ClosureLoopsThroughUnpassedGetEnumerator(), Is.EqualTo(8117));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync("8111", "8112");

            AssertKind(result, "ClosureLoopsThroughUnpassedGetEnumerator", HotReloadMethodOutcomeKind.Skipped);
            AssertNoFailedRow(result);
            Assert.That(host.ClosureLoopsThroughUnpassedGetEnumerator(), Is.EqualTo(8117));
        }

        /// <summary>
        /// What: a method whose own statements loop through an internal GetEnumerator of a type in
        /// a file the run does not pass is Patched and returns the edited value; RevertAll restores
        /// the compiled value.
        /// </summary>
        [Test]
        public async Task Run_OwnStatementsLoopingThroughUnpassedGetEnumerator_IsPatchedAndReverts()
        {
            HotReloadImplicitCallLayoutHost host = new HotReloadImplicitCallLayoutHost();
            Assert.That(host.OwnStatementsLoopThroughUnpassedGetEnumerator(), Is.EqualTo(8327));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync("8321", "8322");

            AssertKind(result, "OwnStatementsLoopThroughUnpassedGetEnumerator", HotReloadMethodOutcomeKind.Patched);
            Assert.That(host.OwnStatementsLoopThroughUnpassedGetEnumerator(), Is.EqualTo(8328));
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            Assert.That(host.OwnStatementsLoopThroughUnpassedGetEnumerator(), Is.EqualTo(8327));
        }

        /// <summary>
        /// What: a lambda whose foreach runs over an int, which has no GetEnumerator anywhere, still
        /// Fails with the compiler's message instead of being skipped as an internal call.
        /// </summary>
        [Test]
        public async Task Run_ClosureLoopingOverAnInt_StillFails()
        {
            HotReloadOrchestratorResult result = await RunEditedFixtureAsync(
                "foreach (int value in new HotReloadUnpassedRange())\n                {\n                    sum += value;\n                }\n\n                return sum;\n            };",
                "foreach (int value in 5)\n                {\n                    sum += value;\n                }\n\n                return sum;\n            };");

            AssertKind(result, "ClosureLoopsThroughUnpassedGetEnumerator", HotReloadMethodOutcomeKind.Failed);
        }

        private static async Task<HotReloadOrchestratorResult> RunEditedFixtureAsync(
            string original,
            string replacement)
        {
            string fixturePath = Path.GetFullPath(
                Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", FixtureFileName));
            string onDisk = File.ReadAllText(fixturePath);
            int first = onDisk.IndexOf(original, System.StringComparison.Ordinal);
            Assert.That(first, Is.GreaterThanOrEqualTo(0), "Precondition: the anchor must be in the fixture.");
            string edited = onDisk.Substring(0, first) + replacement + onDisk.Substring(first + original.Length);
            string editedPath = HotReloadTestSourceWriter.WriteEditedSource("ImplicitCallLayoutE2E.cs", edited);
            return await HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);
        }

        private static void AssertKind(
            HotReloadOrchestratorResult result,
            string methodName,
            HotReloadMethodOutcomeKind expectedKind)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Method.Contains("." + methodName + "("))
                {
                    Assert.That(outcome.Kind, Is.EqualTo(expectedKind), outcome.Method + ": " + outcome.Reason);
                    return;
                }
            }

            Assert.Fail("No row for " + methodName + ".");
        }

        private static void AssertNoFailedRow(HotReloadOrchestratorResult result)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                Assert.That(
                    outcome.Kind,
                    Is.Not.EqualTo(HotReloadMethodOutcomeKind.Failed),
                    outcome.Method + ": " + outcome.Reason);
            }
        }
    }
}
