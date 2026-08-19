using System;
using System.Collections.Generic;
using System.IO;

using HarmonyLib;

using Mono.Cecil;

using UnityEditor.Compilation;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

using UnityCompilationAssembly = UnityEditor.Compilation.Assembly;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds worker and shim-compile assembly reference lists, including publicized copies.
    /// </summary>
    internal static class HotReloadShimReferenceBuilder
    {
        internal static string[] BuildWorkerReferencePaths(
            UnityCompilationAssembly compilationAssembly,
            string targetDllPath)
        {
            List<string> paths = new List<string>();
            if (compilationAssembly.allReferences != null)
            {
                foreach (string reference in compilationAssembly.allReferences)
                {
                    if (!string.IsNullOrEmpty(reference) && File.Exists(reference))
                    {
                        paths.Add(Path.GetFullPath(reference));
                    }
                }
            }

            string fullTarget = Path.GetFullPath(targetDllPath);
            bool hasTarget = false;
            foreach (string path in paths)
            {
                if (string.Equals(path, fullTarget, StringComparison.OrdinalIgnoreCase))
                {
                    hasTarget = true;
                    break;
                }
            }

            if (!hasTarget)
            {
                paths.Add(fullTarget);
            }

            return paths.ToArray();
        }

        internal static bool NeedsHarmonyReference(TransformWorkerOutputDto output)
        {
            return HasDelegationEntry(output.entries) || output.hasAccessorDelegates;
        }

        internal static bool NeedsAddedFieldStoreReference(TransformWorkerOutputDto output)
        {
            Debug.Assert(output != null, "output must not be null.");
            return output.hasAddedFieldRewrites;
        }

        /// <summary>
        /// Appends Harmony and/or the added-field store assembly when the worker output needs them.
        /// Visible to tests so injection can be asserted without running CompilationPipeline.
        /// </summary>
        internal static void AppendOptionalShimAssemblyReferences(
            List<string> references,
            bool includeHarmonyReference,
            bool includeAddedFieldStoreReference)
        {
            Debug.Assert(references != null, "references must not be null.");

            if (includeHarmonyReference)
            {
                AppendIfMissingByFileName(references, typeof(Harmony).Assembly.Location);
            }

            if (includeAddedFieldStoreReference)
            {
                AppendIfMissingByFileName(
                    references,
                    typeof(HotReloadAddedFieldStore).Assembly.Location);
            }
        }

        // Why filename (not full path): ToolContracts lives under ScriptAssemblies and is
        // publicized, so the list may already hold a publicized copy while Location is the raw
        // DLL. Adding both is CS1703. Harmony is a plugin outside ScriptAssemblies, so this
        // collision does not arise there, but the same skip is still correct.
        private static void AppendIfMissingByFileName(List<string> references, string assemblyPath)
        {
            Debug.Assert(!string.IsNullOrEmpty(assemblyPath), "assemblyPath must not be empty.");
            string fileName = Path.GetFileName(assemblyPath);
            for (int index = 0; index < references.Count; index++)
            {
                if (string.Equals(
                    Path.GetFileName(references[index]),
                    fileName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            references.Add(assemblyPath);
        }

        private static bool HasDelegationEntry(TransformWorkerEntryDto[] entries)
        {
            if (entries == null)
            {
                return false;
            }

            foreach (TransformWorkerEntryDto entry in entries)
            {
                if (entry.patchKind == HotReloadConstants.PatchKindDelegation)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Result of <see cref="TryBuildShimReferencePaths"/>. Exactly one of
        /// <see cref="References"/> or <see cref="ErrorMessage"/> is set.
        /// </summary>
        internal sealed class ShimReferencePathsResult
        {
            public List<string> References { get; }
            public string ErrorMessage { get; }

            public ShimReferencePathsResult(List<string> references, string errorMessage)
            {
                References = references;
                ErrorMessage = errorMessage;
            }
        }

        /// <summary>
        /// Builds shim compile references, converting Cecil assembly-resolution failures into an
        /// error message instead of letting them escape as UNITY_RPC_ERROR.
        /// </summary>
        internal static ShimReferencePathsResult TryBuildShimReferencePaths(
            UnityCompilationAssembly compilationAssembly,
            string targetDllPath,
            bool includeHarmonyReference,
            bool includeAddedFieldStoreReference)
        {
            // Why catch only AssemblyResolutionException: publicize fails when Cecil cannot
            // resolve engine/netstandard types during Write; that is a per-file hot-reload
            // outcome, not an internal tool crash. Other exceptions must still Fail Fast.
            try
            {
                return new ShimReferencePathsResult(
                    BuildShimReferencePaths(
                        compilationAssembly,
                        targetDllPath,
                        includeHarmonyReference,
                        includeAddedFieldStoreReference),
                    null);
            }
            catch (AssemblyResolutionException resolutionException)
            {
                return new ShimReferencePathsResult(
                    null,
                    "Publicizing referenced assemblies failed: " + resolutionException.Message
                    + " Hot reload could not build shim references for this file.");
            }
        }

        /// <summary>
        /// Publicize ScriptAssemblies references; leave engine/system DLLs untouched. Never include
        /// the original (non-publicized) target assembly. Harmony is added when the worker
        /// emitted a delegation entry or accessor delegates (addedMethod entries can need them).
        /// The added-field store assembly is added when the worker rewrote added-field accesses.
        /// </summary>
        private static List<string> BuildShimReferencePaths(
            UnityCompilationAssembly compilationAssembly,
            string targetDllPath,
            bool includeHarmonyReference,
            bool includeAddedFieldStoreReference)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string scriptAssembliesDirectory = Path.GetFullPath(
                Path.Combine(projectRoot, HotReloadConstants.ScriptAssembliesRelativeDirectory));

            // Derive Cecil search dirs from Unity's actual compile references so publicize
            // resolves netstandard/engine modules without hardcoding Editor Contents layouts.
            IReadOnlyCollection<string> resolverSearchDirectories =
                ReferencePublicizer.CollectResolverSearchDirectories(compilationAssembly.allReferences);

            List<string> references = new List<string>();
            string publicizedTarget = ReferencePublicizer.GetOrCreatePublicizedCopy(
                targetDllPath,
                resolverSearchDirectories);
            references.Add(publicizedTarget);

            AppendOptionalShimAssemblyReferences(
                references,
                includeHarmonyReference,
                includeAddedFieldStoreReference);

            if (compilationAssembly.allReferences == null)
            {
                return references;
            }

            string fullTarget = Path.GetFullPath(targetDllPath);
            foreach (string reference in compilationAssembly.allReferences)
            {
                if (string.IsNullOrEmpty(reference) || !File.Exists(reference))
                {
                    continue;
                }

                string fullReference = Path.GetFullPath(reference);
                if (string.Equals(fullReference, fullTarget, StringComparison.OrdinalIgnoreCase))
                {
                    // Replaced by the publicized copy above.
                    continue;
                }

                string referenceFileName = Path.GetFileNameWithoutExtension(fullReference);
                if (IsUnderDirectory(fullReference, scriptAssembliesDirectory)
                    && HotReloadConstants.IsPublicizableProjectAssemblyFileName(referenceFileName))
                {
                    references.Add(
                        ReferencePublicizer.GetOrCreatePublicizedCopy(
                            fullReference,
                            resolverSearchDirectories));
                }
                else
                {
                    references.Add(fullReference);
                }
            }

            return references;
        }

        private static bool IsUnderDirectory(string fullPath, string directoryPath)
        {
            string normalizedPath = fullPath.Replace('\\', '/');
            string normalizedDirectory = directoryPath.Replace('\\', '/');
            StringComparison comparison = Application.platform == RuntimePlatform.WindowsEditor
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return normalizedPath.StartsWith(normalizedDirectory + "/", comparison);
        }
    }
}
