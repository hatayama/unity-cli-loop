using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// End-to-end coverage for lambdas and async methods that call an internal extension method of
    /// a public class: they are patched, return the edited value through the accessor, and
    /// RevertAll restores the compiled value.
    /// </summary>
    public class HotReloadPublicClassExtensionCallE2ETests
    {
        private const string FixtureFileName = "HotReloadPublicClassExtensionCallFixtures.cs";

        private const string AddedExtensionAnchor = "        internal static int PlusOne(this int value, int add)";

        private const string AddedExtensionSource =
            "        internal static int AddedTriple(this int value, int add)\n"
            + "        {\n"
            + "            return value * 3 + add + 1000;\n"
            + "        }\n\n";

        private const string ClosureCall = "Func<int> read = () => _value.TriplePlus(7101);";

        private const string AsyncCall = "return _value.TriplePlus(7111);";

        private const string AddedExtensionReducedCallReason =
            "extension method 'AddedTriple' added in this reload has no rewrite shape when called on a receiver; "
            + "write the call as 'HotReloadPublicClassInternalExtensions.AddedTriple(receiver, ...)'.";

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
        /// What: a lambda that calls an internal extension method of a public class is Patched and
        /// returns the edited value; RevertAll restores the compiled value.
        /// </summary>
        [Test]
        public async Task Run_ClosureCallingInternalExtension_IsPatchedAndReverts()
        {
            HotReloadPublicClassExtensionCallHost host = new HotReloadPublicClassExtensionCallHost();
            Assert.That(host.ClosureCallsInternalExtension(), Is.EqualTo(7110));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync("7101", "7102");

            AssertKind(result, "ClosureCallsInternalExtension", HotReloadMethodOutcomeKind.Patched);
            Assert.That(host.ClosureCallsInternalExtension(), Is.EqualTo(7111));
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            Assert.That(host.ClosureCallsInternalExtension(), Is.EqualTo(7110));
        }

        /// <summary>
        /// What: an async method that calls an internal extension method of a public class is
        /// Patched and returns the edited value; RevertAll restores the compiled value.
        /// </summary>
        [Test]
        public async Task Run_AsyncMethodCallingInternalExtension_IsPatchedAndReverts()
        {
            HotReloadPublicClassExtensionCallHost host = new HotReloadPublicClassExtensionCallHost();
            Assert.That(await host.AsyncCallsInternalExtension(), Is.EqualTo(7120));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync("7111", "7112");

            AssertKind(result, "AsyncCallsInternalExtension", HotReloadMethodOutcomeKind.Patched);
            Assert.That(await host.AsyncCallsInternalExtension(), Is.EqualTo(7121));
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            Assert.That(await host.AsyncCallsInternalExtension(), Is.EqualTo(7120));
        }

        /// <summary>
        /// What: a lambda that chains two internal extension methods of a public class is Patched
        /// and returns the edited value; RevertAll restores the compiled value.
        /// </summary>
        [Test]
        public async Task Run_ClosureChainingInternalExtensions_IsPatchedAndReverts()
        {
            HotReloadPublicClassExtensionCallHost host = new HotReloadPublicClassExtensionCallHost();
            Assert.That(host.ClosureChainsInternalExtensions(), Is.EqualTo(7142));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync("7131", "7132");

            AssertKind(result, "ClosureChainsInternalExtensions", HotReloadMethodOutcomeKind.Patched);
            Assert.That(host.ClosureChainsInternalExtensions(), Is.EqualTo(7143));
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            Assert.That(host.ClosureChainsInternalExtensions(), Is.EqualTo(7142));
        }

        /// <summary>
        /// What: a lambda that calls an internal extension method on a call result evaluates the
        /// receiver once and before the argument, as the compiled call does.
        /// </summary>
        [Test]
        public async Task Run_ClosureCallingInternalExtensionOnCallResult_EvaluatesTheReceiverOnceAndFirst()
        {
            HotReloadPublicClassExtensionCallHost host = new HotReloadPublicClassExtensionCallHost();
            Assert.That(host.ClosureCallsInternalExtensionOnCallResult(), Is.EqualTo(7150));
            Assert.That(host.Order, Is.EqualTo("ra"));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync("7141", "7142");

            AssertKind(result, "ClosureCallsInternalExtensionOnCallResult", HotReloadMethodOutcomeKind.Patched);
            Assert.That(host.ClosureCallsInternalExtensionOnCallResult(), Is.EqualTo(7151));
            Assert.That(host.Order, Is.EqualTo("rara"));
            Assert.That(host.ReceiverCalls, Is.EqualTo(2));
        }

        /// <summary>
        /// What: a lambda that calls an internal extension method on a struct passed by value
        /// reads the struct through the accessor and returns the edited value.
        /// </summary>
        [Test]
        public async Task Run_ClosureCallingInternalExtensionOnStructByValue_IsPatchedAndReturnsTheEditedValue()
        {
            HotReloadPublicClassExtensionCallHost host = new HotReloadPublicClassExtensionCallHost();
            Assert.That(host.ClosureCallsInternalExtensionOnStructByValue(), Is.EqualTo(7196));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync("7191", "7192");

            AssertKind(result, "ClosureCallsInternalExtensionOnStructByValue", HotReloadMethodOutcomeKind.Patched);
            Assert.That(host.ClosureCallsInternalExtensionOnStructByValue(), Is.EqualTo(7197));
        }

        /// <summary>
        /// What: a [Conditional] internal method called in a method's own statements, not in a
        /// lambda, is still omitted after the method is Patched, as the compiler omits it.
        /// </summary>
        [Test]
        public async Task Run_StatementCallingConditionalInternalMethod_IsPatchedAndTheCallStaysOmitted()
        {
            HotReloadPublicClassExtensionCallHost host = new HotReloadPublicClassExtensionCallHost();
            Assert.That(host.StatementCallsConditionalInternalMethod(), Is.EqualTo(7371));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync("7371", "8371");

            AssertKind(result, "StatementCallsConditionalInternalMethod", HotReloadMethodOutcomeKind.Patched);
            Assert.That(host.StatementCallsConditionalInternalMethod(), Is.EqualTo(8371));
        }

        /// <summary>
        /// What: a lambda that calls an internal extension method on a null receiver passes the
        /// null through the accessor to the method, as the compiled call does.
        /// </summary>
        [Test]
        public async Task Run_ClosureCallingInternalExtensionOnNullReceiver_PassesTheNullToTheMethod()
        {
            HotReloadPublicClassExtensionCallHost host = new HotReloadPublicClassExtensionCallHost();
            Assert.That(host.ClosureCallsInternalExtensionOnNullReceiver(), Is.EqualTo(8201));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync("7201", "7202");

            AssertKind(result, "ClosureCallsInternalExtensionOnNullReceiver", HotReloadMethodOutcomeKind.Patched);
            Assert.That(host.ClosureCallsInternalExtensionOnNullReceiver(), Is.EqualTo(8202));
        }

        /// <summary>
        /// What: a lambda that calls an extension method added in the same reload on a receiver is
        /// Skipped with the reason that names the static form, keeps its compiled value, and the
        /// rest of the file still applies.
        /// </summary>
        [Test]
        public async Task Run_ClosureCallingAddedExtensionOnReceiver_IsSkippedAndTheFileApplies()
        {
            HotReloadPublicClassExtensionCallHost host = new HotReloadPublicClassExtensionCallHost();
            Assert.That(host.ClosureCallsInternalExtension(), Is.EqualTo(7110));

            HotReloadOrchestratorResult result = await RunWithAddedExtensionAsync(
                ClosureCall,
                "Func<int> read = () => _value.AddedTriple(7101);");

            AssertSkippedWithReason(result, "ClosureCallsInternalExtension", AddedExtensionReducedCallReason);
            AssertNoFailedRow(result);
            Assert.That(host.ClosureCallsInternalExtension(), Is.EqualTo(7110));
            Assert.That(host.ClosureCallsInternalExtensionInStaticForm(), Is.EqualTo(7131));
        }

        /// <summary>
        /// What: an async method that calls an extension method added in the same reload on a
        /// receiver is Skipped with the reason that names the static form, keeps its compiled
        /// value, and the rest of the file still applies.
        /// </summary>
        [Test]
        public async Task Run_AsyncMethodCallingAddedExtensionOnReceiver_IsSkippedAndTheFileApplies()
        {
            HotReloadPublicClassExtensionCallHost host = new HotReloadPublicClassExtensionCallHost();
            Assert.That(await host.AsyncCallsInternalExtension(), Is.EqualTo(7120));

            HotReloadOrchestratorResult result = await RunWithAddedExtensionAsync(
                AsyncCall,
                "return _value.AddedTriple(7111);");

            AssertSkippedWithReason(result, "AsyncCallsInternalExtension", AddedExtensionReducedCallReason);
            AssertNoFailedRow(result);
            Assert.That(await host.AsyncCallsInternalExtension(), Is.EqualTo(7120));
            Assert.That(host.ClosureCallsInternalExtensionInStaticForm(), Is.EqualTo(7131));
        }

        /// <summary>
        /// What: a lambda that calls an extension method added in the same reload in its static
        /// form is Patched and returns the added method's value; RevertAll restores the compiled
        /// value.
        /// </summary>
        [Test]
        public async Task Run_ClosureCallingAddedExtensionInStaticForm_IsPatchedAndReverts()
        {
            HotReloadPublicClassExtensionCallHost host = new HotReloadPublicClassExtensionCallHost();
            Assert.That(host.ClosureCallsInternalExtension(), Is.EqualTo(7110));

            HotReloadOrchestratorResult result = await RunWithAddedExtensionAsync(
                ClosureCall,
                "Func<int> read = () => HotReloadPublicClassInternalExtensions.AddedTriple(_value, 7101);");

            AssertKind(result, "ClosureCallsInternalExtension", HotReloadMethodOutcomeKind.Patched);
            Assert.That(host.ClosureCallsInternalExtension(), Is.EqualTo(8110));
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            Assert.That(host.ClosureCallsInternalExtension(), Is.EqualTo(7110));
        }

        /// <summary>
        /// What: an async method that calls an extension method added in the same reload in its
        /// static form is Patched and returns the added method's value; RevertAll restores the
        /// compiled value.
        /// </summary>
        [Test]
        public async Task Run_AsyncMethodCallingAddedExtensionInStaticForm_IsPatchedAndReverts()
        {
            HotReloadPublicClassExtensionCallHost host = new HotReloadPublicClassExtensionCallHost();
            Assert.That(await host.AsyncCallsInternalExtension(), Is.EqualTo(7120));

            HotReloadOrchestratorResult result = await RunWithAddedExtensionAsync(
                AsyncCall,
                "return HotReloadPublicClassInternalExtensions.AddedTriple(_value, 7111);");

            AssertKind(result, "AsyncCallsInternalExtension", HotReloadMethodOutcomeKind.Patched);
            Assert.That(await host.AsyncCallsInternalExtension(), Is.EqualTo(8120));
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            Assert.That(await host.AsyncCallsInternalExtension(), Is.EqualTo(7120));
        }

        /// <summary>
        /// What: a lambda that calls a generic extension method added in the same reload on a
        /// receiver is Skipped with the generic-method reason, since writing the static form would
        /// not help, and keeps its compiled value.
        /// </summary>
        [Test]
        public async Task Run_ClosureCallingAddedGenericExtensionOnReceiver_IsSkippedWithTheGenericReason()
        {
            HotReloadPublicClassExtensionCallHost host = new HotReloadPublicClassExtensionCallHost();
            Assert.That(host.ClosureCallsInternalExtension(), Is.EqualTo(7110));

            HotReloadOrchestratorResult result = await RunWithAddedExtensionAsync(
                ClosureCall,
                "Func<int> read = () => _value.AddedEcho(7101);",
                "        internal static int AddedEcho<T>(this T value, int add)\n"
                + "        {\n"
                + "            return add;\n"
                + "        }\n\n");

            AssertSkippedWithReason(
                result,
                "ClosureCallsInternalExtension",
                "inaccessible generic method calls are not rewritten.");
            AssertNoFailedRow(result);
            Assert.That(host.ClosureCallsInternalExtension(), Is.EqualTo(7110));
        }

        /// <summary>
        /// What: a property getter whose lambda calls an extension method added in the same
        /// reload on a receiver is Skipped with the reason that names the static form, keeps its
        /// compiled value, and the rest of the file still applies.
        /// </summary>
        [Test]
        public async Task Run_GetterLambdaCallingAddedExtensionOnReceiver_IsSkippedAndTheFileApplies()
        {
            HotReloadPublicClassExtensionCallHost host = new HotReloadPublicClassExtensionCallHost();
            Assert.That(host.ClosureGetterValue, Is.EqualTo(7310));

            HotReloadOrchestratorResult result = await RunWithAddedExtensionAsync(
                "Func<int> read = () => _value.TriplePlus(7301);",
                "Func<int> read = () => _value.AddedTriple(7301);");

            AssertSkippedWithReason(result, "get_ClosureGetterValue", AddedExtensionReducedCallReason);
            AssertNoFailedRow(result);
            Assert.That(host.ClosureGetterValue, Is.EqualTo(7310));
            Assert.That(host.ClosureCallsInternalExtensionInStaticForm(), Is.EqualTo(7131));
        }

        /// <summary>
        /// What: a property setter whose lambda calls an extension method added in the same
        /// reload on a receiver is Skipped with the reason that names the static form, keeps its
        /// compiled behavior, and the rest of the file still applies.
        /// </summary>
        [Test]
        public async Task Run_SetterLambdaCallingAddedExtensionOnReceiver_IsSkippedAndTheFileApplies()
        {
            HotReloadPublicClassExtensionCallHost host = new HotReloadPublicClassExtensionCallHost();
            host.ClosureSetterValue = 3;
            Assert.That(host.ClosureSetterValue, Is.EqualTo(7320));

            HotReloadOrchestratorResult result = await RunWithAddedExtensionAsync(
                "Func<int> read = () => value.TriplePlus(7311);",
                "Func<int> read = () => value.AddedTriple(7311);");

            AssertSkippedWithReason(result, "set_ClosureSetterValue", AddedExtensionReducedCallReason);
            AssertNoFailedRow(result);
            host.ClosureSetterValue = 3;
            Assert.That(host.ClosureSetterValue, Is.EqualTo(7320));
            Assert.That(host.ClosureCallsInternalExtensionInStaticForm(), Is.EqualTo(7131));
        }

        // Adds an extension method to the extensions class, rewrites one call to use it, and edits
        // another method of the file so the test can see that the rest of the file applied.
        private static async Task<HotReloadOrchestratorResult> RunWithAddedExtensionAsync(
            string originalCall,
            string editedCall,
            string addedSource = AddedExtensionSource)
        {
            string fixturePath = Path.GetFullPath(
                Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", FixtureFileName));
            string onDisk = File.ReadAllText(fixturePath);
            Assert.That(onDisk.Contains(AddedExtensionAnchor), "Precondition: the anchor must be in the fixture.");
            Assert.That(onDisk.Contains(originalCall), "Precondition: the call must be in the fixture.");
            string edited = onDisk
                .Replace(AddedExtensionAnchor, addedSource + AddedExtensionAnchor)
                .Replace(originalCall, editedCall)
                .Replace("7121", "7122");
            string editedPath = HotReloadTestSourceWriter.WriteEditedSource("PublicClassAddedExtensionE2E.cs", edited);
            return await HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);
        }

        private static void AssertSkippedWithReason(
            HotReloadOrchestratorResult result,
            string methodName,
            string expectedReasonPart)
        {
            AssertKind(result, methodName, HotReloadMethodOutcomeKind.Skipped);
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Method.Contains("." + methodName + "("))
                {
                    Assert.That(outcome.Reason, Does.Contain(expectedReasonPart));
                    return;
                }
            }
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

        private static async Task<HotReloadOrchestratorResult> RunEditedFixtureAsync(
            string originalToken,
            string editedToken)
        {
            string fixturePath = Path.GetFullPath(
                Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", FixtureFileName));
            string onDisk = File.ReadAllText(fixturePath);
            string edited = onDisk.Replace(originalToken, editedToken);
            Assert.That(edited, Is.Not.EqualTo(onDisk), "Precondition: the token must be in the fixture.");
            string editedPath = HotReloadTestSourceWriter.WriteEditedSource("PublicClassExtensionE2E.cs", edited);
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
