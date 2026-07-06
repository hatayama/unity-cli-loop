using System;
using System.Collections.Generic;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Presents the Tool Settings section in the Unity CLI Loop settings window.
    /// </summary>
    internal sealed class UnityCliLoopSettingsToolSettingsPresenter
    {
        private readonly UnityCliLoopSettingsWindowUI _view;
        private readonly ToolSettingsUseCase _toolSettingsUseCase;

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

        internal void UpdateHeader(bool showToolSettings)
        {
            ToolSettingsSectionData toolSettingsData = CreateToolSettingsHeaderData(showToolSettings);
            _view.UpdateToolSettings(toolSettingsData);
        }

        internal ToolSettingsSectionData UpdateCatalog(bool showToolSettings)
        {
            ToolSettingsSectionData toolSettingsData = CreateToolSettingsData(showToolSettings);
            _view.UpdateToolSettings(toolSettingsData);
            return toolSettingsData;
        }

        private static ToolSettingsSectionData CreateToolSettingsHeaderData(bool showToolSettings)
        {
            return new ToolSettingsSectionData(
                showToolSettings,
                System.Array.Empty<ToolToggleItem>(),
                System.Array.Empty<ToolToggleItem>(),
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
                    System.Array.Empty<ToolToggleItem>(),
                    System.Array.Empty<ToolToggleItem>(),
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
