using System;
using System.IO;
using System.Reflection;

using Mono.Cecil;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Resolves a hot-reload manifest entry (type metadata name + method name + parameter type
    /// full names) to the matching MethodBase in the running AppDomain, using Cecil metadata
    /// tokens and an Mvid guard against stale script assemblies.
    /// </summary>
    internal static class HotReloadMethodMatcher
    {
        /// <summary>
        /// Resolves <paramref name="methodName"/> on <paramref name="typeMetadataName"/> inside
        /// <paramref name="assemblyName"/> whose parameters match
        /// <paramref name="parameterTypeFullNames"/> exactly (Cecil FullName, no <c>this</c>).
        /// </summary>
        public static HotReloadMethodMatchResult Resolve(
            string assemblyName,
            string typeMetadataName,
            string methodName,
            string[] parameterTypeFullNames)
        {
            Debug.Assert(!string.IsNullOrEmpty(assemblyName), "assemblyName must not be null or empty.");
            Debug.Assert(!string.IsNullOrEmpty(typeMetadataName), "typeMetadataName must not be null or empty.");
            Debug.Assert(!string.IsNullOrEmpty(methodName), "methodName must not be null or empty.");
            Debug.Assert(parameterTypeFullNames != null, "parameterTypeFullNames must not be null.");

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string dllPath = Path.Combine(
                projectRoot,
                HotReloadConstants.ScriptAssembliesRelativeDirectory,
                assemblyName + HotReloadConstants.CompiledAssemblyExtension);

            if (!File.Exists(dllPath))
            {
                return HotReloadMethodMatchResult.Failure(
                    HotReloadMethodMatchFailureReason.CompiledAssemblyNotFound,
                    $"Compiled assembly not found at '{dllPath}'. Compile the project first.");
            }

            // InMemory: the DLL is the currently loaded script assembly; keep no file handle on it.
            ReaderParameters readerParameters = new ReaderParameters { InMemory = true };
            using AssemblyDefinition assemblyDefinition = AssemblyDefinition.ReadAssembly(dllPath, readerParameters);

            TypeDefinition typeDefinition = assemblyDefinition.MainModule.GetType(typeMetadataName);
            if (typeDefinition == null)
            {
                return HotReloadMethodMatchResult.Failure(
                    HotReloadMethodMatchFailureReason.TypeNotFound,
                    $"Type '{typeMetadataName}' was not found in assembly '{assemblyName}'.");
            }

            MethodDefinition methodDefinition = FindMatchingMethod(typeDefinition, methodName, parameterTypeFullNames);
            if (methodDefinition == null)
            {
                return HotReloadMethodMatchResult.Failure(
                    HotReloadMethodMatchFailureReason.MethodNotFound,
                    $"No method '{methodName}' with the given parameter types was found on '{typeMetadataName}'.");
            }

            int metadataToken = methodDefinition.MetadataToken.ToInt32();
            string compiledMvid = assemblyDefinition.MainModule.Mvid.ToString();
            return ResolveLoadedMethod(assemblyName, compiledMvid, metadataToken);
        }

        private static MethodDefinition FindMatchingMethod(
            TypeDefinition typeDefinition,
            string methodName,
            string[] parameterTypeFullNames)
        {
            foreach (MethodDefinition candidate in typeDefinition.Methods)
            {
                if (candidate.Name != methodName)
                {
                    continue;
                }

                if (candidate.Parameters.Count != parameterTypeFullNames.Length)
                {
                    continue;
                }

                bool parametersMatch = true;
                for (int index = 0; index < parameterTypeFullNames.Length; index++)
                {
                    if (candidate.Parameters[index].ParameterType.FullName != parameterTypeFullNames[index])
                    {
                        parametersMatch = false;
                        break;
                    }
                }

                if (parametersMatch)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static HotReloadMethodMatchResult ResolveLoadedMethod(
            string assemblyName,
            string compiledMvid,
            int metadataToken)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name != assemblyName)
                {
                    continue;
                }

                if (assembly.ManifestModule.ModuleVersionId.ToString() != compiledMvid)
                {
                    return HotReloadMethodMatchResult.Failure(
                        HotReloadMethodMatchFailureReason.StaleAssembly,
                        $"The loaded assembly '{assemblyName}' no longer matches the compiled assembly on disk.",
                        HotReloadConstants.StaleAssemblyHint);
                }

                MethodBase method = assembly.ManifestModule.ResolveMethod(metadataToken);
                return HotReloadMethodMatchResult.SuccessResult(method);
            }

            return HotReloadMethodMatchResult.Failure(
                HotReloadMethodMatchFailureReason.AssemblyNotLoaded,
                $"Assembly '{assemblyName}' is not currently loaded in the AppDomain.",
                HotReloadConstants.AssemblyNotLoadedHint);
        }
    }
}
