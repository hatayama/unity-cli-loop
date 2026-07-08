using System;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Coordinates Auto Refresh suspension while Unity is unfocused.
    /// </summary>
    internal sealed class ExternalAssetFocusReturnService
    {
        private readonly Func<bool> _getAutoRefreshHeld;
        private readonly Action<bool> _setAutoRefreshHeld;
        private readonly Func<bool> _isEditorFocused;
        private readonly Action _disallowAutoRefresh;
        private readonly Action _allowAutoRefresh;
        private readonly Action _resolveFocusReturnChanges;

        internal ExternalAssetFocusReturnService(
            Func<bool> getAutoRefreshHeld,
            Action<bool> setAutoRefreshHeld,
            Func<bool> isEditorFocused,
            Action disallowAutoRefresh,
            Action allowAutoRefresh,
            Action resolveFocusReturnChanges)
        {
            Debug.Assert(getAutoRefreshHeld != null, "getAutoRefreshHeld must not be null");
            Debug.Assert(setAutoRefreshHeld != null, "setAutoRefreshHeld must not be null");
            Debug.Assert(isEditorFocused != null, "isEditorFocused must not be null");
            Debug.Assert(disallowAutoRefresh != null, "disallowAutoRefresh must not be null");
            Debug.Assert(allowAutoRefresh != null, "allowAutoRefresh must not be null");
            Debug.Assert(resolveFocusReturnChanges != null, "resolveFocusReturnChanges must not be null");

            _getAutoRefreshHeld = getAutoRefreshHeld ?? throw new ArgumentNullException(nameof(getAutoRefreshHeld));
            _setAutoRefreshHeld = setAutoRefreshHeld ?? throw new ArgumentNullException(nameof(setAutoRefreshHeld));
            _isEditorFocused = isEditorFocused ?? throw new ArgumentNullException(nameof(isEditorFocused));
            _disallowAutoRefresh = disallowAutoRefresh ?? throw new ArgumentNullException(nameof(disallowAutoRefresh));
            _allowAutoRefresh = allowAutoRefresh ?? throw new ArgumentNullException(nameof(allowAutoRefresh));
            _resolveFocusReturnChanges =
                resolveFocusReturnChanges ?? throw new ArgumentNullException(nameof(resolveFocusReturnChanges));
        }

        internal bool RestoreAutoRefreshIfHeld()
        {
            if (!_getAutoRefreshHeld())
            {
                return false;
            }

            if (!_isEditorFocused())
            {
                return false;
            }

            HandleFocusChanged(true);
            return true;
        }

        internal void HandleFocusChanged(bool isFocused)
        {
            if (!isFocused)
            {
                HoldAutoRefreshIfNeeded();
                return;
            }

            try
            {
                _resolveFocusReturnChanges();
            }
            finally
            {
                ReleaseAutoRefreshIfHeld();
            }
        }

        private void HoldAutoRefreshIfNeeded()
        {
            if (_getAutoRefreshHeld())
            {
                return;
            }

            _disallowAutoRefresh();
            _setAutoRefreshHeld(true);
        }

        private void ReleaseAutoRefreshIfHeld()
        {
            if (!_getAutoRefreshHeld())
            {
                return;
            }

            _allowAutoRefresh();
            _setAutoRefreshHeld(false);
        }
    }
}
