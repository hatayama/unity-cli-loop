using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// Defines public custom tool registration operations for extension code.
    /// </summary>
    public interface IUnityCliLoopToolRegistrar
    {
        void RegisterCustomTool(IUnityCliLoopTool tool);
        void UnregisterCustomTool(string toolName);
        ToolInfo[] GetRegisteredCustomTools();
        bool IsCustomToolRegistered(string toolName);
        Task<UnityCliLoopToolResponse> ExecuteToolAsync(string toolName, JToken paramsToken, CancellationToken ct);
        string GetDebugInfo();
        void NotifyToolChanges();
    }

    /// <summary>
    /// Public facade used by custom tools to register with Unity CLI Loop.
    /// </summary>
    public static class UnityCliLoopToolRegistrar
    {
        private static IUnityCliLoopToolRegistrar ServiceValue;

        internal static void RegisterService(IUnityCliLoopToolRegistrar service)
        {
            Debug.Assert(service != null, "service must not be null");

            ServiceValue = service ?? throw new ArgumentNullException(nameof(service));
        }

        private static IUnityCliLoopToolRegistrar Service
        {
            get
            {
                if (ServiceValue == null)
                {
                    throw new InvalidOperationException("Unity CLI Loop tool registrar service is not registered.");
                }

                return ServiceValue;
            }
        }

        public static void RegisterCustomTool(IUnityCliLoopTool tool)
        {
            Service.RegisterCustomTool(tool);
        }

        public static void UnregisterCustomTool(string toolName)
        {
            Service.UnregisterCustomTool(toolName);
        }

        public static ToolInfo[] GetRegisteredCustomTools()
        {
            return Service.GetRegisteredCustomTools();
        }

        public static bool IsCustomToolRegistered(string toolName)
        {
            return Service.IsCustomToolRegistered(toolName);
        }

        public static Task<UnityCliLoopToolResponse> ExecuteToolAsync(
            string toolName,
            JToken paramsToken,
            CancellationToken ct)
        {
            return Service.ExecuteToolAsync(toolName, paramsToken, ct);
        }

        public static string GetDebugInfo()
        {
            return Service.GetDebugInfo();
        }

        public static void NotifyToolChanges()
        {
            Service.NotifyToolChanges();
        }
    }
}
