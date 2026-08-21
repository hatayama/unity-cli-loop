#if ULOOP_HAS_TEST_FRAMEWORK
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Lists leaf tests in a TestMode with no filter via TestRunnerApi.RetrieveTestList.
    /// </summary>
    internal static class RunTestsUnfilteredTestListRetriever
    {
        internal static async Task<RunTestsUnfilteredTestListResult> RetrieveAsync(
            UnityCliLoopTestMode testMode,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Debug.Assert(
                MainThreadSwitcher.IsMainThread,
                "RetrieveTestList must run on the main thread because TestRunnerApi is a Unity API.");

            TestMode unityTestMode = testMode == UnityCliLoopTestMode.PlayMode
                ? TestMode.PlayMode
                : TestMode.EditMode;

            TaskCompletionSource<ITestAdaptor> taskCompletionSource =
                new TaskCompletionSource<ITestAdaptor>(TaskCreationOptions.RunContinuationsAsynchronously);
            TestRunnerApi testRunnerApi = ScriptableObject.CreateInstance<TestRunnerApi>();
            List<string> names = null;
            try
            {
                testRunnerApi.RetrieveTestList(
                    unityTestMode,
                    adaptor => taskCompletionSource.TrySetResult(adaptor));
                Task timeoutTask = Task.Delay(
                    RunTestsConstants.UnfilteredTestListRetrieveTimeoutMilliseconds,
                    ct);
                await Task.WhenAny(taskCompletionSource.Task, timeoutTask).ConfigureAwait(false);
                if (ct.IsCancellationRequested)
                {
                    ct.ThrowIfCancellationRequested();
                }

                if (taskCompletionSource.Task.Status == TaskStatus.RanToCompletion)
                {
                    ITestAdaptor root = await taskCompletionSource.Task.ConfigureAwait(false);
                    if (root != null)
                    {
                        names = new List<string>();
                        CollectLeafFullNames(root, names);
                    }
                }
            }
            finally
            {
                await MainThreadSwitcher.SwitchToMainThread(CancellationToken.None);
                Object.DestroyImmediate(testRunnerApi);
            }

            if (names == null)
            {
                return RunTestsUnfilteredTestListResult.NotRetrieved();
            }

            return RunTestsUnfilteredTestListResult.Success(names);
        }

        private static void CollectLeafFullNames(ITestAdaptor node, List<string> names)
        {
            if (node == null)
            {
                return;
            }

            if (!node.HasChildren)
            {
                if (!node.IsSuite && !string.IsNullOrEmpty(node.FullName))
                {
                    names.Add(node.FullName);
                }

                return;
            }

            IEnumerable<ITestAdaptor> children = node.Children;
            if (children == null)
            {
                return;
            }

            foreach (ITestAdaptor child in children)
            {
                CollectLeafFullNames(child, names);
            }
        }
    }
}
#endif
