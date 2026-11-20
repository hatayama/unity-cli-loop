using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Tests.Editor.HotReloadHandlerAttributeFixture;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// End-to-end coverage for a target assembly whose only InterpolatedStringHandlerAttribute is
    /// its own internal declaration in a file the reload does not pass: a lambda whose handler
    /// calls an internal member is skipped and keeps running its compiled body instead of throwing
    /// MethodAccessException from the shim.
    /// </summary>
    public class HotReloadHandlerAttributeE2ETests
    {
        private const string FixtureDirectoryName = "HotReloadHandlerAttributeFixture";
        private const string FixtureFileName = "HotReloadHandlerAttributeFixture.cs";

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
        /// What: a lambda whose handler has an internal AppendFormatted is Skipped, no row fails, and
        /// the method keeps returning the compiled value.
        /// </summary>
        [Test]
        public async Task Run_ClosureBuildingHandler_IsSkippedAndKeepsTheCompiledBody()
        {
            HandlerAttributeFixtureHost host = new HandlerAttributeFixtureHost();
            Assert.That(host.ClosureBuildsHandler(), Is.EqualTo(1110));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync("1105", "1106");

            HotReloadMethodOutcome outcome = FindOutcome(result, "ClosureBuildsHandler");
            Assert.That(outcome.Kind, Is.EqualTo(HotReloadMethodOutcomeKind.Skipped), outcome.Method + ": " + outcome.Reason);
            foreach (HotReloadMethodOutcome row in result.Methods)
            {
                Assert.That(row.Kind, Is.Not.EqualTo(HotReloadMethodOutcomeKind.Failed), row.Method + ": " + row.Reason);
            }

            Assert.That(host.ClosureBuildsHandler(), Is.EqualTo(1110));
        }

        private static async Task<HotReloadOrchestratorResult> RunEditedFixtureAsync(
            string originalToken,
            string editedToken)
        {
            string fixturePath = Path.GetFullPath(
                Path.Combine(Application.dataPath, "Tests", "Editor", FixtureDirectoryName, FixtureFileName));
            string onDisk = File.ReadAllText(fixturePath);
            string edited = onDisk.Replace(originalToken, editedToken);
            Assert.That(edited, Is.Not.EqualTo(onDisk), "Precondition: the token must be in the fixture.");
            string editedPath = HotReloadTestSourceWriter.WriteEditedSource("HandlerAttributeE2E.cs", edited);
            return await HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);
        }

        private static HotReloadMethodOutcome FindOutcome(HotReloadOrchestratorResult result, string methodName)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Method.Contains("." + methodName + "("))
                {
                    return outcome;
                }
            }

            Assert.Fail("No row for " + methodName + ".");
            return null;
        }
    }
}
