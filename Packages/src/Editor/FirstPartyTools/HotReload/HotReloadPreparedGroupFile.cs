using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// One file of a group after preparation: which path it takes and, when its entries resolved,
    /// the entries and the resolution the apply step needs.
    /// </summary>
    internal sealed class HotReloadPreparedGroupFile
    {
        internal HotReloadGroupFile File { get; }
        internal HotReloadGroupFilePreparationKind Kind { get; }
        internal TransformWorkerEntryDto[] Entries { get; }
        internal HotReloadEntryResolution.Result Resolution { get; }

        private HotReloadPreparedGroupFile(
            HotReloadGroupFile file,
            HotReloadGroupFilePreparationKind kind,
            TransformWorkerEntryDto[] entries,
            HotReloadEntryResolution.Result resolution)
        {
            Debug.Assert(file != null, "file must not be null.");

            File = file;
            Kind = kind;
            Entries = entries;
            Resolution = resolution;
        }

        internal static HotReloadPreparedGroupFile SkippedByGroup(HotReloadGroupFile file)
        {
            return new HotReloadPreparedGroupFile(
                file, HotReloadGroupFilePreparationKind.SkippedByGroup, null, null);
        }

        internal static HotReloadPreparedGroupFile NoEntriesToApply(HotReloadGroupFile file)
        {
            return new HotReloadPreparedGroupFile(
                file, HotReloadGroupFilePreparationKind.NoEntriesToApply, null, null);
        }

        internal static HotReloadPreparedGroupFile ResolutionFailed(
            HotReloadGroupFile file,
            TransformWorkerEntryDto[] entries,
            HotReloadEntryResolution.Result resolution)
        {
            Debug.Assert(resolution != null, "resolution must not be null.");
            return new HotReloadPreparedGroupFile(
                file, HotReloadGroupFilePreparationKind.ResolutionFailed, entries, resolution);
        }

        internal static HotReloadPreparedGroupFile Resolved(
            HotReloadGroupFile file,
            TransformWorkerEntryDto[] entries,
            HotReloadEntryResolution.Result resolution)
        {
            Debug.Assert(resolution != null, "resolution must not be null.");
            Debug.Assert(entries != null && entries.Length > 0, "A resolved file must hold an entry.");
            return new HotReloadPreparedGroupFile(
                file, HotReloadGroupFilePreparationKind.Resolved, entries, resolution);
        }
    }
}
