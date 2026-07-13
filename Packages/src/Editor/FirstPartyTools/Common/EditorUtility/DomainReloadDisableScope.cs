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
        private static int _generation;

        private readonly int _createdGeneration;
        private bool _disposed;
        
        public DomainReloadDisableScope()
        {
            if (_activeScopeCount == 0)
            {
                DomainReloadDisableScopeRecovery.RestoreIfPending();
                DomainReloadDisableScopeRecovery.SaveCurrentSettings();
            }

            _activeScopeCount++;
            // Why capture generation: RecoverAbandoned may invalidate this instance while a newer
            // scope owns the static count; Dispose must ignore stale instances.
            _createdGeneration = _generation;
            
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

            if (_createdGeneration != _generation)
            {
                // Why return: RecoverAbandoned already invalidated this instance. Decrementing
                // would steal the count from a newer active scope (e.g. delayed cancel finally).
                return;
            }

            System.Diagnostics.Debug.Assert(
                _activeScopeCount > 0,
                "active scope count must be positive for the current generation before dispose");
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
            // Why bump generation: live instances from the abandoned run must not Dispose into the
            // next run's count if their await completes late.
            _activeScopeCount = 0;
            _generation++;
            DomainReloadDisableScopeRecovery.RestoreIfPending();
        }

        internal static void ResetActiveScopeCountForTests()
        {
            _activeScopeCount = 0;
            _generation = 0;
        }

        internal static int GetActiveScopeCountForTests()
        {
            return _activeScopeCount;
        }

        internal static int GetGenerationForTests()
        {
            return _generation;
        }
    }
}
