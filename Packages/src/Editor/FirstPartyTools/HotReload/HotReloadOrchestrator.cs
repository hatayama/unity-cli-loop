using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Mono.Cecil;

using UnityEditor.Compilation;

using UnityEngine;

using Assembly = System.Reflection.Assembly;
using UnityCompilationAssembly = UnityEditor.Compilation.Assembly;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// End-to-end hot-reload pipeline: resolve assembly → worker → shim compile → match → patch.
    /// </summary>
    internal static class HotReloadOrchestrator
    {
        /// <summary>
        /// Runs hot reload for each path in <paramref name="files"/>.
        /// <paramref name="contentPathOverride"/> is test-only: when set, the worker reads that
        /// path while assembly resolution still uses <paramref name="files"/> (so edited copies
        /// can live under <c>Library/UloopHotReload/TestSources/</c> without provoking AssetDatabase).
        /// </summary>
        public static async Task<HotReloadOrchestratorResult> RunAsync(
            IReadOnlyList<string> files,
            string contentPathOverride,
            CancellationToken ct)
        {
            Debug.Assert(files != null, "files must not be null.");
            Debug.Assert(files.Count > 0, "files must not be empty.");

            List<HotReloadMethodOutcome> outcomes = new List<HotReloadMethodOutcome>();
            List<string> warnings = new List<string>();
            int patchedTotal = 0;

            for (int index = 0; index < files.Count; index++)
            {
                ct.ThrowIfCancellationRequested();
                string filePath = files[index];
                string workerSourcePath = string.IsNullOrEmpty(contentPathOverride)
                    ? filePath
                    : contentPathOverride;

                HotReloadFileProcessResult fileResult = await ProcessFileAsync(
                    filePath,
                    workerSourcePath,
                    ct).ConfigureAwait(true);

                outcomes.AddRange(fileResult.Outcomes);
                warnings.AddRange(fileResult.Warnings);
                patchedTotal += fileResult.PatchedCount;
            }

            return new HotReloadOrchestratorResult(
                outcomes,
                warnings,
                patchedTotal,
                HotReloadPatcher.ActivePatchCount);
        }

        private static async Task<HotReloadFileProcessResult> ProcessFileAsync(
            string assemblyResolvePath,
            string workerSourcePath,
            CancellationToken ct)
        {
            List<HotReloadMethodOutcome> outcomes = new List<HotReloadMethodOutcome>();
            List<string> warnings = new List<string>();

            // CompilationPipeline.GetAssemblyNameFromScriptPath expects a project-relative path
            // (Assets/... or Packages/...) and returns a file name that already includes ".dll".
            string projectRelativePath = ToProjectRelativeScriptPath(assemblyResolvePath);
            string rawAssemblyName = CompilationPipeline.GetAssemblyNameFromScriptPath(projectRelativePath);
            if (string.IsNullOrEmpty(rawAssemblyName))
            {
                outcomes.Add(
                    HotReloadMethodOutcome.Failed(
                        "(file)",
                        "Script path is not part of any compiled assembly (Assets/Packages paths only): "
                        + assemblyResolvePath,
                        assemblyResolvePath));
                return new HotReloadFileProcessResult(outcomes, warnings, 0);
            }

            string assemblyName = Path.GetFileNameWithoutExtension(rawAssemblyName);
            UnityCompilationAssembly compilationAssembly = FindCompilationAssembly(assemblyName);
            if (compilationAssembly == null)
            {
                outcomes.Add(
                    HotReloadMethodOutcome.Failed(
                        "(file)",
                        "CompilationPipeline assembly not found: " + assemblyName,
                        assemblyResolvePath));
                return new HotReloadFileProcessResult(outcomes, warnings, 0);
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string targetDllPath = Path.Combine(
                projectRoot,
                HotReloadConstants.ScriptAssembliesRelativeDirectory,
                assemblyName + HotReloadConstants.CompiledAssemblyExtension);

            if (!File.Exists(targetDllPath))
            {
                outcomes.Add(
                    HotReloadMethodOutcome.Failed(
                        "(file)",
                        "Compiled assembly not found at '" + targetDllPath + "'. Compile the project first.",
                        assemblyResolvePath));
                return new HotReloadFileProcessResult(outcomes, warnings, 0);
            }

            string mvidGuardError = CheckMvidGuard(assemblyName, targetDllPath);
            if (mvidGuardError != null)
            {
                outcomes.Add(HotReloadMethodOutcome.Failed("(file)", mvidGuardError, assemblyResolvePath));
                return new HotReloadFileProcessResult(outcomes, warnings, 0);
            }

            string[] defines = compilationAssembly.defines ?? Array.Empty<string>();
            string[] referencePaths = BuildWorkerReferencePaths(compilationAssembly, targetDllPath);

            TransformWorkerInputDto workerInput = new TransformWorkerInputDto
            {
                sourcePath = Path.GetFullPath(workerSourcePath),
                defines = defines,
                referencePaths = referencePaths,
                targetTypesAssemblyPath = Path.GetFullPath(targetDllPath)
            };

            TransformWorkerClientResult workerResult =
                await TransformWorkerClient.RunAsync(workerInput).ConfigureAwait(true);
            if (!workerResult.Success)
            {
                outcomes.Add(
                    HotReloadMethodOutcome.Failed("(file)", workerResult.ErrorMessage, assemblyResolvePath));
                return new HotReloadFileProcessResult(outcomes, warnings, 0);
            }

            TransformWorkerOutputDto workerOutput = workerResult.Output;
            if (workerOutput.parseErrors != null)
            {
                foreach (string parseError in workerOutput.parseErrors)
                {
                    warnings.Add(parseError);
                }
            }

            if (workerOutput.skipped != null)
            {
                foreach (TransformWorkerSkippedDto skipped in workerOutput.skipped)
                {
                    outcomes.Add(
                        HotReloadMethodOutcome.Skipped(
                            skipped.method ?? "(unknown)",
                            skipped.reason ?? string.Empty,
                            assemblyResolvePath));
                }
            }

            if (string.IsNullOrEmpty(workerOutput.shimSource)
                || workerOutput.entries == null
                || workerOutput.entries.Length == 0)
            {
                return new HotReloadFileProcessResult(outcomes, warnings, 0);
            }

            List<string> shimReferences = BuildShimReferencePaths(compilationAssembly, targetDllPath);
            HotReloadShimCompileResult compileResult = await HotReloadShimCompiler.CompileAndLoadAsync(
                workerOutput.shimSource,
                shimReferences,
                defines,
                ct).ConfigureAwait(true);

            if (!compileResult.Success)
            {
                outcomes.Add(
                    HotReloadMethodOutcome.Failed(
                        "(shim-compile)",
                        compileResult.ErrorMessage,
                        assemblyResolvePath));
                return new HotReloadFileProcessResult(outcomes, warnings, 0);
            }

            int patchedCount = 0;
            foreach (TransformWorkerEntryDto entry in workerOutput.entries)
            {
                HotReloadMethodOutcome outcome = ApplyEntry(
                    entry,
                    assemblyName,
                    compileResult.Assembly,
                    assemblyResolvePath,
                    warnings);
                outcomes.Add(outcome);
                if (outcome.Kind == HotReloadMethodOutcomeKind.Patched)
                {
                    patchedCount++;
                }
            }

            return new HotReloadFileProcessResult(outcomes, warnings, patchedCount);
        }

        private static HotReloadMethodOutcome ApplyEntry(
            TransformWorkerEntryDto entry,
            string assemblyName,
            Assembly shimAssembly,
            string filePath,
            List<string> warnings)
        {
            string methodLabel = entry.typeMetadataName + "." + entry.methodName;
            string[] parameterTypeFullNames = entry.parameterTypeFullNames ?? Array.Empty<string>();

            HotReloadMethodMatchResult matchResult = HotReloadMethodMatcher.Resolve(
                assemblyName,
                entry.typeMetadataName,
                entry.methodName,
                parameterTypeFullNames);
            if (!matchResult.Success)
            {
                return HotReloadMethodOutcome.Failed(methodLabel, matchResult.ErrorMessage, filePath);
            }

            Type shimType = shimAssembly.GetType(entry.shimTypeName);
            if (shimType == null)
            {
                return HotReloadMethodOutcome.Failed(
                    methodLabel,
                    "Shim type not found in compiled shim assembly: " + entry.shimTypeName,
                    filePath);
            }

            MethodInfo shimMethod = shimType.GetMethod(
                entry.shimMethodName,
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
            if (shimMethod == null)
            {
                // Fall back to a broader lookup — DeclaredOnly can miss when the compiler emits
                // unexpected metadata flags, but still prefer public static.
                shimMethod = shimType.GetMethod(
                    entry.shimMethodName,
                    BindingFlags.Public | BindingFlags.Static);
            }

            if (shimMethod == null)
            {
                return HotReloadMethodOutcome.Failed(
                    methodLabel,
                    "Shim method not found: " + entry.shimTypeName + "." + entry.shimMethodName,
                    filePath);
            }

            HotReloadPatchResult patchResult = HotReloadPatcher.Apply(matchResult.Method, shimMethod);
            if (!patchResult.Success)
            {
                return HotReloadMethodOutcome.Failed(methodLabel, patchResult.ErrorMessage, filePath);
            }

            if (!string.IsNullOrEmpty(patchResult.Warning))
            {
                warnings.Add(methodLabel + ": " + patchResult.Warning);
            }

            return HotReloadMethodOutcome.Patched(methodLabel, filePath, patchResult.Warning);
        }

        private static UnityCompilationAssembly FindCompilationAssembly(string assemblyName)
        {
            foreach (UnityCompilationAssembly assembly in CompilationPipeline.GetAssemblies())
            {
                if (assembly.name == assemblyName)
                {
                    return assembly;
                }
            }

            return null;
        }

        private static string CheckMvidGuard(string assemblyName, string targetDllPath)
        {
            ReaderParameters readerParameters = new ReaderParameters { InMemory = true };
            using AssemblyDefinition assemblyDefinition =
                AssemblyDefinition.ReadAssembly(targetDllPath, readerParameters);
            string compiledMvid = assemblyDefinition.MainModule.Mvid.ToString();

            foreach (Assembly loaded in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (loaded.GetName().Name != assemblyName)
                {
                    continue;
                }

                if (loaded.ManifestModule.ModuleVersionId.ToString() != compiledMvid)
                {
                    return HotReloadConstants.StaleAssemblyHint;
                }

                return null;
            }

            return HotReloadConstants.AssemblyNotLoadedHint;
        }

        private static string[] BuildWorkerReferencePaths(
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

        /// <summary>
        /// Publicize ScriptAssemblies references; leave engine/system DLLs untouched. Never include
        /// the original (non-publicized) target assembly.
        /// </summary>
        private static List<string> BuildShimReferencePaths(
            UnityCompilationAssembly compilationAssembly,
            string targetDllPath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string scriptAssembliesDirectory = Path.GetFullPath(
                Path.Combine(projectRoot, HotReloadConstants.ScriptAssembliesRelativeDirectory));

            List<string> references = new List<string>();
            string publicizedTarget = ReferencePublicizer.GetOrCreatePublicizedCopy(targetDllPath);
            references.Add(publicizedTarget);

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
                    references.Add(ReferencePublicizer.GetOrCreatePublicizedCopy(fullReference));
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

        private static string ToProjectRelativeScriptPath(string path)
        {
            Debug.Assert(!string.IsNullOrEmpty(path), "path must not be empty.");
            string normalized = path.Replace('\\', '/');
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/');
            if (!projectRoot.EndsWith("/", StringComparison.Ordinal))
            {
                projectRoot += "/";
            }

            string fullPath = Path.GetFullPath(path).Replace('\\', '/');
            StringComparison comparison = Application.platform == RuntimePlatform.WindowsEditor
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (fullPath.StartsWith(projectRoot, comparison))
            {
                return fullPath.Substring(projectRoot.Length);
            }

            // Already project-relative (Assets/... or Packages/...).
            return normalized;
        }

        private sealed class HotReloadFileProcessResult
        {
            public List<HotReloadMethodOutcome> Outcomes { get; }
            public List<string> Warnings { get; }
            public int PatchedCount { get; }

            public HotReloadFileProcessResult(
                List<HotReloadMethodOutcome> outcomes,
                List<string> warnings,
                int patchedCount)
            {
                Outcomes = outcomes;
                Warnings = warnings;
                PatchedCount = patchedCount;
            }
        }
    }
}
