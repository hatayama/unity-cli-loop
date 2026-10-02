using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;
using UnityEditor.TestTools.TestRunner.Api;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

using NUnitMethodInfo = NUnit.Framework.Interfaces.IMethodInfo;
using NUnitTNode = NUnit.Framework.Interfaces.TNode;
using NUnitTypeInfo = NUnit.Framework.Interfaces.ITypeInfo;
using TestResultStatus = UnityEditor.TestTools.TestRunner.Api.TestStatus;
using TestRunnerMode = UnityEditor.TestTools.TestRunner.Api.TestMode;
using TestRunState = UnityEditor.TestTools.TestRunner.Api.RunState;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the run-tests result conversion edges (missing results, suites without children, and the
    /// detail list limit), the unified run callback, the unavailable Test Framework service, and the
    /// cancel bridge's resolution.
    /// </summary>
    public sealed class RunTestsResultConversionEdgeTests
    {
        private const int OverLimitCount = RunTestsConstants.FailedTestDetailsLimit + 2;

        [Test]
        public void FromTestResult_WithoutAResult_ReportsAnExecutionFailure()
        {
            // Verifies a missing result becomes a failed execution with zero counts instead of throwing.
            SerializableTestResult result = SerializableTestResultConverter.FromTestResult(null);

            Assert.That(result.success, Is.False);
            Assert.That(result.status, Is.EqualTo(RunTestsExecutionStatus.ExecutionFailed));
            Assert.That(result.message, Is.EqualTo("Test execution failed: no test result was produced"));
            Assert.That(result.testCount, Is.EqualTo(0));
        }

        [Test]
        public void FromTestResult_WithAPassedSuiteWithoutChildren_ReportsNoTests()
        {
            // Verifies a suite that carries no child list counts no tests and collects no details.
            SerializableTestResult result = SerializableTestResultConverter.FromTestResult(
                Suite("Root", TestResultStatus.Passed, null));

            Assert.That(result.noTestsFound, Is.True);
            Assert.That(result.testCount, Is.EqualTo(0));
            Assert.That(result.failedTests, Is.Null);
            Assert.That(result.skippedTests, Is.Null);
            Assert.That(result.inconclusiveTests, Is.Null);
            Assert.That(result.failedSuites, Is.Null);
        }

        [Test]
        public void FromTestResult_WithAFailedSuiteWithoutChildren_ReportsTheSuiteAsTheFailure()
        {
            // Verifies a failed suite with no child list is reported as the suite failure that explains the run.
            SerializableTestResult result = SerializableTestResultConverter.FromTestResult(
                Suite("Root", TestResultStatus.Failed, null, message: "cancelled"));

            Assert.That(result.hasFailures, Is.True);
            Assert.That(result.failedSuites.Select(detail => detail.FullName).ToArray(), Is.EqualTo(new[] { "Example.Tests.Root" }));
            Assert.That(result.failedSuites[0].Message, Is.EqualTo("cancelled"));
        }

        [Test]
        public void FromTestResult_WithMoreFailuresThanTheLimit_KeepsOnlyTheFirstDetails()
        {
            // Verifies failed test details stop at the limit while the failed count still covers every test.
            SerializableTestResult result = SerializableTestResultConverter.FromTestResult(
                SuiteOfLeaves(TestResultStatus.Failed));

            Assert.That(result.failedCount, Is.EqualTo(OverLimitCount));
            Assert.That(result.failedTests.Length, Is.EqualTo(RunTestsConstants.FailedTestDetailsLimit));
            Assert.That(result.failedTests[0].FullName, Is.EqualTo("Example.Tests.Group0.Leaf0"));
            Assert.That(result.failedTests.Last().FullName, Is.EqualTo("Example.Tests.Group1.Leaf3"));
        }

        [Test]
        public void FromTestResult_WithMoreSkipsThanTheLimit_KeepsOnlyTheFirstNames()
        {
            // Verifies skipped test names stop at the limit while the skipped count still covers every test.
            SerializableTestResult result = SerializableTestResultConverter.FromTestResult(
                SuiteOfLeaves(TestResultStatus.Skipped));

            Assert.That(result.skippedCount, Is.EqualTo(OverLimitCount));
            Assert.That(result.skippedTests.Length, Is.EqualTo(RunTestsConstants.FailedTestDetailsLimit));
            Assert.That(result.skippedTests.Last(), Is.EqualTo("Example.Tests.Group1.Leaf3"));
        }

        [Test]
        public void FromTestResult_WithMoreInconclusivesThanTheLimit_KeepsOnlyTheFirstDetails()
        {
            // Verifies inconclusive details stop at the limit while the inconclusive count still covers every test.
            SerializableTestResult result = SerializableTestResultConverter.FromTestResult(
                SuiteOfLeaves(TestResultStatus.Inconclusive));

            Assert.That(result.inconclusiveCount, Is.EqualTo(OverLimitCount));
            Assert.That(result.inconclusiveTests.Length, Is.EqualTo(RunTestsConstants.FailedTestDetailsLimit));
            Assert.That(result.inconclusiveTests.Last().FullName, Is.EqualTo("Example.Tests.Group1.Leaf3"));
        }

        [Test]
        public void FromTestResult_WithMoreFailedSuitesThanTheLimit_KeepsOnlyTheFirstSuites()
        {
            // Verifies suite failures outside their tests stop at the limit.
            List<ITestResultAdaptor> suites = new List<ITestResultAdaptor>();
            for (int i = 0; i < OverLimitCount; i++)
            {
                suites.Add(Suite("Fixture" + i, TestResultStatus.Failed, new List<ITestResultAdaptor>(), resultState: "Failed(SetUp)"));
            }

            SerializableTestResult result = SerializableTestResultConverter.FromTestResult(
                Suite("Root", TestResultStatus.Failed, suites, resultState: "Failed:Child"));

            Assert.That(result.failedSuites.Length, Is.EqualTo(RunTestsConstants.FailedTestDetailsLimit));
            Assert.That(result.failedSuites[0].FullName, Is.EqualTo("Example.Tests.Fixture0"));
        }

        [Test]
        public void UnifiedTestCallback_RunFinished_PublishesTheConvertedResult()
        {
            // Verifies a finished run raises the completion event with the converted result and the raw adaptor.
            UnifiedTestCallback callback = new UnifiedTestCallback();
            ITestResultAdaptor rawResult = Suite("Root", TestResultStatus.Passed, new List<ITestResultAdaptor>
            {
                Leaf("Root", "Only", TestResultStatus.Passed)
            });
            List<SerializableTestResult> published = new List<SerializableTestResult>();
            List<ITestResultAdaptor> publishedRaw = new List<ITestResultAdaptor>();
            callback.OnTestCompleted += (result, raw) =>
            {
                published.Add(result);
                publishedRaw.Add(raw);
            };

            callback.RunFinished(rawResult);

            Assert.That(published.Count, Is.EqualTo(1));
            Assert.That(published[0].success, Is.True);
            Assert.That(published[0].passedCount, Is.EqualTo(1));
            Assert.That(publishedRaw[0], Is.SameAs(rawResult));
        }

        [Test]
        public void UnifiedTestCallback_AfterDispose_StopsPublishing()
        {
            // Verifies disposing the callback drops its subscribers so a late finish publishes nothing.
            UnifiedTestCallback callback = new UnifiedTestCallback();
            int publishedCount = 0;
            callback.OnTestCompleted += (result, raw) => publishedCount++;

            callback.Dispose();
            callback.RunFinished(Suite("Root", TestResultStatus.Passed, new List<ITestResultAdaptor>()));

            Assert.That(publishedCount, Is.EqualTo(0));
        }

        [Test]
        public async Task TestFrameworkUnavailableExecutionService_ReturnsUnavailableResults()
        {
            // Verifies every entry point answers with the unavailable result instead of running tests.
            TestFrameworkUnavailableExecutionService service = new TestFrameworkUnavailableExecutionService();

            SerializableTestResult playMode = await service.ExecutePlayModeTestAsync(null, CancellationToken.None, null);
            SerializableTestResult editMode = await service.ExecuteEditModeTestAsync(null, CancellationToken.None);
            RunTestsUnfilteredTestListResult names = await service.RetrieveUnfilteredTestNamesAsync(
                UnityCliLoopTestMode.EditMode,
                CancellationToken.None);
            RunTestsPredefinedAssemblyTestFindings findings = service.ScanPredefinedAssemblyTests();

            Assert.That(playMode.message, Is.EqualTo(RunTestsResponse.TestFrameworkUnavailableMessage));
            Assert.That(editMode.message, Is.EqualTo(RunTestsResponse.TestFrameworkUnavailableMessage));
            Assert.That(names.Retrieved, Is.False);
            Assert.That(findings.TotalCount, Is.EqualTo(0));
        }

        [Test]
        public void TestFrameworkUnavailableExecutionService_WithACanceledToken_Throws()
        {
            // Verifies a canceled request is rejected before the unavailable result is produced.
            TestFrameworkUnavailableExecutionService service = new TestFrameworkUnavailableExecutionService();
            CancellationToken canceled = new CancellationToken(true);

            Assert.Throws<OperationCanceledException>(() => service.ExecutePlayModeTestAsync(null, canceled, null));
            Assert.Throws<OperationCanceledException>(() => service.ExecuteEditModeTestAsync(null, canceled));
            Assert.Throws<OperationCanceledException>(
                () => service.RetrieveUnfilteredTestNamesAsync(UnityCliLoopTestMode.EditMode, canceled));
        }

        [Test]
        public void TestExecutionService_WithACanceledToken_ThrowsBeforeDispatching()
        {
            // Verifies a canceled request never reaches the Test Runner.
            TestExecutionService service = new TestExecutionService();
            CancellationToken canceled = new CancellationToken(true);

            Assert.Throws<OperationCanceledException>(() => service.ExecutePlayModeTestAsync(null, canceled, null));
            Assert.Throws<OperationCanceledException>(() => service.ExecuteEditModeTestAsync(null, canceled));
            Assert.Throws<OperationCanceledException>(
                () => service.RetrieveUnfilteredTestNamesAsync(UnityCliLoopTestMode.EditMode, canceled));
        }

        [Test]
        public void TestRunnerApiCancelBridge_ReportsTheMethodsTheLookupResolves()
        {
            // Verifies the bridge exposes exactly the cancel helpers the lookup finds on this Test Framework.
            (MethodInfo cancel, MethodInfo isRunActive, string _) =
                TestRunnerApiCancelMethodLookup.Resolve(typeof(TestRunnerApi));

            Assert.That(TestRunnerApiCancelBridge.HasCancelTestRun, Is.EqualTo(cancel != null));
            Assert.That(TestRunnerApiCancelBridge.HasIsRunActive, Is.EqualTo(isRunActive != null));
        }

        [Test]
        public void TestRunnerApiCancelBridge_WithoutARunGuid_DoesNotCancel()
        {
            // Verifies a missing run GUID never cancels a run.
            Assert.That(TestRunnerApiCancelBridge.TryCancelTestRun(null), Is.False);
            Assert.That(TestRunnerApiCancelBridge.TryCancelTestRun(string.Empty), Is.False);
        }

        private static ITestResultAdaptor SuiteOfLeaves(TestResultStatus leafStatus)
        {
            // Two groups split the leaves so the limit is reached inside the second group, exercising both
            // the per-child stop and the stop at a later sibling.
            List<ITestResultAdaptor> firstGroup = new List<ITestResultAdaptor>();
            List<ITestResultAdaptor> secondGroup = new List<ITestResultAdaptor>();
            for (int i = 0; i < 6; i++)
            {
                firstGroup.Add(Leaf("Group0", "Leaf" + i, leafStatus));
            }

            for (int i = 0; i < 6; i++)
            {
                secondGroup.Add(Leaf("Group1", "Leaf" + i, leafStatus));
            }

            TestResultStatus suiteStatus = leafStatus == TestResultStatus.Failed ? TestResultStatus.Failed : TestResultStatus.Passed;
            return Suite("Root", suiteStatus, new List<ITestResultAdaptor>
            {
                Suite("Group0", suiteStatus, firstGroup, resultState: suiteStatus == TestResultStatus.Failed ? "Failed:Child" : null),
                Suite("Group1", suiteStatus, secondGroup, resultState: suiteStatus == TestResultStatus.Failed ? "Failed:Child" : null),
                Suite("Group2", suiteStatus, new List<ITestResultAdaptor>(), resultState: suiteStatus == TestResultStatus.Failed ? "Failed:Child" : null)
            }, resultState: suiteStatus == TestResultStatus.Failed ? "Failed:Child" : null);
        }

        private static ITestResultAdaptor Leaf(string group, string name, TestResultStatus status)
        {
            return new FakeTestResultAdaptor(new FakeTestAdaptor(group + "." + name, isSuite: false), status, null, string.Empty, null);
        }

        private static ITestResultAdaptor Suite(
            string name,
            TestResultStatus status,
            IReadOnlyList<ITestResultAdaptor> children,
            string message = "",
            string resultState = null)
        {
            return new FakeTestResultAdaptor(new FakeTestAdaptor(name, isSuite: true), status, children, message, resultState);
        }

        private sealed class FakeTestResultAdaptor : ITestResultAdaptor
        {
            private readonly IReadOnlyList<ITestResultAdaptor> _children;

            public FakeTestResultAdaptor(
                ITestAdaptor test,
                TestResultStatus status,
                IReadOnlyList<ITestResultAdaptor> children,
                string message,
                string resultState)
            {
                Test = test;
                TestStatus = status;
                _children = children;
                Message = message;
                ResultState = resultState ?? status.ToString();
            }

            public ITestAdaptor Test { get; }
            public string Name => Test.Name;
            public string FullName => Test.FullName;
            public string ResultState { get; }
            public TestResultStatus TestStatus { get; }
            public double Duration => 0d;
            public DateTime StartTime => new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            public DateTime EndTime => StartTime;
            public string Message { get; }
            public string StackTrace => string.Empty;
            public int AssertCount => 0;
            public int FailCount => 0;
            public int PassCount => 0;
            public int SkipCount => 0;
            public int InconclusiveCount => 0;
            public bool HasChildren => _children != null && _children.Count > 0;
            public IEnumerable<ITestResultAdaptor> Children => _children;
            public string Output => string.Empty;

            public NUnitTNode ToXml()
            {
                return new NUnitTNode("test-result");
            }
        }

        private sealed class FakeTestAdaptor : ITestAdaptor
        {
            public FakeTestAdaptor(string name, bool isSuite)
            {
                Name = name;
                IsSuite = isSuite;
            }

            public string Id => FullName;
            public string Name { get; }
            public string FullName => $"Example.Tests.{Name}";
            public int TestCaseCount => IsSuite ? 0 : 1;
            public bool HasChildren => false;
            public bool IsSuite { get; }
            public IEnumerable<ITestAdaptor> Children => new List<ITestAdaptor>();
            public ITestAdaptor Parent => null;
            public int TestCaseTimeout => 0;
            public NUnitTypeInfo TypeInfo => null;
            public NUnitMethodInfo Method => null;
            public object[] Arguments => Array.Empty<object>();
            public string[] Categories => Array.Empty<string>();
            public bool IsTestAssembly => false;
            public TestRunState RunState => TestRunState.Runnable;
            public string Description => string.Empty;
            public string SkipReason => string.Empty;
            public string ParentId => string.Empty;
            public string ParentFullName => string.Empty;
            public string UniqueName => FullName;
            public string ParentUniqueName => string.Empty;
            public int ChildIndex => 0;
            public TestRunnerMode TestMode => TestRunnerMode.EditMode;
        }
    }
}
