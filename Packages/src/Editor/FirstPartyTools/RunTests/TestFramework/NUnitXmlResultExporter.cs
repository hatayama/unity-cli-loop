#if ULOOP_HAS_TEST_FRAMEWORK
using System;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;

using io.github.hatayama.UnityCliLoop.ToolContracts;

[assembly: InternalsVisibleTo("UnityCLILoop.Tests.Editor")]

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Writes failing Unity Test Runner results as NUnit XML files for CLI diagnostics.
    /// </summary>
    internal static class NUnitXmlResultExporter
    {
        private const string DurationFormat = "F3";
        private const string FileTimestampFormat = "yyyyMMdd_HHmmss_fffffff";
        private const string XmlFileExtension = ".xml";

        public static string SaveTestResultAsXml(ITestResultAdaptor testResult)
        {
            string fileName = CreateResultFileName();
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
            OutputFileRetention.DeleteOldestBeyondLimit(testResultsDirectory, "*.xml");
            AssetDatabase.Refresh();
            return filePath;
        }

        private static string CreateResultFileName()
        {
            string timestamp = DateTime.UtcNow.ToString(FileTimestampFormat, CultureInfo.InvariantCulture);
            return $"{timestamp}_{Guid.NewGuid():N}{XmlFileExtension}";
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
            testRun.SetAttribute("inconclusive", CountInconclusive(testResult).ToString());
            testRun.SetAttribute("start-time", FormatDateTime(testResult.StartTime));
            testRun.SetAttribute("end-time", FormatDateTime(testResult.EndTime));
            testRun.SetAttribute("duration", FormatDuration(testResult.Duration));
            document.AppendChild(testRun);

            XmlElement testSuite = CreateTestSuiteElement(document, testResult);
            testRun.AppendChild(testSuite);
            return ToFormattedXml(document);
        }

        private static string FormatDateTime(DateTime dateTime)
        {
            return dateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private static string FormatDuration(double duration)
        {
            return duration.ToString(DurationFormat, CultureInfo.InvariantCulture);
        }

        private static string ToFormattedXml(XmlDocument document)
        {
            using Utf8StringWriter stringWriter = new Utf8StringWriter();
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

        private sealed class Utf8StringWriter : StringWriter
        {
            public override Encoding Encoding => Encoding.UTF8;
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
            suite.SetAttribute("start-time", FormatDateTime(result.StartTime));
            suite.SetAttribute("end-time", FormatDateTime(result.EndTime));
            suite.SetAttribute("duration", FormatDuration(result.Duration));
            suite.SetAttribute("total", CountTestCases(result).ToString());
            suite.SetAttribute("passed", CountPassed(result).ToString());
            suite.SetAttribute("failed", CountFailed(result).ToString());
            suite.SetAttribute("skipped", CountSkipped(result).ToString());
            suite.SetAttribute("inconclusive", CountInconclusive(result).ToString());

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
            testCase.SetAttribute("start-time", FormatDateTime(result.StartTime));
            testCase.SetAttribute("end-time", FormatDateTime(result.EndTime));
            testCase.SetAttribute("duration", FormatDuration(result.Duration));

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

            if (CountInconclusive(result) > 0)
            {
                return "Inconclusive";
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
            return CountByStatus(result, TestStatus.Passed);
        }

        private static int CountFailed(ITestResultAdaptor result)
        {
            return CountByStatus(result, TestStatus.Failed);
        }

        private static int CountSkipped(ITestResultAdaptor result)
        {
            return CountByStatus(result, TestStatus.Skipped);
        }

        private static int CountInconclusive(ITestResultAdaptor result)
        {
            return CountByStatus(result, TestStatus.Inconclusive);
        }

        private static int CountByStatus(ITestResultAdaptor result, TestStatus status)
        {
            if (!result.Test.IsSuite)
            {
                return result.TestStatus == status ? 1 : 0;
            }

            if (result.Children == null)
            {
                return 0;
            }

            int count = 0;
            foreach (ITestResultAdaptor child in result.Children)
            {
                count += CountByStatus(child, status);
            }

            return count;
        }
    }
}
#endif
