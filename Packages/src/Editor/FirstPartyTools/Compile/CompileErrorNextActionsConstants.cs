namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Literals used to detect compile-error origins and append corrective NextActions.
    /// </summary>
    internal static class CompileErrorNextActionsConstants
    {
        public const string LanguageVersionPinnedNextActionFormat =
            "error {0}: the project's C# language version is pinned by the Unity Editor version, so raising the language version is not actionable here. Rewrite without the '{1}' feature so the code compiles under C# {2}.";

        public const string ErrorCodePattern = @"\b(CS[0-9]{4})\b";

        public const string LanguageVersionFeaturePattern =
            @"Feature '(?<feature>[^']+)' is not available in C# (?<version>[0-9]+(\.[0-9]+)?)";

        public const int MaxErrorsToScan = 10;

        public const int MaxNextActionsToAppend = 3;
    }
}
