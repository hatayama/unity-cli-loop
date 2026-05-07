using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Stopwatch = System.Diagnostics.Stopwatch;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Application
{
    // Related classes:
    // - UnityToolExecutor: Uses this registry to execute _tools.
    // - IUnityCliLoopTool: The interface for all _tools stored in this registry.
    // - UnityCliLoopTool: The base class for most tool implementations.
    // - UnityCliLoopToolAttribute: Attribute used to discover and register _tools automatically.
    /// <summary>
    /// Unity CLI tool registry class
    /// Supports dynamic tool registration, allowing users to add their own _tools
    /// </summary>
    public class UnityCliLoopToolRegistry
    {
        private const string ApplicationAssemblyName = "UnityCLILoop.Application";
        private const string FirstPartyToolsAssemblyNamePrefix = "UnityCLILoop.FirstPartyTools.";

        private readonly Dictionary<string, IUnityCliLoopTool> _tools = new();
        private readonly IInternalToolNameProvider _internalToolNameProvider;

        internal UnityCliLoopToolRegistry(IInternalToolNameProvider internalToolNameProvider = null)
        {
            _internalToolNameProvider = internalToolNameProvider ?? new EmptyInternalToolNameProvider();
            RegisterDefaultTools();
        }

        private void RegisterDefaultTools()
        {
            RegisterToolsWithAttributes();
        }

        private void RegisterToolsWithAttributes()
        {
            Assembly[] assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
            List<Type> toolTypes = new();

            foreach (Assembly assembly in assemblies)
            {
                Type[] types = assembly.GetTypes()
                    .Where(type => type.GetCustomAttribute<UnityCliLoopToolAttribute>() != null)
                    .Where(type => typeof(IUnityCliLoopTool).IsAssignableFrom(type))
                    .Where(type => !type.IsAbstract && !type.IsInterface)
                    .ToArray();

                toolTypes.AddRange(types);
            }

            foreach (Type type in toolTypes)
            {
                if (!IsValidToolType(type))
                {
                    UnityEngine.Debug.LogWarning($"{UnityCliLoopConstants.SECURITY_LOG_PREFIX} Skipping invalid tool type: {type.FullName}");
                    continue;
                }

                IUnityCliLoopTool tool = CreateTool(type);
                RegisterTool(tool);
            }
        }

        private IUnityCliLoopTool CreateTool(Type type)
        {
            IUnityCliLoopTool tool = (IUnityCliLoopTool)Activator.CreateInstance(type);
            return tool;
        }

        private bool IsValidToolType(Type type)
        {
            if (!typeof(IUnityCliLoopTool).IsAssignableFrom(type))
            {
                return false;
            }

            if (type.IsAbstract || type.IsInterface)
            {
                return false;
            }

            if (type.GetCustomAttribute<UnityCliLoopToolAttribute>() == null)
            {
                return false;
            }

            return type.GetConstructor(Type.EmptyTypes) != null;
        }

        /// <summary>
        /// Register tool
        /// </summary>
        /// <param name="tool">Tool to register</param>
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

        /// <summary>
        /// Unregister tool
        /// </summary>
        /// <param name="toolName">Name of tool to unregister</param>
        public void UnregisterTool(string toolName)
        {
            _tools.Remove(toolName);
        }

        /// <summary>
        /// Execute tool
        /// </summary>
        /// <param name="toolName">Tool name</param>
        /// <param name="paramsToken">Parameters</param>
        /// <returns>Execution result</returns>
        /// <exception cref="ArgumentException">When tool is unknown</exception>
        /// <exception cref="UnityCliLoopSecurityException">When tool is blocked by security settings</exception>
        public async Task<UnityCliLoopToolResponse> ExecuteToolAsync(string toolName, JToken paramsToken)
        {
            if (!_tools.TryGetValue(toolName, out IUnityCliLoopTool tool))
            {
                throw new ArgumentException($"Unknown tool: {toolName}");
            }

            if (!ToolSettings.IsToolEnabled(toolName))
            {
                throw new ToolDisabledException(toolName);
            }

            if (!UnityCliLoopSecurityChecker.IsToolAllowed(toolName))
            {
                throw new UnityCliLoopSecurityException(toolName, "Tool is blocked by security settings");
            }

            Stopwatch mainThreadHopStopwatch = Stopwatch.StartNew();
            await MainThreadSwitcher.SwitchToMainThread();
            mainThreadHopStopwatch.Stop();

            Stopwatch toolBodyStopwatch = Stopwatch.StartNew();
            UnityCliLoopToolResponse response = await tool.ExecuteAsync(paramsToken);
            toolBodyStopwatch.Stop();
            if (response == null)
            {
                throw new InvalidOperationException($"Tool returned null response: {toolName}");
            }

            response.SetVersion(UnityCliLoopVersion.VERSION);

            return response;
        }

        /// <summary>
        /// Get detailed information of registered _tools
        /// </summary>
        /// <returns>Array of tool information</returns>
        public ToolInfo[] GetRegisteredTools()
        {
            return GetRegisteredToolsForProjectRoot(UnityCliLoopPathResolver.GetProjectRoot());
        }

        internal ToolInfo[] GetRegisteredToolsForProjectRoot(string projectRoot)
        {
            HashSet<string> internalToolNames = _internalToolNameProvider.GetInternalToolNames(projectRoot);
            return _tools.Values
                .Where(tool => ToolSettings.IsToolEnabled(tool.ToolName))
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

        /// <summary>
        /// Get all tools including disabled ones. Used by Tool Settings UI.
        /// </summary>
        public ToolInfo[] GetAllRegisteredToolInfos()
        {
            return GetAllRegisteredToolInfosForProjectRoot(UnityCliLoopPathResolver.GetProjectRoot());
        }

        internal ToolInfo[] GetAllRegisteredToolInfosForProjectRoot(string projectRoot)
        {
            HashSet<string> internalToolNames = _internalToolNameProvider.GetInternalToolNames(projectRoot);
            return _tools.Values
                .Where(tool => !internalToolNames.Contains(tool.ToolName))
                .Select(tool =>
            {
                UnityCliLoopToolAttribute attribute = tool.GetType().GetCustomAttribute<UnityCliLoopToolAttribute>();
                bool displayDevelopmentOnly = attribute?.DisplayDevelopmentOnly ?? false;
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
                .Where(tool => !internalToolNames.Contains(tool.ToolName))
                .Select(tool =>
            {
                Type toolType = tool.GetType();
                UnityCliLoopToolAttribute attribute = toolType.GetCustomAttribute<UnityCliLoopToolAttribute>();
                bool displayDevelopmentOnly = attribute?.DisplayDevelopmentOnly ?? false;
                bool isThirdParty = IsThirdPartyAssembly(toolType.Assembly.GetName().Name);

                return new ToolSettingsCatalogItem(
                    tool.ToolName,
                    displayDevelopmentOnly,
                    isThirdParty);
            }).ToArray();
        }

        /// <summary>
        /// Check if a tool belongs to a third-party assembly.
        /// </summary>
        public bool IsThirdPartyTool(string toolName)
        {
            if (!_tools.TryGetValue(toolName, out IUnityCliLoopTool tool))
            {
                return true;
            }
            string assemblyName = tool.GetType().Assembly.GetName().Name;
            return IsThirdPartyAssembly(assemblyName);
        }

        private static bool IsThirdPartyAssembly(string assemblyName)
        {
            return assemblyName != ApplicationAssemblyName &&
                   !assemblyName.StartsWith(FirstPartyToolsAssemblyNamePrefix, StringComparison.Ordinal);
        }

        /// <summary>
        /// Get tool type by name for security checking
        /// </summary>
        /// <param name="toolName">Tool name</param>
        /// <returns>Tool type or null if not found</returns>
        public Type GetToolType(string toolName)
        {
            if (_tools.TryGetValue(toolName, out IUnityCliLoopTool tool))
            {
                return tool.GetType();
            }
            return null;
        }

        /// <summary>
        /// Check if specified tool is registered
        /// </summary>
        /// <param name="toolName">Tool name</param>
        /// <returns>True if registered</returns>
        public bool IsToolRegistered(string toolName)
        {
            return _tools.ContainsKey(toolName);
        }

    }

    /// <summary>
    /// Class representing tool information
    /// </summary>
    public class ToolInfo
    {
        [JsonProperty("name")] public string Name { get; }

        [JsonProperty("parameterSchema")] public ToolParameterSchema ParameterSchema { get; }

        [JsonProperty("displayDevelopmentOnly")] public bool DisplayDevelopmentOnly { get; }

        public ToolInfo(string name, ToolParameterSchema parameterSchema, bool displayDevelopmentOnly = false)
        {
            Name = name;
            ParameterSchema = parameterSchema;
            DisplayDevelopmentOnly = displayDevelopmentOnly;
        }
    }

    /// <summary>
    /// Lightweight registry metadata used by Tool Settings UI without generating parameter schemas.
    /// </summary>
    public class ToolSettingsCatalogItem
    {
        public readonly string Name;
        public readonly bool DisplayDevelopmentOnly;
        public readonly bool IsThirdParty;

        public ToolSettingsCatalogItem(
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
