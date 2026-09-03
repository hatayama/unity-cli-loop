using System.Collections.Generic;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Accumulates enable-time warning entries so Warning stays the space-joined form and Message
    /// names the aggregate.
    /// </summary>
    internal static class PausePointEnableWarningList
    {
        internal static void AddIfNotEmpty(List<string> warnings, string warning)
        {
            if (string.IsNullOrEmpty(warning))
            {
                return;
            }

            warnings.Add(warning);
        }

        internal static void AddRangeIfNotEmpty(List<string> warnings, IReadOnlyList<string> more)
        {
            if (more == null)
            {
                return;
            }

            for (int index = 0; index < more.Count; index++)
            {
                AddIfNotEmpty(warnings, more[index]);
            }
        }

        internal static void Assign(PausePointResponse response, List<string> warnings)
        {
            // Why null rather than an empty list and an empty string: both properties are serialized
            // with NullValueHandling.Ignore, so a response that warned about nothing omits the pair
            // instead of publishing an empty Warning next to an empty Warnings.
            if (warnings.Count == 0)
            {
                response.Warning = null;
                response.Warnings = null;
                return;
            }

            response.Warnings = warnings;
            response.Warning = string.Join(" ", warnings);
            response.Message = WarningsMessagePointer.Append(response.Message, warnings.Count);
        }
    }
}
