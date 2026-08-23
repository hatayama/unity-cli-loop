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

        public const string MissingAssemblyReferenceNextActionFormat =
            "error {0}: '{1}' is declared in {2}. Add the assembly to the failing script's .asmdef references and run 'uloop compile' again. If the failing script has no .asmdef, the declaring assembly may have Auto Referenced disabled or its package may not be installed.";

        public const string MissingNamespacePattern =
            @"The type or namespace name '(?<inner>[^']+)' does not exist in the namespace '(?<outer>[^']+)'";

        public const string Cs0234ErrorCode = "CS0234";

        public const string SingleDeclaringAssemblyFormat = "assembly '{0}'";

        public const string MultipleDeclaringAssembliesFormat = "assemblies '{0}'";

        public const int MaxErrorsToScan = 10;

        public const int MaxNextActionsToAppend = 3;

        public const int MaxDeclaringAssembliesToName = 3;
    }
}
