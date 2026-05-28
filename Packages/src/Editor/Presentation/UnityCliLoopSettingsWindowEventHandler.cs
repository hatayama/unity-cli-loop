using System.Runtime.CompilerServices;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;
using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;

[assembly: InternalsVisibleTo("UnityCLILoop.Tests.Editor")]

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Keeps editor and server event subscriptions out of the settings window presenter.
    /// </summary>
    internal class UnityCliLoopSettingsWindowEventHandler
    {
        private static readonly ProfilerMarker s_onEditorUpdateMarker =
            new ProfilerMarker("UnityCliLoopSettingsWindow.OnEditorUpdate");
        private static readonly ProfilerMarker s_refreshUiMarker =
            new ProfilerMarker("UnityCliLoopSettingsWindow.RefreshUI");

        private readonly UnityCliLoopSettingsModel _model;
        private readonly UnityCliLoopSettingsWindow _window;
        private readonly ToolSettingsUseCase _toolSettingsUseCase;

        public UnityCliLoopSettingsWindowEventHandler(
            UnityCliLoopSettingsModel model,
            UnityCliLoopSettingsWindow window,
            ToolSettingsUseCase toolSettingsUseCase)
        {
            Debug.Assert(toolSettingsUseCase != null, "toolSettingsUseCase must not be null");

            _model = model;
            _window = window;
            _toolSettingsUseCase = toolSettingsUseCase;
        }

        /// <summary>
        /// Initialize all event subscriptions
        /// </summary>
        public void Initialize()
        {
            SubscribeToUnityEvents();
            SubscribeToServerEvents();
        }

        /// <summary>
        /// Cleanup all event subscriptions
        /// </summary>
        public void Cleanup()
        {
            UnsubscribeFromUnityEvents();
            UnsubscribeFromServerEvents();
        }

        /// <summary>
        /// Subscribe to Unity Editor events
        /// </summary>
        private void SubscribeToUnityEvents()
        {
            EditorApplication.update += OnEditorUpdate;
        }

        /// <summary>
        /// Unsubscribe from Unity Editor events
        /// </summary>
        private void UnsubscribeFromUnityEvents()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        private void SubscribeToServerEvents()
        {
            UnsubscribeFromServerEvents();

            UnityCliLoopServerApplicationFacade.AddServerStateChangedHandler(OnServerStateChanged);
            _toolSettingsUseCase.AddToolsChangedHandler(OnToolsChanged);
        }

        private void UnsubscribeFromServerEvents()
        {
            UnityCliLoopServerApplicationFacade.RemoveServerStateChangedHandler(OnServerStateChanged);
            _toolSettingsUseCase.RemoveToolsChangedHandler(OnToolsChanged);
        }

        private void OnServerStateChanged()
        {
            _window.InvalidateToolSettingsCatalog();
            _model.RequestRepaint();
        }

        private void OnToolsChanged()
        {
            _window.InvalidateToolSettingsCatalog();
            _model.RequestRepaint();
        }

        private void OnEditorUpdate()
        {
            using (s_onEditorUpdateMarker.Auto())
            {
                if (!UnityCliLoopSettingsWindowRefreshPolicy.ShouldRefreshOnEditorUpdate(_model.Runtime))
                {
                    return;
                }

                _model.ClearRepaintRequest();
                using (s_refreshUiMarker.Auto())
                {
                    _window.RefreshAllSections();
                }
            }
        }

    }

    // Post-compile recovery can stay active while UI data is unchanged, so explicit repaint
    // requests gate expensive full-section refreshes.
    /// <summary>
    /// Defines the policy used to decide Unity CLI Loop Settings Window Refresh behavior.
    /// </summary>
    internal static class UnityCliLoopSettingsWindowRefreshPolicy
    {
        public static bool ShouldRefreshOnEditorUpdate(RuntimeState runtimeState)
        {
            Debug.Assert(runtimeState != null, "runtimeState must not be null");

            return runtimeState.NeedsRepaint;
        }

        public static bool ShouldRunExpensiveChecks(UnityCliLoopSettingsWindowRefreshMode refreshMode)
        {
            return refreshMode == UnityCliLoopSettingsWindowRefreshMode.Full;
        }

        public static bool ShouldRefreshSkillInstallState(
            UnityCliLoopSettingsWindowRefreshMode refreshMode,
            bool refreshRequested)
        {
            return refreshRequested && ShouldRunExpensiveChecks(refreshMode);
        }

        public static bool ShouldScheduleDeferredInitialRefresh(
            bool isAlreadyScheduled,
            bool hasCompleted)
        {
            return !isAlreadyScheduled && !hasCompleted;
        }

        public static bool ShouldStartSkillInstallStateRefresh(
            bool isCliInstalled,
            bool isRefreshingVersion,
            bool isInstallingSkills,
            bool allowDuringCliRefresh)
        {
            if (!isCliInstalled || isInstallingSkills)
            {
                return false;
            }

            return allowDuringCliRefresh || !isRefreshingVersion;
        }

        public static SkillInstallState ResolveSkillInstallStateWhenRefreshCannotStart(
            bool isCliInstalled,
            SkillInstallState currentState)
        {
            return isCliInstalled ? currentState : SkillInstallState.Missing;
        }

        public static bool ShouldKeepToolSettingsCatalogDirty(ToolSettingsSectionData toolSettingsData)
        {
            Debug.Assert(toolSettingsData != null, "toolSettingsData must not be null");

            return toolSettingsData.ShowToolSettings && !toolSettingsData.IsRegistryAvailable;
        }

        public static bool ShouldStartToolSettingsRegistryWarmup(
            bool isAlreadyScheduled,
            int attemptCount,
            int maxAttempts)
        {
            Debug.Assert(attemptCount >= 0, "attemptCount must not be negative");
            Debug.Assert(maxAttempts > 0, "maxAttempts must be positive");

            return !isAlreadyScheduled && attemptCount < maxAttempts;
        }

        public static double CalculateToolSettingsRegistryWarmupDelaySeconds(
            double initialDelaySeconds,
            double maxDelaySeconds,
            int attemptCount)
        {
            Debug.Assert(initialDelaySeconds > 0.0, "initialDelaySeconds must be positive");
            Debug.Assert(maxDelaySeconds >= initialDelaySeconds, "maxDelaySeconds must not be smaller than initialDelaySeconds");
            Debug.Assert(attemptCount >= 0, "attemptCount must not be negative");

            double delaySeconds = initialDelaySeconds;
            for (int i = 0; i < attemptCount; i++)
            {
                delaySeconds *= 2.0;
            }

            return System.Math.Min(delaySeconds, maxDelaySeconds);
        }
    }

    internal enum UnityCliLoopSettingsWindowRefreshMode
    {
        InitialPaint,
        Full
    }
}
