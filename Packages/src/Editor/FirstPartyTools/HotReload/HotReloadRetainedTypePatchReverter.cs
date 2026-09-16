using System.Collections.Generic;
using System.Reflection;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Peels the patches an earlier reload left on the methods of a type an introduced-type
    /// artifact serves, once the edited source of that type matches the bodies the artifact holds.
    /// </summary>
    /// <remarks>
    /// Why not the unchanged-method peel: the worker reports such a type as a bound declaration
    /// rather than an unchanged method, because the declaration it compared the source against
    /// lives in the artifact and not in the compiled assembly the edited file belongs to. Without
    /// this peel a restored body would leave the artifact running the code of the reload before it,
    /// which is the one case where behavior does not converge back to what the source says.
    /// </remarks>
    internal sealed class HotReloadRetainedTypePatchReverter
    {
        private readonly HotReloadPatcher _patcher;
        private readonly HotReloadIntroducedTypeRegistry _registry;

        internal HotReloadRetainedTypePatchReverter(
            HotReloadPatcher patcher,
            HotReloadIntroducedTypeRegistry registry)
        {
            Debug.Assert(patcher != null, "patcher must not be null.");
            Debug.Assert(registry != null, "registry must not be null.");

            _patcher = patcher;
            _registry = registry;
        }

        /// <summary>
        /// Reverts every leftover patch of the retained declarations this run bound unchanged, and
        /// adds what that peeled to each file's reverted count.
        /// </summary>
        /// <remarks>
        /// Why the type rows and not the worker output: only the preparation of a reload
        /// fingerprints a declaration against the artifact that serves it, and the transform that
        /// follows it reports no reuse of its own, so the rows the preparation left on each file
        /// are the only place a bound declaration and whether its bodies changed are both known.
        /// </remarks>
        internal void RevertRestoredBodiesPerFile(
            IReadOnlyList<HotReloadGroupFile> files,
            string targetAssemblyName,
            string targetAssemblyMvid)
        {
            Debug.Assert(files != null, "files must not be null.");

            foreach (HotReloadGroupFile file in files)
            {
                file.RevertedUnchangedCount +=
                    RevertRestoredBodies(file, targetAssemblyName, targetAssemblyMvid);
            }
        }

        private int RevertRestoredBodies(
            HotReloadGroupFile file,
            string targetAssemblyName,
            string targetAssemblyMvid)
        {
            int revertedCount = 0;
            foreach (HotReloadIntroducedTypeOutcome outcome in file.Sinks.IntroducedTypes)
            {
                // Why a declaration whose bodies changed is left alone: the apply stage of this
                // very run patches those bodies, so peeling them here would undo the edit being
                // applied. Why only an already-active row: a type this run itself introduces has
                // no earlier patch, and a refused one has no artifact to peel from.
                if (outcome.Kind != HotReloadIntroducedTypeOutcomeKind.AlreadyActive
                    || outcome.BodyEdited
                    || string.IsNullOrEmpty(outcome.MetadataName))
                {
                    continue;
                }

                revertedCount +=
                    RevertDeclaration(outcome, file, targetAssemblyName, targetAssemblyMvid);
            }

            return revertedCount;
        }

        // Why the target assembly identity decides which artifacts to search: a declaration is
        // bound from the artifacts of one generation of one compiled assembly, and a type of the
        // same name served by another generation is a different type.
        private int RevertDeclaration(
            HotReloadIntroducedTypeOutcome outcome,
            HotReloadGroupFile file,
            string targetAssemblyName,
            string targetAssemblyMvid)
        {
            HotReloadReflectionTypeName reflectionTypeName =
                new HotReloadMetadataTypeName(outcome.MetadataName).ToReflectionName();
            IReadOnlyList<HotReloadIntroducedTypeArtifact> artifacts =
                _registry.CollectActiveArtifactsForTarget(targetAssemblyName, targetAssemblyMvid);
            int revertedCount = 0;
            for (int index = 0; index < artifacts.Count; index++)
            {
                revertedCount += RevertTypePatches(artifacts[index].Assembly, reflectionTypeName, file);
            }

            return revertedCount;
        }

        private int RevertTypePatches(
            Assembly assembly,
            HotReloadReflectionTypeName reflectionTypeName,
            HotReloadGroupFile file)
        {
            IReadOnlyList<MethodBase> methods =
                _patcher.ListActiveMethodsDeclaredBy(assembly, reflectionTypeName);
            int revertedCount = 0;
            for (int index = 0; index < methods.Count; index++)
            {
                HotReloadRevertOutcome outcome = _patcher.Revert(methods[index], out string failureReason);
                if (outcome == HotReloadRevertOutcome.Reverted)
                {
                    revertedCount++;
                    continue;
                }

                // Why only an unpatch failure is reported: a method the ledger no longer holds is
                // the state this peel exists to reach, so finding it already unpatched is success.
                if (outcome == HotReloadRevertOutcome.UnpatchFailed)
                {
                    file.Sinks.Outcomes.Add(
                        HotReloadMethodOutcome.Failed(
                            HotReloadMethodKeys.FormatMethodLabel(methods[index]),
                            failureReason,
                            file.AssemblyResolvePath));
                }
            }

            return revertedCount;
        }
    }
}
