using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// End-to-end coverage for lambdas that reach an internal member through an interpolated string
    /// handler, the implicit Index support, or the awaiter of await using and await foreach: the
    /// method is skipped and keeps running its compiled body instead of throwing
    /// MethodAccessException from the shim.
    /// </summary>
    public class HotReloadImplicitCallPatternE2ETests
    {
        private const string FixtureFileName = "HotReloadImplicitCallPatternFixtures.cs";
        private const string LayoutFixtureFileName = "HotReloadImplicitCallPatternLayoutFixtures.cs";

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
        /// What: a lambda whose handler calls an internal AppendFormatted is Skipped, no row fails,
        /// and the method keeps returning the compiled value.
        /// </summary>
        [Test]
        public async Task Run_ClosureBuildingHandlerWithInternalAppendFormatted_IsSkippedAndKeepsTheCompiledBody()
        {
            HotReloadImplicitCallPatternHost host = new HotReloadImplicitCallPatternHost();
            Assert.That(host.ClosureBuildsHandlerWithInternalAppendFormatted(), Is.EqualTo(7145));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync("7141", "7142");

            AssertKind(result, "ClosureBuildsHandlerWithInternalAppendFormatted", HotReloadMethodOutcomeKind.Skipped);
            AssertNoFailedRow(result);
            Assert.That(host.ClosureBuildsHandlerWithInternalAppendFormatted(), Is.EqualTo(7145));
        }

        /// <summary>
        /// What: a lambda indexing from the end of a type with an internal Length is Skipped, no row
        /// fails, and the method keeps returning the compiled value.
        /// </summary>
        [Test]
        public async Task Run_ClosureIndexingFromEndWithInternalLength_IsSkippedAndKeepsTheCompiledBody()
        {
            HotReloadImplicitCallPatternHost host = new HotReloadImplicitCallPatternHost();
            Assert.That(host.ClosureIndexesFromEndWithInternalLength(), Is.EqualTo(7303));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync("7201", "7202");

            AssertKind(result, "ClosureIndexesFromEndWithInternalLength", HotReloadMethodOutcomeKind.Skipped);
            AssertNoFailedRow(result);
            Assert.That(host.ClosureIndexesFromEndWithInternalLength(), Is.EqualTo(7303));
        }

        /// <summary>
        /// What: a lambda whose await using awaits a DisposeAsync result with an internal GetResult
        /// is Skipped, no row fails, and the method keeps returning the compiled value.
        /// </summary>
        [Test]
        public async Task Run_ClosureAwaitUsingWithInternalGetResult_IsSkippedAndKeepsTheCompiledBody()
        {
            HotReloadImplicitCallPatternHost host = new HotReloadImplicitCallPatternHost();
            Assert.That(await host.ClosureAwaitUsesDisposeAsyncWithInternalGetResult(), Is.EqualTo(7341));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync("7341", "7342");

            AssertKind(result, "ClosureAwaitUsesDisposeAsyncWithInternalGetResult", HotReloadMethodOutcomeKind.Skipped);
            AssertNoFailedRow(result);
            Assert.That(await host.ClosureAwaitUsesDisposeAsyncWithInternalGetResult(), Is.EqualTo(7341));
        }

        /// <summary>
        /// What: a lambda whose await foreach awaits a MoveNextAsync result with an internal
        /// GetResult is Skipped, no row fails, and the method keeps returning the compiled value.
        /// </summary>
        [Test]
        public async Task Run_ClosureAwaitForEachWithInternalGetResult_IsSkippedAndKeepsTheCompiledBody()
        {
            HotReloadImplicitCallPatternHost host = new HotReloadImplicitCallPatternHost();
            Assert.That(await host.ClosureAwaitForEachesMoveNextWithInternalGetResult(), Is.EqualTo(7444));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync("7441", "7442");

            AssertKind(result, "ClosureAwaitForEachesMoveNextWithInternalGetResult", HotReloadMethodOutcomeKind.Skipped);
            AssertNoFailedRow(result);
            Assert.That(await host.ClosureAwaitForEachesMoveNextWithInternalGetResult(), Is.EqualTo(7444));
        }

        /// <summary>
        /// What: a lambda slicing a range that counts from the start of a type with an internal
        /// Length is Patched and runs the edited body, because that range never reads Length.
        /// </summary>
        [Test]
        public async Task Run_ClosureSlicingFromStartWithInternalLength_IsPatchedAndRunsTheEditedBody()
        {
            HotReloadImplicitCallPatternHost host = new HotReloadImplicitCallPatternHost();
            Assert.That(host.ClosureSlicesFromStartWithInternalLength(), Is.EqualTo(7732));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync("7721", "7722");

            AssertKind(result, "ClosureSlicesFromStartWithInternalLength", HotReloadMethodOutcomeKind.Patched);
            AssertNoFailedRow(result);
            Assert.That(host.ClosureSlicesFromStartWithInternalLength(), Is.EqualTo(7733));
        }

        /// <summary>
        /// What: a lambda slicing an open-start range of a type with an internal Length is Patched
        /// and runs the edited body, because that range never reads Length.
        /// </summary>
        [Test]
        public async Task Run_ClosureSlicingOpenStartWithInternalLength_IsPatchedAndRunsTheEditedBody()
        {
            HotReloadImplicitCallPatternHost host = new HotReloadImplicitCallPatternHost();
            Assert.That(host.ClosureSlicesOpenStartWithInternalLength(), Is.EqualTo(7743));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync("7741", "7742");

            AssertKind(result, "ClosureSlicesOpenStartWithInternalLength", HotReloadMethodOutcomeKind.Patched);
            AssertNoFailedRow(result);
            Assert.That(host.ClosureSlicesOpenStartWithInternalLength(), Is.EqualTo(7744));
        }

        /// <summary>
        /// What: a lambda whose handler, declared in the only file passed to the reload, has an
        /// internal AppendFormatted and takes its InterpolatedStringHandler attribute from an
        /// internal declaration in another file is Skipped, no row fails, and the method keeps
        /// returning the compiled value.
        /// </summary>
        [Test]
        public async Task Run_ClosureBuildingHandlerWhoseAttributeIsInAnotherFile_IsSkippedAndKeepsTheCompiledBody()
        {
            HotReloadImplicitCallPatternLayoutHost host = new HotReloadImplicitCallPatternLayoutHost();
            Assert.That(host.ClosureBuildsPassedFileHandlerWithInternalAppendFormatted(), Is.EqualTo(7770));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync("7765", "7766", LayoutFixtureFileName);

            AssertKind(result, "ClosureBuildsPassedFileHandlerWithInternalAppendFormatted", HotReloadMethodOutcomeKind.Skipped);
            AssertNoFailedRow(result);
            Assert.That(host.ClosureBuildsPassedFileHandlerWithInternalAppendFormatted(), Is.EqualTo(7770));
        }

        private static async Task<HotReloadOrchestratorResult> RunEditedFixtureAsync(
            string originalToken,
            string editedToken,
            string fixtureFileName = FixtureFileName)
        {
            string fixturePath = Path.GetFullPath(
                Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", fixtureFileName));
            string onDisk = File.ReadAllText(fixturePath);
            string edited = onDisk.Replace(originalToken, editedToken);
            Assert.That(edited, Is.Not.EqualTo(onDisk), "Precondition: the token must be in the fixture.");
            string editedPath = HotReloadTestSourceWriter.WriteEditedSource("ImplicitCallPatternE2E.cs", edited);
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
