using System;
using System.Collections.Generic;
using System.Reflection;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// What the latest hot reload of one file says about that file on disk now. Each refusal reads
    /// it at the point it decides, because the answer depends on the file's current bytes.
    /// </summary>
    internal sealed class PausePointHotReloadFileState
    {
        /// <summary>The state of a file while no hot-reload side is installed.</summary>
        internal static readonly PausePointHotReloadFileState NotReloaded = new PausePointHotReloadFileState(
            null,
            string.Empty,
            editedSinceLatestReload: false,
            latestReloadReadFileAsItIs: false,
            shimSpansFollowFile: true,
            Array.Empty<HotReloadUnappliedRow>());

        private readonly IHotReloadPausePointPort _side;
        private readonly string _normalizedFile;

        private PausePointHotReloadFileState(
            IHotReloadPausePointPort side,
            string normalizedFile,
            bool editedSinceLatestReload,
            bool latestReloadReadFileAsItIs,
            bool shimSpansFollowFile,
            IReadOnlyList<HotReloadUnappliedRow> unappliedRows)
        {
            _side = side;
            _normalizedFile = normalizedFile;
            EditedSinceLatestReload = editedSinceLatestReload;
            LatestReloadReadFileAsItIs = latestReloadReadFileAsItIs;
            ShimSpansFollowFile = shimSpansFollowFile;
            UnappliedRows = unappliedRows;
        }

        /// <summary>
        /// True when the file changed since the latest reload that read it; with no such reload
        /// recorded, when it changed since its shim generation was compiled.
        /// </summary>
        internal bool EditedSinceLatestReload { get; }

        /// <summary>True when a reload of the file is recorded and the file still is what it read.</summary>
        internal bool LatestReloadReadFileAsItIs { get; }

        /// <summary>
        /// True when the file has no shim generation, or still is the source that generation was
        /// compiled from, so the generation's spans are lines of the file on disk.
        /// </summary>
        internal bool ShimSpansFollowFile { get; }

        /// <summary>The Skipped and Failed rows of the latest reload; empty unless it read the file as it is.</summary>
        internal IReadOnlyList<HotReloadUnappliedRow> UnappliedRows { get; }

        // The shim path compares --line with spans of the source the shim generation was compiled
        // from. When the latest reload read the file as it is but built no new generation, those
        // spans follow an older version, and reloading the same contents does not refresh them.
        internal bool SkipsShimPath => LatestReloadReadFileAsItIs && !ShimSpansFollowFile;

        /// <summary>
        /// The row the latest reload left for the method, or null when that reload did not read
        /// the file as it is or left no row whose label matches the method.
        /// </summary>
        internal HotReloadUnappliedRow FindUnappliedRowForMethodOrNull(MethodBase method)
        {
            if (!LatestReloadReadFileAsItIs)
            {
                return null;
            }

            // Why the port this state was read from: asking whichever side is installed now could
            // answer from a domain that did not produce the rows this state holds.
            return _side.FindUnappliedRowForMethod(_normalizedFile, method);
        }

        // Why every row, in reported order: the caller finds each one in the hot reload response
        // by its Methods[].Method string, and a cut list could drop the row that holds the line.
        internal string DescribeUnappliedRows()
        {
            List<string> described = new List<string>(UnappliedRows.Count);
            foreach (HotReloadUnappliedRow row in UnappliedRows)
            {
                string outcome = row.Kind == HotReloadUnappliedRowKind.Skipped
                    ? SourcePausePointConstants.HotReloadLeftBehindSkippedVerb
                    : SourcePausePointConstants.HotReloadLeftBehindFailedVerb;
                described.Add("'" + row.Label + "' (" + outcome + ")");
            }

            return string.Join(", ", described);
        }

        internal static PausePointHotReloadFileState Read(string normalizedFile)
        {
            IHotReloadPausePointPort side = HotReloadPausePointCoordination.HotReloadSide;
            if (side == null)
            {
                return NotReloaded;
            }

            bool shimSourceChanged = side.HasShimSourceChangedOnDisk(normalizedFile);
            HotReloadLatestFileReload latest = side.GetLatestReloadOfFile(normalizedFile);
            if (latest == null)
            {
                return Unrecorded(side, normalizedFile, shimSourceChanged);
            }

            if (latest.FileChangedSince)
            {
                return ChangedSinceLatestReload(side, normalizedFile, shimSourceChanged);
            }

            return ReadAsItIs(side, normalizedFile, shimSourceChanged, latest.UnappliedRows);
        }

        // Why a changed shim source counts as an edit when no reload is recorded: that is the
        // only evidence of an edit left, and the refusal it leads to asks for the hot reload that
        // records one.
        internal static PausePointHotReloadFileState Unrecorded(
            IHotReloadPausePointPort side,
            string normalizedFile,
            bool shimSourceChanged)
        {
            return Create(side, normalizedFile, shimSourceChanged, false, shimSourceChanged, Array.Empty<HotReloadUnappliedRow>());
        }

        internal static PausePointHotReloadFileState ChangedSinceLatestReload(
            IHotReloadPausePointPort side,
            string normalizedFile,
            bool shimSourceChanged)
        {
            return Create(side, normalizedFile, true, false, shimSourceChanged, Array.Empty<HotReloadUnappliedRow>());
        }

        internal static PausePointHotReloadFileState ReadAsItIs(
            IHotReloadPausePointPort side,
            string normalizedFile,
            bool shimSourceChanged,
            IReadOnlyList<HotReloadUnappliedRow> unappliedRows)
        {
            if (unappliedRows == null)
            {
                throw new ArgumentNullException(nameof(unappliedRows));
            }

            return Create(side, normalizedFile, false, true, shimSourceChanged, unappliedRows);
        }

        private static PausePointHotReloadFileState Create(
            IHotReloadPausePointPort side,
            string normalizedFile,
            bool editedSinceLatestReload,
            bool latestReloadReadFileAsItIs,
            bool shimSourceChanged,
            IReadOnlyList<HotReloadUnappliedRow> unappliedRows)
        {
            if (side == null)
            {
                throw new ArgumentNullException(nameof(side));
            }

            return new PausePointHotReloadFileState(
                side,
                normalizedFile ?? string.Empty,
                editedSinceLatestReload,
                latestReloadReadFileAsItIs,
                !shimSourceChanged,
                unappliedRows);
        }
    }
}
