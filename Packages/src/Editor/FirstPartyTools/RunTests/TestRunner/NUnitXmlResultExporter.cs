using System;
using System.IO;
using System.Text;
using System.Xml;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Class to export test results as NUnit XML.
    /// </summary>
    public static class NUnitXmlResultExporter
    {
        /// <summary>
        /// Saves the test result as an XML file.
        /// </summary>
        public static string SaveTestResultAsXml(ITestResultAdaptor testResult)
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = $"{timestamp}.xml";

            // Save to .uloop/outputs/TestResults folder at project root (same level as Assets)
            DirectoryInfo parentDir = Directory.GetParent(UnityEngine.Application.dataPath);
            if (parentDir == null)
            {
                throw new System.InvalidOperationException("Unable to determine project root directory");
            }
            string projectRoot = parentDir.FullName;
            string testResultsDir = Path.Combine(projectRoot, UnityCliLoopConstants.OUTPUT_ROOT_DIR, UnityCliLoopConstants.TEST_RESULTS_DIR);

            // Create directory if it doesn't exist
            if (!Directory.Exists(testResultsDir))
            {
                Directory.CreateDirectory(testResultsDir);
            }

            string filePath = Path.Combine(testResultsDir, fileName);
            string xmlContent = GenerateNUnitXml(testResult);
            File.WriteAllText(filePath, xmlContent, Encoding.UTF8);

            // Refresh Assets folder.
            AssetDatabase.Refresh();

            // Test result XML saved
            return filePath;
        }

        /// <summary>
        /// Generates NUnit format XML.
        /// </summary>
        private static string GenerateNUnitXml(ITestResultAdaptor testResult)
        {
            XmlDocument doc = new();

            // Add XML declaration.
            XmlDeclaration declaration = doc.CreateXmlDeclaration("1.0", "UTF-8", null);
            doc.AppendChild(declaration);

            // Create root element.
            XmlElement testRun = doc.CreateElement("test-run");
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
            doc.AppendChild(testRun);

            // Add test suite.
            XmlElement testSuite = CreateTestSuiteElement(doc, testResult);
            testRun.AppendChild(testSuite);

            // Return formatted XML.
            using (StringWriter stringWriter = new StringWriter())
            {
                XmlWriterSettings settings = new()                {
                    Indent = true,
                    IndentChars = "  ",
                    NewLineChars = "\n",
                    NewLineHandling = NewLineHandling.Replace
                };

                using (XmlWriter xmlWriter = XmlWriter.Create(stringWriter, settings))
                {
                    doc.Save(xmlWriter);
                }

                return stringWriter.ToString();
            }
        }

        /// <summary>
        /// Creates a test suite element (recursive).
        /// </summary>
        private static XmlElement CreateTestSuiteElement(XmlDocument doc, ITestResultAdaptor result)
        {
            if (result.Test.IsSuite)
            {
                XmlElement suite = doc.CreateElement("test-suite");
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

                // Add child elements.
                if (result.Children != null)
                {
                    foreach (ITestResultAdaptor child in result.Children)
                    {
                        XmlElement childElement = CreateTestSuiteElement(doc, child);
                        suite.AppendChild(childElement);
                    }
                }

                return suite;
            }
            else
            {
                // Test case.
                XmlElement testCase = doc.CreateElement("test-case");
                testCase.SetAttribute("name", result.Test.Name);
                testCase.SetAttribute("fullname", result.Test.FullName);
                testCase.SetAttribute("result", result.TestStatus.ToString());
                testCase.SetAttribute("start-time", result.StartTime.ToString("yyyy-MM-dd HH:mm:ss"));
                testCase.SetAttribute("end-time", result.EndTime.ToString("yyyy-MM-dd HH:mm:ss"));
                testCase.SetAttribute("duration", result.Duration.ToString("F3"));

                // If failed, add message and stack trace.
                if (result.TestStatus == TestStatus.Failed)
                {
                    XmlElement failure = doc.CreateElement("failure");

                    if (!string.IsNullOrEmpty(result.Message))
                    {
                        XmlElement message = doc.CreateElement("message");
                        message.InnerText = result.Message;
                        failure.AppendChild(message);
                    }

                    if (!string.IsNullOrEmpty(result.StackTrace))
                    {
                        XmlElement stackTrace = doc.CreateElement("stack-trace");
                        stackTrace.InnerText = result.StackTrace;
                        failure.AppendChild(stackTrace);
                    }

                    testCase.AppendChild(failure);
                }

                return testCase;
            }
        }

        /// <summary>
        /// Gets the overall result.
        /// </summary>
        private static string GetOverallResult(ITestResultAdaptor result)
        {
            if (CountFailed(result) > 0)
                return "Failed";
            if (CountSkipped(result) > 0 && CountPassed(result) == 0)
                return "Skipped";
            return "Passed";
        }

        /// <summary>
        /// Counts the number of test cases.
        /// </summary>
        private static int CountTestCases(ITestResultAdaptor result)
        {
            if (!result.Test.IsSuite)
                return 1;

            int count = 0;
            if (result.Children != null)
            {
                foreach (ITestResultAdaptor child in result.Children)
                {
                    count += CountTestCases(child);
                }
            }

            return count;
        }

        /// <summary>
        /// Counts the number of passed tests.
        /// </summary>
        private static int CountPassed(ITestResultAdaptor result)
        {
            if (!result.Test.IsSuite)
                return result.TestStatus == TestStatus.Passed ? 1 : 0;

            int count = 0;
            if (result.Children != null)
            {
                foreach (ITestResultAdaptor child in result.Children)
                {
                    count += CountPassed(child);
                }
            }

            return count;
        }

        /// <summary>
        /// Counts the number of failed tests.
        /// </summary>
        private static int CountFailed(ITestResultAdaptor result)
        {
            if (!result.Test.IsSuite)
                return result.TestStatus == TestStatus.Failed ? 1 : 0;

            int count = 0;
            if (result.Children != null)
            {
                foreach (ITestResultAdaptor child in result.Children)
                {
                    count += CountFailed(child);
                }
            }

            return count;
        }

        /// <summary>
        /// Counts the number of skipped tests.
        /// </summary>
        private static int CountSkipped(ITestResultAdaptor result)
        {
            if (!result.Test.IsSuite)
                return result.TestStatus == TestStatus.Skipped ? 1 : 0;

            int count = 0;
            if (result.Children != null)
            {
                foreach (ITestResultAdaptor child in result.Children)
                {
                    count += CountSkipped(child);
                }
            }

            return count;
        }
    }
}
