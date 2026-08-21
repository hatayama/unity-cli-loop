using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Applies the API Updater decline disclosure onto an already-shaped CompileResponse.
    /// </summary>
    internal static class CompileApiUpdaterConsentResponseComposer
    {
        /// <summary>
        /// Appends the fixed Warning and NextActions literals without overwriting existing values.
        /// </summary>
        internal static void Apply(CompileResponse response, bool apiUpdaterConsentDeclined)
        {
            Debug.Assert(response != null, "response must not be null");
            if (!apiUpdaterConsentDeclined)
            {
                return;
            }

            if (string.IsNullOrEmpty(response.Warning))
            {
                response.Warning = CompileApiUpdaterConsentConstants.WarningText;
            }
            else
            {
                response.Warning = response.Warning + "\n" + CompileApiUpdaterConsentConstants.WarningText;
            }

            if (response.NextActions == null || response.NextActions.Length == 0)
            {
                response.NextActions = new[] { CompileApiUpdaterConsentConstants.NextActionText };
                return;
            }

            string[] nextActions = new string[response.NextActions.Length + 1];
            for (int index = 0; index < response.NextActions.Length; index++)
            {
                nextActions[index] = response.NextActions[index];
            }

            nextActions[response.NextActions.Length] = CompileApiUpdaterConsentConstants.NextActionText;
            response.NextActions = nextActions;
        }
    }
}
