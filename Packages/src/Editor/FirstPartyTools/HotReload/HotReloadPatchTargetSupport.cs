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
        // Why a helper: ProcessFileAsync's pre-worker fail chain (assembly / DLL / MVID)
        // is one resolve stage and kept the method over CA1502. Unchanged-source
        // short-circuit is decided here but applied by the orchestrator so a changed
        // sibling in the same assembly can still pull the file into the group.
        internal static HotReloadPatchTargetResolution ResolvePatchTarget(
            HotReloadDomain domain,
            IHotReloadPackageRootCapture packageRootCapture,
            IHotReloadEditorStateSnapshotCapture editorStateSnapshotCapture,
            string assemblyResolvePath,
            string workerSourcePath,
            List<HotReloadMethodOutcome> outcomes,
            List<string> warnings,
            string correlationId,
            List<HotReloadMethodOutcome> alreadyActiveOutcomes)
        {
            Debug.Assert(domain != null, "domain must not be null.");
            Debug.Assert(packageRootCapture != null, "packageRootCapture must not be null.");
            Debug.Assert(
                editorStateSnapshotCapture != null,
                "editorStateSnapshotCapture must not be null.");
            Debug.Assert(alreadyActiveOutcomes != null, "alreadyActiveOutcomes must not be null.");

            // CompilationPipeline.GetAssemblyNameFromScriptPath expects a project-relative path
            // (Assets/... or Packages/...) and returns a file name that already includes ".dll".
            string projectRelativePath =
                ToProjectRelativeScriptPath(packageRootCapture, assemblyResolvePath);
            string rawAssemblyName = CompilationPipeline.GetAssemblyNameFromScriptPath(projectRelativePath);
            if (string.IsNullOrEmpty(rawAssemblyName))
            {
                outcomes.Add(
                    HotReloadMethodOutcome.Failed(
                        "(file)",
                        "Script path is not part of any compiled assembly (Assets/Packages paths only): "
                        + assemblyResolvePath,
                        assemblyResolvePath));
                return HotReloadPatchTargetResolution.EarlyExit(
                    new HotReloadFileProcessResult(outcomes, warnings, 0));
            }

            string assemblyName = Path.GetFileNameWithoutExtension(rawAssemblyName);
            UnityCompilationAssembly compilationAssembly = FindCompilationAssembly(assemblyName);
            // Why gate on compilationAssembly == null: the unimported-asmdef flag is only
            // consumed on that branch, and walking ancestor directories on every successful
            // resolve (including loose Assembly-CSharp scripts) is wasted disk I/O.
            bool hasUnimportedAsmdefOnDisk = compilationAssembly == null
                && string.IsNullOrEmpty(
                    CompilationPipeline.GetAssemblyDefinitionFilePathFromScriptPath(projectRelativePath))
                && HotReloadAssemblyResolutionDiagnostics.AncestorDirectoryContainsAsmdef(assemblyResolvePath);
            string resolutionFailureReason = HotReloadAssemblyResolutionDiagnostics.TryGetAssemblyResolutionFailureReason(
                assemblyName,
                compilationAssembly,
                projectRelativePath,
                hasUnimportedAsmdefOnDisk);
            if (resolutionFailureReason != null)
            {
                outcomes.Add(
                    HotReloadMethodOutcome.Failed(
                        "(file)",
                        resolutionFailureReason,
                        assemblyResolvePath));
                return HotReloadPatchTargetResolution.EarlyExit(
                    new HotReloadFileProcessResult(outcomes, warnings, 0));
            }

            bool isNewSource = !HotReloadAssemblyResolutionDiagnostics.ContainsProjectRelativeSourceFile(
                compilationAssembly.sourceFiles,
                projectRelativePath);
            if (isNewSource)
            {
                string notReadyReason =
                    editorStateSnapshotCapture.CaptureCurrent().GetNotReadyReason();
                if (notReadyReason != null)
                {
                    outcomes.Add(HotReloadMethodOutcome.Failed("(file)", notReadyReason, assemblyResolvePath));
                    return HotReloadPatchTargetResolution.EarlyExit(
                        new HotReloadFileProcessResult(outcomes, warnings, 0));
                }
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
                return HotReloadPatchTargetResolution.EarlyExit(
                    new HotReloadFileProcessResult(outcomes, warnings, 0));
            }

            string mvidGuardError = CheckMvidGuard(assemblyName, targetDllPath);
            if (mvidGuardError != null)
            {
                outcomes.Add(HotReloadMethodOutcome.Failed("(file)", mvidGuardError, assemblyResolvePath));
                return HotReloadPatchTargetResolution.EarlyExit(
                    new HotReloadFileProcessResult(outcomes, warnings, 0));
            }

            HotReloadNewSourceMembershipEvidence newSourceMembershipEvidence = null;
            if (isNewSource)
            {
                string membershipFailure = HotReloadNewSourceMembershipValidator.TryCapture(
                    editorStateSnapshotCapture,
                    projectRoot,
                    projectRelativePath,
                    assemblyName,
                    compilationAssembly,
                    targetDllPath,
                    out newSourceMembershipEvidence);
                if (membershipFailure != null)
                {
                    outcomes.Add(HotReloadMethodOutcome.Failed("(file)", membershipFailure, assemblyResolvePath));
                    return HotReloadPatchTargetResolution.EarlyExit(
                        new HotReloadFileProcessResult(outcomes, warnings, 0));
                }
            }

            // Why the call is skipped rather than its result discarded: the short-circuit clears
            // the applied-source ledger on its miss paths. A type this domain introduced lives in
            // an artifact assembly whose owner declaration still has to be verified, so an
            // unchanged source must reach that verification with its ledger entry intact.
            HotReloadUnchangedSourceDecision unchangedDecision = HotReloadUnchangedSourceDecision.NotUnchanged;
            if (!domain.IntroducedTypes.HasActiveTypesForOriginalAssembly(assemblyName))
            {
                unchangedDecision = HotReloadAppliedSourceLifecycle.TryShortCircuitUnchangedAppliedSource(
                    domain,
                    workerSourcePath,
                    projectRelativePath,
                    assemblyResolvePath,
                    alreadyActiveOutcomes);
            }
            HotReloadOrchestratorLog.LogHotReloadFileStart(projectRelativePath, unchangedDecision, correlationId);
            if (unchangedDecision == HotReloadUnchangedSourceDecision.ReapplyNonBaseline)
            {
                warnings.Add(
                    string.Format(
                        HotReloadConstants.UnchangedSourceNonBaselineWarningFormat,
                        projectRelativePath));
            }

            return HotReloadPatchTargetResolution.Resolved(
                projectRelativePath,
                assemblyName,
                compilationAssembly,
                targetDllPath,
                projectRoot,
                unchangedDecision,
                newSourceMembershipEvidence);
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

        internal static string ToProjectRelativeScriptPath(
            IHotReloadPackageRootCapture packageRootCapture,
            string path)
        {
            Debug.Assert(packageRootCapture != null, "packageRootCapture must not be null.");
            Debug.Assert(!string.IsNullOrEmpty(path), "path must not be empty.");
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            // Path.GetFullPath resolves a relative path against the current directory (the project
            // root in the Editor) and turns a virtual Packages/<pkg-id>/... path into the physical
            // folder behind it, which resolves to the wrong assembly. The captured package roots
            // map that physical folder back to the virtual path Unity's script APIs expect.
            string fullPath = Path.GetFullPath(path.Replace('\\', '/'));
            StringComparison comparison = Application.platform == RuntimePlatform.WindowsEditor
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return HotReloadScriptPathNormalizer.ToProjectRelative(
                fullPath,
                projectRoot,
                packageRootCapture.Current,
                comparison);
        }
    }
}
