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
        /// the assembly <paramref name="home"/> names, whose parameters and generic arity match
        /// <paramref name="parameterTypeFullNames"/> and <paramref name="genericArity"/>
        /// exactly (Cecil FullName, no <c>this</c>).
        /// </summary>
        public static HotReloadMethodMatchResult Resolve(
            HotReloadTypeHome home,
            string typeMetadataName,
            string methodName,
            string[] parameterTypeFullNames,
            int genericArity)
        {
            Debug.Assert(home != null, "home must not be null.");
            Debug.Assert(!string.IsNullOrEmpty(typeMetadataName), "typeMetadataName must not be null or empty.");
            Debug.Assert(!string.IsNullOrEmpty(methodName), "methodName must not be null or empty.");
            Debug.Assert(parameterTypeFullNames != null, "parameterTypeFullNames must not be null.");
            Debug.Assert(genericArity >= 0, "genericArity must not be negative.");

            string dllPath = home.DllPath;

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
                    $"Type '{typeMetadataName}' was not found in assembly '{home.AssemblyName}'.");
            }

            MethodDefinition methodDefinition = FindMatchingMethod(
                typeDefinition,
                methodName,
                parameterTypeFullNames,
                genericArity);
            if (methodDefinition == null)
            {
                return HotReloadMethodMatchResult.Failure(
                    HotReloadMethodMatchFailureReason.MethodNotFound,
                    $"No method '{methodName}' with the given parameter types was found on '{typeMetadataName}'.");
            }

            int metadataToken = methodDefinition.MetadataToken.ToInt32();
            string compiledMvid = assemblyDefinition.MainModule.Mvid.ToString();
            return ResolveLoadedMethod(home, compiledMvid, metadataToken);
        }

        private static MethodDefinition FindMatchingMethod(
            TypeDefinition typeDefinition,
            string methodName,
            string[] parameterTypeFullNames,
            int genericArity)
        {
            foreach (MethodDefinition candidate in typeDefinition.Methods)
            {
                if (candidate.Name != methodName)
                {
                    continue;
                }

                if (candidate.GenericParameters.Count != genericArity)
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

        // internal so EditMode tests can exercise the Mvid guard without rewriting ScriptAssemblies.
        internal static HotReloadMethodMatchResult ResolveLoadedMethod(
            HotReloadTypeHome home,
            string compiledMvid,
            int metadataToken)
        {
            Debug.Assert(home != null, "home must not be null.");

            HotReloadLoadedAssemblyResolution resolution = home.ResolveLoadedAssembly(compiledMvid);
            if (resolution.State == HotReloadLoadedAssemblyState.Stale)
            {
                return HotReloadMethodMatchResult.Failure(
                    HotReloadMethodMatchFailureReason.StaleAssembly,
                    $"The loaded assembly '{home.AssemblyName}' no longer matches the compiled assembly on disk.",
                    HotReloadConstants.StaleAssemblyHint);
            }

            if (resolution.State == HotReloadLoadedAssemblyState.NotLoaded)
            {
                return HotReloadMethodMatchResult.Failure(
                    HotReloadMethodMatchFailureReason.AssemblyNotLoaded,
                    $"Assembly '{home.AssemblyName}' is not currently loaded in the AppDomain.",
                    HotReloadConstants.AssemblyNotLoadedHint);
            }

            MethodBase method = resolution.Assembly.ManifestModule.ResolveMethod(metadataToken);
            return HotReloadMethodMatchResult.SuccessResult(method);
        }
    }
}
