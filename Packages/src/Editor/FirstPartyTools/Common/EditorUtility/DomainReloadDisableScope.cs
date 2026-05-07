using System;
using UnityEditor;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Provides Domain Reload Disable Scope behavior for Unity CLI Loop.
    /// </summary>
    public class DomainReloadDisableScope : IDisposable
    {
        private readonly bool _originalEnabled;
        private readonly EnterPlayModeOptions _originalOptions;
        
        public DomainReloadDisableScope()
        {
            _originalEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            _originalOptions = EditorSettings.enterPlayModeOptions;
            
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        }
        
        public void Dispose()
        {
            EditorSettings.enterPlayModeOptionsEnabled = _originalEnabled;
            EditorSettings.enterPlayModeOptions = _originalOptions;
        }
    }
}