#if ULOOP_HAS_TEST_FRAMEWORK
using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

[assembly: InternalsVisibleTo("UnityCLILoop.FirstPartyTools.Editor")]

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Registers and executes Unity Test Runner work when the Test Framework package is installed.
    /// </summary>
    internal static class RunTestsTestFrameworkStartup
    {
        internal static void Initialize()
        {
            UnityTestFrameworkExecutionServiceRegistry.Register(new UnityTestFrameworkExecutionService());
        }
    }

    internal sealed class UnityTestFrameworkExecutionService : IUnityTestFrameworkExecutionService
    {
        public Task<SerializableTestResult> ExecutePlayModeTestAsync(TestExecutionFilter filter, CancellationToken ct)
        {
            return PlayModeTestExecuter.ExecutePlayModeTest(filter, ct);
        }

        public Task<SerializableTestResult> ExecuteEditModeTestAsync(TestExecutionFilter filter, CancellationToken ct)
        {
            return PlayModeTestExecuter.ExecuteEditModeTest(filter, ct);
        }
    }

    public static class PlayModeTestExecuter
    {
        public static async Task<SerializableTestResult> ExecutePlayModeTest(
            TestExecutionFilter filter,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            using DomainReloadDisableScope scope = new DomainReloadDisableScope();
            return await ExecuteTestWithEventNotification(TestMode.PlayMode, filter, ct);
        }

        public static async Task<SerializableTestResult> ExecuteEditModeTest(
            TestExecutionFilter filter,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return await ExecuteTestWithEventNotification(TestMode.EditMode, filter, ct);
        }

        private static async Task<SerializableTestResult> ExecuteTestWithEventNotification(
            TestMode testMode,
            TestExecutionFilter filter,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            TaskCompletionSource<SerializableTestResult> taskCompletionSource =
                new TaskCompletionSource<SerializableTestResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            using UnifiedTestCallback callback = new UnifiedTestCallback();
            callback.OnTestCompleted += (result, rawResult) =>
            {
                if (taskCompletionSource.Task.IsCompleted)
                {
                    return;
                }

                if (result.failedCount > 0 && rawResult != null)
                {
                    result.xmlPath = TrySaveFailureXml(rawResult);
                }
                taskCompletionSource.TrySetResult(result);
            };

            StartTestExecution(testMode, filter, callback);
            SerializableTestResult result = await taskCompletionSource.Task;
            ct.ThrowIfCancellationRequested();
            return result;
        }

        internal static string TrySaveFailureXml(ITestResultAdaptor rawResult)
        {
            try
            {
                return NUnitXmlResultExporter.SaveTestResultAsXml(rawResult);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Failed to save NUnit XML result file: {exception}");
                return null;
            }
        }

        private static void StartTestExecution(TestMode testMode, TestExecutionFilter filter, UnifiedTestCallback callback)
        {
            TestRunnerApi testRunnerApi = ScriptableObject.CreateInstance<TestRunnerApi>();
            callback.SetTestRunnerApi(testRunnerApi);
            testRunnerApi.RegisterCallbacks(callback);

            Filter unityFilter = CreateUnityFilter(testMode, filter);
            testRunnerApi.Execute(new ExecutionSettings(unityFilter));
        }

        private static Filter CreateUnityFilter(TestMode testMode, TestExecutionFilter filter)
        {
            Filter unityFilter = new Filter
            {
                testMode = testMode
            };

            if (filter == null || filter.FilterType == TestExecutionFilterType.All)
            {
                return unityFilter;
            }

            switch (filter.FilterType)
            {
                case TestExecutionFilterType.Exact:
                    unityFilter.testNames = new[] { filter.FilterValue };
                    break;
                case TestExecutionFilterType.Regex:
                    unityFilter.groupNames = new[] { filter.FilterValue };
                    break;
                case TestExecutionFilterType.AssemblyName:
                    unityFilter.assemblyNames = new[] { filter.FilterValue };
                    break;
            }

            return unityFilter;
        }
    }

    internal sealed class UnifiedTestCallback : ICallbacks, IDisposable
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
            SerializableTestResult serializableResult = SerializableTestResultConverter.FromTestResult(result);
            OnTestCompleted?.Invoke(serializableResult, result);
        }

        public void TestStarted(ITestAdaptor test)
        {
        }

        public void TestFinished(ITestResultAdaptor result)
        {
        }

        public void Dispose()
        {
            OnTestCompleted = null;
            _testRunnerApi?.UnregisterCallbacks(this);
        }
    }
}
#endif
