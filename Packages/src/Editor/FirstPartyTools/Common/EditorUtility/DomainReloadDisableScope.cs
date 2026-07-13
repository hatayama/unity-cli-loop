using System;
using UnityEditor;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Provides Domain Reload Disable Scope behavior for Unity CLI Loop.
    /// </summary>
    public class DomainReloadDisableScope : IDisposable
    {
        private static int _activeScopeCount;
        private bool _disposed;
        
        public DomainReloadDisableScope()
        {
            if (_activeScopeCount == 0)
            {
                DomainReloadDisableScopeRecovery.RestoreIfPending();
                DomainReloadDisableScopeRecovery.SaveCurrentSettings();
            }

            _activeScopeCount++;
            
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        }
        
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            // Why tolerate zero: RecoverAbandonedScopeBeforeNewRun may have already cleared the
            // static count and restored settings while this instance was still alive.
            if (_activeScopeCount == 0)
            {
                return;
            }

            _activeScopeCount--;

            if (_activeScopeCount == 0)
            {
                DomainReloadDisableScopeRecovery.RestoreIfPending();
            }
        }

        /// <summary>
        /// Clears abandoned static scope state and restores pending Enter Play Mode settings
        /// before a new PlayMode test run starts.
        /// </summary>
        internal static void RecoverAbandonedScopeBeforeNewRun()
        {
            // Why always restore when a marker exists (even if count is already 0): domain reload
            // resets the static count while leaving the recovery marker on disk.
            if (_activeScopeCount == 0)
            {
                DomainReloadDisableScopeRecovery.RestoreIfPending();
                return;
            }

            // Why clear count even without a marker: a non-zero count would skip SaveCurrentSettings
            // on the next constructor and nest on a phantom scope, delaying restore indefinitely.
            _activeScopeCount = 0;
            DomainReloadDisableScopeRecovery.RestoreIfPending();
        }

        internal static void ResetActiveScopeCountForTests()
        {
            _activeScopeCount = 0;
        }

        internal static int GetActiveScopeCountForTests()
        {
            return _activeScopeCount;
        }
    }
}
