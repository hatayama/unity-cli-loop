using System;

using UnityEditor;
using UnityEngine;

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
        /// Reads the installed services when the hold syncs itself.
        /// </summary>
        /// <remarks>
        /// Why a provider and not a captured value: this syncs from Editor callbacks that take no
        /// argument, and a replacement scope installs another domain while those callbacks stay
        /// registered, so a value captured at startup would count the wrong domain's changes.
        /// </remarks>
        internal static Func<HotReloadServices> GetServices { get; set; }

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

        /// <summary>
        /// Syncs the hold against everything the next Domain Reload would discard.
        /// </summary>
        /// <remarks>
        /// Why callers do not pass the count: the hold is synced from four places, and a caller
        /// that counted for itself could count patches alone while another counted types too,
        /// leaving Auto Refresh free to unload a type between two of them.
        /// </remarks>
        internal static HotReloadAutoRefreshHoldSyncResult SyncToActiveChanges()
        {
            Debug.Assert(GetServices != null, "GetServices must be set before the hold syncs.");
            return Sync(GetServices().Domain.CountActiveChanges().RuntimeChangeTotal);
        }

        internal static HotReloadAutoRefreshHoldSyncResult FlushDeferredRefresh()
        {
            return ResolveService().FlushDeferredRefresh();
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
            EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
            // Why not Sync here: CompileEditorStartup assigns the scene-change seam in
            // another assembly, and InitializeOnLoad order can run this before that
            // assignment. _nextReconcileTime starts at 0, so the first update tick Syncs.
        }

        /// <summary>
        /// Runs the update reconcile body without the 0.5s time gate.
        /// </summary>
        internal static void ReconcileForTesting()
        {
            ReconcileNow();
        }

        // Why internal: EditMode tests match this handler by nameof after walking update.
        internal static void ReconcileOnUpdate()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now < _nextReconcileTime)
            {
                return;
            }

            _nextReconcileTime = now + HotReloadAutoRefreshHoldConstants.ReconcileIntervalSeconds;
            ReconcileNow();
        }

        private static void ReconcileNow()
        {
            SyncToActiveChanges();
        }

        private static void HandlePlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode)
            {
                return;
            }

            FlushDeferredRefresh();
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
                ResolveBeforeRefreshForProduction,
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

        private static (bool CanProceed, string Message, string[] ScenePaths)
            ResolveBeforeRefreshForProduction()
        {
            Func<bool, (bool CanProceed, string Message, string[] ScenePaths)> resolve =
                ExternalSceneChangeCoordination.ResolveBeforeRefresh;
            Debug.Assert(
                resolve != null,
                "CompileEditorStartup must assign ExternalSceneChangeCoordination.ResolveBeforeRefresh before refresh");
            return resolve(true);
        }
    }
}
