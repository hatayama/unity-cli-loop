using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// End-to-end pins for private field writes inside lambdas that the accessor rewrite handles
    /// through field references: each edit is Patched, runs the edited value, and RevertAll
    /// restores the compiled value.
    /// </summary>
    public class HotReloadClosureFieldWriteE2ETests
    {
        private const string FixtureFileName = "HotReloadClosureFieldWriteFixtures.cs";

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
        /// What: a lambda that assigns a private static field is Patched and writes the edited value.
        /// </summary>
        [Test]
        public async Task Run_LambdaAssigningPrivateStaticField_IsPatchedAndReverts()
        {
            HotReloadClosureFieldWriteHost host = new HotReloadClosureFieldWriteHost();
            await AssertPatchedAndRevertsAsync("StaticFieldAssignInLambda", host.StaticFieldAssignInLambda, "7101", "7102", 7101, 7102);
        }

        /// <summary>
        /// What: a lambda that compound-assigns a private static field is Patched and adds the
        /// edited amount.
        /// </summary>
        [Test]
        public async Task Run_LambdaCompoundAssigningPrivateStaticField_IsPatchedAndReverts()
        {
            HotReloadClosureFieldWriteHost host = new HotReloadClosureFieldWriteHost();
            await AssertPatchedAndRevertsAsync("StaticFieldCompoundAssignInLambda", host.StaticFieldCompoundAssignInLambda, "7201", "7202", 7201, 7202);
        }

        /// <summary>
        /// What: a lambda that increments a private static field is Patched and still increments
        /// it after the edit.
        /// </summary>
        [Test]
        public async Task Run_LambdaIncrementingPrivateStaticField_IsPatchedAndReverts()
        {
            HotReloadClosureFieldWriteHost host = new HotReloadClosureFieldWriteHost();
            await AssertPatchedAndRevertsAsync("StaticFieldIncrementInLambda", host.StaticFieldIncrementInLambda, "7301", "7302", 7302, 7303);
        }

        /// <summary>
        /// What: a lambda that applies ??= to a private field is Patched and stores the edited value.
        /// </summary>
        [Test]
        public async Task Run_LambdaCoalesceAssigningPrivateField_IsPatchedAndReverts()
        {
            HotReloadClosureFieldWriteHost host = new HotReloadClosureFieldWriteHost();
            await AssertPatchedAndRevertsAsync("FieldCoalesceAssignInLambda", host.FieldCoalesceAssignInLambda, "7401", "7402", 7401, 7402);
        }

        /// <summary>
        /// What: a Func lambda that returns the value of an assignment to a private field is
        /// Patched, returns the edited value, and stores it in the field.
        /// </summary>
        [Test]
        public async Task Run_LambdaReturningAssignmentToPrivateField_IsPatchedAndReverts()
        {
            HotReloadClosureFieldWriteHost host = new HotReloadClosureFieldWriteHost();
            await AssertPatchedAndRevertsAsync("FieldAssignmentValueUsedInLambda", host.FieldAssignmentValueUsedInLambda, "7501", "7502", 75017501, 75027502);
        }

        /// <summary>
        /// What: a lambda that deconstructs into two private fields is Patched and stores the
        /// edited element.
        /// </summary>
        [Test]
        public async Task Run_LambdaDeconstructingIntoPrivateFields_IsPatchedAndReverts()
        {
            HotReloadClosureFieldWriteHost host = new HotReloadClosureFieldWriteHost();
            await AssertPatchedAndRevertsAsync("FieldDeconstructionInLambda", host.FieldDeconstructionInLambda, "7601", "7602", 760117, 760217);
        }

        /// <summary>
        /// What: a lambda that compound-assigns a private property through a private field receiver
        /// is Patched and adds the edited amount.
        /// </summary>
        [Test]
        public async Task Run_LambdaCompoundAssigningThroughPrivateFieldReceiver_IsPatchedAndReverts()
        {
            HotReloadClosureFieldWriteHost host = new HotReloadClosureFieldWriteHost();
            await AssertPatchedAndRevertsAsync("CompoundAssignThroughFieldReceiverInLambda", host.CompoundAssignThroughFieldReceiverInLambda, "7701", "7702", 7701, 7702);
        }

        /// <summary>
        /// What: a lambda that writes an element of a private array field is Patched and stores the
        /// edited value.
        /// </summary>
        [Test]
        public async Task Run_LambdaWritingPrivateArrayFieldElement_IsPatchedAndReverts()
        {
            HotReloadClosureFieldWriteHost host = new HotReloadClosureFieldWriteHost();
            await AssertPatchedAndRevertsAsync("ArrayFieldElementWriteInLambda", host.ArrayFieldElementWriteInLambda, "7801", "7802", 7801, 7802);
        }

        private static async Task AssertPatchedAndRevertsAsync(
            string methodName,
            Func<int> invoke,
            string originalToken,
            string editedToken,
            int compiledValue,
            int editedValue)
        {
            Assert.That(invoke(), Is.EqualTo(compiledValue), "Before apply.");

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync(originalToken, editedToken);

            AssertPatched(result, methodName);
            Assert.That(invoke(), Is.EqualTo(editedValue), "After apply.");
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            Assert.That(invoke(), Is.EqualTo(compiledValue), "After RevertAll.");
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
            string editedPath = HotReloadTestSourceWriter.WriteEditedSource("ClosureFieldWriteE2E.cs", edited);
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
