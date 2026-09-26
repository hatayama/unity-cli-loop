using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Which introduced-type diagnostics the Editor has to treat as a failure of the whole group
    /// rather than as a notice beside a run that continues.
    /// </summary>
    internal static class HotReloadIntroducedTypeFailureCodes
    {
        /// <summary>
        /// Whether the diagnostic refuses a type the run already retains an assembly for. Such a
        /// declaration is taken out of the tree either way, so continuing would bind callers
        /// against the retained definition the edited source no longer declares. Every other
        /// diagnostic names a declaration that was simply not introduced and whose source stays
        /// in the tree, so the run continues and only has to say what will keep not working.
        /// </summary>
        internal static bool IsRedefinedTypeFailure(HotReloadWorkerReasonCode code)
        {
            return code == HotReloadWorkerReasonCode.IntroducedTypeChanged
                || code == HotReloadWorkerReasonCode.IntroducedTypeMemberBodyChanged;
        }

        /// <summary>
        /// The metadata name of the declaration the diagnostic refused, or null when it refuses
        /// none by name. Why listed rather than derived: the argument that holds the type differs
        /// by code, and the run-scoped and redefinition codes hold none, so a new code stays out
        /// until someone checks its arguments.
        /// </summary>
        internal static string FindRefusedTypeMetadataName(HotReloadWorkerReasonCode code, string[] args)
        {
            return RefusedTypeArgumentIndexByCode.TryGetValue(code, out int index) ? args[index] : null;
        }

        private const int DeclarationArgument = 0;

        // The const codes name the const first and the type that references it second.
        private const int ConstReferrerArgument = 1;

        private static readonly Dictionary<HotReloadWorkerReasonCode, int> RefusedTypeArgumentIndexByCode =
            new Dictionary<HotReloadWorkerReasonCode, int>
            {
                [HotReloadWorkerReasonCode.IntroducedTypeGeneric] = DeclarationArgument,
                [HotReloadWorkerReasonCode.IntroducedTypePartial] = DeclarationArgument,
                [HotReloadWorkerReasonCode.IntroducedTypeRecord] = DeclarationArgument,
                [HotReloadWorkerReasonCode.IntroducedTypeNonPublic] = DeclarationArgument,
                [HotReloadWorkerReasonCode.IntroducedTypeRefLike] = DeclarationArgument,
                [HotReloadWorkerReasonCode.IntroducedTypeUnsafe] = DeclarationArgument,
                [HotReloadWorkerReasonCode.IntroducedTypeUnityObject] = DeclarationArgument,
                [HotReloadWorkerReasonCode.IntroducedTypeSerializable] = DeclarationArgument,
                [HotReloadWorkerReasonCode.IntroducedTypeModuleInitializer] = DeclarationArgument,
                [HotReloadWorkerReasonCode.IntroducedTypeUnsupported] = DeclarationArgument,
                [HotReloadWorkerReasonCode.IntroducedTypeDelegate] = DeclarationArgument,
                [HotReloadWorkerReasonCode.IntroducedTypeNested] = DeclarationArgument,
                [HotReloadWorkerReasonCode.IntroducedTypeNestedDeclaration] = DeclarationArgument,
                [HotReloadWorkerReasonCode.IntroducedTypeConstValueUnverifiable] = ConstReferrerArgument,
                [HotReloadWorkerReasonCode.IntroducedTypeConstChanged] = ConstReferrerArgument
            };

        /// <summary>
        /// Whether the diagnostic is about the whole run rather than a declaration. The worker
        /// attaches such a diagnostic to every file it parsed, so the file it lands on need not
        /// declare any type.
        /// </summary>
        internal static bool IsRunScoped(HotReloadWorkerReasonCode code)
        {
            return code == HotReloadWorkerReasonCode.IntroducedTypeArtifactUnusable
                || code == HotReloadWorkerReasonCode.IntroducedTypeInputsUnreadable
                || code == HotReloadWorkerReasonCode.IntroducedTypeIdentityMismatch;
        }
    }
}
