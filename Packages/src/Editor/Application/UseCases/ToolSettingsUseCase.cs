using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Application
{
    internal interface IToolSkillDescriptionProvider
    {
        IReadOnlyDictionary<string, string> GetSkillDescriptionsByToolName();
    }

    internal sealed class ToolSettingsUseCase
    {
        private static readonly string[] NativeToolNames =
        {
            UnityCliLoopConstants.COMMAND_NAME_WAIT_FOR_PAUSE_POINT
        };

        private readonly ToolSettingsService _toolSettingsService;
        private readonly UnityCliLoopToolRegistrarService _toolRegistrarService;
        private readonly IToolSkillDescriptionProvider _toolSkillDescriptionProvider;

        internal ToolSettingsUseCase(
            ToolSettingsService toolSettingsService,
            UnityCliLoopToolRegistrarService toolRegistrarService,
            IToolSkillDescriptionProvider toolSkillDescriptionProvider)
        {
            Debug.Assert(toolSettingsService != null, "toolSettingsService must not be null");
            Debug.Assert(toolRegistrarService != null, "toolRegistrarService must not be null");
            Debug.Assert(toolSkillDescriptionProvider != null, "toolSkillDescriptionProvider must not be null");

            _toolSettingsService = toolSettingsService ?? throw new ArgumentNullException(nameof(toolSettingsService));
            _toolRegistrarService = toolRegistrarService ?? throw new ArgumentNullException(nameof(toolRegistrarService));
            _toolSkillDescriptionProvider = toolSkillDescriptionProvider
                ?? throw new ArgumentNullException(nameof(toolSkillDescriptionProvider));
        }

        internal bool IsToolEnabled(string toolName)
        {
            return ToolSettingsToolLinkPolicy.IsToolEnabled(toolName, _toolSettingsService);
        }

        internal void SetToolEnabled(string toolName, bool enabled)
        {
            string settingsToolName = ToolSettingsToolLinkPolicy.GetSettingsToolName(toolName);
            _toolSettingsService.SetToolEnabled(settingsToolName, enabled);
            _toolRegistrarService.NotifyToolChanges();
        }

        internal void AddToolsChangedHandler(Action handler)
        {
            _toolRegistrarService.OnToolsChanged += handler;
        }

        internal void RemoveToolsChangedHandler(Action handler)
        {
            _toolRegistrarService.OnToolsChanged -= handler;
        }

        internal void WarmupRegistry()
        {
            _toolRegistrarService.WarmupRegistry();
        }

        internal bool TryGetToolCatalog(out ToolCatalogItem[] allTools)
        {
            UnityCliLoopToolRegistry registry = _toolRegistrarService.TryGetRegistry();
            if (registry == null)
            {
                allTools = Array.Empty<ToolCatalogItem>();
                return false;
            }

            IReadOnlyDictionary<string, string> descriptions =
                _toolSkillDescriptionProvider.GetSkillDescriptionsByToolName();
            ToolCatalogItem[] registryTools = registry.GetToolSettingsCatalog()
                .Where(item => ToolSettingsToolLinkPolicy.IsUserFacingToolSettingsTool(item.Name))
                .Select(item => new ToolCatalogItem(
                    item.Name,
                    item.DisplayDevelopmentOnly,
                    item.IsThirdParty,
                    GetDescriptionForTool(item.Name, descriptions)))
                .ToArray();
            allTools = registryTools.Concat(CreateNativeToolCatalogItems(descriptions)).ToArray();
            return true;
        }

        private static ToolCatalogItem[] CreateNativeToolCatalogItems(IReadOnlyDictionary<string, string> descriptions)
        {
            return NativeToolNames
                .Select(toolName => new ToolCatalogItem(
                    toolName,
                    displayDevelopmentOnly: false,
                    isThirdParty: false,
                    skillDescription: GetDescriptionForTool(toolName, descriptions)))
                .ToArray();
        }

        private static string GetDescriptionForTool(
            string toolName,
            IReadOnlyDictionary<string, string> descriptions)
        {
            Debug.Assert(!string.IsNullOrEmpty(toolName), "toolName must not be null or empty");
            Debug.Assert(descriptions != null, "descriptions must not be null");

            if (descriptions.TryGetValue(toolName, out string description)
                && !string.IsNullOrWhiteSpace(description))
            {
                return description;
            }

            return string.Empty;
        }

        internal readonly struct ToolCatalogItem
        {
            internal readonly string Name;
            internal readonly bool DisplayDevelopmentOnly;
            internal readonly bool IsThirdParty;
            internal readonly string SkillDescription;

            internal ToolCatalogItem(
                string name,
                bool displayDevelopmentOnly,
                bool isThirdParty,
                string skillDescription)
            {
                Name = name;
                DisplayDevelopmentOnly = displayDevelopmentOnly;
                IsThirdParty = isThirdParty;
                SkillDescription = skillDescription;
            }
        }
    }
}
