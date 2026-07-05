using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Registry for Unity CLI tool implementations and their catalog metadata.
    /// Tool discovery is performed outside this class; callers pass in the tools to register.
    /// </summary>
    public class UnityCliLoopToolRegistry
    {
        private readonly Dictionary<string, IUnityCliLoopTool> _tools = new();
        private readonly IInternalToolNameProvider _internalToolNameProvider;
        private readonly ToolSettingsService _toolSettingsService;

        /// <summary>
        /// Creates a registry. Callers must pass <paramref name="toolDiscovery"/> explicitly;
        /// pass null to get a manual-registration-only registry with no automatic scan.
        /// </summary>
        /// <param name="toolSettingsService">Service used to resolve per-tool enabled state.</param>
        /// <param name="internalToolNameProvider">Provider of internal tool names to hide from catalogs; null uses an empty provider.</param>
        /// <param name="toolDiscovery">Delegate that returns the tools to auto-register; null registers no tools automatically.</param>
        internal UnityCliLoopToolRegistry(
            ToolSettingsService toolSettingsService,
            IInternalToolNameProvider internalToolNameProvider,
            Func<IReadOnlyList<IUnityCliLoopTool>> toolDiscovery)
        {
            System.Diagnostics.Debug.Assert(toolSettingsService != null, "toolSettingsService must not be null");

            _toolSettingsService = toolSettingsService ?? throw new ArgumentNullException(nameof(toolSettingsService));
            _internalToolNameProvider = internalToolNameProvider ?? new EmptyInternalToolNameProvider();

            if (toolDiscovery == null)
            {
                return;
            }

            foreach (IUnityCliLoopTool tool in toolDiscovery())
            {
                RegisterTool(tool);
            }
        }

        public void RegisterTool(IUnityCliLoopTool tool)
        {
            if (tool == null)
            {
                throw new ArgumentNullException(nameof(tool));
            }

            if (string.IsNullOrWhiteSpace(tool.ToolName))
            {
                throw new ArgumentException("Tool name cannot be null or empty", nameof(tool));
            }

            _tools[tool.ToolName] = tool;
        }

        public void UnregisterTool(string toolName)
        {
            _tools.Remove(toolName);
        }

        public bool TryGetTool(string toolName, out IUnityCliLoopTool tool)
        {
            return _tools.TryGetValue(toolName, out tool);
        }

        public bool IsToolEnabled(string toolName)
        {
            return ToolSettingsToolLinkPolicy.IsToolEnabled(toolName, _toolSettingsService);
        }

        public ToolInfo[] GetRegisteredTools()
        {
            return GetRegisteredToolsForProjectRoot(UnityCliLoopPathResolver.GetProjectRoot());
        }

        internal ToolInfo[] GetRegisteredToolsForProjectRoot(string projectRoot)
        {
            HashSet<string> internalToolNames = _internalToolNameProvider.GetInternalToolNames(projectRoot);
            return _tools.Values
                .Where(tool => ToolExecutionAvailability.ShouldExposeInRegisteredTools(
                    tool.ToolName,
                    ToolSettingsToolLinkPolicy.IsToolEnabled(tool.ToolName, _toolSettingsService)))
                .Where(tool => !internalToolNames.Contains(tool.ToolName))
                .Select(tool =>
            {
                bool displayDevelopmentOnly = false;
                UnityCliLoopToolAttribute attribute = tool.GetType().GetCustomAttribute<UnityCliLoopToolAttribute>();
                if (attribute != null)
                {
                    displayDevelopmentOnly = attribute.DisplayDevelopmentOnly;
                }

                return new ToolInfo(tool.ToolName, tool.ParameterSchema, displayDevelopmentOnly);
            }).ToArray();
        }

        public ToolSettingsCatalogItem[] GetToolSettingsCatalog()
        {
            return GetToolSettingsCatalogForProjectRoot(UnityCliLoopPathResolver.GetProjectRoot());
        }

        internal ToolSettingsCatalogItem[] GetToolSettingsCatalogForProjectRoot(string projectRoot)
        {
            HashSet<string> internalToolNames = _internalToolNameProvider.GetInternalToolNames(projectRoot);
            return _tools.Values
                .Where(tool => ToolSettingsToolLinkPolicy.IsUserFacingToolSettingsTool(tool.ToolName))
                .Where(tool => !internalToolNames.Contains(tool.ToolName))
                .Select(tool =>
            {
                Type toolType = tool.GetType();
                UnityCliLoopToolAttribute attribute = toolType.GetCustomAttribute<UnityCliLoopToolAttribute>();
                bool displayDevelopmentOnly = attribute?.DisplayDevelopmentOnly ?? false;
                bool isThirdParty = ToolAssemblyClassifier.IsThirdPartyAssembly(toolType.Assembly.GetName().Name);

                return new ToolSettingsCatalogItem(
                    tool.ToolName,
                    displayDevelopmentOnly,
                    isThirdParty);
            }).ToArray();
        }

        public Type GetToolType(string toolName)
        {
            if (_tools.TryGetValue(toolName, out IUnityCliLoopTool tool))
            {
                return tool.GetType();
            }
            return null;
        }

        public bool IsToolRegistered(string toolName)
        {
            return _tools.ContainsKey(toolName);
        }

    }
}
