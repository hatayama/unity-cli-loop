using System;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Pure policy for holding Auto Refresh while hot-reload patches are active.
    /// Unity AssetDatabase calls are injected so EditMode tests can record them.
    /// </summary>
    internal sealed class HotReloadAutoRefreshHoldService
    {
        private readonly Func<bool> _getHeld;
        private readonly Action<bool> _setHeld;
        private readonly Func<bool> _isEditorFocused;
        private readonly Func<bool> _isPlaying;
        private readonly Action _disallowAutoRefresh;
        private readonly Action _allowAutoRefresh;
        private readonly Func<(bool CanProceed, string Message, string[] ScenePaths)> _resolveBeforeRefresh;
        private readonly Action _refresh;
        private readonly Action<string, string, object> _logVibeInfo;
        private readonly Action<string, string, object> _logVibeWarning;
        private bool _refreshDeferred;

        internal HotReloadAutoRefreshHoldService(
            Func<bool> getHeld,
            Action<bool> setHeld,
            Func<bool> isEditorFocused,
            Func<bool> isPlaying,
            Action disallowAutoRefresh,
            Action allowAutoRefresh,
            Func<(bool CanProceed, string Message, string[] ScenePaths)> resolveBeforeRefresh,
            Action refresh,
            Action<string, string, object> logVibeInfo = null,
            Action<string, string, object> logVibeWarning = null)
        {
            Debug.Assert(getHeld != null, "getHeld must not be null");
            Debug.Assert(setHeld != null, "setHeld must not be null");
            Debug.Assert(isEditorFocused != null, "isEditorFocused must not be null");
            Debug.Assert(isPlaying != null, "isPlaying must not be null");
            Debug.Assert(disallowAutoRefresh != null, "disallowAutoRefresh must not be null");
            Debug.Assert(allowAutoRefresh != null, "allowAutoRefresh must not be null");
            Debug.Assert(resolveBeforeRefresh != null, "resolveBeforeRefresh must not be null");
            Debug.Assert(refresh != null, "refresh must not be null");

            _getHeld = getHeld ?? throw new ArgumentNullException(nameof(getHeld));
            _setHeld = setHeld ?? throw new ArgumentNullException(nameof(setHeld));
            _isEditorFocused = isEditorFocused ?? throw new ArgumentNullException(nameof(isEditorFocused));
            _isPlaying = isPlaying ?? throw new ArgumentNullException(nameof(isPlaying));
            _disallowAutoRefresh = disallowAutoRefresh
                ?? throw new ArgumentNullException(nameof(disallowAutoRefresh));
            _allowAutoRefresh = allowAutoRefresh ?? throw new ArgumentNullException(nameof(allowAutoRefresh));
            _resolveBeforeRefresh = resolveBeforeRefresh
                ?? throw new ArgumentNullException(nameof(resolveBeforeRefresh));
            _refresh = refresh ?? throw new ArgumentNullException(nameof(refresh));
            // Why inject: pure C# unit tests stay free of VibeLogger; production wires VibeLogger.
            _logVibeInfo = logVibeInfo ?? ((operation, message, context) => { });
            _logVibeWarning = logVibeWarning ?? ((operation, message, context) => { });
        }

        internal bool IsHeld => _getHeld();

        /// <summary>
        /// Aligns the Auto Refresh hold with the live patch ledger. Idempotent when already aligned.
        /// </summary>
        internal HotReloadAutoRefreshHoldSyncResult Sync(int activeChangeCount)
        {
            Debug.Assert(activeChangeCount >= 0, "activeChangeCount must not be negative");
            if (activeChangeCount > 0)
            {
                return ArmIfNeeded();
            }

            return ReleaseIfHeld();
        }

        private HotReloadAutoRefreshHoldSyncResult ArmIfNeeded()
        {
            if (_getHeld())
            {
                return HotReloadAutoRefreshHoldSyncResult.Unchanged(true);
            }

            if (!TryDisallowAutoRefresh())
            {
                return HotReloadAutoRefreshHoldSyncResult.Unchanged(false);
            }

            // Why only after success: a failed Disallow must not leave SessionState claiming a hold.
            _setHeld(true);
            _refreshDeferred = false;
            _logVibeInfo(
                HotReloadAutoRefreshHoldConstants.VibeArmed,
                "Auto Refresh hold armed while hot-reload patches are active",
                new { held = true, isPlaying = _isPlaying(), isFocused = _isEditorFocused() });
            return new HotReloadAutoRefreshHoldSyncResult(true, true, false);
        }

        private HotReloadAutoRefreshHoldSyncResult ReleaseIfHeld()
        {
            if (!_getHeld())
            {
                return HotReloadAutoRefreshHoldSyncResult.Unchanged(false);
            }

            if (!TryAllowAutoRefresh())
            {
                return HotReloadAutoRefreshHoldSyncResult.Unchanged(true);
            }

            _setHeld(false);
            _logVibeInfo(
                HotReloadAutoRefreshHoldConstants.VibeReleased,
                "Auto Refresh hold released because no hot-reload patches remain",
                new { held = false, isPlaying = _isPlaying(), isFocused = _isEditorFocused() });
            return RefreshAfterRelease();
        }

        /// <summary>
        /// Runs a deferred Refresh after Play ends if the Editor is focused.
        /// </summary>
        internal HotReloadAutoRefreshHoldSyncResult FlushDeferredRefresh()
        {
            if (!_refreshDeferred)
            {
                return HotReloadAutoRefreshHoldSyncResult.Unchanged(_getHeld());
            }

            if (_isPlaying() || !_isEditorFocused())
            {
                return HotReloadAutoRefreshHoldSyncResult.Unchanged(_getHeld());
            }

            return RefreshIfPreflightAllows();
        }

        private HotReloadAutoRefreshHoldSyncResult RefreshAfterRelease()
        {
            if (_isPlaying())
            {
                // Why not Refresh during Play: an explicit import recompiles and stops Play Mode.
                _refreshDeferred = true;
                _logVibeInfo(
                    HotReloadAutoRefreshHoldConstants.VibeReleaseDeferred,
                    "Auto Refresh hold released; Refresh deferred until focus return or compile",
                    new { held = false, isPlaying = true, isFocused = _isEditorFocused() });
                return new HotReloadAutoRefreshHoldSyncResult(false, false, true);
            }

            if (!_isEditorFocused())
            {
                return HotReloadAutoRefreshHoldSyncResult.Unchanged(false);
            }

            return RefreshIfPreflightAllows();
        }

        private HotReloadAutoRefreshHoldSyncResult RefreshIfPreflightAllows()
        {
            (bool canProceed, _, _) = _resolveBeforeRefresh();
            _refreshDeferred = false;
            if (!canProceed)
            {
                return new HotReloadAutoRefreshHoldSyncResult(
                    false,
                    false,
                    false,
                    HotReloadAutoRefreshHoldConstants.SceneRefreshBlockedWarning);
            }

            _refresh();
            return HotReloadAutoRefreshHoldSyncResult.Unchanged(false);
        }

        private bool TryDisallowAutoRefresh()
        {
            // Why try-catch (approved, Disallow/Allow boundary only): Unity throws during kCodeReload.
            try
            {
                _disallowAutoRefresh();
                return true;
            }
            catch (Exception exception)
            {
                _logVibeWarning(
                    HotReloadAutoRefreshHoldConstants.VibeFailed,
                    "DisallowAutoRefresh failed",
                    new
                    {
                        exceptionType = exception.GetType().FullName,
                        exceptionMessage = exception.Message,
                        held = _getHeld(),
                        isPlaying = _isPlaying(),
                        isFocused = _isEditorFocused()
                    });
                return false;
            }
        }

        private bool TryAllowAutoRefresh()
        {
            try
            {
                _allowAutoRefresh();
                return true;
            }
            catch (Exception exception)
            {
                _logVibeWarning(
                    HotReloadAutoRefreshHoldConstants.VibeReleaseFailed,
                    "AllowAutoRefresh failed",
                    new
                    {
                        exceptionType = exception.GetType().FullName,
                        exceptionMessage = exception.Message,
                        held = _getHeld(),
                        isPlaying = _isPlaying(),
                        isFocused = _isEditorFocused()
                    });
                return false;
            }
        }
    }
}
