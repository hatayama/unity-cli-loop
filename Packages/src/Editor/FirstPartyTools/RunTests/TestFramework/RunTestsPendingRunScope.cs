#if ULOOP_HAS_TEST_FRAMEWORK
using System;
using System.Diagnostics;
using UnityEditor;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Writes the pending run record and observes Domain Reload on the respect path.
    /// </summary>
    internal sealed class RunTestsPendingRunScope
    {
        private readonly string _requestId;
        private readonly DateTime _expiresAtUtc;
        private bool _reloadObserved;

        internal RunTestsPendingRunScope(string requestId, DateTime expiresAtUtc)
        {
            Debug.Assert(!string.IsNullOrEmpty(requestId), "requestId must not be empty on the respect path");
            Debug.Assert(expiresAtUtc.Kind == DateTimeKind.Utc, "expiresAtUtc must be UTC");

            _requestId = requestId;
            _expiresAtUtc = expiresAtUtc;
        }

        internal bool ReloadObserved => _reloadObserved;

        internal void Begin()
        {
            UnityCliLoopRunTestsSessionRepositoryFacade.Repository.StorePendingRun(_requestId, _expiresAtUtc);
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        }

        internal void ClearPending()
        {
            UnityCliLoopRunTestsSessionRepositoryFacade.Repository.ClearPendingRun(_requestId);
        }

        internal void End()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
        }

        private void OnBeforeAssemblyReload()
        {
            _reloadObserved = true;
        }
    }
}
#endif
