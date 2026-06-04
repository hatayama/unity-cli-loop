using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace io.github.hatayama.uLoopMCP
{
    // Removes uLoop's prefab-only runtime assembly from Player builds after Editor compilation keeps prefab scripts resolvable.
    internal sealed class RuntimeAssemblyBuildFilter : IFilterBuildAssemblies
    {
        public int callbackOrder => 0;

        public string[] OnFilterAssemblies(BuildOptions buildOptions, string[] assemblies)
        {
            Debug.Assert(assemblies != null, "assemblies must be provided by Unity build pipeline");

            if ((buildOptions & BuildOptions.IncludeTestAssemblies) == BuildOptions.IncludeTestAssemblies)
            {
                // Player test builds need runtime references from test assemblies, so filtering here breaks PlayMode test players.
                return assemblies;
            }

            List<string> filteredAssemblies = new List<string>(assemblies.Length);
            for (int i = 0; i < assemblies.Length; i++)
            {
                string assemblyPath = assemblies[i];
                if (IsRuntimeAssembly(assemblyPath))
                {
                    continue;
                }

                filteredAssemblies.Add(assemblyPath);
            }

            return filteredAssemblies.ToArray();
        }

        private static bool IsRuntimeAssembly(string assemblyPath)
        {
            Debug.Assert(!string.IsNullOrEmpty(assemblyPath), "assemblyPath must be provided by Unity build pipeline");

            string fileName = Path.GetFileName(assemblyPath);
            return string.Equals(fileName, McpConstants.RUNTIME_ASSEMBLY_FILE_NAME, StringComparison.Ordinal);
        }
    }
}
