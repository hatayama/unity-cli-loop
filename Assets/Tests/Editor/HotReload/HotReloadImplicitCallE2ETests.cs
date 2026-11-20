using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// End-to-end coverage for lambdas that reach an internal method through a call the compiler
    /// generates: the method is skipped and keeps running its compiled body instead of throwing
    /// MethodAccessException from the shim.
    /// </summary>
    public class HotReloadImplicitCallE2ETests
    {
        private const string FixtureFileName = "HotReloadImplicitCallFixtures.cs";

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
        /// What: a lambda whose foreach goes through an internal extension GetEnumerator is
        /// Skipped, the run reports no failure, and the method keeps returning the compiled value.
        /// </summary>
        [Test]
        public async Task Run_ClosureLoopingThroughInternalGetEnumerator_IsSkippedAndKeepsTheCompiledBody()
        {
            HotReloadImplicitCallHost host = new HotReloadImplicitCallHost();
            Assert.That(host.ClosureLoopsThroughInternalGetEnumerator(), Is.EqualTo(6207));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync("6201", "6202");

            AssertKind(result, "ClosureLoopsThroughInternalGetEnumerator", HotReloadMethodOutcomeKind.Skipped);
            AssertNoFailedRow(result);
            Assert.That(host.ClosureLoopsThroughInternalGetEnumerator(), Is.EqualTo(6207));
        }

        /// <summary>
        /// What: a lambda whose deconstruction goes through an internal instance Deconstruct is
        /// Skipped, the run reports no failure, and the method keeps returning the compiled value.
        /// </summary>
        [Test]
        public async Task Run_ClosureDeconstructingThroughInternalInstanceMethod_IsSkippedAndKeepsTheCompiledBody()
        {
            HotReloadImplicitCallHost host = new HotReloadImplicitCallHost();
            Assert.That(host.ClosureDeconstructsThroughInternalInstanceMethod(), Is.EqualTo(6401));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync("6311", "6312");

            AssertKind(result, "ClosureDeconstructsThroughInternalInstanceMethod", HotReloadMethodOutcomeKind.Skipped);
            AssertNoFailedRow(result);
            Assert.That(host.ClosureDeconstructsThroughInternalInstanceMethod(), Is.EqualTo(6401));
        }

        private static async Task<HotReloadOrchestratorResult> RunEditedFixtureAsync(
            string originalToken,
            string editedToken)
        {
            string fixturePath = Path.GetFullPath(
                Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", FixtureFileName));
            string onDisk = File.ReadAllText(fixturePath);
            string edited = onDisk.Replace(originalToken, editedToken);
            Assert.That(edited, Is.Not.EqualTo(onDisk), "Precondition: the token must be in the fixture.");
            string editedPath = HotReloadTestSourceWriter.WriteEditedSource("ImplicitCallE2E.cs", edited);
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
