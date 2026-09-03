using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds the enable-time warning that names the parameters capture cannot box.
    /// </summary>
    internal static class PausePointNotCapturableWarnings
    {
        /// <summary>
        /// Warns about parameters left out of CapturedVariables, or empty when there are none.
        /// </summary>
        internal static string BuildNotCapturableParametersWarningOrEmpty(
            IReadOnlyList<string> notCapturableVariables)
        {
            if (notCapturableVariables == null || notCapturableVariables.Count == 0)
            {
                return string.Empty;
            }

            return string.Format(
                SourcePausePointConstants.NotCapturableParametersWarningFormat,
                string.Join(", ", notCapturableVariables));
        }
    }
}
