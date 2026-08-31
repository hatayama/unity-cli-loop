using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Presents the Tool Settings section and owns catalog cache / registry warmup.
    /// </summary>
    internal sealed class UnityCliLoopSettingsToolSettingsPresenter
    {
        private const double RegistryWarmupInitialDelaySeconds = 0.05;
        private const double RegistryWarmupMaxDelaySeconds = 0.8;
        private const int RegistryWarmupMaxAttempts = 5;

        private readonly UnityCliLoopSettingsWindowUI _view;
        private readonly ToolSettingsUseCase _toolSettingsUseCase;

        private bool _isCatalogDirty = true;
        private bool _isRegistryWarmupScheduled;
        private double _registryWarmupDueTime;
        private int _registryWarmupAttemptCount;
        private bool _showToolSettings = true;
        private bool _isViewReady;

        internal UnityCliLoopSettingsToolSettingsPresenter(
            UnityCliLoopSettingsWindowUI view,
            ToolSettingsUseCase toolSettingsUseCase)
        {
            Debug.Assert(view != null, "view must not be null");
            Debug.Assert(toolSettingsUseCase != null, "toolSettingsUseCase must not be null");

            _view = view ?? throw new ArgumentNullException(nameof(view));
            _toolSettingsUseCase = toolSettingsUseCase
                ?? throw new ArgumentNullException(nameof(toolSettingsUseCase));
        }

        internal void SetViewReady(bool isViewReady)
        {
            _isViewReady = isViewReady;
            if (!isViewReady)
            {
                CancelRegistryWarmup();
            }
        }

        internal void InvalidateCatalog()
        {
            _isCatalogDirty = true;
        }

        internal void UpdateHeader(bool showToolSettings)
        {
            _showToolSettings = showToolSettings;
            ToolSettingsSectionData toolSettingsData = CreateToolSettingsHeaderData(showToolSettings);
            _view.UpdateToolSettings(toolSettingsData);
        }

        internal void RefreshCatalogIfNeeded(bool showToolSettings)
        {
            _showToolSettings = showToolSettings;
            if (!showToolSettings || !_isCatalogDirty || !_isViewReady)
            {
                return;
            }

            RefreshCatalog(showToolSettings);
        }

        internal void HandleShowToolSettingsChanged(bool show)
        {
            UpdateHeader(show);
            if (!show)
            {
                _isCatalogDirty = true;
                CancelRegistryWarmup();
                ResetRegistryWarmupAttemptCount();
                return;
            }

            RefreshCatalogIfNeeded(show);
        }

        internal void CancelRegistryWarmup()
        {
            if (!_isRegistryWarmupScheduled)
            {
                return;
            }

            EditorApplication.update -= RunRegistryWarmupWhenDue;
            _isRegistryWarmupScheduled = false;
        }

        internal void ResetRegistryWarmupAttemptCount()
        {
            _registryWarmupAttemptCount = 0;
        }

        private void RefreshCatalog(bool showToolSettings)
        {
            ToolSettingsSectionData toolSettingsData = CreateToolSettingsData(showToolSettings);
            _view.UpdateToolSettings(toolSettingsData);

            if (UnityCliLoopSettingsWindowRefreshPolicy.ShouldKeepToolSettingsCatalogDirty(toolSettingsData))
            {
                if (ScheduleRegistryWarmup())
                {
                    _isCatalogDirty = true;
                    return;
                }

                _isCatalogDirty = false;
                return;
            }

            CancelRegistryWarmup();
            ResetRegistryWarmupAttemptCount();
            _isCatalogDirty = false;
        }

        private bool ScheduleRegistryWarmup()
        {
            if (UnityCliLoopSettingsWindowRefreshPolicy.ShouldStartToolSettingsRegistryWarmup(
                    _isRegistryWarmupScheduled,
                    _registryWarmupAttemptCount,
                    RegistryWarmupMaxAttempts))
            {
                double delaySeconds = UnityCliLoopSettingsWindowRefreshPolicy.CalculateToolSettingsRegistryWarmupDelaySeconds(
                    RegistryWarmupInitialDelaySeconds,
                    RegistryWarmupMaxDelaySeconds,
                    _registryWarmupAttemptCount);

                _isRegistryWarmupScheduled = true;
                _registryWarmupDueTime = EditorApplication.timeSinceStartup + delaySeconds;
                _registryWarmupAttemptCount++;
                EditorApplication.update += RunRegistryWarmupWhenDue;
                return true;
            }

            return _isRegistryWarmupScheduled;
        }

        private void RunRegistryWarmupWhenDue()
        {
            if (EditorApplication.timeSinceStartup < _registryWarmupDueTime)
            {
                return;
            }

            CancelRegistryWarmup();

            if (!_isViewReady || !_showToolSettings)
            {
                ResetRegistryWarmupAttemptCount();
                return;
            }

            _toolSettingsUseCase.WarmupRegistry();
            InvalidateCatalog();
            RefreshCatalogIfNeeded(_showToolSettings);
        }

        private static ToolSettingsSectionData CreateToolSettingsHeaderData(bool showToolSettings)
        {
            return new ToolSettingsSectionData(
                showToolSettings,
                Array.Empty<ToolToggleItem>(),
                Array.Empty<ToolToggleItem>(),
                true,
                false);
        }

        private ToolSettingsSectionData CreateToolSettingsData(bool showToolSettings)
        {
            bool isRegistryAvailable =
                _toolSettingsUseCase.TryGetToolCatalog(
                    out ToolSettingsUseCase.ToolCatalogItem[] allTools);
            if (!isRegistryAvailable)
            {
                return new ToolSettingsSectionData(
                    showToolSettings,
                    Array.Empty<ToolToggleItem>(),
                    Array.Empty<ToolToggleItem>(),
                    false,
                    true);
            }

            List<ToolToggleItem> builtIn = new();
            List<ToolToggleItem> thirdParty = new();

            foreach (ToolSettingsUseCase.ToolCatalogItem tool in allTools)
            {
                if (tool.DisplayDevelopmentOnly)
                {
                    continue;
                }

                bool isEnabled = _toolSettingsUseCase.IsToolEnabled(tool.Name);
                bool isThirdPartyTool = tool.IsThirdParty;

                ToolToggleItem item = new(
                    tool.Name,
                    isEnabled,
                    isThirdPartyTool,
                    tool.SkillDescription);
                if (isThirdPartyTool)
                {
                    thirdParty.Add(item);
                }
                else
                {
                    builtIn.Add(item);
                }
            }

            Comparison<ToolToggleItem> compareByName = (a, b) => string.Compare(a.ToolName, b.ToolName, StringComparison.Ordinal);
            builtIn.Sort(compareByName);
            thirdParty.Sort(compareByName);

            return new ToolSettingsSectionData(
                showToolSettings,
                builtIn.ToArray(),
                thirdParty.ToArray(),
                true,
                true);
        }
    }
}
