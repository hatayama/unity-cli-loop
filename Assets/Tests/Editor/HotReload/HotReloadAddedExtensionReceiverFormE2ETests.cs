using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// End-to-end coverage for calls to an extension method the same reload adds, written on a
    /// receiver ('value.Added(a)'): each gives the outcome the same call written in the static form
    /// ('Extensions.Added(value, a)') gives, and none fails the file.
    /// </summary>
    public class HotReloadAddedExtensionReceiverFormE2ETests
    {
        private const string FixtureFileName = "HotReloadPublicClassExtensionCallFixtures.cs";

        private const string AddedExtensionAnchor = "        internal static int PlusOne(this int value, int add)";

        private const string AddedExtensions =
            "        internal static int AddedTriple(this int value, int add) { return value * 3 + add + 1000; }\n"
            + "        internal static int AddedSegmentPlus(this ArraySegment<int> segment, int add) { return segment.Count + add + 1000; }\n"
            + "        internal static int AddedMemoryPlus(this Memory<int> memory, int add) { return memory.Length + add + 1000; }\n"
            + "        internal static int AddedRefRead(this ref HotReloadExtensionCallPoint value, int add) { return value.X + add + 1000; }\n"
            + "        internal static int AddedLengthPlus(this string value, int add) { return value.Length + add + 1000; }\n"
            + "        internal static int AddedSkipped(this int value, int add) { Func<int> f = () => GenericEcho<int>(value, add); return f() + 1000; }\n\n";

        private const string NonPublicFixtureFileName = "HotReloadNonPublicClassExtensionCallFixtures.cs";

        private const string NonPublicAddedExtensionAnchor = "        internal static int NonPublicPlusOne(this int value, int add)";

        private const string StatementCall = "return _value.TriplePlus(7401);";

        private const string StructStatementCall = "return _point.PointRead(7421);";

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

        // Each receiver-form row sits beside the static-form row of the same call, which is the
        // oracle: both must give the same value.
        private static IEnumerable<TestCaseData> CallsThatApply()
        {
            yield return new TestCaseData("StatementCallsInternalExtension", StatementCall, "return _value.AddedTriple(7401);", 7410, 8410)
                .SetName("Run_CallToAddedExtension_IsPatchedLikeTheStaticFormAndReverts(int receiver)");
            yield return new TestCaseData("StatementCallsInternalExtension", StatementCall, "return HotReloadPublicClassInternalExtensions.AddedTriple(_value, 7401);", 7410, 8410)
                .SetName("Run_CallToAddedExtension_IsPatchedLikeTheStaticFormAndReverts(int static form)");
            yield return new TestCaseData("StatementCallsInternalExtension", StatementCall, "return new ArraySegment<int>(_ints).AddedSegmentPlus(7401);", 7410, 8403)
                .SetName("Run_CallToAddedExtension_IsPatchedLikeTheStaticFormAndReverts(ArraySegment receiver)");
            yield return new TestCaseData("StatementCallsInternalExtension", StatementCall, "return new Memory<int>(_ints).AddedMemoryPlus(7401);", 7410, 8403)
                .SetName("Run_CallToAddedExtension_IsPatchedLikeTheStaticFormAndReverts(Memory receiver)");
            yield return new TestCaseData("StatementCallsInternalExtension", StatementCall, "return HotReloadPublicClassInternalExtensions.AddedMemoryPlus(new Memory<int>(_ints), 7401);", 7410, 8403)
                .SetName("Run_CallToAddedExtension_IsPatchedLikeTheStaticFormAndReverts(Memory static form)");
            yield return new TestCaseData("StatementCallsInternalExtensionOnStruct", StructStatementCall, "return _point.AddedRefRead(7421);", 7426, 8426)
                .SetName("Run_CallToAddedExtension_IsPatchedLikeTheStaticFormAndReverts(ref receiver)");
            yield return new TestCaseData("StatementCallsInternalExtensionOnStruct", StructStatementCall, "return HotReloadPublicClassInternalExtensions.AddedRefRead(ref _point, 7421);", 7426, 8426)
                .SetName("Run_CallToAddedExtension_IsPatchedLikeTheStaticFormAndReverts(ref static form)");
            yield return new TestCaseData("StatementCallsInternalExtensionConditionally", "return _label?.NullableLengthPlus(7431) ?? 0;", "return _label == null ? 0 : HotReloadPublicClassInternalExtensions.AddedLengthPlus(_label, 7431);", 7434, 8434)
                .SetName("Run_CallToAddedExtension_IsPatchedLikeTheStaticFormAndReverts(null check and static form instead of conditional access)");
            yield return new TestCaseData("ClosureCallsInternalExtension", "Func<int> read = () => _value.TriplePlus(7101);", "Func<int> read = () => new Memory<int>(_ints).AddedMemoryPlus(7101);", 7110, 8103)
                .SetName("Run_CallToAddedExtension_IsPatchedLikeTheStaticFormAndReverts(Memory receiver in a lambda)");
        }

        private static IEnumerable<TestCaseData> CallsThatAreSkipped()
        {
            yield return new TestCaseData("StatementCallsInternalExtensionConditionally", "return _label?.NullableLengthPlus(7431) ?? 0;", "return _label?.AddedLengthPlus(7431) ?? 0;", 7434, "Added-method calls through conditional access are skipped")
                .SetName("Run_CallToAddedExtensionTheRewriteCannotExpress_IsSkippedAndTheFileApplies(conditional receiver)");
            yield return new TestCaseData("StatementCallsInternalExtension", StatementCall, "Func<int, int> f = _label.AddedLengthPlus; return f(7401);", 7410, "(such as 'a => _label.AddedLengthPlus(a)')")
                .SetName("Run_CallToAddedExtensionTheRewriteCannotExpress_IsSkippedAndTheFileApplies(method group on a receiver)");
            yield return new TestCaseData("StatementCallsInternalExtension", StatementCall, "string a = _label; Func<int, int> f = a.AddedLengthPlus; return f(7401);", 7410, "(such as 'b => a.AddedLengthPlus(b)')")
                .SetName("Run_CallToAddedExtensionTheRewriteCannotExpress_IsSkippedAndTheFileApplies(method group on a receiver named like a lambda parameter)");
            yield return new TestCaseData("StatementCallsInternalExtension", StatementCall, "return _value.AddedSkipped(7401);", 7410, "AddedSkipped(int, int)', which this reload skipped")
                .SetName("Run_CallToAddedExtensionTheRewriteCannotExpress_IsSkippedAndTheFileApplies(skipped callee on a receiver)");
            yield return new TestCaseData("StatementCallsInternalExtension", StatementCall, "return HotReloadPublicClassInternalExtensions.AddedSkipped(_value, 7401);", 7410, "AddedSkipped(int, int)', which this reload skipped")
                .SetName("Run_CallToAddedExtensionTheRewriteCannotExpress_IsSkippedAndTheFileApplies(skipped callee in the static form)");
        }

        /// <summary>
        /// What: a call to an extension method the same reload adds is Patched and returns the added
        /// method's value whether it is written on a receiver or in the static form, including a
        /// BCL twin receiver and a 'this ref' receiver; RevertAll restores the compiled value.
        /// </summary>
        [TestCaseSource(nameof(CallsThatApply))]
        public async Task Run_CallToAddedExtension_IsPatchedLikeTheStaticFormAndReverts(
            string hostMethod,
            string originalCall,
            string editedCall,
            int compiledValue,
            int patchedValue)
        {
            HotReloadPublicClassExtensionCallHost host = new HotReloadPublicClassExtensionCallHost();
            Assert.That(Read(host, hostMethod), Is.EqualTo(compiledValue));

            HotReloadOrchestratorResult result = await RunWithAddedExtensionsAsync(originalCall, editedCall);

            AssertNoFailedRow(result);
            Assert.That(FindOutcome(result, hostMethod).Kind, Is.EqualTo(HotReloadMethodOutcomeKind.Patched));
            Assert.That(Read(host, hostMethod), Is.EqualTo(patchedValue));
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            Assert.That(Read(host, hostMethod), Is.EqualTo(compiledValue));
        }

        /// <summary>
        /// What: a call on a receiver to an extension method the same reload adds that the rewrite
        /// cannot express (a conditional receiver, a method group, a callee the reload skips) is
        /// Skipped with the reason for that shape rather than failing the file, keeps its compiled
        /// value, and the rest of the file still applies. A method group's example lambda calls
        /// the method on the receiver with parameter names the receiver does not use.
        /// </summary>
        [TestCaseSource(nameof(CallsThatAreSkipped))]
        public async Task Run_CallToAddedExtensionTheRewriteCannotExpress_IsSkippedAndTheFileApplies(
            string hostMethod,
            string originalCall,
            string editedCall,
            int compiledValue,
            string expectedReason)
        {
            HotReloadPublicClassExtensionCallHost host = new HotReloadPublicClassExtensionCallHost();
            Assert.That(Read(host, hostMethod), Is.EqualTo(compiledValue));

            HotReloadOrchestratorResult result = await RunWithAddedExtensionsAsync(originalCall, editedCall);

            AssertNoFailedRow(result);
            HotReloadMethodOutcome outcome = FindOutcome(result, hostMethod);
            Assert.That(outcome.Kind, Is.EqualTo(HotReloadMethodOutcomeKind.Skipped));
            Assert.That(outcome.Reason, Does.Contain(expectedReason));
            Assert.That(Read(host, hostMethod), Is.EqualTo(compiledValue));
            Assert.That(host.ClosureCallsInternalExtensionInStaticForm(), Is.EqualTo(7131));
        }

        /// <summary>
        /// What: when one edit changes the return type of an extension method of a public class and
        /// calls it from a lambda and from a method's own statements, both callers are Skipped with
        /// the signature-change reason and keep their compiled values, in the receiver form as in
        /// the static form, with no Failed row.
        /// </summary>
        [TestCase("(int)_point.PointRead(7192)", "(int)_point.PointRead(7422)")]
        [TestCase("(int)HotReloadPublicClassInternalExtensions.PointRead(_point, 7192)", "(int)HotReloadPublicClassInternalExtensions.PointRead(_point, 7422)")]
        public async Task Run_CallsToExtensionWhoseReturnTypeChanged_AreSkippedLikeTheStaticForm(string lambdaCall, string statementCall)
        {
            HotReloadPublicClassExtensionCallHost host = new HotReloadPublicClassExtensionCallHost();
            Assert.That(host.ClosureCallsInternalExtensionOnStructByValue(), Is.EqualTo(7196));
            Assert.That(host.StatementCallsInternalExtensionOnStruct(), Is.EqualTo(7426));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync(
                FixtureFileName,
                "internal static int PointRead(this HotReloadExtensionCallPoint value, int add)",
                "internal static long PointRead(this HotReloadExtensionCallPoint value, int add)",
                "Func<int> read = () => _point.PointRead(7191);",
                "Func<int> read = () => " + lambdaCall + ";",
                StructStatementCall,
                "return " + statementCall + ";");

            AssertNoFailedRow(result);
            foreach (string hostMethod in new[] { "ClosureCallsInternalExtensionOnStructByValue", "StatementCallsInternalExtensionOnStruct" })
            {
                HotReloadMethodOutcome outcome = FindOutcome(result, hostMethod);
                Assert.That(outcome.Kind, Is.EqualTo(HotReloadMethodOutcomeKind.Skipped), hostMethod);
                Assert.That(outcome.Reason, Does.Contain("signature change was not applied"), hostMethod);
            }

            Assert.That(host.ClosureCallsInternalExtensionOnStructByValue(), Is.EqualTo(7196));
            Assert.That(host.StatementCallsInternalExtensionOnStruct(), Is.EqualTo(7426));
        }

        /// <summary>
        /// What: a lambda and an async method that call an extension method the same reload adds to
        /// an internal static class are Patched and return the added method's value, in the receiver
        /// form as in the static form; RevertAll restores the compiled values.
        /// </summary>
        [TestCase("_value.NonPublicAddedTriple(6101)", "_value.NonPublicAddedTriple(6111)")]
        [TestCase("HotReloadNonPublicClassExtensions.NonPublicAddedTriple(_value, 6101)", "HotReloadNonPublicClassExtensions.NonPublicAddedTriple(_value, 6111)")]
        public async Task Run_CallsToExtensionAddedToAnInternalClass_ArePatchedLikeTheStaticFormAndRevert(string lambdaCall, string asyncCall)
        {
            HotReloadNonPublicClassExtensionCallHost host = new HotReloadNonPublicClassExtensionCallHost();
            Assert.That(host.ClosureCallsNonPublicClassExtension(), Is.EqualTo(6110));
            Assert.That(await host.AsyncCallsNonPublicClassExtension(), Is.EqualTo(6120));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync(
                NonPublicFixtureFileName,
                NonPublicAddedExtensionAnchor,
                "        internal static int NonPublicAddedTriple(this int value, int add) { return value * 3 + add + 1000; }\n\n"
                + NonPublicAddedExtensionAnchor,
                "Func<int> read = () => _value.NonPublicTriple(6101);",
                "Func<int> read = () => " + lambdaCall + ";",
                "return _value.NonPublicTriple(6111);",
                "return " + asyncCall + ";");

            AssertNoFailedRow(result);
            Assert.That(FindOutcome(result, "ClosureCallsNonPublicClassExtension").Kind, Is.EqualTo(HotReloadMethodOutcomeKind.Patched));
            Assert.That(FindOutcome(result, "AsyncCallsNonPublicClassExtension").Kind, Is.EqualTo(HotReloadMethodOutcomeKind.Patched));
            Assert.That(host.ClosureCallsNonPublicClassExtension(), Is.EqualTo(7110));
            Assert.That(await host.AsyncCallsNonPublicClassExtension(), Is.EqualTo(7120));
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            Assert.That(host.ClosureCallsNonPublicClassExtension(), Is.EqualTo(6110));
            Assert.That(await host.AsyncCallsNonPublicClassExtension(), Is.EqualTo(6120));
        }

        private static int Read(HotReloadPublicClassExtensionCallHost host, string hostMethod)
        {
            switch (hostMethod)
            {
                case "StatementCallsInternalExtension":
                    return host.StatementCallsInternalExtension();
                case "StatementCallsInternalExtensionOnStruct":
                    return host.StatementCallsInternalExtensionOnStruct();
                case "StatementCallsInternalExtensionConditionally":
                    return host.StatementCallsInternalExtensionConditionally();
                case "ClosureCallsInternalExtension":
                    return host.ClosureCallsInternalExtension();
                default:
                    Assert.Fail("Unknown host method: " + hostMethod);
                    return 0;
            }
        }

        // Why 7121: every test also edits a call outside the method under test, so a Skipped row
        // can show that the rest of the file still applies.
        private static Task<HotReloadOrchestratorResult> RunWithAddedExtensionsAsync(string originalCall, string editedCall)
        {
            return RunEditedFixtureAsync(
                FixtureFileName,
                AddedExtensionAnchor,
                AddedExtensions + AddedExtensionAnchor,
                originalCall,
                editedCall,
                "7121",
                "7122");
        }

        // Why pairs of strings and not a dictionary: the replacements run in order, and each must
        // find its text in the fixture, so a typo fails the precondition instead of editing nothing.
        private static async Task<HotReloadOrchestratorResult> RunEditedFixtureAsync(string fixtureFileName, params string[] replacements)
        {
            string fixturePath = Path.GetFullPath(
                Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", fixtureFileName));
            string edited = File.ReadAllText(fixturePath);
            for (int index = 0; index < replacements.Length; index += 2)
            {
                Assert.That(edited.Contains(replacements[index]), "Precondition: the fixture must contain " + replacements[index]);
                edited = edited.Replace(replacements[index], replacements[index + 1]);
            }

            string editedPath = HotReloadTestSourceWriter.WriteEditedSource(
                "AddedExtensionReceiverFormE2E" + System.Math.Abs(edited.GetHashCode()) + ".cs",
                edited);
            return await HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);
        }

        private static HotReloadMethodOutcome FindOutcome(HotReloadOrchestratorResult result, string hostMethod)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Method.Contains("." + hostMethod + "("))
                {
                    return outcome;
                }
            }

            Assert.Fail("No outcome row for " + hostMethod + ".");
            return null;
        }

        private static void AssertNoFailedRow(HotReloadOrchestratorResult result)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                Assert.That(
                    outcome.Kind,
                    Is.Not.EqualTo(HotReloadMethodOutcomeKind.Failed),
                    outcome.Method + " :: " + outcome.Reason);
            }
        }
    }
}
