#if ULOOP_HAS_TEST_FRAMEWORK
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Re-registers RunFinished after Domain Reload when a pending respect-path run exists.
    /// First-domain loads see no pending record yet because StorePendingRun happens just before
    /// Execute, so only UnifiedTestCallback runs there. After reload, SessionState still has the
    /// pending id and this registrar attaches again, including after Play Mode exit reload.
    /// </summary>
    internal static class RunTestsPendingRunCallbackRegistrar
    {
        [InitializeOnLoadMethod]
        private static void ScheduleRegisterIfPending()
        {
            // Why delayCall: Facade registration comes from UnityCliLoopEditorBootstrap's
            // [InitializeOnLoadMethod]. Unity runs [InitializeOnLoad] static constructors first
            // and does not order [InitializeOnLoadMethod] peers, so reading Repository here can
            // throw before CompositionRoot has registered it.
            EditorApplication.delayCall += RegisterIfPending;
        }

        private static void RegisterIfPending()
        {
            IRunTestsSessionRepository repository = UnityCliLoopRunTestsSessionRepositoryFacade.Repository;
            if (!repository.HasAnyPendingRun())
            {
                return;
            }

            TestRunnerApi testRunnerApi = ScriptableObject.CreateInstance<TestRunnerApi>();
            testRunnerApi.RegisterCallbacks(new RunTestsPendingRunCallback(testRunnerApi));
        }
    }
}
#endif
