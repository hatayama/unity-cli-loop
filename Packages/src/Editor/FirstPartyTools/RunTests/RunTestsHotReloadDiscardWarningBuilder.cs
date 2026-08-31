using System.Globalization;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds the run-tests Warning when hot-reload changes were live at the start of the
    /// run: a deferred domain reload after UnlockReloadAssemblies can discard those patches,
    /// but the response is assembled before that reload so the wording stays policy-form.
    /// </summary>
    internal static class RunTestsHotReloadDiscardWarningBuilder
    {
        public static string Build(int activeChangeCountAtStart)
        {
            if (activeChangeCountAtStart <= 0)
            {
                return string.Empty;
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                RunTestsConstants.HotReloadDiscardWarningFormat,
                activeChangeCountAtStart);
        }
    }
}
