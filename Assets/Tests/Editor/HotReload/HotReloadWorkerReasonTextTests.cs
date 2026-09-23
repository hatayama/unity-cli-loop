using System;
using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for the one place that turns a worker reason code into English. The
    /// expected sentences are literal copies of what the worker used to send over the wire, so a
    /// failure here means a response string changed for the user.
    /// </summary>
    public class HotReloadWorkerReasonTextTests
    {
        // The fragment used as a detail below; its own sentence is pinned by its own case.
        private static readonly string[] NoArgs = new string[0];

        private const string GenericMethodFragment = "inaccessible generic method calls are not rewritten.";

        /// <summary>
        /// What: every reason code has a byte-match case below, so a code added without a
        /// sentence - or a case left behind after a code was removed - fails here.
        /// </summary>
        [Test]
        public void RenderCases_CoverEveryReasonCode()
        {
            HashSet<string> declared = new HashSet<string>(
                Enum.GetNames(typeof(HotReloadWorkerReasonCode)),
                StringComparer.Ordinal);
            HashSet<string> covered = new HashSet<string>(StringComparer.Ordinal);
            foreach (TestCaseData testCase in RenderCases())
            {
                covered.Add((string)testCase.Arguments[0]);
            }

            List<string> missing = new List<string>(declared);
            missing.RemoveAll(covered.Contains);
            missing.Sort(StringComparer.Ordinal);
            List<string> unknown = new List<string>(covered);
            unknown.RemoveAll(declared.Contains);
            unknown.Sort(StringComparer.Ordinal);

            Assert.That(
                missing,
                Is.Empty,
                "reason codes with no expected sentence: " + string.Join(", ", missing));
            Assert.That(
                unknown,
                Is.Empty,
                "expected sentences for codes that no longer exist: " + string.Join(", ", unknown));
        }

        /// <summary>
        /// What: each reason code renders the exact sentence the worker used to send.
        /// </summary>
        [TestCaseSource(nameof(RenderCases))]
        public void Render_ReasonCode_ProducesTheWireSentence(
            string codeName,
            string[] args,
            string detailCodeName,
            string[] detailArgs,
            string expected)
        {
            TransformWorkerReasonDto reason = BuildReason(codeName, args);
            reason.detail = detailCodeName == null ? null : BuildReason(detailCodeName, detailArgs);

            Assert.That(HotReloadWorkerReasonText.Render(reason), Is.EqualTo(expected));
        }

        // The code is passed by name because the enum is internal to the package and a public
        // test signature cannot name it.
        private static TransformWorkerReasonDto BuildReason(string codeName, string[] args)
        {
            return new TransformWorkerReasonDto
            {
                code = (HotReloadWorkerReasonCode)Enum.Parse(typeof(HotReloadWorkerReasonCode), codeName),
                args = args
            };
        }

        /// <summary>
        /// What: rendering a null reason fails fast instead of producing an empty sentence.
        /// </summary>
        [Test]
        public void Render_NullReason_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => HotReloadWorkerReasonText.Render(null));
        }

        /// <summary>
        /// What: a reason whose argument count does not match its sentence fails fast.
        /// </summary>
        [Test]
        public void Render_ArgumentCountMismatch_Throws()
        {
            TransformWorkerReasonDto reason = new TransformWorkerReasonDto
            {
                code = HotReloadWorkerReasonCode.IntroducedTypeGeneric,
                args = new[] { "A0", "A1" }
            };

            Assert.Throws<ArgumentException>(() => HotReloadWorkerReasonText.Render(reason));
        }

        /// <summary>
        /// What: a detail attached to a reason that does not compose fails fast rather than
        /// being appended to a sentence that has no place for it.
        /// </summary>
        [Test]
        public void Render_DetailOnANonComposingCode_Throws()
        {
            TransformWorkerReasonDto reason = new TransformWorkerReasonDto
            {
                code = HotReloadWorkerReasonCode.IntroducedTypeGeneric,
                args = new[] { "Example.Type" },
                detail = new TransformWorkerReasonDto
                {
                    code = HotReloadWorkerReasonCode.IntroducedTypeSymbolUnresolved
                }
            };

            Assert.Throws<ArgumentException>(() => HotReloadWorkerReasonText.Render(reason));
        }

        /// <summary>
        /// What: a reason that only ever appears with a detail fails fast when it arrives alone,
        /// because its sentence would otherwise end in the middle.
        /// </summary>
        [Test]
        public void Render_MissingRequiredDetail_Throws()
        {
            TransformWorkerReasonDto reason = new TransformWorkerReasonDto
            {
                code = HotReloadWorkerReasonCode.EventAccessorRewriteUnavailable
            };

            Assert.Throws<ArgumentException>(() => HotReloadWorkerReasonText.Render(reason));
        }

        /// <summary>
        /// What: the added-method variant of the detail-required contract fails fast on its own,
        /// so weakening only that template is caught instead of being covered by the event one.
        /// </summary>
        [Test]
        public void Render_AddedMethodInaccessibleAccessWithoutDetail_Throws()
        {
            TransformWorkerReasonDto reason = new TransformWorkerReasonDto
            {
                code = HotReloadWorkerReasonCode.AddedMethodInaccessibleAccessNoRewrite
            };

            Assert.Throws<ArgumentException>(() => HotReloadWorkerReasonText.Render(reason));
        }

        /// <summary>
        /// What: a sentence that takes no values renders the same whether args is null or empty.
        /// </summary>
        [Test]
        public void Render_NullArgs_IsTreatedAsNoArguments()
        {
            TransformWorkerReasonDto withNull = new TransformWorkerReasonDto
            {
                code = HotReloadWorkerReasonCode.IntroducedTypeSymbolUnresolved
            };
            TransformWorkerReasonDto withEmpty = new TransformWorkerReasonDto
            {
                code = HotReloadWorkerReasonCode.IntroducedTypeSymbolUnresolved,
                args = Array.Empty<string>()
            };

            Assert.That(
                HotReloadWorkerReasonText.Render(withNull),
                Is.EqualTo(HotReloadWorkerReasonText.Render(withEmpty)));
        }

        private static IEnumerable<TestCaseData> RenderCases()
        {
            yield return Case(
                HotReloadWorkerReasonCode.MethodTransformNoBody,
                NoArgs,
                "Methods without a body (abstract/extern) are skipped.");
            yield return Case(
                HotReloadWorkerReasonCode.MethodTransformBaseMemberCall,
                NoArgs,
                "Methods that call base. members are skipped; C# cannot express base calls outside the type.");
            yield return Case(
                HotReloadWorkerReasonCode.MethodTransformClosureInaccessibleAccess,
                NoArgs,
                "Lambda, local-function, or query-expression bodies that access private/internal members "
                + "are skipped (closure methods JIT-compile normally and fail accessibility checks).");
            yield return DetailCase(
                HotReloadWorkerReasonCode.MethodTransformClosureInaccessibleAccess,
                NoArgs,
                HotReloadWorkerReasonCode.AccessorGenericMethodNotRewritten,
                "Lambda, local-function, or query-expression bodies that access private/internal members "
                + "are skipped (closure methods JIT-compile normally and fail accessibility checks)."
                + " Accessor rewrite unavailable: " + GenericMethodFragment);
            yield return Case(
                HotReloadWorkerReasonCode.MethodTransformAsyncIteratorInaccessibleAccess,
                NoArgs,
                "Async or iterator methods whose bodies access private/internal members are skipped "
                + "(state-machine MoveNext JIT-compiles normally and fails accessibility checks).");
            yield return DetailCase(
                HotReloadWorkerReasonCode.MethodTransformAsyncIteratorInaccessibleAccess,
                NoArgs,
                HotReloadWorkerReasonCode.AccessorGenericMethodNotRewritten,
                "Async or iterator methods whose bodies access private/internal members are skipped "
                + "(state-machine MoveNext JIT-compiles normally and fails accessibility checks)."
                + " Accessor rewrite unavailable: " + GenericMethodFragment);
            yield return Case(
                HotReloadWorkerReasonCode.MethodTransformPartialType,
                NoArgs,
                "Partial types are skipped because a single file cannot provide a complete semantic model.");
            yield return Case(
                HotReloadWorkerReasonCode.MethodTransformStructHost,
                NoArgs,
                "Struct (value type) methods are skipped; byref instance transplant is unverified. "
                + "Leave the struct's methods as they are and put the new logic at the call site or in a "
                + "non-struct helper, or run 'uloop compile' to change the struct.");
            yield return Case(
                HotReloadWorkerReasonCode.MethodTransformGenericMethodOrType,
                NoArgs,
                "Generic methods and methods inside generic types cannot be safely patched with Harmony. "
                + "Run 'uloop compile'.");
            yield return Case(
                HotReloadWorkerReasonCode.MethodTransformExplicitInterfaceImplementation,
                NoArgs,
                "Explicit interface implementations are skipped.");
            yield return Case(
                HotReloadWorkerReasonCode.AddedMethodVirtualOrAbstract,
                NoArgs,
                "Added virtual, override, or abstract methods are skipped; the loaded type has no vtable slot. "
                + "Run 'uloop compile' to add the method.");
            yield return Case(
                HotReloadWorkerReasonCode.AddedMethodGeneric,
                NoArgs,
                "Added generic methods are skipped; hot reload cannot emit a typed shim for them. "
                + "Run 'uloop compile'.");
            yield return Case(
                HotReloadWorkerReasonCode.AddedMethodMethodGroupReference,
                NoArgs,
                "Methods that capture an added method as a method group or delegate are skipped; "
                + "the shim signature does not match. Run 'uloop compile'.");
            yield return Case(
                HotReloadWorkerReasonCode.AddedMethodConditionalAccess,
                NoArgs,
                "Added-method calls through conditional access are skipped; there is no rewrite shape. "
                + "Run 'uloop compile'.");
            yield return Case(
                HotReloadWorkerReasonCode.AddedMethodUnavailableAddedCall,
                new[] { "Outer.Inner.Ping()" },
                "Calls the added method 'Outer.Inner.Ping()', which this reload skipped; the Skipped row "
                + "for that member names the fix. Apply it and rerun; run 'uloop compile' only if that "
                + "row asks for it.");
            yield return Case(
                HotReloadWorkerReasonCode.AddedMethodTypeNotIntroduced,
                NoArgs,
                "Declared on a type that is not in the compiled assembly and was not introduced by this "
                + "run. Run 'uloop compile'; when the file's introduced-type diagnostics name this type, "
                + "that line gives the reason it was not introduced.");
            yield return Case(
                HotReloadWorkerReasonCode.AddedMethodInterfaceMember,
                NoArgs,
                "Interface members are not patchable. Run 'uloop compile'.");
            yield return DetailCase(
                HotReloadWorkerReasonCode.AddedMethodInaccessibleAccessNoRewrite,
                NoArgs,
                HotReloadWorkerReasonCode.AccessorGenericMethodNotRewritten,
                "Added methods whose bodies access private/internal members are skipped when the access "
                + "has no accessor rewrite (the added method JIT-compiles normally and fails accessibility "
                + "checks). Accessor rewrite unavailable: " + GenericMethodFragment
                + " Run 'uloop compile' to keep the code as written.");
            yield return Case(
                HotReloadWorkerReasonCode.AddedMethodNotForwardedUnityMessageNeedsCompile,
                new[] { "OnEnable" },
                "The added 'OnEnable' is a Unity message that hot reload does not forward (Awake, "
                + "OnEnable, OnDisable, OnDestroy, and the editor-only messages), so no rewrite of its "
                + "body would make the engine call it. Run 'uloop compile' to have Unity invoke it.");
            yield return Case(
                HotReloadWorkerReasonCode.AddedMethodBodyUnbound,
                new[] { "CS1503: Argument 1: cannot convert" },
                "The added member's body could not be fully bound in the hot-reload compilation "
                + "(CS1503: Argument 1: cannot convert); hot reload cannot verify a member it cannot bind, "
                + "so it is skipped. If the name is declared in a new file, pass that file to --files too "
                + "(new files are not selected automatically); run 'uloop compile' only if it still does not bind.");
            yield return Case(
                HotReloadWorkerReasonCode.AddedMethodBodyBindsCompiledSignature,
                new[] { "CS1503: Argument 1: cannot convert", "'Example.Payload'", "'Example.Registry'", "'Assets/Registry.cs'" },
                "The added member's body could not be fully bound in the hot-reload compilation "
                + "(CS1503: Argument 1: cannot convert): this reload declares 'Example.Payload' from source, "
                + "while the compiled signatures of 'Example.Registry', declared in 'Assets/Registry.cs', "
                + "still name the compiled 'Example.Payload', so it is skipped.");
            yield return Case(
                HotReloadWorkerReasonCode.AddedMethodCallsIntroducedMemberBoundToCompiledType,
                new[] { "CS1503: Argument 1: cannot convert", "'Example.Sink'", "'Example.Payload'", "'Assets/Payload.cs'" },
                "The added member's body could not be fully bound in the hot-reload compilation "
                + "(CS1503: Argument 1: cannot convert): the members of the introduced type 'Example.Sink' were "
                + "bound to the compiled 'Example.Payload' when that type was introduced, while this reload builds "
                + "'Example.Payload' from source ('Assets/Payload.cs'), so the 'Example.Payload' this body uses "
                + "no longer matches and it is skipped.");
            yield return Case(
                HotReloadWorkerReasonCode.AddedFieldStructHost,
                NoArgs,
                "Added fields on struct types are skipped; the store requires a reference-type instance. "
                + "Run 'uloop compile' to add the field.");
            yield return Case(
                HotReloadWorkerReasonCode.AddedFieldInitializerNotLiteralOrExternalStatic,
                new[] { "_cache" },
                "Added field '_cache' has an initializer that is not a literal or an externally visible static member "
                + "(object creation other than an introduced type, and instance, host-type, or same-file "
                + "added members cannot run in the shim lambda). Drop the initializer and assign the field inside the patched method "
                + "instead - an added field without an initializer applies without compiling and starts "
                + "at default(T); for a reference type, guard the assignment with "
                + "'if (_field == null) { _field = ...; }' ('??=' is not rewritable). "
                + "Or run 'uloop compile'.");
            yield return Case(
                HotReloadWorkerReasonCode.AddedFieldFieldTypeNotExternallyVisible,
                NoArgs,
                "Added field type is not visible to the shim assembly. Run 'uloop compile'.");
            yield return Case(
                HotReloadWorkerReasonCode.AddedFieldFieldTypeUnresolved,
                new[] { "Missing" },
                "Added field type 'Missing' could not be resolved; check for a missing using directive or a typo, fix the declaration, and rerun. If the type is declared in a new file, pass that file to --files too (new files are not selected automatically); run 'uloop compile' only if it still does not resolve.");
            yield return Case(
                HotReloadWorkerReasonCode.AddedFieldIncrementNotNumeric,
                NoArgs,
                "Increment or decrement of an added field is skipped unless the type is a numeric "
                + "primitive or enum. Run 'uloop compile'.");
            yield return Case(
                HotReloadWorkerReasonCode.AddedFieldRefOutIn,
                NoArgs,
                "Added fields cannot be passed by ref, out, or in. Run 'uloop compile'.");
            yield return Case(
                HotReloadWorkerReasonCode.AddedFieldConsumedWrite,
                NoArgs,
                "The value of an assignment to an added field is consumed; the store write returns void. "
                + "Run 'uloop compile'.");
            yield return Case(
                HotReloadWorkerReasonCode.AddedFieldDoubleEvalReceiver,
                NoArgs,
                "Assignment to an added field would evaluate a receiver with possible side effects twice. "
                + "Run 'uloop compile'.");
            yield return Case(
                HotReloadWorkerReasonCode.AddedFieldValueTypeMemberWrite,
                NoArgs,
                "Writes to members of an added value-type field, and instance method calls on that field, "
                + "cannot be rewritten. Copy the field into a local, change the local, and assign the whole "
                + "value back. Run 'uloop compile'.");
            yield return Case(
                HotReloadWorkerReasonCode.AddedFieldUnavailableAddedField,
                new[] { "_score" },
                "Uses added field '_score' in a way hot reload cannot emit. Run 'uloop compile'.");
            yield return Case(
                HotReloadWorkerReasonCode.AddedFieldCoalesceAssignment,
                new[] { "_score" },
                "'??=' on added field '_score' cannot be rewritten. "
                + "Write 'if (_score == null) { _score = ...; }' instead, or run 'uloop compile'.");
            yield return Case(
                HotReloadWorkerReasonCode.AddedFieldFieldTypeChanged,
                new[] { "_score" },
                "Field '_score' has a different type in the compiled assembly. Run 'uloop compile'.");
            yield return Case(
                HotReloadWorkerReasonCode.AddedFieldFieldModifiersChanged,
                new[] { "_score" },
                "Field '_score' changed its static or const modifier in the compiled assembly. Run 'uloop compile'.");
            yield return Case(
                HotReloadWorkerReasonCode.AddedFieldMemberKindChanged,
                new[] { "_score" },
                "Field '_score' is declared as a property or an event in the compiled assembly. Run 'uloop compile'.");
            yield return Case(
                HotReloadWorkerReasonCode.AddedPropertySetOnly,
                NoArgs,
                "Added properties with only a setter are skipped; the shim requires a getter identity. "
                + "Run 'uloop compile' to add the property.");
            yield return Case(
                HotReloadWorkerReasonCode.AddedPropertyVirtualOrAbstract,
                NoArgs,
                "Added virtual, override, abstract, or interface properties are skipped; the loaded type has no vtable slot. "
                + "Run 'uloop compile' to add the property.");
            yield return Case(
                HotReloadWorkerReasonCode.AddedPropertyExplicitInterface,
                NoArgs,
                "Added explicit interface properties are skipped; the compiled type has no interface member slot. "
                + "Run 'uloop compile' to add the property.");
            yield return Case(
                HotReloadWorkerReasonCode.AddedPropertyInitAccessor,
                NoArgs,
                "Added properties with init accessors are skipped; the shim cannot preserve initialization-only assignment. "
                + "Run 'uloop compile' to add the property.");
            yield return Case(
                HotReloadWorkerReasonCode.AddedPropertyPropertyPattern,
                NoArgs,
                "Property patterns that match an added property are skipped; a pattern member name cannot "
                + "be replaced by an accessor shim call. Run 'uloop compile' to add the property.");
            yield return Case(
                HotReloadWorkerReasonCode.AddedPropertyGenericHostType,
                NoArgs,
                "Added properties on generic types are skipped; one accessor identity and one store entry "
                + "cannot stand for every closed instantiation. Run 'uloop compile' to add the property.");
            yield return Case(
                HotReloadWorkerReasonCode.AddedPropertyStructHost,
                NoArgs,
                "Added properties on struct types are skipped; the shim requires a reference-type instance. "
                + "Run 'uloop compile' to add the property.");
            yield return Case(
                HotReloadWorkerReasonCode.AddedPropertyValueTypeUnresolved,
                new[] { "Missing" },
                "Added property type 'Missing' could not be resolved; check for a missing using directive or a typo, "
                + "fix the declaration, and rerun. If the type is declared in a new file, pass that file to --files too (new files are not selected automatically); run 'uloop compile' only if it still does not resolve.");
            yield return Case(
                HotReloadWorkerReasonCode.AddedPropertyValueTypeNotExternallyVisible,
                NoArgs,
                "Added property type is not visible to the shim assembly. Run 'uloop compile' to add the property.");
            yield return Case(
                HotReloadWorkerReasonCode.AddedPropertyCompoundAssignment,
                NoArgs,
                "Compound assignment, increment, and decrement of an added property are skipped; the accessor shim cannot preserve the operation. "
                + "Rewrite it as a plain assignment statement ('X = X + 1;') to keep hot reloading. "
                + "Run 'uloop compile' to keep the code as written.");
            yield return Case(
                HotReloadWorkerReasonCode.AddedPropertyConsumedWrite,
                NoArgs,
                "The value of an assignment to an added property is consumed; the setter shim returns void. "
                + "Run 'uloop compile' to add the property.");
            yield return Case(
                HotReloadWorkerReasonCode.AddedPropertyNameofReference,
                NoArgs,
                "References to added properties inside nameof are skipped; the member does not exist in the compiled assembly. "
                + "Run 'uloop compile' to add the property.");
            yield return Case(
                HotReloadWorkerReasonCode.AddedPropertyObjectInitializer,
                NoArgs,
                "Object initializers that assign added properties are skipped; the setter shim cannot rewrite the initializer. "
                + "Run 'uloop compile' to add the property.");
            yield return Case(
                HotReloadWorkerReasonCode.AddedPropertyDeconstructionTarget,
                NoArgs,
                "Deconstruction assignment to an added property is skipped; the setter shim cannot stand as a "
                + "deconstruction target. Run 'uloop compile' to add the property.");
            yield return Case(
                HotReloadWorkerReasonCode.AddedPropertyConditionalAccess,
                NoArgs,
                "Conditional access to added properties is skipped; there is no rewrite shape. "
                + "Run 'uloop compile' to add the property.");
            yield return Case(
                HotReloadWorkerReasonCode.AddedPropertyRefOutIn,
                NoArgs,
                "Added properties cannot be passed by ref, out, or in. Run 'uloop compile' to add the property.");
            yield return Case(
                HotReloadWorkerReasonCode.AddedPropertyUnavailableAddedProperty,
                NoArgs,
                "Uses an added property that hot reload cannot emit. Run 'uloop compile'.");
            yield return Case(
                HotReloadWorkerReasonCode.AddedPropertyAccessorExcludedFromReload,
                new[] { "Doubled" },
                "Added property 'Doubled' is left out of this reload because one of its accessors was. "
                + "The Failed or Skipped row that names that accessor gives the reason; fix it and rerun.");
            yield return Case(
                HotReloadWorkerReasonCode.AddedPropertyCompiledMemberKindChanged,
                new[] { "Score" },
                "Property 'Score' is declared as a field or an event in the compiled assembly. Run 'uloop compile'.");
            yield return Case(
                HotReloadWorkerReasonCode.AddedPropertyInitializerNotEmittable,
                NoArgs,
                "Added property initializer cannot run in the shim lambda. Run 'uloop compile' to add the property.");
            yield return Case(
                HotReloadWorkerReasonCode.UnsupportedMemberExplicitAccessor,
                NoArgs,
                "Property setter, init, or indexer accessors are skipped; "
                + "run 'uloop compile' to apply accessor edits.");
            yield return Case(
                HotReloadWorkerReasonCode.UnsupportedMemberUnsupportedKind,
                NoArgs,
                "Constructors, operators, and event accessors are skipped; "
                + "run 'uloop compile' to apply these edits.");
            yield return Case(
                HotReloadWorkerReasonCode.EventCustomAccessor,
                NoArgs,
                "Methods that raise or read an event with custom add/remove accessors are skipped; "
                + "there is no backing field for the shim to reach. Use uloop compile.");
            yield return Case(
                HotReloadWorkerReasonCode.EventNoBackingField,
                NoArgs,
                "Methods that raise or read an abstract, extern, or interface event are skipped; "
                + "there is no backing field for the shim to reach. Use uloop compile.");
            yield return Case(
                HotReloadWorkerReasonCode.EventDelegateTypeNotVisible,
                NoArgs,
                "Methods that raise or read an event whose delegate type is not visible from an external "
                + "assembly are skipped; the shim cannot name the accessor field type. Use uloop compile.");
            yield return Case(
                HotReloadWorkerReasonCode.EventAddedInThisEdit,
                NoArgs,
                "Methods that raise a field-like event added in this edit are skipped; "
                + "the compiled assembly has no backing field yet. Use uloop compile.");
            yield return Case(
                HotReloadWorkerReasonCode.EventSubscriptionToAddedEvent,
                new[] { "Ns.Publisher.Changed" },
                "Subscribes to the event 'Ns.Publisher.Changed', which this edit adds; the compiled "
                + "assembly has no such event yet, so the subscription cannot bind until 'uloop compile'.");
            yield return Case(
                HotReloadWorkerReasonCode.EventNameof,
                NoArgs,
                "Methods that name a field-like event inside nameof are skipped; the shim is a different "
                + "type and cannot keep the bare event name. Use uloop compile.");
            yield return Case(
                HotReloadWorkerReasonCode.EventConditionalReceiver,
                NoArgs,
                "Methods that raise or read a field-like event through a conditional receiver "
                + "('a?.E') are skipped; the shim cannot name the conditional receiver as the accessor "
                + "call's argument. Use uloop compile.");
            yield return Case(
                HotReloadWorkerReasonCode.EventPassedByRef,
                NoArgs,
                "Methods that pass a field-like event by ref/out/in are skipped; the shim reads the "
                + "event through an accessor call, which cannot be passed by reference. Use uloop compile.");
            yield return DetailCase(
                HotReloadWorkerReasonCode.EventAccessorRewriteUnavailable,
                NoArgs,
                HotReloadWorkerReasonCode.AccessorGenericMethodNotRewritten,
                "Methods that raise or read a field-like event are skipped when the body cannot be "
                + "rewritten into accessor delegates. Accessor rewrite unavailable: " + GenericMethodFragment);
            yield return Case(
                HotReloadWorkerReasonCode.AccessorContainingTypeNotVisible,
                NoArgs,
                "containing type is not visible from an external assembly (condition c).");
            yield return Case(
                HotReloadWorkerReasonCode.AccessorSignatureTypeNotVisible,
                new[] { "global::Example.Hidden" },
                "accessor signature type is not visible from an external assembly: global::Example.Hidden (condition c).");
            yield return Case(
                HotReloadWorkerReasonCode.AccessorPropertyIncrementNoShape,
                NoArgs,
                "inaccessible property increment/decrement has no accessor rewrite shape; write it as a "
                + "statement 'X += 1' or 'X = X + 1', which the accessor rewrite handles.");
            yield return Case(
                HotReloadWorkerReasonCode.AccessorMethodReturnTypeUnresolved,
                new[] { "Missing" },
                "method return type 'Missing' could not be resolved (missing using directive, typo, or a type that is not compiled yet).");
            yield return Case(
                HotReloadWorkerReasonCode.AccessorMethodParameterTypeUnresolved,
                new[] { "Missing" },
                "method parameter type 'Missing' could not be resolved (missing using directive, typo, or a type that is not compiled yet).");
            yield return Case(
                HotReloadWorkerReasonCode.AccessorMethodReturnTypeNotVisible,
                NoArgs,
                "method return type is not visible from an external assembly (condition c).");
            yield return Case(
                HotReloadWorkerReasonCode.AccessorMethodParameterTypeNotVisible,
                NoArgs,
                "method parameter type is not visible from an external assembly (condition c).");
            yield return Case(
                HotReloadWorkerReasonCode.AccessorBodyTypeNotVisible,
                new[] { "global::Example.Hidden" },
                "body uses a type that is not visible from an external assembly: global::Example.Hidden (condition c).");
            yield return Case(
                HotReloadWorkerReasonCode.AccessorIndexerNoShape,
                NoArgs,
                "inaccessible indexer access has no accessor rewrite shape.");
            yield return Case(
                HotReloadWorkerReasonCode.AccessorEventCompoundAssignmentNoShape,
                NoArgs,
                "compound assignment to an event has no accessor rewrite shape.");
            yield return Case(
                HotReloadWorkerReasonCode.AccessorExtensionMethodNotRewritten,
                NoArgs,
                "inaccessible extension method calls are not rewritten.");
            yield return Case(
                HotReloadWorkerReasonCode.AccessorGenericMethodNotRewritten,
                NoArgs,
                "inaccessible generic method calls are not rewritten.");
            yield return Case(
                HotReloadWorkerReasonCode.AccessorMemberKindUnsupported,
                NoArgs,
                "inaccessible member kind is not field/method/property access.");
            yield return Case(
                HotReloadWorkerReasonCode.AccessorPropertyNoSetter,
                NoArgs,
                "inaccessible property has no setter to bind.");
            yield return Case(
                HotReloadWorkerReasonCode.AccessorPropertyNoGetter,
                NoArgs,
                "inaccessible property has no getter to bind.");
            yield return Case(
                HotReloadWorkerReasonCode.AccessorCoalesceAssignmentNoShape,
                NoArgs,
                "null-coalescing assignment writes conditionally and has no accessor rewrite shape.");
            yield return Case(
                HotReloadWorkerReasonCode.AccessorCompoundAssignmentKindUnsupported,
                NoArgs,
                "unsupported compound assignment kind has no accessor rewrite shape.");
            yield return Case(
                HotReloadWorkerReasonCode.AccessorCompoundInaccessibleGetterNoShape,
                NoArgs,
                "compound assignment reading an inaccessible getter with an accessible setter "
                + "has no accessor rewrite shape.");
            yield return Case(
                HotReloadWorkerReasonCode.AccessorAssignmentValueConsumed,
                NoArgs,
                "assignment value is consumed; the setter delegate returns void.");
            yield return Case(
                HotReloadWorkerReasonCode.AccessorReceiverDoubleEvaluation,
                NoArgs,
                "receiver with possible side effects would be evaluated twice.");
            yield return Case(
                HotReloadWorkerReasonCode.AccessorRefReturningPropertyNoShape,
                NoArgs,
                "inaccessible ref-returning properties have no accessor rewrite shape.");
            yield return Case(
                HotReloadWorkerReasonCode.AccessorConstructorCallNoShape,
                NoArgs,
                "inaccessible constructor call has no accessor rewrite shape.");
            yield return Case(
                HotReloadWorkerReasonCode.AccessorInitializerAssignmentNoShape,
                NoArgs,
                "inaccessible member assignment in an object/collection initializer has no "
                + "accessor rewrite shape.");
            yield return Case(
                HotReloadWorkerReasonCode.AccessorConditionalAccessNoShape,
                NoArgs,
                "inaccessible member access via conditional access has no rewrite shape.");
            yield return Case(
                HotReloadWorkerReasonCode.AccessorRefReturningMethodNoShape,
                NoArgs,
                "inaccessible methods that return by ref have no accessor rewrite shape.");
            yield return Case(
                HotReloadWorkerReasonCode.AccessorRefOutInParameterNotRewritten,
                NoArgs,
                "inaccessible method calls with ref/out/in parameters are not rewritten; the call is "
                + "refused whatever is passed, because the rewrite cannot forward ref/out/in parameters. "
                + "No form of this call applies while the callee stays a compiled inaccessible method - an "
                + "added method that calls it is refused for the same reason. Only a method this same "
                + "reload adds is called directly with ref/out/in arguments, so declare that logic as a "
                + "method this reload adds, or route the work through an API this code can already "
                + "access, to keep the edit applying without leaving Play Mode.");
            yield return Case(
                HotReloadWorkerReasonCode.AccessorNamedArgumentNotRewritten,
                NoArgs,
                "inaccessible method calls with named arguments are not rewritten.");
            yield return Case(
                HotReloadWorkerReasonCode.AccessorOptionalOrParamsArgumentNotRewritten,
                NoArgs,
                "inaccessible method calls with omitted optional or expanded params arguments "
                + "are not rewritten.");
            yield return Case(
                HotReloadWorkerReasonCode.AccessorMethodGroupNoShape,
                new[] { "Helper", " (such as 'a => Helper(a)')" },
                "inaccessible method group 'Helper' (non-invocation) has no accessor rewrite shape. "
                + "A call is rewritten, so wrapping the method group in a lambda that calls it "
                + "(such as 'a => Helper(a)') keeps hot reloading.");
            yield return Case(
                HotReloadWorkerReasonCode.AccessorMethodGroupNoShape,
                new[] { "Helper", "" },
                "inaccessible method group 'Helper' (non-invocation) has no accessor rewrite shape. "
                + "A call is rewritten, so wrapping the method group in a lambda that calls it "
                + "keeps hot reloading.");
            yield return Case(
                HotReloadWorkerReasonCode.AccessorMethodGroupUnsubscribeNoShape,
                new[] { "Helper" },
                "inaccessible method group 'Helper' on the right of '-=' has no accessor rewrite shape, "
                + "and wrapping it in a lambda would remove a different delegate and leave the handler "
                + "subscribed.");
            yield return Case(
                HotReloadWorkerReasonCode.IntroducedTypeSymbolUnresolved,
                new string[0],
                "Could not resolve a declared type symbol.");
            yield return Case(
                HotReloadWorkerReasonCode.IntroducedTypeGeneric,
                new[] { "Example.Type" },
                "Generic introduced type requires a compile: Example.Type");
            yield return Case(
                HotReloadWorkerReasonCode.IntroducedTypePartial,
                new[] { "Example.Type" },
                "Partial introduced type requires a compile: Example.Type");
            yield return Case(
                HotReloadWorkerReasonCode.IntroducedTypeRecord,
                new[] { "Example.Type" },
                "Record introduced type requires a compile: Example.Type");
            yield return Case(
                HotReloadWorkerReasonCode.IntroducedTypeNonPublic,
                new[] { "Example.Type" },
                "Non-public introduced type requires a compile: Example.Type");
            yield return Case(
                HotReloadWorkerReasonCode.IntroducedTypeRefLike,
                new[] { "Example.Type" },
                "Ref-like introduced type requires a compile: Example.Type");
            yield return Case(
                HotReloadWorkerReasonCode.IntroducedTypeUnsafe,
                new[] { "Example.Type" },
                "Unsafe introduced type requires a compile: Example.Type");
            yield return Case(
                HotReloadWorkerReasonCode.IntroducedTypeUnityObject,
                new[] { "Example.Type" },
                "Unity object introduced type requires a compile: Example.Type");
            yield return Case(
                HotReloadWorkerReasonCode.IntroducedTypeSerializable,
                new[] { "Example.Type" },
                "Serializable introduced type requires a compile: Example.Type");
            yield return Case(
                HotReloadWorkerReasonCode.IntroducedTypeModuleInitializer,
                new[] { "Example.Type" },
                "Module initializer introduced type requires a compile: Example.Type");
            yield return Case(
                HotReloadWorkerReasonCode.IntroducedTypeUnsupported,
                new[] { "Example.Type" },
                "Unsupported introduced type requires a compile: Example.Type");
            yield return Case(
                HotReloadWorkerReasonCode.IntroducedTypeConstValueUnverifiable,
                new[] { "Example.Other.Limit", "Example.Type" },
                "Const value cannot be verified: Example.Other.Limit referenced by Example.Type");
            yield return Case(
                HotReloadWorkerReasonCode.IntroducedTypeConstChanged,
                new[] { "Example.Other.Limit", "Example.Type" },
                "Changed const requires a compile: Example.Other.Limit referenced by Example.Type");
            yield return Case(
                HotReloadWorkerReasonCode.IntroducedTypeDelegate,
                new[] { "Example.Handler" },
                "Delegate introduced type requires a compile: Example.Handler");
            yield return Case(
                HotReloadWorkerReasonCode.IntroducedTypeNested,
                new[] { "Example.Outer/Inner" },
                "Nested type requires a compile: Example.Outer/Inner");
            yield return Case(
                HotReloadWorkerReasonCode.IntroducedTypeNestedDeclaration,
                new[] { "Example.Outer", "Inner" },
                "Nested declaration inside an introduced type requires a compile: Example.Outer/Inner");
            yield return Case(
                HotReloadWorkerReasonCode.IntroducedTypeChanged,
                new[] { "Example.Type" },
                "Changed introduced type requires a compile: Example.Type");
            yield return DetailArgsCase(
                HotReloadWorkerReasonCode.IntroducedTypeChanged,
                new[] { "Example.Type" },
                HotReloadWorkerReasonCode.IntroducedTypeDifferenceList,
                new[] { "header, added:Example.Type::Extra()" },
                "Changed introduced type requires a compile: Example.Type"
                + " Declaration differences: header, added:Example.Type::Extra().");
            yield return DetailArgsCase(
                HotReloadWorkerReasonCode.IntroducedTypeMemberBodyChanged,
                new[] { "Example.Type" },
                HotReloadWorkerReasonCode.IntroducedTypeDifferenceList,
                new[] { "Example.Type::.ctor()" },
                "Changed member body of introduced type requires a compile: Example.Type"
                + " Changed members: Example.Type::.ctor()."
                + " Only ordinary method bodies and getter-only property bodies of an introduced type can be hot reloaded.");
            yield return Case(
                HotReloadWorkerReasonCode.IntroducedTypeDifferenceList,
                new[] { "header, order" },
                "header, order");
            yield return Case(
                HotReloadWorkerReasonCode.IntroducedTypeArtifactUnusable,
                new[] { "an artifact could not be read." },
                "Introduced types require a compile: an artifact could not be read.");
            yield return Case(
                HotReloadWorkerReasonCode.IntroducedTypeInputsUnreadable,
                new string[0],
                "Introduced types require a compile: the target assembly or its references could not be read.");
            yield return Case(
                HotReloadWorkerReasonCode.IntroducedTypeIdentityMismatch,
                new string[0],
                "Introduced types require a compile: the target assembly identity does not match the request.");
            yield return Case(
                HotReloadWorkerReasonCode.EditorIsolatedAddedMethodCaller,
                new[] { "Host.Added()" },
                "Calls the added method 'Host.Added()', which this reload did not apply; the caller was left unpatched. "
                + "That method's own row gives the reason: fix its compile error when it Failed, or the failure "
                + "elsewhere in its file when it was Skipped, and reload again, or run 'uloop compile'.");
        }

        /// <summary>
        /// What: a carried-in skip nested under an added property keeps only its facts, so the
        /// step chosen for the accessor's own row is not repeated here with other wording.
        /// </summary>
        [Test]
        public void Render_AddedPropertyWithCarriedInDetail_KeepsTheDetailToItsFacts()
        {
            string rendered = HotReloadWorkerReasonText.Render(
                AddedPropertyWithDetail(CarriedInDetail(HotReloadWorkerReasonCode.AddedMethodBodyBindsCompiledSignature)));

            Assert.That(
                rendered,
                Does.EndWith(
                    "The property body was refused because: The added member's body could not be fully bound "
                    + "in the hot-reload compilation (CS1503: cannot convert): this reload declares "
                    + "'Example.Payload' from source, while the compiled signatures of 'Example.Registry', "
                    + "declared in 'Assets/Registry.cs', still name the compiled 'Example.Payload', so it is skipped."));
            Assert.That(rendered, Does.Not.Contain("Pass "));
        }

        /// <summary>
        /// What: an added property refused for a carried-in skip ends by pointing to the accessor's
        /// Skipped row instead of asking for a compile, for either carried-in reason.
        /// </summary>
        [Test]
        public void Render_AddedPropertyWithCarriedInDetail_PointsToTheAccessorRow()
        {
            string compiledSignature = HotReloadWorkerReasonText.Render(
                AddedPropertyWithDetail(CarriedInDetail(HotReloadWorkerReasonCode.AddedMethodBodyBindsCompiledSignature)));
            string carriedIn = HotReloadWorkerReasonText.Render(
                AddedPropertyWithDetail(
                    CarriedInDetail(HotReloadWorkerReasonCode.AddedMethodCallsIntroducedMemberBoundToCompiledType)));

            string expectedStart = "Uses an added property that hot reload cannot emit. "
                + "The Skipped row for the property's accessor names the step to take. "
                + "The property body was refused because: ";
            Assert.That(compiledSignature, Does.StartWith(expectedStart));
            Assert.That(carriedIn, Does.StartWith(expectedStart));
            Assert.That(compiledSignature, Does.Not.Contain("uloop compile"));
            Assert.That(carriedIn, Does.Not.Contain("uloop compile"));
        }

        /// <summary>
        /// What: an added property refused for any other reason still ends with the compile call.
        /// </summary>
        [Test]
        public void Render_AddedPropertyWithOtherDetail_KeepsTheCompileCall()
        {
            TransformWorkerReasonDto detail = new TransformWorkerReasonDto
            {
                code = HotReloadWorkerReasonCode.AddedMethodBodyUnbound,
                args = new[] { "CS0103: missing" }
            };

            string rendered = HotReloadWorkerReasonText.Render(AddedPropertyWithDetail(detail));

            Assert.That(
                rendered,
                Does.StartWith(
                    "Uses an added property that hot reload cannot emit. Run 'uloop compile'. "
                    + "The property body was refused because: "));
            Assert.That(rendered, Does.Not.Contain("accessor names the step"));
        }

        private static TransformWorkerReasonDto AddedPropertyWithDetail(TransformWorkerReasonDto detail)
        {
            return new TransformWorkerReasonDto
            {
                code = HotReloadWorkerReasonCode.AddedPropertyUnavailableAddedProperty,
                args = NoArgs,
                detail = detail
            };
        }

        private static TransformWorkerReasonDto CarriedInDetail(HotReloadWorkerReasonCode code)
        {
            return new TransformWorkerReasonDto
            {
                code = code,
                args = new[] { "CS1503: cannot convert", "'Example.Payload'", "'Example.Registry'", "'Assets/Registry.cs'" }
            };
        }

        private static TestCaseData Case(HotReloadWorkerReasonCode code, string[] args, string expected)
        {
            return new TestCaseData(code.ToString(), args, null, null, expected).SetName("Render_" + code);
        }

        // The detail of a composed reason carries values of its own here, which the no-argument
        // DetailCase cannot express.
        private static TestCaseData DetailArgsCase(
            HotReloadWorkerReasonCode code,
            string[] args,
            HotReloadWorkerReasonCode detailCode,
            string[] detailArgs,
            string expected)
        {
            return new TestCaseData(code.ToString(), args, detailCode.ToString(), detailArgs, expected)
                .SetName("Render_" + code + "_WithDetailArgs");
        }

        private static TestCaseData DetailCase(
            HotReloadWorkerReasonCode code,
            string[] args,
            HotReloadWorkerReasonCode detailCode,
            string expected)
        {
            return new TestCaseData(code.ToString(), args, detailCode.ToString(), NoArgs, expected)
                .SetName("Render_" + code + "_WithDetail");
        }
    }
}
