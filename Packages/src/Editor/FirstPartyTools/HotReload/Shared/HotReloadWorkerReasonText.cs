using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds the English sentence for a reason the worker reported. This is the only place a
    /// worker reason is worded: the worker sends a code and the values the sentence needs, so a
    /// wording change never has to be mirrored across the process boundary.
    /// </summary>
    internal static class HotReloadWorkerReasonText
    {
        // The "compile it properly" call to action the skip sentences end with, and the phrase
        // that introduces a rejected accessor rewrite. They are shared because several sentences
        // end the same way, not because they carry meaning of their own.
        private const string CompileCallToAction = "Run 'uloop compile'.";

        private const string CompileCallToActionToAddIt = "Run 'uloop compile' to add it.";

        private const string CompileCallToActionToAddThem = "Run 'uloop compile' to add them.";

        private const string AccessorRewriteUnavailableSeparator = " Accessor rewrite unavailable: ";

        private static readonly Dictionary<HotReloadWorkerReasonCode, ReasonTemplate> Templates =
            BuildTemplates();

        /// <summary>
        /// The sentence for this reason, with its values substituted and its detail appended.
        /// </summary>
        internal static string Render(TransformWorkerReasonDto reason)
        {
            if (reason == null)
            {
                throw new ArgumentNullException(nameof(reason));
            }

            ReasonTemplate template = Templates[reason.code];
            string[] args = reason.args ?? Array.Empty<string>();
            if (args.Length != template.PlaceholderCount)
            {
                throw new ArgumentException(
                    "Reason " + reason.code + " takes " + template.PlaceholderCount
                    + " value(s) but carries " + args.Length + ".",
                    nameof(reason));
            }

            string text = template.Text;
            for (int index = 0; index < args.Length; index++)
            {
                // Why not string.Format: some sentences quote C# source containing braces, which
                // a format string would read as a placeholder and reject.
                text = text.Replace("{" + index + "}", args[index] ?? string.Empty);
            }

            if (reason.detail == null)
            {
                if (template.RequiresDetail)
                {
                    throw new ArgumentException(
                        "Reason " + reason.code + " is only reported with a detail.",
                        nameof(reason));
                }

                return text;
            }

            if (!template.AllowsDetail)
            {
                throw new ArgumentException(
                    "Reason " + reason.code + " has no place for a detail.",
                    nameof(reason));
            }

            return text + template.DetailSeparator + Render(reason.detail) + template.DetailSuffix;
        }

        private static Dictionary<HotReloadWorkerReasonCode, ReasonTemplate> BuildTemplates()
        {
            Dictionary<HotReloadWorkerReasonCode, ReasonTemplate> templates =
                new Dictionary<HotReloadWorkerReasonCode, ReasonTemplate>();

            templates.Add(
                HotReloadWorkerReasonCode.MethodTransformNoBody,
                Plain("Methods without a body (abstract/extern) are skipped.", 0));
            templates.Add(
                HotReloadWorkerReasonCode.MethodTransformBaseMemberCall,
                Plain(
                    "Methods that call base. members are skipped; C# cannot express base calls outside the type.",
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.MethodTransformClosureInaccessibleAccess,
                Composing(
                    "Lambda, local-function, or query-expression bodies that access private/internal members "
                    + "are skipped (closure methods JIT-compile normally and fail accessibility checks).",
                    0,
                    AccessorRewriteUnavailableSeparator,
                    string.Empty));
            templates.Add(
                HotReloadWorkerReasonCode.MethodTransformAsyncIteratorInaccessibleAccess,
                Composing(
                    "Async or iterator methods whose bodies access private/internal members are skipped "
                    + "(state-machine MoveNext JIT-compiles normally and fails accessibility checks).",
                    0,
                    AccessorRewriteUnavailableSeparator,
                    string.Empty));
            templates.Add(
                HotReloadWorkerReasonCode.MethodTransformPartialType,
                Plain(
                    "Partial types are skipped because a single file cannot provide a complete semantic model.",
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.MethodTransformStructHost,
                Plain(
                    "Struct (value type) methods are skipped; byref instance transplant is unverified.",
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.MethodTransformGenericMethodOrType,
                Plain(
                    "Generic methods and methods inside generic types cannot be safely patched with Harmony. "
                    + CompileCallToAction,
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.MethodTransformExplicitInterfaceImplementation,
                Plain("Explicit interface implementations are skipped.", 0));

            templates.Add(
                HotReloadWorkerReasonCode.AddedMethodVirtualOrAbstract,
                Plain(
                    "Added virtual, override, or abstract methods are skipped; the compiled type has no vtable slot. "
                    + CompileCallToActionToAddThem,
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.AddedMethodGeneric,
                Plain(
                    "Added generic methods are skipped; hot reload cannot emit a typed shim for them. "
                    + CompileCallToAction,
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.AddedMethodMethodGroupReference,
                Plain(
                    "Methods that capture an added method as a method group or delegate are skipped; "
                    + "the shim signature does not match. " + CompileCallToAction,
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.AddedMethodConditionalAccess,
                Plain(
                    "Added-method calls through conditional access are skipped; there is no rewrite shape. "
                    + CompileCallToAction,
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.AddedMethodUnavailableAddedCall,
                Plain("Calls an added method that hot reload cannot emit. " + CompileCallToAction, 0));
            templates.Add(
                HotReloadWorkerReasonCode.AddedMethodTypeNotIntroduced,
                Plain(
                    "Declared on a type that is not in the compiled assembly and was not introduced by this "
                    + "run. Run 'uloop compile'; when the file's introduced-type diagnostics name this type, "
                    + "that line gives the reason it was not introduced.",
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.AddedMethodInterfaceMember,
                Plain("Interface members are not patchable. " + CompileCallToAction, 0));
            templates.Add(
                HotReloadWorkerReasonCode.AddedMethodInaccessibleAccessNoRewrite,
                RequiringDetail(
                    "Added methods whose bodies access private/internal members are skipped when the access "
                    + "has no accessor rewrite (the added method JIT-compiles normally and fails accessibility "
                    + "checks).",
                    0,
                    AccessorRewriteUnavailableSeparator,
                    " " + CompileCallToAction));

            templates.Add(
                HotReloadWorkerReasonCode.AddedFieldStructHost,
                Plain(
                    "Added fields on struct types are skipped; the store requires a reference-type instance. "
                    + CompileCallToActionToAddThem,
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.AddedFieldInitializerNotLiteralOrExternalStatic,
                Plain(
                    "Added field initializer is not a literal or an externally visible static member "
                    + "(object creation other than an introduced type, and instance, host-type, or same-file "
                    + "added members cannot run in the shim lambda). Drop the initializer and assign the field inside the patched method "
                    + "instead - an added field without an initializer applies without compiling and starts "
                    + "at default(T); for a reference type, guard the assignment with "
                    + "'if (_field == null) { _field = ...; }' ('??=' is not rewritable). "
                    + "Or run 'uloop compile'.",
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.AddedFieldFieldTypeNotExternallyVisible,
                Plain("Added field type is not visible to the shim assembly. " + CompileCallToAction, 0));
            templates.Add(
                HotReloadWorkerReasonCode.AddedFieldFieldTypeUnresolved,
                Plain(
                    "Added field type '{0}' could not be resolved; check for a missing using directive or a typo, fix the declaration, and rerun. Run 'uloop compile' if the type is new.",
                    1));
            templates.Add(
                HotReloadWorkerReasonCode.AddedFieldIncrementNotNumeric,
                Plain(
                    "Increment or decrement of an added field is skipped unless the type is a numeric "
                    + "primitive or enum. " + CompileCallToAction,
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.AddedFieldRefOutIn,
                Plain("Added fields cannot be passed by ref, out, or in. " + CompileCallToAction, 0));
            templates.Add(
                HotReloadWorkerReasonCode.AddedFieldConsumedWrite,
                Plain(
                    "The value of an assignment to an added field is consumed; the store write returns void. "
                    + CompileCallToAction,
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.AddedFieldDoubleEvalReceiver,
                Plain(
                    "Assignment to an added field would evaluate a receiver with possible side effects twice. "
                    + CompileCallToAction,
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.AddedFieldValueTypeMemberWrite,
                Plain(
                    "Writes to members of an added value-type field, and instance method calls on that field, "
                    + "cannot be rewritten. " + CompileCallToAction,
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.AddedFieldUnavailableAddedField,
                Plain("Uses an added field that hot reload cannot emit. " + CompileCallToAction, 0));
            templates.Add(
                HotReloadWorkerReasonCode.AddedFieldFieldTypeChanged,
                Plain(
                    "Field '{0}' has a different type in the compiled assembly. " + CompileCallToAction,
                    1));
            templates.Add(
                HotReloadWorkerReasonCode.AddedFieldFieldModifiersChanged,
                Plain(
                    "Field '{0}' changed its static or const modifier in the compiled assembly. "
                    + CompileCallToAction,
                    1));
            templates.Add(
                HotReloadWorkerReasonCode.AddedFieldMemberKindChanged,
                Plain(
                    "Field '{0}' is declared as a property or an event in the compiled assembly. "
                    + CompileCallToAction,
                    1));

            templates.Add(
                HotReloadWorkerReasonCode.AddedPropertySetOnly,
                Plain(
                    "Added properties with only a setter are skipped; the shim requires a getter identity. "
                    + CompileCallToActionToAddThem,
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.AddedPropertyVirtualOrAbstract,
                Plain(
                    "Added virtual, override, abstract, or interface properties are skipped; the compiled type has no vtable slot. "
                    + CompileCallToActionToAddThem,
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.AddedPropertyExplicitInterface,
                Plain(
                    "Added explicit interface properties are skipped; the compiled type has no interface member slot. "
                    + CompileCallToActionToAddThem,
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.AddedPropertyInitAccessor,
                Plain(
                    "Added properties with init accessors are skipped; the shim cannot preserve initialization-only assignment. "
                    + CompileCallToActionToAddThem,
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.AddedPropertyPropertyPattern,
                Plain(
                    "Property patterns that match an added property are skipped; a pattern member name cannot "
                    + "be replaced by an accessor shim call. " + CompileCallToActionToAddIt,
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.AddedPropertyGenericHostType,
                Plain(
                    "Added properties on generic types are skipped; one accessor identity and one store entry "
                    + "cannot stand for every closed instantiation. " + CompileCallToActionToAddThem,
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.AddedPropertyStructHost,
                Plain(
                    "Added properties on struct types are skipped; the shim requires a reference-type instance. "
                    + CompileCallToActionToAddThem,
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.AddedPropertyValueTypeUnresolved,
                Plain(
                    "Added property type '{0}' could not be resolved; check for a missing using directive or a typo, "
                    + "fix the declaration, and rerun. Run 'uloop compile' if the type is new.",
                    1));
            templates.Add(
                HotReloadWorkerReasonCode.AddedPropertyValueTypeNotExternallyVisible,
                Plain(
                    "Added property type is not visible to the shim assembly. " + CompileCallToActionToAddIt,
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.AddedPropertyCompoundAssignment,
                Plain(
                    "Compound assignment, increment, and decrement of an added property are skipped; the accessor shim cannot preserve the operation. "
                    + CompileCallToActionToAddIt,
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.AddedPropertyConsumedWrite,
                Plain(
                    "The value of an assignment to an added property is consumed; the setter shim returns void. "
                    + CompileCallToActionToAddIt,
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.AddedPropertyNameofReference,
                Plain(
                    "References to added properties inside nameof are skipped; the member does not exist in the compiled assembly. "
                    + CompileCallToActionToAddIt,
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.AddedPropertyObjectInitializer,
                Plain(
                    "Object initializers that assign added properties are skipped; the setter shim cannot rewrite the initializer. "
                    + CompileCallToActionToAddIt,
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.AddedPropertyDeconstructionTarget,
                Plain(
                    "Deconstruction assignment to an added property is skipped; the setter shim cannot stand as a "
                    + "deconstruction target. " + CompileCallToActionToAddIt,
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.AddedPropertyConditionalAccess,
                Plain(
                    "Conditional access to added properties is skipped; there is no rewrite shape. "
                    + CompileCallToActionToAddIt,
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.AddedPropertyRefOutIn,
                Plain(
                    "Added properties cannot be passed by ref, out, or in. " + CompileCallToActionToAddThem,
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.AddedPropertyUnavailableAddedProperty,
                Plain("Uses an added property that hot reload cannot emit. " + CompileCallToAction, 0));
            templates.Add(
                HotReloadWorkerReasonCode.AddedPropertyCompiledMemberKindChanged,
                Plain(
                    "Property '{0}' is declared as a field or an event in the compiled assembly. "
                    + CompileCallToAction,
                    1));
            templates.Add(
                HotReloadWorkerReasonCode.AddedPropertyInitializerNotEmittable,
                Plain(
                    "Added property initializer cannot run in the shim lambda. " + CompileCallToActionToAddIt,
                    0));

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
                Plain("inaccessible property increment/decrement has no accessor rewrite shape.", 0));
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
                HotReloadWorkerReasonCode.AccessorStaticPropertyNoShape,
                Plain("inaccessible static property access has no accessor rewrite shape.", 0));
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
                Plain("inaccessible method calls with ref/out/in parameters are not rewritten.", 0));
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
                Plain("inaccessible method group (non-invocation) has no accessor rewrite shape.", 0));

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
            templates.Add(
                HotReloadWorkerReasonCode.IntroducedTypeChanged,
                Plain("Changed introduced type requires a compile: {0}", 1));

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

            return templates;
        }

        private static ReasonTemplate Plain(string text, int placeholderCount)
        {
            return new ReasonTemplate(text, placeholderCount, false, false, string.Empty, string.Empty);
        }

        // A reason that reads on its own but appends a detail when it has one.
        private static ReasonTemplate Composing(
            string text,
            int placeholderCount,
            string detailSeparator,
            string detailSuffix)
        {
            return new ReasonTemplate(text, placeholderCount, true, false, detailSeparator, detailSuffix);
        }

        // A reason that is incomplete without its detail.
        private static ReasonTemplate RequiringDetail(
            string text,
            int placeholderCount,
            string detailSeparator,
            string detailSuffix)
        {
            return new ReasonTemplate(text, placeholderCount, true, true, detailSeparator, detailSuffix);
        }

        /// <summary>
        /// One reason's sentence and the shape of reason it words.
        /// </summary>
        private sealed class ReasonTemplate
        {
            internal ReasonTemplate(
                string text,
                int placeholderCount,
                bool allowsDetail,
                bool requiresDetail,
                string detailSeparator,
                string detailSuffix)
            {
                Text = text;
                PlaceholderCount = placeholderCount;
                AllowsDetail = allowsDetail;
                RequiresDetail = requiresDetail;
                DetailSeparator = detailSeparator;
                DetailSuffix = detailSuffix;
            }

            internal string Text { get; }

            internal int PlaceholderCount { get; }

            internal bool AllowsDetail { get; }

            internal bool RequiresDetail { get; }

            internal string DetailSeparator { get; }

            internal string DetailSuffix { get; }
        }
    }
}
