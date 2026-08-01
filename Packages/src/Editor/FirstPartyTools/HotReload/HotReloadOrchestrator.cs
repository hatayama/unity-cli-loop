using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using HarmonyLib;

using Mono.Cecil;

using UnityEditor.Compilation;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

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

                // Why ConfigureAwait(false): UnityCliLoopTool forbids capturing Unity's
                // SynchronizationContext across awaits — while Play Mode is paused that context
                // does not run continuations, so a true resume would hang the tool forever.
                // ProcessFileAsync switches back via MainThreadSwitcher (EditorApplication.update
                // queue) before any main-thread-only editor API or Harmony patch.
                HotReloadFileProcessResult fileResult = await ProcessFileAsync(
                    filePath,
                    workerSourcePath,
                    ct).ConfigureAwait(false);

                outcomes.AddRange(fileResult.Outcomes);
                warnings.AddRange(fileResult.Warnings);
                patchedTotal += fileResult.PatchedCount;
            }

            await MainThreadSwitcher.SwitchToMainThread(ct);
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

            // CompilationPipeline / Application.dataPath require the Unity main thread.
            await MainThreadSwitcher.SwitchToMainThread(ct);

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
                await TransformWorkerClient.RunAsync(workerInput, ct).ConfigureAwait(false);
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

            if (workerOutput.declarationDriftWarnings != null)
            {
                // Surfaced before the empty-entries early return so const drift still reaches
                // the response when every method in the file is skipped or unchanged.
                foreach (string driftWarning in workerOutput.declarationDriftWarnings)
                {
                    warnings.Add(driftWarning);
                }
            }

            if (string.IsNullOrEmpty(workerOutput.shimSource)
                || workerOutput.entries == null
                || workerOutput.entries.Length == 0)
            {
                return new HotReloadFileProcessResult(outcomes, warnings, 0);
            }

            // BuildShimReferencePaths reads Application.dataPath / platform; stay on main thread.
            await MainThreadSwitcher.SwitchToMainThread(ct);
            bool includeHarmonyReference = HasDelegationEntry(workerOutput.entries);
            List<string> shimReferences = BuildShimReferencePaths(
                compilationAssembly,
                targetDllPath,
                includeHarmonyReference);
            HotReloadShimCompileResult compileResult = await HotReloadShimCompiler.CompileAndLoadAsync(
                workerOutput.shimSource,
                shimReferences,
                defines,
                ct).ConfigureAwait(false);

            if (!compileResult.Success)
            {
                outcomes.Add(
                    HotReloadMethodOutcome.Failed(
                        "(shim-compile)",
                        compileResult.ErrorMessage,
                        assemblyResolvePath));
                return new HotReloadFileProcessResult(outcomes, warnings, 0);
            }

            // Harmony Patch/Unpatch and method resolution against loaded modules require main thread.
            await MainThreadSwitcher.SwitchToMainThread(ct);
            Dictionary<string, string> bindFailureReasonByShimTypeName =
                BindShimAccessors(compileResult.Assembly);
            int patchedCount = 0;
            foreach (TransformWorkerEntryDto entry in workerOutput.entries)
            {
                HotReloadMethodOutcome outcome = ApplyEntry(
                    entry,
                    assemblyName,
                    compileResult.Assembly,
                    bindFailureReasonByShimTypeName,
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
            IReadOnlyDictionary<string, string> bindFailureReasonByShimTypeName,
            string filePath,
            List<string> warnings)
        {
            string methodLabel = entry.typeMetadataName + "." + entry.methodName;

            // Only "delegation" selects the forwarding patch; null/empty/anything else is transplant.
            HotReloadPatchShape patchShape = entry.patchKind == HotReloadConstants.PatchKindDelegation
                ? HotReloadPatchShape.Delegation
                : HotReloadPatchShape.Transplant;

            // Transplant entries never read accessor delegates, so a sibling bind failure in the
            // same shim type must not take them down; only delegation entries depend on the bind.
            if (patchShape == HotReloadPatchShape.Delegation
                && bindFailureReasonByShimTypeName.TryGetValue(entry.shimTypeName ?? string.Empty, out string bindFailureReason))
            {
                return HotReloadMethodOutcome.Failed(methodLabel, bindFailureReason, filePath);
            }

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

            Type shimType = FindShimType(shimAssembly, entry.shimTypeName);
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

            HotReloadPatchResult patchResult = HotReloadPatcher.Apply(matchResult.Method, shimMethod, patchShape);
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

        /// <summary>
        /// Invokes each shim type's binder (emitted when the type carries at least one accessor
        /// delegate) once, before any patch is applied, so no delegation shim can run with
        /// unbound accessor delegates. Returns bind failures keyed by shim type name; every
        /// delegation entry in a failed type becomes Failed instead of being patched.
        /// Internal so tests can pin the failure contract directly — an end-to-end bind failure
        /// cannot be fabricated once shim compilation has succeeded against the same assembly.
        /// </summary>
        internal static Dictionary<string, string> BindShimAccessors(Assembly shimAssembly)
        {
            Debug.Assert(shimAssembly != null, "shimAssembly must not be null.");

            Dictionary<string, string> failureReasonByShimTypeName = new Dictionary<string, string>();
            foreach (Type shimType in shimAssembly.GetTypes())
            {
                MethodInfo bindMethod = shimType.GetMethod(
                    HotReloadConstants.ShimBindAccessorsMethodName,
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly,
                    null,
                    Type.EmptyTypes,
                    null);
                if (bindMethod == null)
                {
                    continue;
                }

                try
                {
                    bindMethod.Invoke(null, null);
                }
                catch (TargetInvocationException invocationException)
                {
                    // Approved deviation from the no-try-catch rule: a bind failure (the source
                    // references a member the compiled assembly does not have yet) is an expected
                    // per-type outcome that must fail that type's methods with a remediation hint,
                    // not crash the whole hot-reload run. Nothing is swallowed — the cause becomes
                    // the Failed reason for every affected method.
                    Exception cause = invocationException.InnerException ?? invocationException;
                    failureReasonByShimTypeName[shimType.Name] =
                        "Accessor binding failed for shim type '" + shimType.Name + "': "
                        + cause.Message + " Run 'uloop compile' and retry.";
                }
            }

            return failureReasonByShimTypeName;
        }

        private static Type FindShimType(Assembly shimAssembly, string shimTypeName)
        {
            if (string.IsNullOrEmpty(shimTypeName))
            {
                return null;
            }

            // Prefer the short-name lookup used when shims are in the global namespace; fall back
            // to scanning because production emits shims into the original type's namespace.
            Type direct = shimAssembly.GetType(shimTypeName);
            if (direct != null)
            {
                return direct;
            }

            foreach (Type candidate in shimAssembly.GetTypes())
            {
                if (candidate.Name == shimTypeName)
                {
                    return candidate;
                }
            }

            return null;
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

        private static bool HasDelegationEntry(TransformWorkerEntryDto[] entries)
        {
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
        /// Publicize ScriptAssemblies references; leave engine/system DLLs untouched. Never include
        /// the original (non-publicized) target assembly. Harmony is added only when the worker
        /// emitted at least one delegation entry so transplant-only compiles stay byte-identical.
        /// </summary>
        private static List<string> BuildShimReferencePaths(
            UnityCompilationAssembly compilationAssembly,
            string targetDllPath,
            bool includeHarmonyReference)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string scriptAssembliesDirectory = Path.GetFullPath(
                Path.Combine(projectRoot, HotReloadConstants.ScriptAssembliesRelativeDirectory));

            List<string> references = new List<string>();
            string publicizedTarget = ReferencePublicizer.GetOrCreatePublicizedCopy(targetDllPath);
            references.Add(publicizedTarget);

            if (includeHarmonyReference)
            {
                references.Add(typeof(Harmony).Assembly.Location);
            }

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
