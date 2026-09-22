using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    internal static partial class HotReloadWorkerReasonText
    {
        /// <summary>
        /// Adds the reasons an added method, field, or property could not be emitted.
        /// </summary>
        private static void AddAddedMemberTemplates(
            Dictionary<HotReloadWorkerReasonCode, ReasonTemplate> templates)
        {
            templates.Add(
                HotReloadWorkerReasonCode.AddedMethodVirtualOrAbstract,
                Plain(
                    "Added virtual, override, or abstract methods are skipped; the loaded type has no vtable slot.",
                    0).EndingWith(CompileCallToActionToAddTheMethod));
            templates.Add(
                HotReloadWorkerReasonCode.AddedMethodGeneric,
                Plain(
                    "Added generic methods are skipped; hot reload cannot emit a typed shim for them.",
                    0).EndingWith(CompileCallToAction));
            templates.Add(
                HotReloadWorkerReasonCode.AddedMethodMethodGroupReference,
                Plain(
                    "Methods that capture an added method as a method group or delegate are skipped; "
                    + "the shim signature does not match.",
                    0).EndingWith(CompileCallToAction));
            templates.Add(
                HotReloadWorkerReasonCode.AddedMethodConditionalAccess,
                Plain(
                    "Added-method calls through conditional access are skipped; there is no rewrite shape.",
                    0).EndingWith(CompileCallToAction));
            templates.Add(
                HotReloadWorkerReasonCode.AddedMethodUnavailableAddedCall,
                // Why point at the callee's row instead of a compile: the callee was skipped in
                // this same response, and its row names a fix that often needs no compile (such as
                // passing a new file to --files). Repeating a bare compile here hid that fix.
                Plain(
                    "Calls the added method '{0}', which this reload skipped; the Skipped row for that "
                    + "member names the fix. Apply it and rerun; run 'uloop compile' only if that row "
                    + "asks for it.",
                    1));
            templates.Add(
                HotReloadWorkerReasonCode.AddedMethodTypeNotIntroduced,
                Plain(
                    "Declared on a type that is not in the compiled assembly and was not introduced by this "
                    + "run. Run 'uloop compile'; when the file's introduced-type diagnostics name this type, "
                    + "that line gives the reason it was not introduced.",
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.AddedMethodInterfaceMember,
                Plain("Interface members are not patchable.", 0).EndingWith(CompileCallToAction));
            templates.Add(
                HotReloadWorkerReasonCode.AddedMethodInaccessibleAccessNoRewrite,
                RequiringDetail(
                    "Added methods whose bodies access private/internal members are skipped when the access "
                    + "has no accessor rewrite (the added method JIT-compiles normally and fails accessibility "
                    + "checks).",
                    0,
                    AccessorRewriteUnavailableSeparator,
                    // Why "to keep the code as written": several fragments end with a no-compile
                    // rewrite, and a bare compile call after it reads as the row asking for a compile.
                    " " + CompileCallToActionToKeepTheCode));
            templates.Add(
                HotReloadWorkerReasonCode.AddedMethodBodyUnbound,
                Plain(
                    "The added member's body could not be fully bound in the hot-reload compilation ({0}); "
                    + "hot reload cannot verify a member it cannot bind, so it is skipped. If the name is "
                    + "declared in a new file, pass that file to --files too (new files are not selected "
                    + "automatically); run 'uloop compile' only if it still does not bind.",
                    1));
            templates.Add(
                HotReloadWorkerReasonCode.AddedMethodBodyBindsCompiledSignature,
                Plain(
                    "The added member's body could not be fully bound in the hot-reload compilation ({0}): "
                    + "this reload declares {1} from source, while the compiled signatures of {2} still name "
                    + "the compiled {1}, so it is skipped. Pass {3} to this reload as well so both bind to "
                    + "the same type. Otherwise run 'uloop compile'.",
                    4));
            // Why no --files advice: the file declaring the compiled type is carried into every
            // reload that keeps the introduced type's binding, so dropping it does not help and
            // passing it is what already happened.
            templates.Add(
                HotReloadWorkerReasonCode.AddedMethodCallsIntroducedMemberBoundToCompiledType,
                Plain(
                    "The added member's body could not be fully bound in the hot-reload compilation ({0}): "
                    + "the members of the introduced type {1} were bound to the compiled {2} when that type "
                    + "was introduced, while this reload builds {2} from source ({3}, passed or carried in to "
                    + "keep an earlier reload's binding), so the {2} this body uses no longer matches and it "
                    + "is skipped. Run 'uloop compile'; changing --files does not avoid this, because {3} is "
                    + "carried in again.",
                    4));

            templates.Add(
                HotReloadWorkerReasonCode.AddedFieldStructHost,
                Plain(
                    "Added fields on struct types are skipped; the store requires a reference-type instance.",
                    0).EndingWith(CompileCallToActionToAddTheField));
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
                Plain("Added field type is not visible to the shim assembly.", 0).EndingWith(CompileCallToAction));
            templates.Add(
                HotReloadWorkerReasonCode.AddedFieldFieldTypeUnresolved,
                Plain(
                    "Added field type '{0}' could not be resolved; check for a missing using directive or a typo, fix the declaration, and rerun. If the type is declared in a new file, pass that file to --files too (new files are not selected automatically); run 'uloop compile' only if it still does not resolve.",
                    1));
            templates.Add(
                HotReloadWorkerReasonCode.AddedFieldIncrementNotNumeric,
                Plain(
                    "Increment or decrement of an added field is skipped unless the type is a numeric "
                    + "primitive or enum.",
                    0).EndingWith(CompileCallToAction));
            templates.Add(
                HotReloadWorkerReasonCode.AddedFieldRefOutIn,
                Plain("Added fields cannot be passed by ref, out, or in.", 0).EndingWith(CompileCallToAction));
            templates.Add(
                HotReloadWorkerReasonCode.AddedFieldConsumedWrite,
                Plain(
                    "The value of an assignment to an added field is consumed; the store write returns void.",
                    0).EndingWith(CompileCallToAction));
            templates.Add(
                HotReloadWorkerReasonCode.AddedFieldDoubleEvalReceiver,
                Plain(
                    "Assignment to an added field would evaluate a receiver with possible side effects twice.",
                    0).EndingWith(CompileCallToAction));
            templates.Add(
                HotReloadWorkerReasonCode.AddedFieldValueTypeMemberWrite,
                Plain(
                    "Writes to members of an added value-type field, and instance method calls on that field, "
                    + "cannot be rewritten. Copy the field into a local, change the local, and assign the "
                    + "whole value back.",
                    0).EndingWith(CompileCallToAction));
            templates.Add(
                HotReloadWorkerReasonCode.AddedFieldUnavailableAddedField,
                Plain("Uses added field '{0}' in a way hot reload cannot emit.", 1).EndingWith(CompileCallToAction));
            templates.Add(
                HotReloadWorkerReasonCode.AddedFieldCoalesceAssignment,
                Plain(
                    "'??=' on added field '{0}' cannot be rewritten. "
                    + "Write 'if ({0} == null) { {0} = ...; }' instead, or run 'uloop compile'.",
                    1));
            templates.Add(
                HotReloadWorkerReasonCode.AddedFieldFieldTypeChanged,
                Plain(
                    "Field '{0}' has a different type in the compiled assembly.",
                    1).EndingWith(CompileCallToAction));
            templates.Add(
                HotReloadWorkerReasonCode.AddedFieldFieldModifiersChanged,
                Plain(
                    "Field '{0}' changed its static or const modifier in the compiled assembly.",
                    1).EndingWith(CompileCallToAction));
            templates.Add(
                HotReloadWorkerReasonCode.AddedFieldMemberKindChanged,
                Plain(
                    "Field '{0}' is declared as a property or an event in the compiled assembly.",
                    1).EndingWith(CompileCallToAction));

            templates.Add(
                HotReloadWorkerReasonCode.AddedPropertySetOnly,
                Plain(
                    "Added properties with only a setter are skipped; the shim requires a getter identity.",
                    0).EndingWith(CompileCallToActionToAddTheProperty));
            templates.Add(
                HotReloadWorkerReasonCode.AddedPropertyVirtualOrAbstract,
                Plain(
                    "Added virtual, override, abstract, or interface properties are skipped; the loaded type has no vtable slot.",
                    0).EndingWith(CompileCallToActionToAddTheProperty));
            templates.Add(
                HotReloadWorkerReasonCode.AddedPropertyExplicitInterface,
                Plain(
                    "Added explicit interface properties are skipped; the compiled type has no interface member slot.",
                    0).EndingWith(CompileCallToActionToAddTheProperty));
            templates.Add(
                HotReloadWorkerReasonCode.AddedPropertyInitAccessor,
                Plain(
                    "Added properties with init accessors are skipped; the shim cannot preserve initialization-only assignment.",
                    0).EndingWith(CompileCallToActionToAddTheProperty));
            templates.Add(
                HotReloadWorkerReasonCode.AddedPropertyPropertyPattern,
                Plain(
                    "Property patterns that match an added property are skipped; a pattern member name cannot "
                    + "be replaced by an accessor shim call.",
                    0).EndingWith(CompileCallToActionToAddTheProperty));
            templates.Add(
                HotReloadWorkerReasonCode.AddedPropertyGenericHostType,
                Plain(
                    "Added properties on generic types are skipped; one accessor identity and one store entry "
                    + "cannot stand for every closed instantiation.",
                    0).EndingWith(CompileCallToActionToAddTheProperty));
            templates.Add(
                HotReloadWorkerReasonCode.AddedPropertyStructHost,
                Plain(
                    "Added properties on struct types are skipped; the shim requires a reference-type instance.",
                    0).EndingWith(CompileCallToActionToAddTheProperty));
            templates.Add(
                HotReloadWorkerReasonCode.AddedPropertyValueTypeUnresolved,
                Plain(
                    "Added property type '{0}' could not be resolved; check for a missing using directive or a typo, "
                    + "fix the declaration, and rerun. If the type is declared in a new file, pass that file to --files too (new files are not selected automatically); run 'uloop compile' only if it still does not resolve.",
                    1));
            templates.Add(
                HotReloadWorkerReasonCode.AddedPropertyValueTypeNotExternallyVisible,
                Plain(
                    "Added property type is not visible to the shim assembly.",
                    0).EndingWith(CompileCallToActionToAddTheProperty));
            templates.Add(
                HotReloadWorkerReasonCode.AddedPropertyCompoundAssignment,
                Plain(
                    "Compound assignment, increment, and decrement of an added property are skipped; the accessor shim cannot preserve the operation. "
                    + "Rewrite it as a plain assignment statement ('X = X + 1;') to keep hot reloading.",
                    0).EndingWith(CompileCallToActionToKeepTheCode));
            templates.Add(
                HotReloadWorkerReasonCode.AddedPropertyConsumedWrite,
                Plain(
                    "The value of an assignment to an added property is consumed; the setter shim returns void.",
                    0).EndingWith(CompileCallToActionToAddTheProperty));
            templates.Add(
                HotReloadWorkerReasonCode.AddedPropertyNameofReference,
                Plain(
                    "References to added properties inside nameof are skipped; the member does not exist in the compiled assembly.",
                    0).EndingWith(CompileCallToActionToAddTheProperty));
            templates.Add(
                HotReloadWorkerReasonCode.AddedPropertyObjectInitializer,
                Plain(
                    "Object initializers that assign added properties are skipped; the setter shim cannot rewrite the initializer.",
                    0).EndingWith(CompileCallToActionToAddTheProperty));
            templates.Add(
                HotReloadWorkerReasonCode.AddedPropertyDeconstructionTarget,
                Plain(
                    "Deconstruction assignment to an added property is skipped; the setter shim cannot stand as a "
                    + "deconstruction target.",
                    0).EndingWith(CompileCallToActionToAddTheProperty));
            templates.Add(
                HotReloadWorkerReasonCode.AddedPropertyConditionalAccess,
                Plain(
                    "Conditional access to added properties is skipped; there is no rewrite shape.",
                    0).EndingWith(CompileCallToActionToAddTheProperty));
            templates.Add(
                HotReloadWorkerReasonCode.AddedPropertyRefOutIn,
                Plain(
                    "Added properties cannot be passed by ref, out, or in.",
                    0).EndingWith(CompileCallToActionToAddTheProperty));
            templates.Add(
                HotReloadWorkerReasonCode.AddedPropertyUnavailableAddedProperty,
                Composing(
                    "Uses an added property that hot reload cannot emit.",
                    0,
                    " The property body was refused because: ",
                    "").EndingWith(CompileCallToAction));
            templates.Add(
                HotReloadWorkerReasonCode.AddedPropertyAccessorExcludedFromReload,
                // Why no single row is named: an accessor is left out when its own shim failed to
                // compile, when it calls one that did, or when its file was dropped, and the
                // worker cannot tell those apart. Each of them reports the accessor on its own row.
                Plain(
                    "Added property '{0}' is left out of this reload because one of its accessors was. "
                    + "The Failed or Skipped row that names that accessor gives the reason; fix it and "
                    + "rerun.",
                    1));
            templates.Add(
                HotReloadWorkerReasonCode.AddedPropertyCompiledMemberKindChanged,
                Plain(
                    "Property '{0}' is declared as a field or an event in the compiled assembly.",
                    1).EndingWith(CompileCallToAction));
            templates.Add(
                HotReloadWorkerReasonCode.AddedPropertyInitializerNotEmittable,
                Plain(
                    "Added property initializer cannot run in the shim lambda.",
                    0).EndingWith(CompileCallToActionToAddTheProperty));
        }
    }
}
