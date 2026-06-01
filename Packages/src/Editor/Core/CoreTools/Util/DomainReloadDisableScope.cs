using System;
using UnityEditor;
using UnityEngine;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Temporarily disables domain reload while preserving the user's Enter Play Mode settings.
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
            Debug.Assert(_activeScopeCount > 0, "active scope count must be positive before dispose");
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
