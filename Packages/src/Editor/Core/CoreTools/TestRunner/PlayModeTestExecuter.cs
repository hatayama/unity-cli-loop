#if ULOOPMCP_HAS_TEST_FRAMEWORK
using System;
using System.Threading.Tasks;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Unified test executor with domain reload control and filtering support
    /// Supports both EditMode and PlayMode tests with advanced filtering capabilities
    /// </summary>
    public static class PlayModeTestExecuter
    {
        /// <summary>
        /// Execute PlayMode tests asynchronously with domain reload control
        /// </summary>
        /// <param name="filter">Test execution filter (null for all tests)</param>
        /// <returns>Test execution result</returns>
        public static async Task<SerializableTestResult> ExecutePlayModeTest(
            TestExecutionFilter filter = null)
        {
            using DomainReloadDisableScope scope = new();
            return await ExecuteTestWithEventNotification(TestMode.PlayMode, filter);
        }

        /// <summary>
        /// Execute EditMode tests asynchronously
        /// </summary>
        /// <param name="filter">Test execution filter (null for all tests)</param>
        /// <returns>Test execution result</returns>
        public static async Task<SerializableTestResult> ExecuteEditModeTest(
            TestExecutionFilter filter = null)
        {
            return await ExecuteTestWithEventNotification(TestMode.EditMode, filter);
        }

        /// <summary>
        /// Execute test with event-based result notification
        /// </summary>
        private static async Task<SerializableTestResult> ExecuteTestWithEventNotification(
            TestMode testMode,
            TestExecutionFilter filter)
        {
            TaskCompletionSource<SerializableTestResult> tcs = new();

            using UnifiedTestCallback callback = new();
            callback.OnTestCompleted += (result, rawResult) =>
            {
                if (!tcs.Task.IsCompleted)
                {
                    // Auto-save XML when tests fail for detailed failure diagnosis
                    if (result.failedCount > 0 && rawResult != null)
                    {
                        result.xmlPath = NUnitXmlResultExporter.SaveTestResultAsXml(rawResult);
                    }
                    tcs.SetResult(result);
                }
            };

            StartTestExecution(testMode, filter, callback);
            return await tcs.Task;
        }

        /// <summary>
        /// Start test execution with callback handler
        /// </summary>
        private static void StartTestExecution(TestMode testMode, TestExecutionFilter filter, UnifiedTestCallback callback)
        {
            TestRunnerApi testRunnerApi = ScriptableObject.CreateInstance<TestRunnerApi>();
            callback.SetTestRunnerApi(testRunnerApi);
            testRunnerApi.RegisterCallbacks(callback);
            
            Filter unityFilter = CreateUnityFilter(testMode, filter);
            testRunnerApi.Execute(new ExecutionSettings(unityFilter));
        }

        /// <summary>
        /// Create Unity Filter from TestExecutionFilter
        /// </summary>
        private static Filter CreateUnityFilter(TestMode testMode, TestExecutionFilter filter)
        {
            Filter unityFilter = new Filter
            {
                testMode = testMode
            };

            if (filter != null && filter.FilterType != TestExecutionFilterType.All)
            {
                switch (filter.FilterType)
                {
                    case TestExecutionFilterType.Exact:
                        // Use testNames for exact matching (individual test methods)
                        unityFilter.testNames = new string[] { filter.FilterValue };
                        break;
                    case TestExecutionFilterType.Regex:
                        // Use groupNames with regex for pattern matching (classes, namespaces)
                        unityFilter.groupNames = new string[] { filter.FilterValue };
                        break;
                    case TestExecutionFilterType.AssemblyName:
                        unityFilter.assemblyNames = new string[] { filter.FilterValue };
                        break;
                }
            }

            return unityFilter;
        }
    }

    /// <summary>
    /// Unified callback handler for both EditMode and PlayMode test execution
    /// </summary>
    internal class UnifiedTestCallback : ICallbacks, IDisposable
    {
        public event Action<SerializableTestResult, ITestResultAdaptor> OnTestCompleted;

        private TestRunnerApi _testRunnerApi;

        public void SetTestRunnerApi(TestRunnerApi testRunnerApi)
        {
            _testRunnerApi = testRunnerApi;
        }

        public void RunStarted(ITestAdaptor tests)
        {
        }

        public void RunFinished(ITestResultAdaptor result)
        {
            SerializableTestResult serializableResult = SerializableTestResult.FromTestResult(result);
            OnTestCompleted?.Invoke(serializableResult, result);
        }

        public void TestStarted(ITestAdaptor test) { }

        public void TestFinished(ITestResultAdaptor result) { }

        public void Dispose()
        {
            OnTestCompleted = null;
            _testRunnerApi?.UnregisterCallbacks(this);
        }
    }
}
#endif
