using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Application
{
    // Port for persisted tool toggle settings.
    /// <summary>
    /// Defines the Tool Settings boundary required by the application layer.
    /// </summary>
    public interface IToolSettingsPort
    {
        ToolSettingsData GetSettings();
        void SaveSettings(ToolSettingsData settings);
        bool IsToolEnabled(string toolName);
        void SetToolEnabled(string toolName, bool enabled);
        string[] GetDisabledTools();
        void InvalidateCache();
    }

    // Static facade retained for Unity callbacks and legacy call sites outside constructor control.
    /// <summary>
    /// Holds Tool settings used by Unity CLI Loop.
    /// </summary>
    public static class ToolSettings
    {
        private static IToolSettingsPort ServiceValue;

        internal static void RegisterService(IToolSettingsPort service)
        {
            Debug.Assert(service != null, "service must not be null");

            ServiceValue = service ?? throw new ArgumentNullException(nameof(service));
        }

        public static ToolSettingsData GetSettings()
        {
            return Service.GetSettings();
        }

        public static void SaveSettings(ToolSettingsData settings)
        {
            Service.SaveSettings(settings);
        }

        public static bool IsToolEnabled(string toolName)
        {
            return Service.IsToolEnabled(toolName);
        }

        public static void SetToolEnabled(string toolName, bool enabled)
        {
            Service.SetToolEnabled(toolName, enabled);
        }

        public static string[] GetDisabledTools()
        {
            return Service.GetDisabledTools();
        }

        public static void InvalidateCache()
        {
            Service.InvalidateCache();
        }

        private static IToolSettingsPort Service
        {
            get
            {
                if (ServiceValue == null)
                {
                    throw new InvalidOperationException("Unity CLI Loop tool settings service is not registered.");
                }

                return ServiceValue;
            }
        }
    }

    // Application facade for tool catalog and security settings workflows.
    /// <summary>
    /// Provides a narrow facade over Tool Settings Application behavior.
    /// </summary>
    public static class ToolSettingsApplicationFacade
    {
        public readonly struct ToolCatalogItem
        {
            public readonly string Name;
            public readonly bool DisplayDevelopmentOnly;
            public readonly bool IsThirdParty;

            public ToolCatalogItem(
                string name,
                bool displayDevelopmentOnly,
                bool isThirdParty)
            {
                Name = name;
                DisplayDevelopmentOnly = displayDevelopmentOnly;
                IsThirdParty = isThirdParty;
            }
        }

        public static void AddToolsChangedHandler(Action handler)
        {
            UnityCliLoopToolRegistrar.AddToolsChangedHandler(handler);
        }

        public static void RemoveToolsChangedHandler(Action handler)
        {
            UnityCliLoopToolRegistrar.RemoveToolsChangedHandler(handler);
        }

        public static DynamicCodeSecurityLevel GetDynamicCodeSecurityLevel()
        {
            return ULoopSettings.GetDynamicCodeSecurityLevel();
        }

        public static void SetDynamicCodeSecurityLevel(DynamicCodeSecurityLevel level)
        {
            ULoopSettings.SetDynamicCodeSecurityLevel(level);
        }

        public static bool IsToolEnabled(string toolName)
        {
            return ToolSettings.IsToolEnabled(toolName);
        }

        public static void SetToolEnabled(string toolName, bool enabled)
        {
            ToolSettings.SetToolEnabled(toolName, enabled);
        }

        public static void WarmupRegistry()
        {
            UnityCliLoopToolRegistrar.WarmupRegistry();
        }

        public static bool TryGetToolCatalog(out ToolCatalogItem[] catalog)
        {
            UnityCliLoopToolRegistry registry = UnityCliLoopToolRegistrar.TryGetRegistry();
            if (registry == null)
            {
                catalog = Array.Empty<ToolCatalogItem>();
                return false;
            }

            catalog = registry.GetToolSettingsCatalog()
                .Select(ToFacadeItem)
                .ToArray();
            return true;
        }

        private static ToolCatalogItem ToFacadeItem(ToolSettingsCatalogItem item)
        {
            return new ToolCatalogItem(
                item.Name,
                item.DisplayDevelopmentOnly,
                item.IsThirdParty);
        }
    }
}
