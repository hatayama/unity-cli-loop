using System;
using System.Diagnostics;
using System.Linq;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Application
{
    internal sealed class ToolSettingsUseCase
    {
        private readonly ToolSettingsService _toolSettingsService;
        private readonly UnityCliLoopToolRegistrarService _toolRegistrarService;

        internal ToolSettingsUseCase(
            ToolSettingsService toolSettingsService,
            UnityCliLoopToolRegistrarService toolRegistrarService)
        {
            Debug.Assert(toolSettingsService != null, "toolSettingsService must not be null");
            Debug.Assert(toolRegistrarService != null, "toolRegistrarService must not be null");

            _toolSettingsService = toolSettingsService ?? throw new ArgumentNullException(nameof(toolSettingsService));
            _toolRegistrarService = toolRegistrarService ?? throw new ArgumentNullException(nameof(toolRegistrarService));
        }

        internal DynamicCodeSecurityLevel GetDynamicCodeSecurityLevel()
        {
            return ULoopSettings.GetDynamicCodeSecurityLevel();
        }

        internal void SetDynamicCodeSecurityLevel(DynamicCodeSecurityLevel level)
        {
            ULoopSettings.SetDynamicCodeSecurityLevel(level);
        }

        internal bool IsToolEnabled(string toolName)
        {
            return _toolSettingsService.IsToolEnabled(toolName);
        }

        internal void SetToolEnabled(string toolName, bool enabled)
        {
            _toolSettingsService.SetToolEnabled(toolName, enabled);
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

            allTools = registry.GetToolSettingsCatalog()
                .Select(item => new ToolCatalogItem(
                    item.Name,
                    item.DisplayDevelopmentOnly,
                    item.IsThirdParty))
                .ToArray();
            return true;
        }

        internal readonly struct ToolCatalogItem
        {
            internal readonly string Name;
            internal readonly bool DisplayDevelopmentOnly;
            internal readonly bool IsThirdParty;

            internal ToolCatalogItem(
                string name,
                bool displayDevelopmentOnly,
                bool isThirdParty)
            {
                Name = name;
                DisplayDevelopmentOnly = displayDevelopmentOnly;
                IsThirdParty = isThirdParty;
            }
        }
    }
}
