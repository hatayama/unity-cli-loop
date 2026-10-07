using System;
using System.Collections.Generic;
using System.IO;

using Mono.Cecil;

using UnityEditor.Compilation;

using UnityEngine;

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
            string unimportedAsmdefPath = compilationAssembly == null
                && string.IsNullOrEmpty(
                    CompilationPipeline.GetAssemblyDefinitionFilePathFromScriptPath(projectRelativePath))
                ? HotReloadAssemblyResolutionDiagnostics.FindAncestorAsmdefProjectRelativePath(assemblyResolvePath)
                : null;
            string resolutionFailureReason = HotReloadAssemblyResolutionDiagnostics.TryGetAssemblyResolutionFailureReason(
                assemblyName,
                compilationAssembly,
                projectRelativePath,
                unimportedAsmdefPath);
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
                HotReloadFailureDescription notReadyFailure =
                    editorStateSnapshotCapture.CaptureCurrent().GetNotReadyFailure();
                if (notReadyFailure != null)
                {
                    outcomes.Add(HotReloadMethodOutcome.FailedBecause("(file)", notReadyFailure, assemblyResolvePath));
                    return HotReloadPatchTargetResolution.EarlyExit(
                        new HotReloadFileProcessResult(outcomes, warnings, 0));
                }
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            HotReloadTypeHome home = domain.ResolveTypeHome(projectRoot, assemblyName);

            if (!File.Exists(home.DllPath))
            {
                outcomes.Add(
                    HotReloadMethodOutcome.FailedBecause(
                        "(file)",
                        HotReloadVirtualPlayerProject.DescribeMissingCompiledAssembly(projectRoot, home.DllPath),
                        assemblyResolvePath));
                return HotReloadPatchTargetResolution.EarlyExit(
                    new HotReloadFileProcessResult(outcomes, warnings, 0));
            }

            HotReloadFailureDescription mvidGuardFailure = CheckMvidGuard(home);
            if (mvidGuardFailure != null)
            {
                outcomes.Add(HotReloadMethodOutcome.FailedBecause("(file)", mvidGuardFailure, assemblyResolvePath));
                return HotReloadPatchTargetResolution.EarlyExit(
                    new HotReloadFileProcessResult(outcomes, warnings, 0));
            }

            HotReloadNewSourceMembershipEvidence newSourceMembershipEvidence = null;
            if (isNewSource)
            {
                HotReloadFailureDescription membershipFailure = HotReloadNewSourceMembershipValidator.TryCapture(
                    editorStateSnapshotCapture,
                    projectRoot,
                    projectRelativePath,
                    assemblyName,
                    compilationAssembly,
                    home.DllPath,
                    out newSourceMembershipEvidence);
                if (membershipFailure != null)
                {
                    outcomes.Add(HotReloadMethodOutcome.FailedBecause("(file)", membershipFailure, assemblyResolvePath));
                    return HotReloadPatchTargetResolution.EarlyExit(
                        new HotReloadFileProcessResult(outcomes, warnings, 0));
                }
            }

            // Why the call is skipped for an assembly that owns an introduced type: an unchanged
            // source there still has to reach group processing, which verifies the introduced
            // types' declarations and reports their IntroducedTypes rows. Protecting the
            // applied-source record is not the reason; the short-circuit only reads it.
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

            HotReloadFailureDescription busyFailure = DescribeBusyRefusal(editorStateSnapshotCapture, unchangedDecision);
            if (busyFailure != null)
            {
                outcomes.Add(HotReloadMethodOutcome.FailedBecause("(file)", busyFailure, assemblyResolvePath));
                return HotReloadPatchTargetResolution.EarlyExit(
                    new HotReloadFileProcessResult(outcomes, warnings, 0));
            }

            return HotReloadPatchTargetResolution.Resolved(
                projectRelativePath,
                assemblyName,
                compilationAssembly,
                home,
                projectRoot,
                unchangedDecision,
                newSourceMembershipEvidence);
        }

        // Why last and not for a short-circuited file: every earlier exit (assembly resolution,
        // missing DLL, MVID guard, new-source membership) keeps its own, more specific
        // classification, and a file whose patches are already active and unchanged has nothing
        // to apply, so a compile in flight does not stop it. Why before the transform: a compile
        // that is already running ends in a domain reload that would discard this reload anyway.
        private static HotReloadFailureDescription DescribeBusyRefusal(
            IHotReloadEditorStateSnapshotCapture editorStateSnapshotCapture,
            HotReloadUnchangedSourceDecision unchangedDecision)
        {
            if (unchangedDecision == HotReloadUnchangedSourceDecision.ShortCircuited)
            {
                return null;
            }

            return editorStateSnapshotCapture.CaptureCurrent().GetBusyFailure();
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

        // Why a stale assembly is EditorNotReady: a compile or a domain reload replaced it during
        // the run, so nothing in the source needs a change. An assembly that is not loaded stays a
        // Declaration: what loads it is the reader's code path, which waiting does not run.
        internal static HotReloadFailureDescription CheckMvidGuard(HotReloadTypeHome home)
        {
            Debug.Assert(home != null, "home must not be null.");

            ReaderParameters readerParameters = new ReaderParameters { InMemory = true };
            using AssemblyDefinition assemblyDefinition =
                AssemblyDefinition.ReadAssembly(home.DllPath, readerParameters);
            string compiledMvid = assemblyDefinition.MainModule.Mvid.ToString();

            HotReloadLoadedAssemblyState state = home.ResolveLoadedAssembly(compiledMvid).State;
            if (state == HotReloadLoadedAssemblyState.Stale)
            {
                return HotReloadFailureDescription.EditorNotReady(HotReloadConstants.StaleAssemblyHint);
            }

            if (state == HotReloadLoadedAssemblyState.NotLoaded)
            {
                return HotReloadFailureDescription.Declaration(HotReloadConstants.AssemblyNotLoadedHint);
            }

            return null;
        }

        internal static string ToProjectRelativeScriptPath(
            IHotReloadPackageRootCapture packageRootCapture,
            string path)
        {
            Debug.Assert(packageRootCapture != null, "packageRootCapture must not be null.");
            Debug.Assert(!string.IsNullOrEmpty(path), "path must not be empty.");
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            // The captured package roots map a package's physical folder back to the virtual path
            // Unity's script APIs expect; the physical path would resolve to the wrong assembly.
            return ScriptPathNormalizer.ToAssetPath(path, projectRoot, packageRootCapture.Current);
        }
    }
}
