using System;
using System.Reflection;

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

        /// <summary>
        /// Reports whether EditorApplication.update already has this type's reconcile handler.
        /// </summary>
        internal static bool IsReconcileRegistered()
        {
            FieldInfo[] fields = typeof(EditorApplication).GetFields(
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            for (int index = 0; index < fields.Length; index++)
            {
                if (SourceContainsHoldHandler(fields[index].GetValue(null)))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ReconcileOnUpdate()
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
            Sync(HotReloadPatcher.ActiveChangeCount);
        }

        private static bool SourceContainsHoldHandler(object source)
        {
            if (source == null)
            {
                return false;
            }

            Delegate current = source as Delegate;
            if (current != null)
            {
                return InvocationListContainsHoldHandler(current);
            }

            return EventTrackerContainsHoldHandler(source);
        }

        private static bool InvocationListContainsHoldHandler(Delegate current)
        {
            Delegate[] listeners = current.GetInvocationList();
            for (int index = 0; index < listeners.Length; index++)
            {
                if (IsReconcileHandler(listeners[index]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool EventTrackerContainsHoldHandler(object source)
        {
            string typeName = source.GetType().Name;
            if (typeName.IndexOf("EventWithPerformanceTracker", StringComparison.Ordinal) < 0)
            {
                return false;
            }

            MethodInfo getEnumerator = source.GetType().GetMethod(
                "GetEnumerator",
                BindingFlags.Instance | BindingFlags.Public);
            if (getEnumerator == null || getEnumerator.GetParameters().Length != 0)
            {
                return false;
            }

            object enumerator = getEnumerator.Invoke(source, null);
            if (enumerator == null)
            {
                return false;
            }

            MethodInfo moveNext = enumerator.GetType().GetMethod("MoveNext");
            PropertyInfo currentProperty = enumerator.GetType().GetProperty("Current");
            if (moveNext == null || currentProperty == null)
            {
                return false;
            }

            while ((bool)moveNext.Invoke(enumerator, null))
            {
                Delegate listener = currentProperty.GetValue(enumerator) as Delegate;
                if (IsReconcileHandler(listener))
                {
                    return true;
                }
            }

            return false;
        }

        private static void HandlePlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode)
            {
                return;
            }

            FlushDeferredRefresh();
        }

        private static bool IsReconcileHandler(Delegate listener)
        {
            return listener != null
                && listener.Method.DeclaringType == typeof(HotReloadAutoRefreshHold)
                && listener.Method.Name == nameof(ReconcileOnUpdate);
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
