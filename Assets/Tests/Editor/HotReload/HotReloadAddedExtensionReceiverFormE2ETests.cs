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
        }

        private static IEnumerable<TestCaseData> CallsThatAreSkipped()
        {
            yield return new TestCaseData("StatementCallsInternalExtensionConditionally", "return _label?.NullableLengthPlus(7431) ?? 0;", "return _label?.AddedLengthPlus(7431) ?? 0;", 7434, "Added-method calls through conditional access are skipped")
                .SetName("Run_CallToAddedExtensionTheRewriteCannotExpress_IsSkippedAndTheFileApplies(conditional receiver)");
            yield return new TestCaseData("StatementCallsInternalExtension", StatementCall, "Func<int, int> f = _label.AddedLengthPlus; return f(7401);", 7410, "(such as 'a => _label.AddedLengthPlus(a)')")
                .SetName("Run_CallToAddedExtensionTheRewriteCannotExpress_IsSkippedAndTheFileApplies(method group on a receiver)");
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
        /// value, and the rest of the file still applies.
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
                default:
                    Assert.Fail("Unknown host method: " + hostMethod);
                    return 0;
            }
        }

        private static async Task<HotReloadOrchestratorResult> RunWithAddedExtensionsAsync(string originalCall, string editedCall)
        {
            string fixturePath = Path.GetFullPath(
                Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", FixtureFileName));
            string onDisk = File.ReadAllText(fixturePath);
            Assert.That(onDisk.Contains(AddedExtensionAnchor), "Precondition: the anchor must be in the fixture.");
            Assert.That(onDisk.Contains(originalCall), "Precondition: the call must be in the fixture.");
            string edited = onDisk
                .Replace(AddedExtensionAnchor, AddedExtensions + AddedExtensionAnchor)
                .Replace(originalCall, editedCall)
                .Replace("7121", "7122");
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
