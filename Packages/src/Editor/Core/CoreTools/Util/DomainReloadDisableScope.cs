using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Temporarily disables domain reload while preserving the user's Enter Play Mode settings.
    /// </summary>
    public class DomainReloadDisableScope : IDisposable
    {
        private static readonly List<WeakReference> ActiveScopeReferences = new List<WeakReference>();
        private bool _disposed;

        public DomainReloadDisableScope()
        {
            PruneInactiveScopeReferences();
            if (ActiveScopeReferences.Count == 0)
            {
                DomainReloadDisableScopeRecovery.RestoreIfPending();
                DomainReloadDisableScopeRecovery.SaveCurrentSettings();
            }

            ActiveScopeReferences.Add(new WeakReference(this));

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
            bool removed = RemoveScopeReference(this);
            Debug.Assert(removed, "active scope reference must exist before dispose");

            if (ActiveScopeReferences.Count == 0)
            {
                DomainReloadDisableScopeRecovery.RestoreIfPending();
            }
        }

        internal static void ResetActiveScopeCountForTests()
        {
            ActiveScopeReferences.Clear();
        }

        private static void PruneInactiveScopeReferences()
        {
            for (int index = ActiveScopeReferences.Count - 1; index >= 0; index--)
            {
                DomainReloadDisableScope activeScope =
                    ActiveScopeReferences[index].Target as DomainReloadDisableScope;
                if (activeScope == null || activeScope._disposed)
                {
                    ActiveScopeReferences.RemoveAt(index);
                }
            }
        }

        private static bool RemoveScopeReference(DomainReloadDisableScope scope)
        {
            bool removed = false;
            for (int index = ActiveScopeReferences.Count - 1; index >= 0; index--)
            {
                DomainReloadDisableScope activeScope =
                    ActiveScopeReferences[index].Target as DomainReloadDisableScope;
                if (activeScope == null || activeScope._disposed || ReferenceEquals(activeScope, scope))
                {
                    ActiveScopeReferences.RemoveAt(index);
                }

                if (ReferenceEquals(activeScope, scope))
                {
                    removed = true;
                }
            }

            return removed;
        }
    }
}
