#nullable enable
using System.Globalization;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Formats input-simulation durations for user-facing CLI result messages.
    /// </summary>
    internal static class InputSimulationDurationFormatter
    {
        private const string SecondsFormat = "0.###";

        internal static string FormatSeconds(float seconds)
        {
            return seconds.ToString(SecondsFormat, CultureInfo.InvariantCulture);
        }
    }
}
