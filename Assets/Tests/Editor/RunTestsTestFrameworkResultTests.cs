using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using NUnit.Framework;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.TestTools;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

using NUnitMethodInfo = NUnit.Framework.Interfaces.IMethodInfo;
using NUnitTNode = NUnit.Framework.Interfaces.TNode;
using NUnitTypeInfo = NUnit.Framework.Interfaces.ITypeInfo;
using TestRunnerMode = UnityEditor.TestTools.TestRunner.Api.TestMode;
using TestResultStatus = UnityEditor.TestTools.TestRunner.Api.TestStatus;
using TestRunState = UnityEditor.TestTools.TestRunner.Api.RunState;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests Run Tests Test Framework result serialization behavior.
    /// </summary>
    public sealed class RunTestsTestFrameworkResultTests
    {
        [Test]
        public void SaveTestResultAsXml_WhenSavingResult_UsesCollisionResistantFileName()
        {
            // Verifies that NUnit XML filenames include entropy beyond second-resolution timestamps.
            ITestResultAdaptor result = CreateTestSuite(
                "RootSuite",
                TestResultStatus.Passed,
                0.1,
                new List<ITestResultAdaptor>
                {
                    CreateTestCase("PassingTest", TestResultStatus.Passed, 0.1)
                });

            string filePath = NUnitXmlResultExporter.SaveTestResultAsXml(result);

            try
            {
                string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);

                Assert.That(fileNameWithoutExtension, Does.Not.Match("^\\d{8}_\\d{6}$"));
            }
            finally
            {
                DeleteIfExists(filePath);
            }
        }

        [Test]
        public void SaveTestResultAsXml_WhenCurrentCultureUsesCommaDecimal_WritesInvariantDuration()
        {
            // Verifies that NUnit XML duration values use invariant decimal separators.
            CultureInfo originalCulture = Thread.CurrentThread.CurrentCulture;
            CultureInfo originalUiCulture = Thread.CurrentThread.CurrentUICulture;
            CultureInfo commaDecimalCulture = new CultureInfo("fr-FR");
            ITestResultAdaptor result = CreateTestSuite(
                "RootSuite",
                TestResultStatus.Passed,
                1.25,
                new List<ITestResultAdaptor>
                {
                    CreateTestCase("PassingTest", TestResultStatus.Passed, 1.25)
                });

            Thread.CurrentThread.CurrentCulture = commaDecimalCulture;
            Thread.CurrentThread.CurrentUICulture = commaDecimalCulture;

            string filePath = null;
            try
            {
                XmlDocument document = SaveResultAndLoadXml(result, out filePath);
                XmlElement testRun = document.DocumentElement;

                Assert.That(testRun.GetAttribute("duration"), Is.EqualTo("1.250"));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = originalCulture;
                Thread.CurrentThread.CurrentUICulture = originalUiCulture;
                DeleteIfExists(filePath);
            }
        }

        [Test]
        public void SaveTestResultAsXml_WhenResultIsInconclusive_WritesInconclusiveAggregate()
        {
            // Verifies that inconclusive test results are not reported as passed in NUnit XML.
            ITestResultAdaptor result = CreateTestSuite(
                "RootSuite",
                TestResultStatus.Inconclusive,
                0.5,
                new List<ITestResultAdaptor>
                {
                    CreateTestCase("InconclusiveTest", TestResultStatus.Inconclusive, 0.5)
                });

            string filePath = null;
            try
            {
                XmlDocument document = SaveResultAndLoadXml(result, out filePath);
                XmlElement testRun = document.DocumentElement;
                XmlNode testCase = document.SelectSingleNode("//test-case");

                Assert.That(testRun.GetAttribute("result"), Is.EqualTo("Inconclusive"));
                Assert.That(testRun.GetAttribute("inconclusive"), Is.EqualTo("1"));
                Assert.That(testCase.Attributes["result"].Value, Is.EqualTo("Inconclusive"));
            }
            finally
            {
                DeleteIfExists(filePath);
            }
        }

        [Test]
        public void FromTestResult_WhenResultIsNull_ReturnsFailureWithoutCounts()
        {
            // Verifies that missing Unity Test Runner results are surfaced as execution failures.
            SerializableTestResult result = SerializableTestResultConverter.FromTestResult(null);

            Assert.That(result.success, Is.False);
            Assert.That(result.status, Is.EqualTo(RunTestsExecutionStatus.ExecutionFailed));
            Assert.That(result.hasFailures, Is.False);
            Assert.That(result.noTestsFound, Is.False);
            Assert.That(result.noTestsFoundExplanation, Is.Empty);
            Assert.That(result.message, Is.EqualTo("Test execution failed: no test result was produced"));
            Assert.That(result.testCount, Is.EqualTo(0));
            Assert.That(result.passedCount, Is.EqualTo(0));
            Assert.That(result.failedCount, Is.EqualTo(0));
            Assert.That(result.skippedCount, Is.EqualTo(0));
            Assert.That(result.xmlPath, Is.Null);
            Assert.That(result.failedTests, Is.Null);
        }

        [Test]
        public void FromTestResult_WhenNoTestsWereDiscovered_ReturnsNoTestsFoundState()
        {
            // Verifies that zero discovered tests are reported separately from failed tests.
            ITestResultAdaptor resultAdaptor = CreateTestSuite(
                "RootSuite",
                TestResultStatus.Passed,
                0.1,
                new List<ITestResultAdaptor>());

            SerializableTestResult result = SerializableTestResultConverter.FromTestResult(resultAdaptor);

            Assert.That(result.success, Is.False);
            Assert.That(result.status, Is.EqualTo(RunTestsExecutionStatus.NoTestsFound));
            Assert.That(result.hasFailures, Is.False);
            Assert.That(result.noTestsFound, Is.True);
            Assert.That(result.noTestsFoundExplanation, Does.Contain("not a test failure"));
            Assert.That(result.message, Is.EqualTo(RunTestsResponse.NoTestsFoundMessage));
            Assert.That(result.testCount, Is.EqualTo(0));
            Assert.That(result.failedCount, Is.EqualTo(0));
            Assert.That(result.failedTests, Is.Null);
        }

        [Test]
        public void FromTestResult_WhenAChildTestFails_ReturnsFailureState()
        {
            // Verifies that real failed tests are marked independently from command success.
            ITestResultAdaptor resultAdaptor = CreateTestSuite(
                "RootSuite",
                TestResultStatus.Failed,
                0.1,
                new List<ITestResultAdaptor>
                {
                    CreateTestCase("FailingTest", TestResultStatus.Failed, 0.1)
                });

            SerializableTestResult result = SerializableTestResultConverter.FromTestResult(resultAdaptor);

            Assert.That(result.success, Is.False);
            Assert.That(result.status, Is.EqualTo(RunTestsExecutionStatus.Failed));
            Assert.That(result.hasFailures, Is.True);
            Assert.That(result.noTestsFound, Is.False);
            Assert.That(result.noTestsFoundExplanation, Is.Empty);
            Assert.That(result.testCount, Is.EqualTo(1));
            Assert.That(result.failedCount, Is.EqualTo(1));
        }

        /// <summary>
        /// What: a failed leaf copies FullName, Message, and File/Line parsed from (at path:line).
        /// </summary>
        [Test]
        public void FromTestResult_WhenAChildTestFails_CollectsFailedTestDetailWithStackLocation()
        {
            ITestResultAdaptor resultAdaptor = CreateTestSuite(
                "RootSuite",
                TestResultStatus.Failed,
                0.1,
                new List<ITestResultAdaptor>
                {
                    CreateTestCase(
                        "FailingTest",
                        TestResultStatus.Failed,
                        0.1,
                        "Expected 2 But was: 1",
                        "at Example.Tests.FailingTest () [0x00000] in /ignored/path.cs:1\n  (at Assets/Tests/FailingTest.cs:42)")
                });

            SerializableTestResult result = SerializableTestResultConverter.FromTestResult(resultAdaptor);

            Assert.That(result.failedTests, Is.Not.Null);
            Assert.That(result.failedTests.Length, Is.EqualTo(1));
            Assert.That(result.failedTests[0].FullName, Is.EqualTo("Example.Tests.FailingTest"));
            Assert.That(result.failedTests[0].Message, Is.EqualTo("Expected 2 But was: 1"));
            Assert.That(result.failedTests[0].File, Is.EqualTo("Assets/Tests/FailingTest.cs"));
            Assert.That(result.failedTests[0].Line, Is.EqualTo(42));
        }

        /// <summary>
        /// What: only the first 10 failed leaves are listed when eleven tests fail.
        /// </summary>
        [Test]
        public void FromTestResult_WhenElevenTestsFail_ListsFirstTenFailedDetails()
        {
            List<ITestResultAdaptor> children = new List<ITestResultAdaptor>();
            for (int index = 0; index < 11; index++)
            {
                string suffix = index.ToString(CultureInfo.InvariantCulture);
                children.Add(
                    CreateTestCase(
                        "FailingTest" + suffix,
                        TestResultStatus.Failed,
                        0.1,
                        "boom " + suffix,
                        "(at Assets/Tests/FailingTest.cs:" + (10 + index).ToString(CultureInfo.InvariantCulture) + ")"));
            }

            ITestResultAdaptor resultAdaptor = CreateTestSuite(
                "RootSuite",
                TestResultStatus.Failed,
                0.1,
                children);

            SerializableTestResult result = SerializableTestResultConverter.FromTestResult(resultAdaptor);

            Assert.That(result.failedCount, Is.EqualTo(11));
            Assert.That(result.failedTests, Is.Not.Null);
            Assert.That(result.failedTests.Length, Is.EqualTo(10));
            Assert.That(result.failedTests[0].FullName, Is.EqualTo("Example.Tests.FailingTest0"));
            Assert.That(result.failedTests[9].FullName, Is.EqualTo("Example.Tests.FailingTest9"));
        }

        /// <summary>
        /// What: a passing leaf is omitted from FailedTests even when a sibling failed.
        /// </summary>
        [Test]
        public void FromTestResult_WhenMixedPassAndFail_OmitsPassingLeafFromFailedTests()
        {
            ITestResultAdaptor resultAdaptor = CreateTestSuite(
                "RootSuite",
                TestResultStatus.Failed,
                0.1,
                new List<ITestResultAdaptor>
                {
                    CreateTestCase("PassingTest", TestResultStatus.Passed, 0.1),
                    CreateTestCase(
                        "FailingTest",
                        TestResultStatus.Failed,
                        0.1,
                        "failed")
                });

            SerializableTestResult result = SerializableTestResultConverter.FromTestResult(resultAdaptor);

            Assert.That(result.failedCount, Is.EqualTo(1));
            Assert.That(result.failedTests.Length, Is.EqualTo(1));
            Assert.That(result.failedTests[0].FullName, Is.EqualTo("Example.Tests.FailingTest"));
            Assert.That(result.failedTests[0].File, Is.Null);
            Assert.That(result.failedTests[0].Line, Is.Null);
        }

        [Test]
        public void TrySaveFailureXml_WhenExporterThrows_ReturnsNull()
        {
            // Verifies that XML export failure does not prevent test completion.
            ITestResultAdaptor result = new FakeTestResultAdaptor(
                null,
                TestResultStatus.Failed,
                0.1,
                new List<ITestResultAdaptor>());
            Regex warningPattern = new Regex("^Failed to save failure XML result file: ");

            LogAssert.Expect(LogType.Warning, warningPattern);

            string xmlPath = PlayModeTestExecuter.TrySaveFailureXml(result);

            Assert.That(xmlPath, Is.Null);
        }

        private static XmlDocument SaveResultAndLoadXml(ITestResultAdaptor result, out string filePath)
        {
            filePath = NUnitXmlResultExporter.SaveTestResultAsXml(result);
            XmlDocument document = new XmlDocument();
            document.Load(filePath);
            return document;
        }

        private static void DeleteIfExists(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return;
            }

            if (!File.Exists(filePath))
            {
                return;
            }

            File.Delete(filePath);
        }

        private static ITestResultAdaptor CreateTestSuite(
            string name,
            TestResultStatus status,
            double durationSeconds,
            IReadOnlyList<ITestResultAdaptor> children)
        {
            FakeTestAdaptor test = new FakeTestAdaptor(name, true);
            return new FakeTestResultAdaptor(test, status, durationSeconds, children);
        }

        private static ITestResultAdaptor CreateTestCase(
            string name,
            TestResultStatus status,
            double durationSeconds,
            string message = "",
            string stackTrace = "")
        {
            FakeTestAdaptor test = new FakeTestAdaptor(name, false);
            return new FakeTestResultAdaptor(
                test,
                status,
                durationSeconds,
                new List<ITestResultAdaptor>(),
                message,
                stackTrace);
        }

        private sealed class FakeTestResultAdaptor : ITestResultAdaptor
        {
            private readonly ITestAdaptor _test;
            private readonly TestResultStatus _status;
            private readonly double _durationSeconds;
            private readonly IReadOnlyList<ITestResultAdaptor> _children;
            private readonly string _message;
            private readonly string _stackTrace;

            public FakeTestResultAdaptor(
                ITestAdaptor test,
                TestResultStatus status,
                double durationSeconds,
                IReadOnlyList<ITestResultAdaptor> children,
                string message = "",
                string stackTrace = "")
            {
                _test = test;
                _status = status;
                _durationSeconds = durationSeconds;
                _children = children;
                _message = message ?? string.Empty;
                _stackTrace = stackTrace ?? string.Empty;
            }

            public ITestAdaptor Test => _test;
            public string Name => _test.Name;
            public string FullName => _test.FullName;
            public string ResultState => _status.ToString();
            public TestResultStatus TestStatus => _status;
            public double Duration => _durationSeconds;
            public DateTime StartTime => new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            public DateTime EndTime => StartTime.AddSeconds(_durationSeconds);
            public string Message => _message;
            public string StackTrace => _stackTrace;
            public int AssertCount => 0;
            public int FailCount => CountByStatus(TestResultStatus.Failed);
            public int PassCount => CountByStatus(TestResultStatus.Passed);
            public int SkipCount => CountByStatus(TestResultStatus.Skipped);
            public int InconclusiveCount => CountByStatus(TestResultStatus.Inconclusive);
            public bool HasChildren => _children.Count > 0;
            public IEnumerable<ITestResultAdaptor> Children => _children;
            public string Output => string.Empty;

            public NUnitTNode ToXml()
            {
                return new NUnitTNode("test-result");
            }

            private int CountByStatus(TestResultStatus status)
            {
                if (!_test.IsSuite)
                {
                    return _status == status ? 1 : 0;
                }

                int count = 0;
                foreach (ITestResultAdaptor child in _children)
                {
                    if (child.TestStatus == status)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        private sealed class FakeTestAdaptor : ITestAdaptor
        {
            private readonly string _name;
            private readonly bool _isSuite;

            public FakeTestAdaptor(string name, bool isSuite)
            {
                _name = name;
                _isSuite = isSuite;
            }

            public string Id => FullName;
            public string Name => _name;
            public string FullName => $"Example.Tests.{_name}";
            public int TestCaseCount => _isSuite ? 0 : 1;
            public bool HasChildren => false;
            public bool IsSuite => _isSuite;
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
