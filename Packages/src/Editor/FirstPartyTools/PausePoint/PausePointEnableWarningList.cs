using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Accumulates enable-time warning entries so Warning stays the space-joined form and Message
    /// names the aggregate.
    /// </summary>
    internal static class PausePointEnableWarningList
    {
        // Repeats hot reload's suffix verbatim: an agent reads Message first, and warnings Message
        // does not point at are warnings that go unread.
        private const string WarningCountMessageSuffix = " warning(s). See Warnings.";

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
            if (warnings.Count == 0)
            {
                return;
            }

            response.Message = AppendMessagePart(response.Message, warnings.Count + WarningCountMessageSuffix);
        }

        // Keeps the suffix from starting with a stray space when the response carries no message.
        private static string AppendMessagePart(string message, string part)
        {
            return string.IsNullOrEmpty(message) ? part : message + " " + part;
        }
    }
}
