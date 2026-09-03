using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Accumulates enable-time warning entries so Warning stays the space-joined form.
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
            response.Warnings = warnings;
            response.Warning = string.Join(" ", warnings);
        }
    }
}
