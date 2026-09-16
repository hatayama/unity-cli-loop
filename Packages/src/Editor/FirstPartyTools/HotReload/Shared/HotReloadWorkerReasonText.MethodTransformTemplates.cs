using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    internal static partial class HotReloadWorkerReasonText
    {
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
        }
    }
}
