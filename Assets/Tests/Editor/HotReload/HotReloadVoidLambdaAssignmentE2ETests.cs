using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// End-to-end coverage for lambdas whose whole body assigns a private-setter or private static
    /// property: the edit is Patched, the delegate writes the edited value, and RevertAll restores
    /// the compiled behavior.
    /// </summary>
    public class HotReloadVoidLambdaAssignmentE2ETests
    {
        private const string FixtureFileName = "HotReloadVoidLambdaAssignmentFixtures.cs";

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
        /// What: an Action lambda that assigns a private-setter property is Patched, writes the
        /// edited value, and writes the compiled value again after RevertAll.
        /// </summary>
        [Test]
        public async Task Run_ActionLambdaAssigningPrivateSetterProperty_IsPatchedAndReverts()
        {
            HotReloadVoidLambdaAssignmentHost host = new HotReloadVoidLambdaAssignmentHost();
            Assert.That(host.AssignInActionLambda(), Is.EqualTo(5101));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync("5101", "5102");

            AssertPatched(result, "AssignInActionLambda");
            Assert.That(host.AssignInActionLambda(), Is.EqualTo(5102));
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            Assert.That(host.AssignInActionLambda(), Is.EqualTo(5101));
        }

        /// <summary>
        /// What: an Action lambda that compound-assigns a private-setter property is Patched, adds
        /// the edited amount, and adds the compiled amount again after RevertAll.
        /// </summary>
        [Test]
        public async Task Run_ActionLambdaCompoundAssigningPrivateSetterProperty_IsPatchedAndReverts()
        {
            HotReloadVoidLambdaAssignmentHost host = new HotReloadVoidLambdaAssignmentHost();
            Assert.That(host.CompoundAssignInActionLambda(), Is.EqualTo(5201));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync("5201", "5202");

            AssertPatched(result, "CompoundAssignInActionLambda");
            Assert.That(host.CompoundAssignInActionLambda(), Is.EqualTo(5202));
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            Assert.That(host.CompoundAssignInActionLambda(), Is.EqualTo(5201));
        }

        /// <summary>
        /// What: an Action lambda that assigns a private static property is Patched, writes the
        /// edited value, and writes the compiled value again after RevertAll.
        /// </summary>
        [Test]
        public async Task Run_ActionLambdaAssigningPrivateStaticProperty_IsPatchedAndReverts()
        {
            HotReloadVoidLambdaAssignmentHost host = new HotReloadVoidLambdaAssignmentHost();
            Assert.That(host.StaticAssignInActionLambda(), Is.EqualTo(5701));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync("5701", "5702");

            AssertPatched(result, "StaticAssignInActionLambda");
            Assert.That(host.StaticAssignInActionLambda(), Is.EqualTo(5702));
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            Assert.That(host.StaticAssignInActionLambda(), Is.EqualTo(5701));
        }

        /// <summary>
        /// What: an Action lambda that compound-assigns a private static property is Patched, adds
        /// the edited amount, and adds the compiled amount again after RevertAll.
        /// </summary>
        [Test]
        public async Task Run_ActionLambdaCompoundAssigningPrivateStaticProperty_IsPatchedAndReverts()
        {
            HotReloadVoidLambdaAssignmentHost host = new HotReloadVoidLambdaAssignmentHost();
            Assert.That(host.StaticCompoundAssignInActionLambda(), Is.EqualTo(5801));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync("5801", "5802");

            AssertPatched(result, "StaticCompoundAssignInActionLambda");
            Assert.That(host.StaticCompoundAssignInActionLambda(), Is.EqualTo(5802));
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            Assert.That(host.StaticCompoundAssignInActionLambda(), Is.EqualTo(5801));
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
            string editedPath = HotReloadTestSourceWriter.WriteEditedSource("VoidLambdaAssignmentE2E.cs", edited);
            return await HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);
        }

        private static void AssertPatched(HotReloadOrchestratorResult result, string methodName)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Method.Contains("." + methodName + "("))
                {
                    Assert.That(
                        outcome.Kind,
                        Is.EqualTo(HotReloadMethodOutcomeKind.Patched),
                        outcome.Method + ": " + outcome.Reason);
                    return;
                }
            }

            Assert.Fail("No row for " + methodName + ".");
        }
    }
}
