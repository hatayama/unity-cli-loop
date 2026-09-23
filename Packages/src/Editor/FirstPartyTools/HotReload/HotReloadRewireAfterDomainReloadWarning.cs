using System;
using System.Collections.Generic;
using System.Globalization;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds the warnings that ask to wire added fields again after a domain reload or a revert
    /// discarded the values an earlier apply wired into them.
    /// </summary>
    internal static class HotReloadRewireAfterDomainReloadWarning
    {
        /// <summary>
        /// Appends the warning when this run added again fields a domain reload or revert had
        /// discarded.
        /// </summary>
        internal static void Append(List<string> warnings, IReadOnlyList<string> rewireFields)
        {
            if (warnings == null)
            {
                throw new ArgumentNullException(nameof(warnings));
            }

            if (rewireFields == null)
            {
                throw new ArgumentNullException(nameof(rewireFields));
            }

            if (rewireFields.Count == 0)
            {
                return;
            }

            warnings.Add(
                string.Format(
                    CultureInfo.InvariantCulture,
                    HotReloadConstants.RewireAfterDomainReloadWarningFormat,
                    string.Join(", ", rewireFields)));
        }

        /// <summary>
        /// Appends the revert warning when the revert dropped added fields.
        /// </summary>
        internal static void AppendRevertDropped(List<string> warnings, IReadOnlyList<string> droppedFields)
        {
            if (warnings == null)
            {
                throw new ArgumentNullException(nameof(warnings));
            }

            if (droppedFields == null)
            {
                throw new ArgumentNullException(nameof(droppedFields));
            }

            if (droppedFields.Count == 0)
            {
                return;
            }

            warnings.Add(
                string.Format(
                    CultureInfo.InvariantCulture,
                    HotReloadConstants.RevertDroppedAddedFieldValuesWarningFormat,
                    droppedFields.Count,
                    string.Join(", ", droppedFields)));
        }
    }
}
