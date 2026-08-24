using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using HarmonyLib;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Injects a call to <see cref="SourcePausePointCapture.Capture"/> at a resolved instruction
    /// index via a Harmony transpiler, and tracks the resulting patches so they can be removed.
    /// The injected IL calls an internal method across assembly boundaries; this works because
    /// Harmony/MonoMod builds the replacement method as a skip-visibility DynamicMethod, so the
    /// CLR's normal accessibility check for the call site never runs (verified in PR 4's design
    /// discussion against the vendored Harmony's own private-nested-class smoke test).
    /// </summary>
    internal static class SourcePausePointPatcher
    {
        private static readonly Harmony HarmonyInstance = new(SourcePausePointConstants.HarmonyId);

        internal static readonly Dictionary<MethodBase, List<SourcePausePointPatchInjection>> InjectionsByMethod = new();
        private static readonly Dictionary<string, MethodBase> MethodById = new();
        internal static readonly Dictionary<string, MethodBase> LogicalOwnerById = new();
        // Why store file:line separately: auto-retarget must re-resolve with the original enable
        // request, and parsing the id string would couple us to id formatting.
        internal static readonly Dictionary<string, (string NormalizedFile, int Line)> RequestById = new();
        internal static readonly List<SourcePausePointHotReloadRetarget.RetargetLineDriftWarning> PendingRetargetLineDriftWarnings =
            new List<SourcePausePointHotReloadRetarget.RetargetLineDriftWarning>();
        internal static readonly List<string> PendingExpiredNotRetargetedIds = new List<string>();

        // The registry lives in a Runtime assembly this Editor-only tool assembly may depend on,
        // but not the reverse (patching is an outer/implementation concern the registry's inner
        // layer must not know about). Wiring these hooks here - rather than having every Clear
        // caller reference this class directly - lets the Infrastructure CLI bridge call
        // UloopPausePointRegistry.Clear/ClearAll without ever referencing this assembly.
        static SourcePausePointPatcher()
        {
            UloopPausePointRegistry.OnCleared = Unpatch;
            UloopPausePointRegistry.OnClearedAll = UnpatchAll;
            HotReloadPausePointCoordination.GetArmedMarkerIdsOnMethod =
                SourcePausePointHotReloadRetarget.GetArmedMarkerIds;
            HotReloadPausePointCoordination.GetSuppressedMarkerIdsOnMethod =
                SourcePausePointHotReloadRetarget.GetSuppressedMarkerIds;
            HotReloadPausePointCoordination.ConsumeExpiredNotRetargetedMarkerIds =
                SourcePausePointHotReloadRetarget.ConsumeExpiredNotRetargetedMarkerIds;
            HotReloadPausePointCoordination.OnHotReloadPatchStateChanged =
                SourcePausePointHotReloadRetarget.HandleHotReloadPatchStateChanged;
            HotReloadPausePointCoordination.ConsumeRetargetLineDriftWarnings =
                SourcePausePointHotReloadRetarget.ConsumeRetargetLineDriftWarnings;
        }

        public static SourcePausePointPatchResult Patch(
            string id,
            SourcePausePointResolution resolution,
            string normalizedFile = "",
            int requestedLine = 0,
            MethodBase logicalOwnerOverride = null)
        {
            Debug.Assert(!string.IsNullOrEmpty(id), "id must not be null or empty.");
            Debug.Assert(resolution != null, "resolution must not be null.");

            SourcePausePointPatchResult resolveResult = TryResolveMethod(resolution, out MethodBase method);
            if (!resolveResult.Success)
            {
                return resolveResult;
            }

            SourcePausePointPatchResult patchabilityResult = CheckPatchable(method);
            if (!patchabilityResult.Success)
            {
                return patchabilityResult;
            }

            bool patchedByHotReload =
                HotReloadPausePointCoordination.GetActiveShimForMethod?.Invoke(method) != null;
            if (patchedByHotReload)
            {
                string typeName = method.DeclaringType != null ? method.DeclaringType.Name : "?";
                string errorMessage = string.Format(
                    SourcePausePointConstants.HotReloadPatchedLineOutsidePatchedBodyMessageFormat,
                    typeName,
                    method.Name,
                    requestedLine);
                if (resolution.CompiledMethodStartLine > 0 && resolution.CompiledMethodEndLine > 0)
                {
                    errorMessage += string.Format(
                        SourcePausePointConstants.HotReloadPatchedCompiledMethodSpanFormat,
                        typeName,
                        method.Name,
                        resolution.CompiledMethodStartLine,
                        resolution.CompiledMethodEndLine);
                }

                return SourcePausePointPatchResult.Failure(
                    SourcePausePointPatchFailureReason.MethodPatchedByHotReload,
                    errorMessage,
                    SourcePausePointConstants.HotReloadPatchedLineOutsidePatchedBodyNextAction);
            }

            MethodBase logicalOwner = logicalOwnerOverride ?? method;

            // Why conditional no-op: ShouldInject can leave a prior injection inert (e.g. stale
            // OriginalBody under an active shim). Re-enable must replace mismatched ledger state
            // instead of reporting success while the call site never fires.
            if (TryReuseExistingPatch(
                    id,
                    SourcePausePointPatchInjectionTargetKind.OriginalBody,
                    method,
                    donorShim: null))
            {
                RememberRequest(id, normalizedFile, requestedLine);
                LogicalOwnerById[id] = logicalOwner;
                return SourcePausePointPatchResult.SuccessResult();
            }

            SourcePausePointPatchInjection injection = new(
                id,
                resolution.InstructionIndex,
                resolution.IsStatic,
                resolution.IsDeclaringTypeValueType,
                resolution.Parameters,
                resolution.Locals,
                SourcePausePointPatchInjectionTargetKind.OriginalBody);

            return CommitPatch(id, method, logicalOwner, injection, normalizedFile, requestedLine);
        }

        /// <summary>
        /// Patches a pause point onto a hot-reload shim target (transplant chain-join or
        /// shim-direct) without AppDomain name/MVID resolution.
        /// </summary>
        public static SourcePausePointPatchResult PatchShimTarget(
            string id,
            SourcePausePointShimResolution shim,
            string normalizedFile,
            int requestedLine)
        {
            Debug.Assert(!string.IsNullOrEmpty(id), "id must not be null or empty.");
            Debug.Assert(shim != null, "shim must not be null.");
            Debug.Assert(
                shim.Kind == SourcePausePointShimResolveKind.TransplantChainJoin
                || shim.Kind == SourcePausePointShimResolveKind.ShimDirect,
                "PatchShimTarget requires a successful shim resolution.");

            // Why skip TryResolveMethod: shim assemblies are all named HotReloadShim across
            // generations, so name+MVID lookup cannot identify the loaded generation; the
            // resolver already handed us the MethodBase from that generation's LoadedAssembly.
            MethodBase method = shim.TargetMethod;
            SourcePausePointPatchResult patchabilityResult = CheckPatchable(method);
            if (!patchabilityResult.Success)
            {
                return patchabilityResult;
            }

            SourcePausePointPatchInjectionTargetKind targetKind =
                shim.Kind == SourcePausePointShimResolveKind.TransplantChainJoin
                    ? SourcePausePointPatchInjectionTargetKind.TransplantChainJoin
                    : SourcePausePointPatchInjectionTargetKind.ShimDirect;

            if (TryReuseExistingPatch(id, targetKind, method, shim.DonorShim))
            {
                RememberRequest(id, normalizedFile, requestedLine);
                return SourcePausePointPatchResult.SuccessResult();
            }

            bool isStatic = !shim.InstanceFromFirstArgument && method.IsStatic;
            // Why physical DeclaringType: async MoveNext state machines are structs even when
            // the user type is a class. Boxing decisions must follow the method we actually
            // patch, not the logical owner.
            bool isDeclaringTypeValueType =
                method.DeclaringType != null && method.DeclaringType.IsValueType;

            SourcePausePointPatchInjection injection = new(
                id,
                InstructionIndexForInjection(targetKind, method, shim.InstructionIndex),
                isStatic,
                isDeclaringTypeValueType,
                shim.Parameters,
                shim.Locals,
                targetKind,
                shim.DonorShim,
                shim.InstanceFromFirstArgument);

            return CommitPatch(id, method, shim.LogicalOwner, injection, normalizedFile, requestedLine);
        }

        // Why add preamble only for TransplantChainJoin: the recorded index is from the shim's
        // raw IL. Transplant prepends ldstr+call before that stream; ShimDirect and OriginalBody
        // have no such prefix.
        private static int InstructionIndexForInjection(
            SourcePausePointPatchInjectionTargetKind targetKind,
            MethodBase method,
            int instructionIndex)
        {
            if (targetKind != SourcePausePointPatchInjectionTargetKind.TransplantChainJoin)
            {
                return instructionIndex;
            }

            Func<MethodBase, int> getPreambleLength =
                HotReloadPausePointCoordination.GetTransplantPreambleLength;
            if (getPreambleLength == null)
            {
                return instructionIndex;
            }

            return instructionIndex + getPreambleLength(method);
        }

        private static bool TryReuseExistingPatch(
            string id,
            SourcePausePointPatchInjectionTargetKind targetKind,
            MethodBase physicalTarget,
            MethodBase donorShim)
        {
            if (!MethodById.TryGetValue(id, out MethodBase existingPhysical)
                || !InjectionsByMethod.TryGetValue(existingPhysical, out List<SourcePausePointPatchInjection> injections))
            {
                return false;
            }

            SourcePausePointPatchInjection existing = FindInjectionById(injections, id);
            if (existing == null)
            {
                return false;
            }

            bool sameKind = existing.TargetKind == targetKind;
            bool sameTarget = existingPhysical.Equals(physicalTarget);
            bool sameDonor = targetKind != SourcePausePointPatchInjectionTargetKind.TransplantChainJoin
                || (existing.DonorShim != null && donorShim != null && existing.DonorShim.Equals(donorShim));
            if (sameKind && sameTarget && sameDonor)
            {
                return true;
            }

            UnpatchInstrumentationOnly(id);
            return false;
        }

        private static SourcePausePointPatchInjection FindInjectionById(
            List<SourcePausePointPatchInjection> injections,
            string id)
        {
            for (int index = 0; index < injections.Count; index++)
            {
                if (injections[index].Id == id)
                {
                    return injections[index];
                }
            }

            return null;
        }

        private static void RememberRequest(string id, string normalizedFile, int requestedLine)
        {
            RequestById[id] = (normalizedFile, requestedLine);
        }

        private static SourcePausePointPatchResult CommitPatch(
            string id,
            MethodBase method,
            MethodBase logicalOwner,
            SourcePausePointPatchInjection injection,
            string normalizedFile,
            int requestedLine)
        {
            bool methodAlreadyPatched = InjectionsByMethod.TryGetValue(method, out List<SourcePausePointPatchInjection> injections);
            if (!methodAlreadyPatched)
            {
                injections = new List<SourcePausePointPatchInjection>();
                InjectionsByMethod[method] = injections;
            }

            injections.Add(injection);
            MethodById[id] = method;
            LogicalOwnerById[id] = logicalOwner;
            RememberRequest(id, normalizedFile, requestedLine);

            // The ledger above is written before Harmony actually rebuilds the method. If
            // Unpatch/Patch throws (e.g. a byref-like `this` produces invalid IL at JIT time), a
            // plain rethrow would leave this id's ledger entry claiming success while Harmony
            // never attached the transpiler; a later caller would then see a false "already
            // patched" and silently miss the pause point. try-finally (not catch) restores the
            // ledger to its honest pre-call shape while letting the original exception propagate
            // unchanged, keeping this Fail Fast rather than swallowing the failure.
            bool committed = false;
            try
            {
                if (methodAlreadyPatched)
                {
                    // Harmony only regenerates a method's replacement when Patch/Unpatch is called;
                    // re-declaring the same transpiler risks double-registration, so drop and redo it
                    // to force a clean rebuild from the (untouched) original IL against the new injection set.
                    HarmonyInstance.Unpatch(method, HarmonyPatchType.Transpiler, SourcePausePointConstants.HarmonyId);
                }
                HarmonyInstance.Patch(method, transpiler: new HarmonyMethod(SourcePausePointInjectionEmitter.TranspilerMethodInfo));
                committed = true;
            }
            finally
            {
                if (!committed)
                {
                    injections.Remove(injection);
                    if (injections.Count == 0)
                    {
                        InjectionsByMethod.Remove(method);
                    }
                    MethodById.Remove(id);
                    LogicalOwnerById.Remove(id);
                    RequestById.Remove(id);
                }
            }

            (string warning, bool hasPhysicsCallbackWarning) = BuildPatchWarning(method);
            return SourcePausePointPatchResult.SuccessResult(warning, method.DeclaringType, hasPhysicsCallbackWarning);
        }

        // What: removes Harmony instrumentation and patcher ledgers for one id without touching
        // the runtime registry. Used by auto-retarget (keep armed/hit) and by registry clear.
        // Why keep Unpatch as the OnCleared hook name: the registry wires OnCleared = Unpatch.
        public static void Unpatch(string id)
        {
            UnpatchInstrumentationOnly(id);
        }

        public static void UnpatchInstrumentationOnly(string id)
        {
            Debug.Assert(!string.IsNullOrEmpty(id), "id must not be null or empty.");

            // Why before MethodById guard: ReanchorMarkerWithoutInjection leaves MethodById
            // empty while LogicalOwnerById/RequestById still hold the id; clear must scrub
            // those ledgers so a later transition cannot revive a cleared marker's request.
            LogicalOwnerById.Remove(id);
            RequestById.Remove(id);

            if (!MethodById.TryGetValue(id, out MethodBase method))
            {
                return;
            }
            MethodById.Remove(id);

            List<SourcePausePointPatchInjection> injections = InjectionsByMethod[method];
            injections.RemoveAll(injection => injection.Id == id);

            HarmonyInstance.Unpatch(method, HarmonyPatchType.Transpiler, SourcePausePointConstants.HarmonyId);
            if (injections.Count == 0)
            {
                InjectionsByMethod.Remove(method);
                return;
            }

            // If re-patching the remaining injections throws, Harmony is left with no transpiler
            // attached at all, yet the ledger would still claim the other ids are patched. Wipe
            // this method's ledger entries entirely on failure so a later call sees the honest
            // "not patched" state instead of a silently stale success (see the same reasoning in Patch).
            bool committed = false;
            try
            {
                HarmonyInstance.Patch(method, transpiler: new HarmonyMethod(SourcePausePointInjectionEmitter.TranspilerMethodInfo));
                committed = true;
            }
            finally
            {
                if (!committed)
                {
                    foreach (string remainingId in injections.Select(remaining => remaining.Id).ToList())
                    {
                        MethodById.Remove(remainingId);
                        LogicalOwnerById.Remove(remainingId);
                        RequestById.Remove(remainingId);
                    }
                    InjectionsByMethod.Remove(method);
                }
            }
        }

        public static void UnpatchAll()
        {
            HarmonyInstance.UnpatchAll(SourcePausePointConstants.HarmonyId);
            InjectionsByMethod.Clear();
            MethodById.Clear();
            LogicalOwnerById.Clear();
            RequestById.Clear();
            PendingExpiredNotRetargetedIds.Clear();
            PendingRetargetLineDriftWarnings.Clear();
        }

        internal static SourcePausePointPatchResult TryResolveMethod(SourcePausePointResolution resolution, out MethodBase method)
        {
            method = null;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name != resolution.AssemblyName)
                {
                    continue;
                }

                if (assembly.ManifestModule.ModuleVersionId.ToString() != resolution.Mvid)
                {
                    return SourcePausePointPatchResult.Failure(
                        SourcePausePointPatchFailureReason.StaleAssembly,
                        $"The loaded assembly '{resolution.AssemblyName}' no longer matches the "
                        + "compiled assembly this resolution was taken from.",
                        SourcePausePointConstants.StaleAssemblyHint);
                }

                method = assembly.ManifestModule.ResolveMethod(resolution.MetadataToken);
                return SourcePausePointPatchResult.SuccessResult();
            }

            return SourcePausePointPatchResult.Failure(
                SourcePausePointPatchFailureReason.AssemblyNotLoaded,
                $"Assembly '{resolution.AssemblyName}' is not currently loaded in the AppDomain.",
                SourcePausePointConstants.AssemblyNotLoadedHint);
        }

        private static SourcePausePointPatchResult CheckPatchable(MethodBase method)
        {
            if (method.IsAbstract)
            {
                return SourcePausePointPatchResult.Failure(
                    SourcePausePointPatchFailureReason.UnpatchableAbstract,
                    $"'{method}' is abstract and has no method body to patch.",
                    SourcePausePointConstants.ManualMarkerFallbackHint);
            }

            if (method.GetMethodBody() == null)
            {
                return SourcePausePointPatchResult.Failure(
                    SourcePausePointPatchFailureReason.UnpatchableExtern,
                    $"'{method}' has no IL method body (extern or an internal call) and cannot be patched.",
                    SourcePausePointConstants.ManualMarkerFallbackHint);
            }

            if (method.ContainsGenericParameters)
            {
                return SourcePausePointPatchResult.Failure(
                    SourcePausePointPatchFailureReason.UnpatchableOpenGeneric,
                    $"'{method}' is declared with unresolved generic type parameters and cannot be safely patched.",
                    SourcePausePointConstants.ManualMarkerFallbackHint);
            }

            if (HasBurstCompileAttribute(method) || HasBurstCompileAttribute(method.DeclaringType))
            {
                return SourcePausePointPatchResult.Failure(
                    SourcePausePointPatchFailureReason.UnpatchableBurstCompiled,
                    $"'{method}' (or its declaring type) is marked [BurstCompile] and cannot be patched.",
                    SourcePausePointConstants.ManualMarkerFallbackHint);
            }

            return SourcePausePointPatchResult.SuccessResult();
        }

        private static bool HasBurstCompileAttribute(MemberInfo member)
        {
            foreach (object attribute in member.GetCustomAttributes(inherit: false))
            {
                if (attribute.GetType().FullName == SourcePausePointConstants.BurstCompileAttributeFullName)
                {
                    return true;
                }
            }

            return false;
        }

        private static (string Warning, bool HasPhysicsCallbackWarning) BuildPatchWarning(MethodBase method)
        {
            List<string> warnings = new();
            bool hasPhysicsCallbackWarning = false;

            if (!method.IsStatic && SourcePausePointInjectionEmitter.IsByRefLikeType(method.DeclaringType))
            {
                warnings.Add(SourcePausePointConstants.RefStructInstanceNotCapturedWarning);
            }

            if (SourcePausePointPhysicalMessageMethods.IsPhysicalMessageMethod(method.Name) &&
                typeof(MonoBehaviour).IsAssignableFrom(method.DeclaringType))
            {
                warnings.Add(SourcePausePointConstants.PhysicalCallbackMayMissExistingInstanceWarning);
                hasPhysicsCallbackWarning = true;
            }
            else if (SourcePausePointPhysicalCallbackCallSiteScanner.IsCalledFromPhysicalMessageMethod(method))
            {
                warnings.Add(SourcePausePointConstants.PhysicalCallbackIndirectCallMayMissExistingInstanceWarning);
                hasPhysicsCallbackWarning = true;
            }

            if (hasPhysicsCallbackWarning)
            {
                warnings.Add(SourcePausePointConstants.PhysicalCallbackMidSolverValuesWarning);
            }

            if (IsLikelyJitInlined(method))
            {
                warnings.Add(SourcePausePointConstants.SmallMethodInliningRiskWarning);
            }

            return (string.Join(" ", warnings), hasPhysicsCallbackWarning);
        }

        // Heuristic, not a guarantee: [AggressiveInlining] is only a hint the JIT may ignore, and
        // IL body size alone cannot predict Mono's actual inlining decision (call-site count, caller
        // size, and tiering all matter too). Both false positives (flagged but never inlined) and
        // false negatives (inlined despite exceeding the threshold) are possible; this exists solely
        // to explain a HitCount=0 symptom, not to predict it precisely.
        // Harmony detours the JIT-compiled native code and never rewrites the metadata IL, so
        // measuring after Patch still reads the original method body size.
        private static bool IsLikelyJitInlined(MethodBase method)
        {
            if ((method.GetMethodImplementationFlags() & MethodImplAttributes.AggressiveInlining) != 0)
            {
                return true;
            }

            byte[] ilBytes = method.GetMethodBody()?.GetILAsByteArray();
            return ilBytes != null && ilBytes.Length <= SourcePausePointConstants.SmallMethodInliningRiskThresholdBytes;
        }
    }
}
