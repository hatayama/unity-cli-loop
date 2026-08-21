namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Fixed literals for declining Unity's Script Updating Consent dialog during CLI compiles.
    /// </summary>
    internal static class CompileApiUpdaterConsentConstants
    {
        public const string HarmonyId = "io.github.hatayama.uloop.compile-api-updater-consent";

        public const string DialogTitle = "Script Updating Consent";

        public const int DeclinedDialogResult = 1;

        public const string WarningText =
            "Unity's API Updater requested consent to rewrite source files ('Script Updating Consent' dialog). uloop declines this automatically: source files are not rewritten without explicit user consent. The obsolete-API compile errors it would have fixed are reported in Errors.";

        public const string NextActionText =
            "Fix the obsolete API usages reported in Errors, or ask the user to accept the Script Updating Consent dialog in an interactive Unity session.";
    }
}
