using System;
using UnityEditor;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Temporarily disables domain reload while preserving the user's Enter Play Mode settings.
    /// </summary>
    public class DomainReloadDisableScope : IDisposable
    {
        public DomainReloadDisableScope()
        {
            DomainReloadDisableScopeRecovery.RestoreIfPending();
            DomainReloadDisableScopeRecovery.SaveCurrentSettingsIfNeeded();

            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        }
        
        public void Dispose()
        {
            DomainReloadDisableScopeRecovery.RestoreIfPending();
        }
    }
}
