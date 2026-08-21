using System;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Pure intercept decision for the Script Updating Consent dialog during a CLI compile.
    /// </summary>
    internal static class CompileApiUpdaterConsentDecision
    {
        /// <summary>
        /// Returns whether <paramref name="title"/> should be declined without showing the dialog.
        /// </summary>
        internal static (bool intercept, int declinedResult) Decide(bool isCliCompileInFlight, string title)
        {
            if (!isCliCompileInFlight)
            {
                return (false, 0);
            }

            if (!string.Equals(title, CompileApiUpdaterConsentConstants.DialogTitle, StringComparison.Ordinal))
            {
                return (false, 0);
            }

            return (true, CompileApiUpdaterConsentConstants.DeclinedDialogResult);
        }
    }
}
