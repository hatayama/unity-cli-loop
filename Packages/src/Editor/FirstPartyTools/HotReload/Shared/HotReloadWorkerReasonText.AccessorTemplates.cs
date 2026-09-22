using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    internal static partial class HotReloadWorkerReasonText
    {
        /// <summary>
        /// Adds the reasons an accessor rewrite, an event use, or an unsupported member was rejected.
        /// </summary>
        private static void AddAccessorTemplates(
            Dictionary<HotReloadWorkerReasonCode, ReasonTemplate> templates)
        {
            templates.Add(
                HotReloadWorkerReasonCode.UnsupportedMemberExplicitAccessor,
                Plain(
                    "Property setter, init, or indexer accessors are skipped; "
                    + "run 'uloop compile' to apply accessor edits.",
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.UnsupportedMemberUnsupportedKind,
                Plain(
                    "Constructors, operators, and event accessors are skipped; "
                    + "run 'uloop compile' to apply these edits.",
                    0));

            templates.Add(
                HotReloadWorkerReasonCode.EventCustomAccessor,
                Plain(
                    "Methods that raise or read an event with custom add/remove accessors are skipped; "
                    + "there is no backing field for the shim to reach. Use uloop compile.",
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.EventNoBackingField,
                Plain(
                    "Methods that raise or read an abstract, extern, or interface event are skipped; "
                    + "there is no backing field for the shim to reach. Use uloop compile.",
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.EventDelegateTypeNotVisible,
                Plain(
                    "Methods that raise or read an event whose delegate type is not visible from an external "
                    + "assembly are skipped; the shim cannot name the accessor field type. Use uloop compile.",
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.EventAddedInThisEdit,
                Plain(
                    "Methods that raise a field-like event added in this edit are skipped; "
                    + "the compiled assembly has no backing field yet. Use uloop compile.",
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.EventNameof,
                Plain(
                    "Methods that name a field-like event inside nameof are skipped; the shim is a different "
                    + "type and cannot keep the bare event name. Use uloop compile.",
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.EventConditionalReceiver,
                Plain(
                    "Methods that raise or read a field-like event through a conditional receiver "
                    + "('a?.E') are skipped; the shim cannot name the conditional receiver as the accessor "
                    + "call's argument. Use uloop compile.",
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.EventPassedByRef,
                Plain(
                    "Methods that pass a field-like event by ref/out/in are skipped; the shim reads the "
                    + "event through an accessor call, which cannot be passed by reference. Use uloop compile.",
                    0));
            // Why the sentence ends in a colon and the separator is empty: the rejected accessor
            // rewrite is the whole reason the method was skipped, so it reads as one sentence.
            templates.Add(
                HotReloadWorkerReasonCode.EventAccessorRewriteUnavailable,
                RequiringDetail(
                    "Methods that raise or read a field-like event are skipped when the body cannot be "
                    + "rewritten into accessor delegates. Accessor rewrite unavailable: ",
                    0,
                    string.Empty,
                    string.Empty));

            // The accessor fragments below never stand alone: each one is the detail of a skip
            // reason above, and reads as the continuation of that sentence.
            templates.Add(
                HotReloadWorkerReasonCode.AccessorContainingTypeNotVisible,
                Plain("containing type is not visible from an external assembly (condition c).", 0));
            templates.Add(
                HotReloadWorkerReasonCode.AccessorSignatureTypeNotVisible,
                Plain(
                    "accessor signature type is not visible from an external assembly: {0} (condition c).",
                    1));
            templates.Add(
                HotReloadWorkerReasonCode.AccessorPropertyIncrementNoShape,
                // Why name the rewrite: the accessor rewrite already handles the same write as a
                // plain or compound assignment statement, so that edit applies without a compile.
                Plain(
                    "inaccessible property increment/decrement has no accessor rewrite shape; write it "
                    + "as a statement 'X += 1' or 'X = X + 1', which the accessor rewrite handles.",
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.AccessorMethodReturnTypeUnresolved,
                Plain(
                    "method return type '{0}' could not be resolved (missing using directive, typo, or a type that is not compiled yet).",
                    1));
            templates.Add(
                HotReloadWorkerReasonCode.AccessorMethodParameterTypeUnresolved,
                Plain(
                    "method parameter type '{0}' could not be resolved (missing using directive, typo, or a type that is not compiled yet).",
                    1));
            templates.Add(
                HotReloadWorkerReasonCode.AccessorMethodReturnTypeNotVisible,
                Plain("method return type is not visible from an external assembly (condition c).", 0));
            templates.Add(
                HotReloadWorkerReasonCode.AccessorMethodParameterTypeNotVisible,
                Plain("method parameter type is not visible from an external assembly (condition c).", 0));
            templates.Add(
                HotReloadWorkerReasonCode.AccessorBodyTypeNotVisible,
                Plain(
                    "body uses a type that is not visible from an external assembly: {0} (condition c).",
                    1));
            templates.Add(
                HotReloadWorkerReasonCode.AccessorIndexerNoShape,
                Plain("inaccessible indexer access has no accessor rewrite shape.", 0));
            templates.Add(
                HotReloadWorkerReasonCode.AccessorEventCompoundAssignmentNoShape,
                Plain("compound assignment to an event has no accessor rewrite shape.", 0));
            templates.Add(
                HotReloadWorkerReasonCode.AccessorExtensionMethodNotRewritten,
                Plain("inaccessible extension method calls are not rewritten.", 0));
            templates.Add(
                HotReloadWorkerReasonCode.AccessorGenericMethodNotRewritten,
                Plain("inaccessible generic method calls are not rewritten.", 0));
            templates.Add(
                HotReloadWorkerReasonCode.AccessorMemberKindUnsupported,
                Plain("inaccessible member kind is not field/method/property access.", 0));
            templates.Add(
                HotReloadWorkerReasonCode.AccessorPropertyNoSetter,
                Plain("inaccessible property has no setter to bind.", 0));
            templates.Add(
                HotReloadWorkerReasonCode.AccessorPropertyNoGetter,
                Plain("inaccessible property has no getter to bind.", 0));
            templates.Add(
                HotReloadWorkerReasonCode.AccessorCoalesceAssignmentNoShape,
                Plain(
                    "null-coalescing assignment writes conditionally and has no accessor rewrite shape.",
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.AccessorCompoundAssignmentKindUnsupported,
                Plain("unsupported compound assignment kind has no accessor rewrite shape.", 0));
            templates.Add(
                HotReloadWorkerReasonCode.AccessorCompoundInaccessibleGetterNoShape,
                Plain(
                    "compound assignment reading an inaccessible getter with an accessible setter "
                    + "has no accessor rewrite shape.",
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.AccessorAssignmentValueConsumed,
                Plain("assignment value is consumed; the setter delegate returns void.", 0));
            templates.Add(
                HotReloadWorkerReasonCode.AccessorReceiverDoubleEvaluation,
                Plain("receiver with possible side effects would be evaluated twice.", 0));
            templates.Add(
                HotReloadWorkerReasonCode.AccessorRefReturningPropertyNoShape,
                Plain("inaccessible ref-returning properties have no accessor rewrite shape.", 0));
            templates.Add(
                HotReloadWorkerReasonCode.AccessorConstructorCallNoShape,
                Plain("inaccessible constructor call has no accessor rewrite shape.", 0));
            templates.Add(
                HotReloadWorkerReasonCode.AccessorInitializerAssignmentNoShape,
                Plain(
                    "inaccessible member assignment in an object/collection initializer has no "
                    + "accessor rewrite shape.",
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.AccessorConditionalAccessNoShape,
                Plain("inaccessible member access via conditional access has no rewrite shape.", 0));
            templates.Add(
                HotReloadWorkerReasonCode.AccessorRefReturningMethodNoShape,
                Plain("inaccessible methods that return by ref have no accessor rewrite shape.", 0));
            templates.Add(
                HotReloadWorkerReasonCode.AccessorRefOutInParameterNotRewritten,
                Plain(
                    "inaccessible method calls with ref/out/in parameters are not rewritten; the call is "
                    + "refused whatever is passed, because the rewrite cannot forward ref/out/in parameters. "
                    + "No form of this call applies while the callee stays a compiled inaccessible method - an "
                    + "added method that calls it is refused for the same reason. Only a method this same "
                    + "reload adds is called directly with ref/out/in arguments, so declare that logic as a "
                    + "method this reload adds, or route the work through an API this code can already "
                    + "access, to keep the edit applying without leaving Play Mode.",
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.AccessorNamedArgumentNotRewritten,
                Plain("inaccessible method calls with named arguments are not rewritten.", 0));
            templates.Add(
                HotReloadWorkerReasonCode.AccessorOptionalOrParamsArgumentNotRewritten,
                Plain(
                    "inaccessible method calls with omitted optional or expanded params arguments "
                    + "are not rewritten.",
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.AccessorMethodGroupNoShape,
                Plain(
                    "inaccessible method group '{0}' (non-invocation) has no accessor rewrite shape. "
                    + "A call is rewritten, so wrapping the method group in a lambda that calls it "
                    + "(such as '(a, b) => {0}(a, b)') keeps hot reloading.",
                    1));
        }
    }
}
