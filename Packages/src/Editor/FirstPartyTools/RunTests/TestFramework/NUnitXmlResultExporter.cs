#if ULOOP_HAS_TEST_FRAMEWORK
using System;
using System.IO;
using System.Text;
using System.Xml;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Writes failing Unity Test Runner results as NUnit XML files for CLI diagnostics.
    /// </summary>
    internal static class NUnitXmlResultExporter
    {
        public static string SaveTestResultAsXml(ITestResultAdaptor testResult)
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = $"{timestamp}.xml";
            DirectoryInfo parentDirectory = Directory.GetParent(UnityEngine.Application.dataPath);
            if (parentDirectory == null)
            {
                throw new InvalidOperationException("Unable to determine project root directory");
            }

            string testResultsDirectory = Path.Combine(
                parentDirectory.FullName,
                UnityCliLoopConstants.OUTPUT_ROOT_DIR,
                UnityCliLoopConstants.TEST_RESULTS_DIR);
            if (!Directory.Exists(testResultsDirectory))
            {
                Directory.CreateDirectory(testResultsDirectory);
            }

            string filePath = Path.Combine(testResultsDirectory, fileName);
            string xmlContent = GenerateNUnitXml(testResult);
            File.WriteAllText(filePath, xmlContent, Encoding.UTF8);
            AssetDatabase.Refresh();
            return filePath;
        }

        private static string GenerateNUnitXml(ITestResultAdaptor testResult)
        {
            XmlDocument document = new XmlDocument();
            XmlDeclaration declaration = document.CreateXmlDeclaration("1.0", "UTF-8", null);
            document.AppendChild(declaration);

            XmlElement testRun = document.CreateElement("test-run");
            testRun.SetAttribute("id", "2");
            testRun.SetAttribute("testcasecount", CountTestCases(testResult).ToString());
            testRun.SetAttribute("result", GetOverallResult(testResult));
            testRun.SetAttribute("total", CountTestCases(testResult).ToString());
            testRun.SetAttribute("passed", CountPassed(testResult).ToString());
            testRun.SetAttribute("failed", CountFailed(testResult).ToString());
            testRun.SetAttribute("skipped", CountSkipped(testResult).ToString());
            testRun.SetAttribute("start-time", testResult.StartTime.ToString("yyyy-MM-dd HH:mm:ss"));
            testRun.SetAttribute("end-time", testResult.EndTime.ToString("yyyy-MM-dd HH:mm:ss"));
            testRun.SetAttribute("duration", testResult.Duration.ToString("F3"));
            document.AppendChild(testRun);

            XmlElement testSuite = CreateTestSuiteElement(document, testResult);
            testRun.AppendChild(testSuite);
            return ToFormattedXml(document);
        }

        private static string ToFormattedXml(XmlDocument document)
        {
            using StringWriter stringWriter = new StringWriter();
            XmlWriterSettings settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                NewLineChars = "\n",
                NewLineHandling = NewLineHandling.Replace
            };

            using (XmlWriter xmlWriter = XmlWriter.Create(stringWriter, settings))
            {
                document.Save(xmlWriter);
            }

            return stringWriter.ToString();
        }

        private static XmlElement CreateTestSuiteElement(XmlDocument document, ITestResultAdaptor result)
        {
            if (!result.Test.IsSuite)
            {
                return CreateTestCaseElement(document, result);
            }

            XmlElement suite = document.CreateElement("test-suite");
            suite.SetAttribute("type", result.Test.TypeInfo?.FullName ?? "TestSuite");
            suite.SetAttribute("name", result.Test.Name);
            suite.SetAttribute("fullname", result.Test.FullName);
            suite.SetAttribute("result", result.TestStatus.ToString());
            suite.SetAttribute("start-time", result.StartTime.ToString("yyyy-MM-dd HH:mm:ss"));
            suite.SetAttribute("end-time", result.EndTime.ToString("yyyy-MM-dd HH:mm:ss"));
            suite.SetAttribute("duration", result.Duration.ToString("F3"));
            suite.SetAttribute("total", CountTestCases(result).ToString());
            suite.SetAttribute("passed", CountPassed(result).ToString());
            suite.SetAttribute("failed", CountFailed(result).ToString());
            suite.SetAttribute("skipped", CountSkipped(result).ToString());

            if (result.Children == null)
            {
                return suite;
            }

            foreach (ITestResultAdaptor child in result.Children)
            {
                XmlElement childElement = CreateTestSuiteElement(document, child);
                suite.AppendChild(childElement);
            }

            return suite;
        }

        private static XmlElement CreateTestCaseElement(XmlDocument document, ITestResultAdaptor result)
        {
            XmlElement testCase = document.CreateElement("test-case");
            testCase.SetAttribute("name", result.Test.Name);
            testCase.SetAttribute("fullname", result.Test.FullName);
            testCase.SetAttribute("result", result.TestStatus.ToString());
            testCase.SetAttribute("start-time", result.StartTime.ToString("yyyy-MM-dd HH:mm:ss"));
            testCase.SetAttribute("end-time", result.EndTime.ToString("yyyy-MM-dd HH:mm:ss"));
            testCase.SetAttribute("duration", result.Duration.ToString("F3"));

            if (result.TestStatus != TestStatus.Failed)
            {
                return testCase;
            }

            XmlElement failure = document.CreateElement("failure");
            AppendFailureMessage(document, failure, result);
            testCase.AppendChild(failure);
            return testCase;
        }

        private static void AppendFailureMessage(XmlDocument document, XmlElement failure, ITestResultAdaptor result)
        {
            if (!string.IsNullOrEmpty(result.Message))
            {
                XmlElement message = document.CreateElement("message");
                message.InnerText = result.Message;
                failure.AppendChild(message);
            }

            if (string.IsNullOrEmpty(result.StackTrace))
            {
                return;
            }

            XmlElement stackTrace = document.CreateElement("stack-trace");
            stackTrace.InnerText = result.StackTrace;
            failure.AppendChild(stackTrace);
        }

        private static string GetOverallResult(ITestResultAdaptor result)
        {
            if (CountFailed(result) > 0)
            {
                return "Failed";
            }

            if (CountSkipped(result) > 0 && CountPassed(result) == 0)
            {
                return "Skipped";
            }

            return "Passed";
        }

        private static int CountTestCases(ITestResultAdaptor result)
        {
            if (!result.Test.IsSuite)
            {
                return 1;
            }

            if (result.Children == null)
            {
                return 0;
            }

            int count = 0;
            foreach (ITestResultAdaptor child in result.Children)
            {
                count += CountTestCases(child);
            }

            return count;
        }

        private static int CountPassed(ITestResultAdaptor result)
        {
            if (!result.Test.IsSuite)
            {
                return result.TestStatus == TestStatus.Passed ? 1 : 0;
            }

            return CountChildrenByStatus(result, TestStatus.Passed);
        }

        private static int CountFailed(ITestResultAdaptor result)
        {
            if (!result.Test.IsSuite)
            {
                return result.TestStatus == TestStatus.Failed ? 1 : 0;
            }

            return CountChildrenByStatus(result, TestStatus.Failed);
        }

        private static int CountSkipped(ITestResultAdaptor result)
        {
            if (!result.Test.IsSuite)
            {
                return result.TestStatus == TestStatus.Skipped ? 1 : 0;
            }

            return CountChildrenByStatus(result, TestStatus.Skipped);
        }

        private static int CountChildrenByStatus(ITestResultAdaptor result, TestStatus status)
        {
            if (result.Children == null)
            {
                return 0;
            }

            int count = 0;
            foreach (ITestResultAdaptor child in result.Children)
            {
                if (status == TestStatus.Passed)
                {
                    count += CountPassed(child);
                }
                else if (status == TestStatus.Failed)
                {
                    count += CountFailed(child);
                }
                else if (status == TestStatus.Skipped)
                {
                    count += CountSkipped(child);
                }
            }

            return count;
        }
    }
}
#endif
