using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    internal static partial class HotReloadWorkerReasonText
    {
        /// <summary>
        /// Adds the reasons a newly introduced type was rejected, plus the reasons the Editor itself reports.
        /// </summary>
        private static void AddIntroducedTypeTemplates(
            Dictionary<HotReloadWorkerReasonCode, ReasonTemplate> templates)
        {
            templates.Add(
                HotReloadWorkerReasonCode.IntroducedTypeSymbolUnresolved,
                Plain("Could not resolve a declared type symbol.", 0));
            templates.Add(
                HotReloadWorkerReasonCode.IntroducedTypeGeneric,
                Plain("Generic introduced type requires a compile: {0}", 1));
            templates.Add(
                HotReloadWorkerReasonCode.IntroducedTypePartial,
                Plain("Partial introduced type requires a compile: {0}", 1));
            templates.Add(
                HotReloadWorkerReasonCode.IntroducedTypeRecord,
                Plain("Record introduced type requires a compile: {0}", 1));
            templates.Add(
                HotReloadWorkerReasonCode.IntroducedTypeNonPublic,
                Plain("Non-public introduced type requires a compile: {0}", 1));
            templates.Add(
                HotReloadWorkerReasonCode.IntroducedTypeRefLike,
                Plain("Ref-like introduced type requires a compile: {0}", 1));
            templates.Add(
                HotReloadWorkerReasonCode.IntroducedTypeUnsafe,
                Plain("Unsafe introduced type requires a compile: {0}", 1));
            templates.Add(
                HotReloadWorkerReasonCode.IntroducedTypeUnityObject,
                Plain("Unity object introduced type requires a compile: {0}", 1));
            templates.Add(
                HotReloadWorkerReasonCode.IntroducedTypeSerializable,
                Plain("Serializable introduced type requires a compile: {0}", 1));
            templates.Add(
                HotReloadWorkerReasonCode.IntroducedTypeModuleInitializer,
                Plain("Module initializer introduced type requires a compile: {0}", 1));
            templates.Add(
                HotReloadWorkerReasonCode.IntroducedTypeUnsupported,
                Plain("Unsupported introduced type requires a compile: {0}", 1));
            templates.Add(
                HotReloadWorkerReasonCode.IntroducedTypeConstValueUnverifiable,
                Plain("Const value cannot be verified: {0} referenced by {1}", 2));
            templates.Add(
                HotReloadWorkerReasonCode.IntroducedTypeConstChanged,
                Plain("Changed const requires a compile: {0} referenced by {1}", 2));
            templates.Add(
                HotReloadWorkerReasonCode.IntroducedTypeDelegate,
                Plain("Delegate introduced type requires a compile: {0}", 1));
            templates.Add(
                HotReloadWorkerReasonCode.IntroducedTypeNested,
                Plain("Nested type requires a compile: {0}", 1));
            templates.Add(
                HotReloadWorkerReasonCode.IntroducedTypeNestedDeclaration,
                Plain("Nested declaration inside an introduced type requires a compile: {0}/{1}", 2));
            // Why composing rather than plain: the declaration differences are worth naming when
            // the comparison found them, and a run that only knows the type name still has to
            // read the same sentence it always did.
            templates.Add(
                HotReloadWorkerReasonCode.IntroducedTypeChanged,
                Composing(
                    "Changed introduced type requires a compile: {0}",
                    1,
                    " Declaration differences: ",
                    "."));
            templates.Add(
                HotReloadWorkerReasonCode.IntroducedTypeMemberBodyChanged,
                RequiringDetail(
                    "Changed member body of introduced type requires a compile: {0}",
                    1,
                    " Changed members: ",
                    ". Only ordinary method bodies and getter-only property bodies of an "
                    + "introduced type can be hot reloaded."));

            // The fragment that carries a list the worker already joined: the tokens name
            // fingerprint parts and member keys rather than reading as English, so wording them
            // here would only reformat values the reader has to match against their source.
            templates.Add(
                HotReloadWorkerReasonCode.IntroducedTypeDifferenceList,
                Plain("{0}", 1));

            // The one sentence whose value is written by the worker rather than here: the same
            // artifact error is also returned as a fatal transform message, so it stays a single
            // sentence owned by the artifact map instead of being split into codes twice.
            templates.Add(
                HotReloadWorkerReasonCode.IntroducedTypeArtifactUnusable,
                Plain("Introduced types require a compile: {0}", 1));
            templates.Add(
                HotReloadWorkerReasonCode.IntroducedTypeInputsUnreadable,
                Plain(
                    "Introduced types require a compile: the target assembly or its references could not be read.",
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.IntroducedTypeIdentityMismatch,
                Plain(
                    "Introduced types require a compile: the target assembly identity does not match the request.",
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.EditorIsolatedAddedMethodCaller,
                Plain(HotReloadConstants.IsolatedAddedMethodCallerSkipReason, 0));
        }
    }
}
