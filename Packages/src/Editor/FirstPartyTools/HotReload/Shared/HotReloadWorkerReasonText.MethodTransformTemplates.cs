using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    internal static partial class HotReloadWorkerReasonText
    {
        // Format: diagnostic, quoted type name, quoted member name. Why the member and its type lead:
        // which member is out of reach is what the reader acts on, and the diagnostic follows as the
        // evidence for it.
        private const string UnpassedInternalMemberSentence =
            "{2} is internal to {1}, whose source this reload was not given ({0}). Hot reload patches a use of "
            + "such a member only where it is a field, a property or a method call written with its receiver "
            + "('this.Name', 'Type.Name', 'value.Name') in the method's own statements: not a bare name, a "
            + "method passed as a delegate, or a use inside a lambda, local function, query, iterator or async "
            + "method, or in a body patched through a delegating shim. A lambda, local function or query that "
            + "works with a value hot reload could not resolve, such as the member's result, keeps the whole "
            + "body out as well.";

        /// <summary>
        /// Adds the reasons an existing method body could not be transformed.
        /// </summary>
        private static void AddMethodTransformTemplates(
            Dictionary<HotReloadWorkerReasonCode, ReasonTemplate> templates)
        {
            templates.Add(
                HotReloadWorkerReasonCode.MethodTransformNoBody,
                Plain("Methods without a body (abstract/extern) are skipped.", 0));
            templates.Add(
                HotReloadWorkerReasonCode.MethodTransformBodylessNotCompared,
                Plain(
                    "Has no body, so there is nothing to patch. It could not be compared with the compiled source, "
                    + "so a change to its default values, attributes or parameter names cannot be detected; "
                    + "run 'uloop compile' only if you made one.",
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.MethodTransformBaseMemberCall,
                Plain(
                    "Uses base. for something other than a direct call to a base method (a base property, indexer, "
                    + "event, or method group); C# cannot express that outside the type, so run 'uloop compile'.",
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.MethodTransformBaseCallOutsideOwnStatements,
                Plain(
                    "Calls a base method from a lambda, local function, query, async or iterator body, a property "
                    + "accessor, a member or type hot reload added, or a body patched through a delegating shim; "
                    + "hot reload applies a base call only in a compiled method's own statements. Move the call "
                    + "there, or run 'uloop compile'.",
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.MethodTransformBaseCallArgumentShape,
                Plain(
                    "Calls a generic or ref-returning base method, or one with a params parameter or a pointer type "
                    + "in its signature, or passes named or omitted optional arguments to a base method; for named or "
                    + "omitted arguments write every argument positionally, otherwise run 'uloop compile'.",
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.MethodTransformBaseCallTargetNotCompiled,
                Plain(
                    "Calls a base method the compiled assembly does not hold yet (added or re-signatured since the "
                    + "last compile); run 'uloop compile'.",
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.MethodTransformBaseCallConditionalTarget,
                Plain(
                    "Calls a base method marked [Conditional]; the compiler removes such a call when the symbol is "
                    + "not defined, and hot reload cannot do the same; run 'uloop compile'.",
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.MethodTransformBaseCallAmbiguousTarget,
                Plain(
                    "Calls a base method whose type declares another method with the same name and the same "
                    + "parameter types once type arguments are filled in; hot reload cannot tell the two apart, so "
                    + "run 'uloop compile'.",
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
                HotReloadWorkerReasonCode.MethodTransformPartialOtherPartChanged,
                Plain(
                    "Another part of this partial type changed since the last compile ({0}), so hot reload "
                    + "cannot bind this method against the compiled type. Pass that file with --files too, "
                    + "or run 'uloop compile'.",
                    1));
            templates.Add(
                HotReloadWorkerReasonCode.MethodTransformPartialOtherPartsUnverified,
                Plain(
                    "The other parts of this partial type could not be checked against the last compile "
                    + "(no source snapshot for the assembly, or too many changed files to scan), so hot "
                    + "reload cannot bind this method against the compiled type. Run 'uloop compile'.",
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.MethodTransformPartialBodyUnbound,
                Plain(
                    "{0}. None of this partial type's source files known to hot reload declares that name: "
                    + "a part generated at compile time is not visible to it, and a file added since the "
                    + "last compile must be passed with --files. Otherwise run 'uloop compile'.",
                    1));
            // Why the three share every sentence but the last: the member, its type and the rule are
            // the same whatever the use, and only the change that brings the use within reach differs.
            templates.Add(
                HotReloadWorkerReasonCode.MethodTransformUnpassedInternalMemberOutOfReach,
                Plain(UnpassedInternalMemberSentence, 3).EndingWith(CompileCallToAction));
            templates.Add(
                HotReloadWorkerReasonCode.MethodTransformUnpassedInternalMemberBareName,
                Plain(UnpassedInternalMemberSentence, 3).EndingWith(
                    "Qualify the bare name with 'this.' or the type name, or run 'uloop compile'."));
            templates.Add(
                HotReloadWorkerReasonCode.MethodTransformUnpassedInternalMemberInsideClosure,
                Plain(UnpassedInternalMemberSentence, 3).EndingWith(
                    "Move the use out of the lambda, local function or query, or run 'uloop compile'."));
            templates.Add(
                HotReloadWorkerReasonCode.MethodTransformStructHost,
                Plain(
                    "Struct (value type) methods are skipped; byref instance transplant is unverified. "
                    + "Leave the struct's methods as they are and put the new logic at the call site or in a "
                    + "non-struct helper, or run 'uloop compile' to change the struct.",
                    0));
            templates.Add(
                HotReloadWorkerReasonCode.MethodTransformGenericMethodOrType,
                Plain(
                    "Generic methods and methods inside generic types cannot be safely patched with Harmony.",
                    0).EndingWith(CompileCallToAction));
            templates.Add(
                HotReloadWorkerReasonCode.MethodTransformUnsafeCode,
                Plain(
                    "Methods and accessors that need an unsafe context (pointers, function pointers, fixed "
                    + "statements, or unsafe blocks) are skipped; the patch is compiled without unsafe code.",
                    0).EndingWith(CompileCallToAction));
            // Why both members are named: the reader has to see which of two same-named members the
            // patch could call, and overloads differ only in their parameter lists.
            templates.Add(
                HotReloadWorkerReasonCode.MethodTransformUseResolvesToHiddenMember,
                Plain(
                    "Unity's compiler uses '{1}' here, but the patch could use '{0}' instead: the patch is "
                    + "compiled against a copy of the assembly where every member is public, so '{0}', which "
                    + "this code cannot reach, takes part in choosing the member.",
                    2).EndingWith(CompileCallToAction));
            templates.Add(
                HotReloadWorkerReasonCode.MethodTransformExplicitInterfaceImplementation,
                Plain(
                    "Explicit interface implementations the compiled assembly does not hold yet, and accessors "
                    + "of explicit interface indexers, are skipped.",
                    0).EndingWith(CompileCallToAction));
            templates.Add(
                HotReloadWorkerReasonCode.MethodTransformInitAccessorWritesInitOnlyMember,
                Plain(
                    "Init accessors that assign a readonly field or an init-only property are skipped; the patch "
                    + "is compiled outside the type, where only the type's own constructors and init accessors "
                    + "may assign them.",
                    0).EndingWith(CompileCallToAction));
            // Why {2} is not a worker value: it is the file that declares the compiled type,
            // which only the Editor resolves, and TransformWorkerCompiledTypeFileCompleter appends it.
            templates.Add(
                HotReloadWorkerReasonCode.MethodTransformSiblingBodyBindsCompiledType,
                Plain(
                    "This file was brought back to re-bind its active patches, but this method's body no "
                    + "longer binds here ({0}): it uses a member of {1} that this reload was not given the "
                    + "source of. Any earlier patch of this method stays active. Pass {2} to --files "
                    + "together with this file, or run 'uloop compile'.",
                    3));
            templates.Add(
                HotReloadWorkerReasonCode.MethodTransformSiblingBodyUnbound,
                Plain(
                    "This file was brought back to re-bind its active patches, but this method's body no "
                    + "longer binds here ({0}), so it is skipped rather than failed; any earlier patch of it "
                    + "stays active. Pass the file that declares the missing name to --files together with "
                    + "this file, or run 'uloop compile'.",
                    1));
        }
    }
}
