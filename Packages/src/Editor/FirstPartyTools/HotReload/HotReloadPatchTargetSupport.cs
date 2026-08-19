using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

using Mono.Cecil;

using UnityEditor.Compilation;

using UnityEngine;

using Assembly = System.Reflection.Assembly;
using UnityCompilationAssembly = UnityEditor.Compilation.Assembly;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Resolves the compiled assembly target for one hot-reload file and related path helpers.
    /// </summary>
    internal static class HotReloadPatchTargetSupport
    {
        // Why a helper: ProcessFileAsync's pre-worker fail chain (assembly / DLL / MVID /
        // unchanged short-circuit) is one resolve stage and kept the method over CA1502.
        internal static (
            HotReloadOrchestrator.HotReloadFileProcessResult EarlyResult,
            string ProjectRelativePath,
            string AssemblyName,
            UnityCompilationAssembly CompilationAssembly,
            string TargetDllPath,
            string ProjectRoot) ResolvePatchTarget(
            string assemblyResolvePath,
            string workerSourcePath,
            List<HotReloadMethodOutcome> outcomes,
            List<string> warnings,
            string correlationId)
        {
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
                return (new HotReloadOrchestrator.HotReloadFileProcessResult(outcomes, warnings, 0), null, null, null, null, null);
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
                return (new HotReloadOrchestrator.HotReloadFileProcessResult(outcomes, warnings, 0), null, null, null, null, null);
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
                return (new HotReloadOrchestrator.HotReloadFileProcessResult(outcomes, warnings, 0), null, null, null, null, null);
            }

            string mvidGuardError = CheckMvidGuard(assemblyName, targetDllPath);
            if (mvidGuardError != null)
            {
                outcomes.Add(HotReloadMethodOutcome.Failed("(file)", mvidGuardError, assemblyResolvePath));
                return (new HotReloadOrchestrator.HotReloadFileProcessResult(outcomes, warnings, 0), null, null, null, null, null);
            }

            HotReloadUnchangedSourceDecision unchangedDecision = HotReloadAppliedSourceLifecycle.TryShortCircuitUnchangedAppliedSource(
                workerSourcePath,
                projectRelativePath,
                assemblyResolvePath,
                outcomes);
            HotReloadOrchestratorLog.LogHotReloadFileStart(projectRelativePath, unchangedDecision, correlationId);
            if (unchangedDecision == HotReloadUnchangedSourceDecision.ShortCircuited)
            {
                return (new HotReloadOrchestrator.HotReloadFileProcessResult(outcomes, warnings, 0), null, null, null, null, null);
            }

            if (unchangedDecision == HotReloadUnchangedSourceDecision.ReapplyNonBaseline)
            {
                warnings.Add(
                    string.Format(
                        HotReloadConstants.UnchangedSourceNonBaselineWarningFormat,
                        projectRelativePath));
            }

            return (null, projectRelativePath, assemblyName, compilationAssembly, targetDllPath, projectRoot);
        }

        internal static UnityCompilationAssembly FindCompilationAssembly(string assemblyName)
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

        // Why Path.Combine then GetFullPath: Unity Assembly.sourceFiles are project-relative
        // (slash-separated). The worker cwd is Library/UloopHotReload/Worker/<hash>/, so it
        // can only open absolute paths. Normalization matches HotReloadSourceSnapshotter.
        internal static string[] BuildAssemblySourcePaths(string projectRoot, string[] sourceFiles)
        {
            if (sourceFiles == null || sourceFiles.Length == 0)
            {
                return Array.Empty<string>();
            }

            string[] paths = new string[sourceFiles.Length];
            for (int index = 0; index < sourceFiles.Length; index++)
            {
                string normalizedRelativePath = sourceFiles[index].Replace('\\', '/');
                string absoluteSourcePath = Path.Combine(
                    projectRoot,
                    normalizedRelativePath.Replace('/', Path.DirectorySeparatorChar));
                paths[index] = Path.GetFullPath(absoluteSourcePath);
            }

            return paths;
        }

        internal static string CheckMvidGuard(string assemblyName, string targetDllPath)
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

        internal static string ToProjectRelativeScriptPath(string path)
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
    }
}
