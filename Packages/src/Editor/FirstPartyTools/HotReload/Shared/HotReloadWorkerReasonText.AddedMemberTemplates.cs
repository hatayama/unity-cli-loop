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
                HotReloadWorkerReasonCode.AddedMethodBodyUnbound,
                Plain(
                    "The added member's body could not be fully bound in the hot-reload compilation ({0}); "
                    + "hot reload cannot verify a member it cannot bind, so it is skipped. " + CompileCallToAction,
                    1));

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
                    + "cannot be rewritten. Copy the field into a local, change the local, and assign the "
                    + "whole value back. " + CompileCallToAction,
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.AddedFieldUnavailableAddedField,
                Plain("Uses added field '{0}' in a way hot reload cannot emit. " + CompileCallToAction, 1));
            templates.Add(
                HotReloadWorkerReasonCode.AddedFieldCoalesceAssignment,
                Plain(
                    "'??=' on added field '{0}' cannot be rewritten. "
                    + "Write 'if ({0} == null) { {0} = ...; }' instead, or run 'uloop compile'.",
                    1));
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
                Composing(
                    "Uses an added property that hot reload cannot emit. " + CompileCallToAction,
                    0,
                    " The property body was refused because: ",
                    ""));
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
        }
    }
}
