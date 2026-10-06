using System;
using System.IO;
using System.Reflection;

using Mono.Cecil;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>Resolves a compiled method to the live MethodBase; the shape of HotReloadMethodMatcher.Resolve.</summary>
    internal delegate HotReloadMethodMatchResult HotReloadMethodResolver(
        HotReloadTypeHome home,
        string typeMetadataName,
        string methodName,
        string[] parameterTypeFullNames,
        int genericArity);

    /// <summary>Reads the compiled image at a path; the shape of the matcher's assembly loader.</summary>
    internal delegate AssemblyDefinition HotReloadCompiledAssemblyLoader(string dllPath);

    /// <summary>
    /// Resolves a hot-reload manifest entry (type metadata name + method name + parameter type
    /// full names) to the matching MethodBase in the running AppDomain, using Cecil metadata
    /// tokens and an Mvid guard against stale script assemblies.
    /// </summary>
    internal sealed class HotReloadMethodMatcher : IDisposable
    {
        private readonly HotReloadCompiledAssemblyLoader _loadAssembly;

        /// <param name="loadAssembly">
        /// How a compiled image is read; production passes ReadCompiledAssembly, and tests count
        /// the reads.
        /// </param>
        internal HotReloadMethodMatcher(HotReloadCompiledAssemblyLoader loadAssembly)
        {
            if (loadAssembly == null)
            {
                throw new ArgumentNullException(nameof(loadAssembly));
            }

            _loadAssembly = loadAssembly;
        }

        /// <summary>A matcher for one run that reads the compiled images from disk.</summary>
        internal static HotReloadMethodMatcher CreateReadingFromDisk()
        {
            return new HotReloadMethodMatcher(ReadCompiledAssembly);
        }

        /// <summary>The loader production uses: reads the whole compiled image at the path.</summary>
        internal static AssemblyDefinition ReadCompiledAssembly(string dllPath)
        {
            // InMemory: the DLL is the currently loaded script assembly; keep no file handle on it.
            ReaderParameters readerParameters = new ReaderParameters { InMemory = true };
            return AssemblyDefinition.ReadAssembly(dllPath, readerParameters);
        }

        /// <summary>
        /// Resolves <paramref name="methodName"/> on <paramref name="typeMetadataName"/> inside
        /// the assembly <paramref name="home"/> names, whose parameters and generic arity match
        /// <paramref name="parameterTypeFullNames"/> and <paramref name="genericArity"/>
        /// exactly (Cecil FullName, no <c>this</c>).
        /// </summary>
        public HotReloadMethodMatchResult Resolve(
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

            using AssemblyDefinition assemblyDefinition = _loadAssembly(dllPath);

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

        public void Dispose()
        {
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
