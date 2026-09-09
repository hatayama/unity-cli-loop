using System;
using System.Collections.Generic;
using System.IO;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Resolves one input path's patch target and the source file the worker reads for it.
    /// </summary>
    internal sealed class HotReloadInputFileResolver
    {
        private readonly HotReloadDomain _domain;
        private readonly IHotReloadPackageRootCapture _packageRootCapture;
        private readonly IHotReloadEditorStateSnapshotCapture _editorStateSnapshotCapture;

        internal HotReloadInputFileResolver(
            HotReloadDomain domain,
            IHotReloadPackageRootCapture packageRootCapture,
            IHotReloadEditorStateSnapshotCapture editorStateSnapshotCapture)
        {
            Debug.Assert(domain != null, "domain must not be null.");
            Debug.Assert(packageRootCapture != null, "packageRootCapture must not be null.");
            Debug.Assert(
                editorStateSnapshotCapture != null,
                "editorStateSnapshotCapture must not be null.");
            _domain = domain;
            _packageRootCapture = packageRootCapture;
            _editorStateSnapshotCapture = editorStateSnapshotCapture;
        }

        // Resolves one input path's patch target and either records its early result or enrolls
        // it in the group plan.
        internal void ResolveInputFile(
            string filePath,
            int index,
            string contentPathOverride,
            IReadOnlyDictionary<string, string> contentPathOverrideByFile,
            string correlationId,
            HotReloadRunAccumulator run,
            HotReloadInputResolutionSlot slot,
            List<(int InputIndex, string AssemblyName, string ProjectRelativePath)> plannerInput)
        {
            string workerSourcePath = ResolveWorkerSourcePath(
                filePath,
                contentPathOverride,
                contentPathOverrideByFile);
            HotReloadFileSinks sinks = new HotReloadFileSinks(
                run.SiblingDerivedWarnings,
                run.OneShotCallerNoteCandidates);
            List<HotReloadMethodOutcome> alreadyActiveOutcomes = new List<HotReloadMethodOutcome>();

            HotReloadPatchTargetResolution resolution = HotReloadPatchTargetSupport.ResolvePatchTarget(
                _domain,
                _packageRootCapture,
                _editorStateSnapshotCapture,
                filePath,
                workerSourcePath,
                sinks.Outcomes,
                sinks.Warnings,
                correlationId,
                alreadyActiveOutcomes);
            if (resolution.IsEarlyExit)
            {
                slot.Result = resolution.EarlyResult;
                slot.ResultPath = HotReloadPatchTargetSupport.ToProjectRelativeScriptPath(
                    _packageRootCapture,
                    filePath);
                return;
            }

            if (resolution.UnchangedDecision == HotReloadUnchangedSourceDecision.ShortCircuited)
            {
                slot.DeferredAlreadyActive = alreadyActiveOutcomes;
            }

            slot.GroupFile = new HotReloadGroupFile(
                filePath,
                workerSourcePath,
                resolution.ProjectRelativePath,
                resolution.AssemblyName,
                resolution.CompilationAssembly,
                resolution.TargetDllPath,
                resolution.ProjectRoot,
                sinks,
                resolution.NewSourceMembershipEvidence);
            slot.ResultPath = resolution.ProjectRelativePath;
            plannerInput.Add((index, resolution.AssemblyName, resolution.ProjectRelativePath));
        }

        internal string ResolveSiblingWorkerSourcePath(
            string projectRelativePath,
            string projectRoot,
            IReadOnlyDictionary<string, string> overrideByFile)
        {
            if (overrideByFile != null)
            {
                StringComparer comparer = HotReloadSourcePathNormalizer.ProjectRelativePathComparer();
                foreach (KeyValuePair<string, string> pair in overrideByFile)
                {
                    string keyRelative = HotReloadPatchTargetSupport.ToProjectRelativeScriptPath(
                        _packageRootCapture,
                        pair.Key);
                    if (comparer.Equals(keyRelative, projectRelativePath))
                    {
                        return Path.GetFullPath(pair.Value);
                    }
                }
            }

            return Path.GetFullPath(
                Path.Combine(
                    projectRoot,
                    projectRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        // Why keyed by path (not by index): one contentPathOverride cannot feed two edited
        // copies, and a positional list would silently misalign once files are grouped or
        // reordered before the worker runs.
        private string ResolveWorkerSourcePath(
            string filePath,
            string contentPathOverride,
            IReadOnlyDictionary<string, string> contentPathOverrideByFile)
        {
            if (contentPathOverrideByFile != null
                && contentPathOverrideByFile.TryGetValue(filePath, out string perFileOverride)
                && !string.IsNullOrEmpty(perFileOverride))
            {
                return perFileOverride;
            }

            if (string.IsNullOrEmpty(contentPathOverride))
            {
                return filePath;
            }

            return contentPathOverride;
        }
    }
}
