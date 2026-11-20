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
    /// End-to-end coverage for lambdas that pass a BCL type a referenced assembly also declares
    /// internally (ReadOnlySpan, Memory) to a non-public member: shapes with no accessor are skipped
    /// and keep running the compiled code, and a Memory argument runs through the accessor.
    /// </summary>
    public class HotReloadBclTwinTypeE2ETests
    {
        private const string FixtureFileName = "HotReloadBclTwinTypeFixtures.cs";

        private HotReloadDomainTestScope _scope;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            HotReloadBclTwinTypePrecondition.AssertReferencedAssemblyCarriesInternalBclTwins();
        }

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
        /// What: an edited lambda that passes a ReadOnlySpan, a Memory, or a ReadOnlySpan receiver to a
        /// non-public method, constructor, or indexer with no accessor shape, or converts a non-public
        /// method group to a delegate over Memory, is Skipped and the method keeps returning its
        /// compiled value without throwing.
        /// </summary>
        [TestCase("6011", "ClosureCallsInternalMethodWithSpan", 6013)]
        [TestCase("6031", "ClosureCallsInternalExtensionWithSpan", 6036)]
        [TestCase("6121", "ClosureCreatesWithInternalConstructorTakingMemory", 6123)]
        [TestCase("6161", "ClosureReadsPrivateIndexerWithMemory", 6163)]
        [TestCase("6761", "ClosureConvertsPrivateMethodGroupTakingMemory", 6763)]
        [TestCase("6801", "ClosureConvertsInternalStaticMethodGroupTakingMemory", 6803)]
        public async Task Run_ClosurePassingTwinTypeToMemberWithoutAccessor_IsSkippedAndKeepsTheCompiledValue(
            string token,
            string methodName,
            int compiledValue)
        {
            HotReloadBclTwinHost host = new HotReloadBclTwinHost();
            Assert.That(Call(host, methodName), Is.EqualTo(compiledValue));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync(token);

            AssertKind(result, methodName, HotReloadMethodOutcomeKind.Skipped);
            Assert.That(Call(host, methodName), Is.EqualTo(compiledValue));
        }

        /// <summary>
        /// What: an edited lambda that passes a Memory to an internal method is Patched, returns the
        /// edited value through the accessor, and RevertAll restores the compiled value.
        /// </summary>
        [Test]
        public async Task Run_ClosurePassingMemoryToInternalMethod_IsPatchedAndReverts()
        {
            HotReloadBclTwinHost host = new HotReloadBclTwinHost();
            Assert.That(host.ClosureCallsInternalMethodWithMemory(), Is.EqualTo(6103));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync("6101");

            AssertKind(result, "ClosureCallsInternalMethodWithMemory", HotReloadMethodOutcomeKind.Patched);
            Assert.That(host.ClosureCallsInternalMethodWithMemory(), Is.EqualTo(6104));
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            Assert.That(host.ClosureCallsInternalMethodWithMemory(), Is.EqualTo(6103));
        }

        /// <summary>
        /// What: an edited lambda that declares and calls a delegate typed with Memory is Patched and
        /// returns the edited value, which it reaches only when the shim runs.
        /// </summary>
        [Test]
        public async Task Run_ClosureDeclaringDelegateTakingMemory_IsPatchedAndReturnsTheEditedValue()
        {
            HotReloadBclTwinHost host = new HotReloadBclTwinHost();
            Assert.That(host.ClosureDeclaresDelegateTakingMemory(), Is.EqualTo(6783));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync("6781");

            AssertKind(result, "ClosureDeclaresDelegateTakingMemory", HotReloadMethodOutcomeKind.Patched);
            Assert.That(host.ClosureDeclaresDelegateTakingMemory(), Is.EqualTo(6784));
        }

        /// <summary>
        /// What: an edited async method that passes a Memory to an internal method is Patched and
        /// returns the edited value through the accessor.
        /// </summary>
        [Test]
        public async Task Run_AsyncPassingMemoryToInternalMethod_IsPatchedAndReturnsTheEditedValue()
        {
            HotReloadBclTwinHost host = new HotReloadBclTwinHost();
            Assert.That(await host.AsyncCallsInternalMethodWithMemory(), Is.EqualTo(6353));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync("6351");

            AssertKind(result, "AsyncCallsInternalMethodWithMemory", HotReloadMethodOutcomeKind.Patched);
            Assert.That(await host.AsyncCallsInternalMethodWithMemory(), Is.EqualTo(6354));
        }

        private static int Call(HotReloadBclTwinHost host, string methodName)
        {
            switch (methodName)
            {
                case "ClosureCallsInternalMethodWithSpan":
                    return host.ClosureCallsInternalMethodWithSpan();
                case "ClosureCallsInternalExtensionWithSpan":
                    return host.ClosureCallsInternalExtensionWithSpan();
                case "ClosureCreatesWithInternalConstructorTakingMemory":
                    return host.ClosureCreatesWithInternalConstructorTakingMemory();
                case "ClosureReadsPrivateIndexerWithMemory":
                    return host.ClosureReadsPrivateIndexerWithMemory();
                case "ClosureConvertsPrivateMethodGroupTakingMemory":
                    return host.ClosureConvertsPrivateMethodGroupTakingMemory();
                case "ClosureConvertsInternalStaticMethodGroupTakingMemory":
                    return host.ClosureConvertsInternalStaticMethodGroupTakingMemory();
                default:
                    throw new ArgumentException("No call for " + methodName, nameof(methodName));
            }
        }

        private static async Task<HotReloadOrchestratorResult> RunEditedFixtureAsync(string token)
        {
            string fixturePath = Path.GetFullPath(
                Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", FixtureFileName));
            string onDisk = File.ReadAllText(fixturePath);
            string edited = onDisk.Replace(token, token.Substring(0, 3) + "2", StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk), "Precondition: the token must be in the fixture.");
            string editedPath = HotReloadTestSourceWriter.WriteEditedSource("BclTwinTypeE2E.cs", edited);
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
    }
}
