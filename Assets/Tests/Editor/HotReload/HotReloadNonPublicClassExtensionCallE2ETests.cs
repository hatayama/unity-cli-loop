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
    /// End-to-end coverage for lambdas and async methods that call an extension method of a
    /// non-public static class: they are patched, return the edited value through the accessor,
    /// and RevertAll restores the compiled value.
    /// </summary>
    public class HotReloadNonPublicClassExtensionCallE2ETests
    {
        private const string FixtureFileName = "HotReloadNonPublicClassExtensionCallFixtures.cs";

        private const string OwnClassAddedMethodAnchor =
            "        internal static int NonPublicClosureOverOwnClass(this int value, int add)";

        private const string OwnClassAddedMethodSource =
            "        internal static int NonPublicAddedFive(int value)\n"
            + "        {\n"
            + "            return value * 5;\n"
            + "        }\n\n";

        private const string AddedExtensionAnchor = "        internal static int NonPublicPlusOne(this int value, int add)";

        private const string AddedExtensionSource =
            "        internal static int NonPublicAddedTriple(this int value, int add)\n"
            + "        {\n"
            + "            return value * 3 + add + 1000;\n"
            + "        }\n\n";

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
        /// What: a lambda that calls an extension method of a non-public static class in the receiver
        /// form is Patched and returns the edited value; RevertAll restores the compiled value.
        /// </summary>
        [Test]
        public async Task Run_ClosureCallingNonPublicClassExtension_IsPatchedAndReverts()
        {
            HotReloadNonPublicClassExtensionCallHost host = new HotReloadNonPublicClassExtensionCallHost();
            Assert.That(host.ClosureCallsNonPublicClassExtension(), Is.EqualTo(6110));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync("6101", "6102");

            AssertKind(result, "ClosureCallsNonPublicClassExtension", HotReloadMethodOutcomeKind.Patched);
            Assert.That(host.ClosureCallsNonPublicClassExtension(), Is.EqualTo(6111));
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            Assert.That(host.ClosureCallsNonPublicClassExtension(), Is.EqualTo(6110));
        }

        /// <summary>
        /// What: an async method that calls an extension method of a non-public static class is
        /// Patched and returns the edited value; RevertAll restores the compiled value.
        /// </summary>
        [Test]
        public async Task Run_AsyncMethodCallingNonPublicClassExtension_IsPatchedAndReverts()
        {
            HotReloadNonPublicClassExtensionCallHost host = new HotReloadNonPublicClassExtensionCallHost();
            Assert.That(await host.AsyncCallsNonPublicClassExtension(), Is.EqualTo(6120));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync("6111", "6112");

            AssertKind(result, "AsyncCallsNonPublicClassExtension", HotReloadMethodOutcomeKind.Patched);
            Assert.That(await host.AsyncCallsNonPublicClassExtension(), Is.EqualTo(6121));
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            Assert.That(await host.AsyncCallsNonPublicClassExtension(), Is.EqualTo(6120));
        }

        /// <summary>
        /// What: a lambda that calls an extension method of a non-public static class in the static
        /// form is Patched and returns the edited value; RevertAll restores the compiled value.
        /// </summary>
        [Test]
        public async Task Run_ClosureCallingNonPublicClassExtensionInStaticForm_IsPatchedAndReverts()
        {
            HotReloadNonPublicClassExtensionCallHost host = new HotReloadNonPublicClassExtensionCallHost();
            Assert.That(host.ClosureCallsNonPublicClassExtensionInStaticForm(), Is.EqualTo(6130));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync("6121", "6122");

            AssertKind(result, "ClosureCallsNonPublicClassExtensionInStaticForm", HotReloadMethodOutcomeKind.Patched);
            Assert.That(host.ClosureCallsNonPublicClassExtensionInStaticForm(), Is.EqualTo(6131));
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            Assert.That(host.ClosureCallsNonPublicClassExtensionInStaticForm(), Is.EqualTo(6130));
        }

        /// <summary>
        /// What: a lambda that chains two extension methods of a non-public static class is Patched
        /// and returns the edited value.
        /// </summary>
        [Test]
        public async Task Run_ClosureChainingNonPublicClassExtensions_IsPatched()
        {
            HotReloadNonPublicClassExtensionCallHost host = new HotReloadNonPublicClassExtensionCallHost();
            Assert.That(host.ClosureChainsNonPublicClassExtensions(), Is.EqualTo(6142));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync("6131", "6132");

            AssertKind(result, "ClosureChainsNonPublicClassExtensions", HotReloadMethodOutcomeKind.Patched);
            Assert.That(host.ClosureChainsNonPublicClassExtensions(), Is.EqualTo(6143));
        }

        /// <summary>
        /// What: a lambda that calls a public extension method of a non-public static class is
        /// Patched and returns the edited value.
        /// </summary>
        [Test]
        public async Task Run_ClosureCallingPublicExtensionOfNonPublicClass_IsPatched()
        {
            HotReloadNonPublicClassExtensionCallHost host = new HotReloadNonPublicClassExtensionCallHost();
            Assert.That(host.ClosureCallsPublicExtensionOfNonPublicClass(), Is.EqualTo(6147));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync("6141", "6142");

            AssertKind(result, "ClosureCallsPublicExtensionOfNonPublicClass", HotReloadMethodOutcomeKind.Patched);
            Assert.That(host.ClosureCallsPublicExtensionOfNonPublicClass(), Is.EqualTo(6148));
        }

        /// <summary>
        /// What: a lambda that calls, in the static form, an extension method this reload adds to a
        /// non-public static class is Patched and returns what the added method computes, because the
        /// added-method rewrite replaces the call, class name included.
        /// </summary>
        [Test]
        public async Task Run_ClosureCallingAddedNonPublicClassExtensionInStaticForm_IsPatched()
        {
            HotReloadNonPublicClassExtensionCallHost host = new HotReloadNonPublicClassExtensionCallHost();
            Assert.That(host.ClosureCallsNonPublicClassExtensionInStaticForm(), Is.EqualTo(6130));

            HotReloadOrchestratorResult result = await RunEditAsync(
                onDisk => onDisk
                    .Replace(AddedExtensionAnchor, AddedExtensionSource + AddedExtensionAnchor)
                    .Replace(
                        "HotReloadNonPublicClassExtensions.NonPublicTriple(_value, 6121)",
                        "HotReloadNonPublicClassExtensions.NonPublicAddedTriple(_value, 6122)"));

            AssertKind(result, "ClosureCallsNonPublicClassExtensionInStaticForm", HotReloadMethodOutcomeKind.Patched);
            Assert.That(host.ClosureCallsNonPublicClassExtensionInStaticForm(), Is.EqualTo(7131));
        }

        /// <summary>
        /// What: a lambda that calls a public void extension method of a non-public static class,
        /// which binds ambiguously against both copies of the class, is Patched and the edited body
        /// runs the call through the accessor.
        /// </summary>
        [Test]
        public async Task Run_ClosureCallingPublicVoidExtensionOfNonPublicClass_IsPatched()
        {
            HotReloadNonPublicClassExtensionCallHost host = new HotReloadNonPublicClassExtensionCallHost();
            Assert.That(host.ClosureRecordsThroughPublicExtensionOfNonPublicClass(), Is.EqualTo(6292));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync("6291", "6292");

            AssertKind(result, "ClosureRecordsThroughPublicExtensionOfNonPublicClass", HotReloadMethodOutcomeKind.Patched);
            Assert.That(host.ClosureRecordsThroughPublicExtensionOfNonPublicClass(), Is.EqualTo(6294));
        }

        /// <summary>
        /// What: a lambda inside a non-public static class that calls, in the static form, a method
        /// this reload adds to that class is Patched and returns what the added method computes.
        /// </summary>
        [Test]
        public async Task Run_ClosureInNonPublicClassCallingItsAddedMethodInStaticForm_IsPatched()
        {
            Assert.That(3.NonPublicClosureOverOwnClass(1), Is.EqualTo(6305));

            HotReloadOrchestratorResult result = await RunEditAsync(
                onDisk => onDisk
                    .Replace(OwnClassAddedMethodAnchor, OwnClassAddedMethodSource + OwnClassAddedMethodAnchor)
                    .Replace(
                        "() => value + add + 6301",
                        "() => HotReloadNonPublicClassExtensions.NonPublicAddedFive(value) + add + 6302"));

            AssertKind(result, "NonPublicClosureOverOwnClass", HotReloadMethodOutcomeKind.Patched);
            Assert.That(3.NonPublicClosureOverOwnClass(1), Is.EqualTo(6318));
        }

        /// <summary>
        /// What: a lambda that calls an extension method of an internal static class in a namespace
        /// named with a keyword is Patched and returns the edited value, because the accessor's
        /// typeof escapes the keyword (@event).
        /// </summary>
        [Test]
        public async Task Run_ClosureCallingExtensionInKeywordNamedNamespace_IsPatched()
        {
            HotReloadNonPublicClassExtensionCallHost host = new HotReloadNonPublicClassExtensionCallHost();
            Assert.That(host.ClosureCallsKeywordNamespaceExtension(), Is.EqualTo(6320));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync("6311", "6312");

            AssertKind(result, "ClosureCallsKeywordNamespaceExtension", HotReloadMethodOutcomeKind.Patched);
            Assert.That(host.ClosureCallsKeywordNamespaceExtension(), Is.EqualTo(6321));
        }

        private static Task<HotReloadOrchestratorResult> RunEditedFixtureAsync(
            string originalToken,
            string editedToken)
        {
            return RunEditAsync(onDisk => onDisk.Replace(originalToken, editedToken));
        }

        private static async Task<HotReloadOrchestratorResult> RunEditAsync(Func<string, string> edit)
        {
            string fixturePath = Path.GetFullPath(
                Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", FixtureFileName));
            string onDisk = File.ReadAllText(fixturePath);
            string edited = edit(onDisk);
            Assert.That(edited, Is.Not.EqualTo(onDisk), "Precondition: the edit must change the fixture.");
            string editedPath = HotReloadTestSourceWriter.WriteEditedSource("NonPublicClassExtensionE2E.cs", edited);
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
