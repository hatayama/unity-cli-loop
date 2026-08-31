using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using UnityEditor;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.CompositionRoot
{
    /// <summary>
    /// Discovers Unity CLI tool implementations indexed by Unity's editor type cache and
    /// instantiates them for registration.
    /// </summary>
    internal static class UnityCliLoopToolDiscovery
    {
        internal static IReadOnlyList<IUnityCliLoopTool> DiscoverTools()
        {
            Type[] toolTypes = TypeCache.GetTypesWithAttribute<UnityCliLoopToolAttribute>()
                .Where(type => typeof(IUnityCliLoopTool).IsAssignableFrom(type))
                .Where(type => !type.IsAbstract && !type.IsInterface)
                .ToArray();

            List<IUnityCliLoopTool> tools = new();
            foreach (Type type in toolTypes)
            {
                if (!IsValidToolType(type))
                {
                    UnityEngine.Debug.LogWarning($"{UnityCliLoopConstants.SECURITY_LOG_PREFIX} Skipping invalid tool type: {type.FullName}");
                    continue;
                }

                tools.Add(CreateTool(type));
            }

            return tools;
        }

        private static IUnityCliLoopTool CreateTool(Type type)
        {
            IUnityCliLoopTool tool = (IUnityCliLoopTool)Activator.CreateInstance(type);
            return tool;
        }

        internal static bool IsValidToolType(Type type)
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
    }
}
