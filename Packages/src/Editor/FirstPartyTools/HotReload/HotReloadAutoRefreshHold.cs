using UnityEditor;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Wires the Auto Refresh hold policy to SessionState, AssetDatabase, startup, and reconcile.
    /// </summary>
    internal static class HotReloadAutoRefreshHold
    {
        private static HotReloadAutoRefreshHoldService _productionService;
        private static HotReloadAutoRefreshHoldService _overrideService;
        private static bool _initialized;
        private static double _nextReconcileTime;

        /// <summary>
        /// Test hook that replaces Unity AssetDatabase calls with recording delegates.
        /// </summary>
        internal static HotReloadAutoRefreshHoldService OverrideServiceForTesting
        {
            get => _overrideService;
            set => _overrideService = value;
        }

        internal static bool IsHeld => ResolveService().IsHeld;

        internal static HotReloadAutoRefreshHoldSyncResult Sync(int activeChangeCount)
        {
            return ResolveService().Sync(activeChangeCount);
        }

        internal static void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            EditorApplication.update -= ReconcileOnUpdate;
            EditorApplication.update += ReconcileOnUpdate;
            // Why immediate Sync: domain reload clears the ledger, so a stale SessionState
            // flag from Play exit, compile, or a crash must be released on the next load.
            Sync(HotReloadPatcher.ActiveChangeCount);
        }

        private static void ReconcileOnUpdate()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now < _nextReconcileTime)
            {
                return;
            }

            _nextReconcileTime = now + HotReloadAutoRefreshHoldConstants.ReconcileIntervalSeconds;
            Sync(HotReloadPatcher.ActiveChangeCount);
        }

        private static HotReloadAutoRefreshHoldService ResolveService()
        {
            if (_overrideService != null)
            {
                return _overrideService;
            }

            if (_productionService == null)
            {
                _productionService = CreateProductionService();
            }

            return _productionService;
        }

        private static HotReloadAutoRefreshHoldService CreateProductionService()
        {
            return new HotReloadAutoRefreshHoldService(
                () => SessionState.GetBool(HotReloadAutoRefreshHoldConstants.SessionStateKey, false),
                value => SessionState.SetBool(HotReloadAutoRefreshHoldConstants.SessionStateKey, value),
                () => EditorApplication.isFocused,
                () => EditorApplication.isPlaying,
                AssetDatabase.DisallowAutoRefresh,
                AssetDatabase.AllowAutoRefresh,
                AssetDatabase.Refresh,
                (operation, message, context) =>
                {
                    VibeLogger.LogInfo(operation, message, context, includeStackTrace: false);
                },
                (operation, message, context) =>
                {
                    VibeLogger.LogWarning(operation, message, context, includeStackTrace: false);
                });
        }
    }
}
