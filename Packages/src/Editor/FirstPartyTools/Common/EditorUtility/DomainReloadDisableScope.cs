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
            System.Diagnostics.Debug.Assert(_activeScopeCount > 0, "active scope count must be positive before dispose");
            _activeScopeCount--;

            if (_activeScopeCount == 0)
            {
                DomainReloadDisableScopeRecovery.RestoreIfPending();
            }
        }

        internal static void ResetActiveScopeCountForTests()
        {
            _activeScopeCount = 0;
        }
    }
}
