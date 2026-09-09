using System.Collections.Generic;
using System.Globalization;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Shapes what --status and --revert-all report about the types this domain introduced.
    /// </summary>
    internal static class HotReloadIntroducedTypeStatusSection
    {
        /// <summary>
        /// One row per active introduced type, taken from the registry snapshot.
        /// </summary>
        /// <remarks>
        /// Why the descriptors and not the artifact count: one artifact carries every type of the
        /// batch that compiled it, so a report built from artifacts would name one row for two
        /// types the caller can each reach by name.
        /// </remarks>
        internal static List<HotReloadIntroducedTypeResult> BuildActiveRows(HotReloadDomain domain)
        {
            Debug.Assert(domain != null, "domain must not be null.");
            IReadOnlyList<HotReloadIntroducedTypeDescriptor> descriptors =
                domain.IntroducedTypes.DescribeActive();
            List<HotReloadIntroducedTypeResult> rows =
                new List<HotReloadIntroducedTypeResult>(descriptors.Count);
            foreach (HotReloadIntroducedTypeDescriptor descriptor in descriptors)
            {
                rows.Add(
                    new HotReloadIntroducedTypeResult
                    {
                        Kind = HotReloadConstants.ActiveIntroducedTypeStatusKind,
                        TypeName = descriptor.MetadataName.Value,
                        AssemblyName = descriptor.OriginalAssemblyName,
                        FilePath = descriptor.OwnerProjectRelativePath ?? string.Empty,
                        Reason = string.Empty
                    });
            }

            return rows;
        }

        /// <summary>
        /// Adds what a revert leaves behind, so a caller told every change was reverted is not
        /// left believing the introduced types went with them, and says how the hold those types
        /// keep armed is released.
        /// </summary>
        internal static string AppendRevertAllNote(
            string message,
            int introducedTypeCount,
            bool autoRefreshHeld)
        {
            if (introducedTypeCount == 0)
            {
                return message;
            }

            string note = message + string.Format(
                CultureInfo.InvariantCulture,
                HotReloadConstants.ActiveIntroducedTypesRevertAllNoteFormat,
                introducedTypeCount);
            if (!autoRefreshHeld)
            {
                return note;
            }

            return note + HotReloadConstants.ActiveIntroducedTypesRevertAllHoldNote;
        }
    }
}
