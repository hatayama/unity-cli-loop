using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// End-to-end coverage for edited bodies that call extension methods: an inaccessible one
    /// reached from a lambda is skipped and keeps running the compiled body, and accessible
    /// calls are patched and run the edited body.
    /// </summary>
    public class HotReloadExtensionCallE2ETests
    {
        private const string FixtureFileName = "HotReloadExtensionCallFixtures.cs";

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
        /// What: a lambda that calls an internal extension method in reduced form is Skipped, the
        /// run reports no failure, and the method keeps returning the compiled value without
        /// throwing.
        /// </summary>
        [Test]
        public async Task Run_ClosureCallingInternalExtension_IsSkippedAndKeepsTheCompiledBody()
        {
            HotReloadExtensionCallHost host = new HotReloadExtensionCallHost();
            Assert.That(host.ClosureReadsPrivateFieldAndCallsInternalExtension(), Is.EqualTo(4107));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync("4101", "4102");

            AssertKind(result, "ClosureReadsPrivateFieldAndCallsInternalExtension", HotReloadMethodOutcomeKind.Skipped);
            AssertNoFailedRow(result);
            Assert.That(host.ClosureReadsPrivateFieldAndCallsInternalExtension(), Is.EqualTo(4107));
        }

        /// <summary>
        /// What: a lambda that calls a public extension method of an internal class declared in
        /// the same file is Skipped, the run reports no failure, and the method keeps returning
        /// the compiled value instead of throwing MethodAccessException from the shim.
        /// </summary>
        [Test]
        public async Task Run_ClosureCallingPublicExtensionOfInternalClassInEditedFile_IsSkippedAndKeepsTheCompiledBody()
        {
            HotReloadExtensionCallHost host = new HotReloadExtensionCallHost();
            Assert.That(host.ClosureCallsPublicExtensionOfInternalClass(), Is.EqualTo(5110));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync("5101", "5102");

            AssertKind(result, "ClosureCallsPublicExtensionOfInternalClass", HotReloadMethodOutcomeKind.Skipped);
            AssertNoFailedRow(result);
            Assert.That(host.ClosureCallsPublicExtensionOfInternalClass(), Is.EqualTo(5110));
        }

        /// <summary>
        /// What: a lambda that calls an overloaded extension method of an internal class declared
        /// in the same file is Skipped, the run reports no failure, and the method keeps returning
        /// the compiled value instead of throwing MethodAccessException from the shim.
        /// </summary>
        [Test]
        public async Task Run_ClosureCallingOverloadedExtensionOfInternalClassInEditedFile_IsSkippedAndKeepsTheCompiledBody()
        {
            HotReloadExtensionCallHost host = new HotReloadExtensionCallHost();
            Assert.That(host.ClosureCallsOverloadedExtensionOfInternalClass(), Is.EqualTo(5410));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync("5401", "5402");

            AssertKind(result, "ClosureCallsOverloadedExtensionOfInternalClass", HotReloadMethodOutcomeKind.Skipped);
            AssertNoFailedRow(result);
            Assert.That(host.ClosureCallsOverloadedExtensionOfInternalClass(), Is.EqualTo(5410));
        }

        /// <summary>
        /// What: a lambda that reads a private field and calls an overloaded public extension
        /// method of a public class in the same file is Patched and returns the edited value;
        /// RevertAll restores the compiled value.
        /// </summary>
        [Test]
        public async Task Run_ClosureCallingOverloadedPublicExtensionOfPublicClass_IsPatchedAndReverts()
        {
            HotReloadExtensionCallHost host = new HotReloadExtensionCallHost();
            Assert.That(host.ClosureReadsPrivateFieldAndCallsOverloadedPublicExtension(), Is.EqualTo(5613));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync("5601", "5602");

            AssertKind(result, "ClosureReadsPrivateFieldAndCallsOverloadedPublicExtension", HotReloadMethodOutcomeKind.Patched);
            Assert.That(host.ClosureReadsPrivateFieldAndCallsOverloadedPublicExtension(), Is.EqualTo(5614));
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            Assert.That(host.ClosureReadsPrivateFieldAndCallsOverloadedPublicExtension(), Is.EqualTo(5613));
        }

        /// <summary>
        /// What: a method that calls an internal extension method from its own statements is
        /// Patched and returns the edited value; RevertAll restores the compiled value.
        /// </summary>
        [Test]
        public async Task Run_OwnStatementCallingInternalExtension_IsPatchedAndReverts()
        {
            HotReloadExtensionCallHost host = new HotReloadExtensionCallHost();
            Assert.That(host.OwnStatementCallsInternalExtension(), Is.EqualTo(4407));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync("4401", "4402");

            AssertKind(result, "OwnStatementCallsInternalExtension", HotReloadMethodOutcomeKind.Patched);
            Assert.That(host.OwnStatementCallsInternalExtension(), Is.EqualTo(4408));
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            Assert.That(host.OwnStatementCallsInternalExtension(), Is.EqualTo(4407));
        }

        /// <summary>
        /// What: a lambda that reads a private field and calls a public extension method is
        /// Patched and returns the edited value; RevertAll restores the compiled value.
        /// </summary>
        [Test]
        public async Task Run_ClosureCallingPublicExtension_IsPatchedAndReverts()
        {
            HotReloadExtensionCallHost host = new HotReloadExtensionCallHost();
            Assert.That(host.ClosureReadsPrivateFieldAndCallsPublicExtension(), Is.EqualTo(4607));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync("4601", "4602");

            AssertKind(result, "ClosureReadsPrivateFieldAndCallsPublicExtension", HotReloadMethodOutcomeKind.Patched);
            Assert.That(host.ClosureReadsPrivateFieldAndCallsPublicExtension(), Is.EqualTo(4608));
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            Assert.That(host.ClosureReadsPrivateFieldAndCallsPublicExtension(), Is.EqualTo(4607));
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
            string editedPath = HotReloadTestSourceWriter.WriteEditedSource("ExtensionCallE2E.cs", edited);
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
