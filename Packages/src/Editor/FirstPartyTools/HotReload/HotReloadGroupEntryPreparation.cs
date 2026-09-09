using System.Collections.Generic;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Resolves every file of a group against the one compiled shim assembly before the group
    /// mutates anything, so the apply step only has to act on decided files.
    /// </summary>
    /// <remarks>
    /// Why ahead of the apply step: binding and resolution touch no registry and apply no patch,
    /// while starting a generation or patching a method does. Keeping the whole group's
    /// resolution on this side of that boundary is what lets a later stage decide, per batch,
    /// whether one file's preflight failure may leave its siblings applied.
    /// </remarks>
    internal static class HotReloadGroupEntryPreparation
    {
        internal static IReadOnlyList<HotReloadPreparedGroupFile> PrepareGroup(
            HotReloadGroupStageCollaborators collaborators,
            HotReloadApplyContext context,
            HotReloadShimCompileResult compileResult,
            TransformWorkerEntryDto[] entriesToPatch)
        {
            Debug.Assert(collaborators != null, "collaborators must not be null.");
            Debug.Assert(context != null, "context must not be null.");
            Debug.Assert(compileResult != null, "compileResult must not be null.");
            Debug.Assert(entriesToPatch != null, "entriesToPatch must not be null.");

            Dictionary<string, List<TransformWorkerEntryDto>> entriesByFile =
                HotReloadWorkerRowsByFile.GroupEntriesBySourceFile(entriesToPatch, context.ProjectRelativePaths);
            // Why once for the group: every shim type of the group lives in this one assembly, so
            // binding per file would re-run the same binders and hide which file first failed.
            Dictionary<string, string> bindFailures =
                collaborators.EntryApplier.BindShimAccessors(compileResult.Assembly);
            List<HotReloadPreparedGroupFile> prepared =
                new List<HotReloadPreparedGroupFile>(context.Files.Count);
            foreach (HotReloadGroupFile file in context.Files)
            {
                prepared.Add(PrepareFile(context, compileResult, file, entriesByFile, bindFailures));
            }

            return prepared;
        }

        private static HotReloadPreparedGroupFile PrepareFile(
            HotReloadApplyContext context,
            HotReloadShimCompileResult compileResult,
            HotReloadGroupFile file,
            Dictionary<string, List<TransformWorkerEntryDto>> entriesByFile,
            Dictionary<string, string> bindFailures)
        {
            if (file.SkipApply)
            {
                return HotReloadPreparedGroupFile.SkippedByGroup(file);
            }

            List<TransformWorkerEntryDto> fileEntries = entriesByFile[file.ProjectRelativePath];
            if (fileEntries.Count == 0)
            {
                return HotReloadPreparedGroupFile.NoEntriesToApply(file);
            }

            TransformWorkerEntryDto[] entries = fileEntries.ToArray();
            HotReloadEntryResolution.Result resolution = HotReloadEntryResolution.ResolveEntries(
                context.AssemblyName,
                file.AssemblyResolvePath,
                compileResult.Assembly,
                entries,
                bindFailures);
            if (!resolution.AllResolved)
            {
                return HotReloadPreparedGroupFile.ResolutionFailed(file, entries, resolution);
            }

            return HotReloadPreparedGroupFile.Resolved(file, entries, resolution);
        }
    }
}
