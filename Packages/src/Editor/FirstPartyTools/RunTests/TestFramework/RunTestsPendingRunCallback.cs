#if ULOOP_HAS_TEST_FRAMEWORK
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEditor.TestTools.TestRunner.Api;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Records the post-reload RunFinished result against every pending run-tests request id.
    /// </summary>
    internal sealed class RunTestsPendingRunCallback : ICallbacks
    {
        private readonly TestRunnerApi _testRunnerApi;

        internal RunTestsPendingRunCallback(TestRunnerApi testRunnerApi)
        {
            _testRunnerApi = testRunnerApi;
        }

        public void RunStarted(ITestAdaptor tests)
        {
        }

        public void RunFinished(ITestResultAdaptor result)
        {
            SerializableTestResult serializableResult = SerializableTestResultConverter.FromTestResult(result);
            if (serializableResult.failedCount > 0)
            {
                serializableResult.xmlPath = PlayModeTestExecuter.TrySaveFailureXml(result);
            }

            RunTestsResponse response = RunTestsResponseFactory.FromResult(serializableResult);
            response.Warning = RunTestsConstants.DomainReloadRecoveredWarning;
            string resultJson = JsonConvert.SerializeObject(
                response,
                Formatting.None,
                UnityCliLoopJsonResponseSerializerSettings.Settings);
            IRunTestsSessionRepository repository = UnityCliLoopRunTestsSessionRepositoryFacade.Repository;
            DateTime completedAtUtc = DateTime.UtcNow;
            // Why: an expired pending id from an abandoned run must not receive this unrelated result.
            repository.ClearExpired(completedAtUtc);
            IReadOnlyList<string> requestIds = repository.GetPendingRunRequestIds();
            foreach (string requestId in requestIds)
            {
                repository.StoreRunResult(requestId, resultJson, completedAtUtc);
            }

            _testRunnerApi.UnregisterCallbacks(this);
        }

        public void TestStarted(ITestAdaptor test)
        {
        }

        public void TestFinished(ITestResultAdaptor result)
        {
        }
    }
}
#endif
