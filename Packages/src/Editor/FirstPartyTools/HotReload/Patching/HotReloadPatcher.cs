using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

using HarmonyLib;

using UnityEditor.Compilation;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Applies Harmony patches that replace a target method's body. Transplant copies a shim
    /// method's IL so it runs inside Harmony's skip-visibility DynamicMethod; delegation
    /// forwards every argument to a normally-JIT-compiled shim whose inaccessible accesses
    /// were rewritten to accessor delegates.
    /// </summary>
    internal sealed class HotReloadPatcher
    {
        private static readonly MethodInfo TransplantTranspilerMethodInfo =
            typeof(HotReloadPatcher).GetMethod(
                nameof(ReplaceWithTransplantSourceTranspiler),
                BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo DelegationTranspilerMethodInfo =
            typeof(HotReloadPatcher).GetMethod(
                nameof(ReplaceWithDelegationTranspiler),
                BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo IncrementInvocationMethodInfo = ResolveIncrementInvocationMethod();

        private readonly HotReloadDomain _domain;
        private readonly IHotReloadHarmony _harmony;

        internal HotReloadPatcher(HotReloadDomain domain, IHotReloadHarmony harmony)
        {
            Debug.Assert(domain != null, "domain must not be null.");
            Debug.Assert(harmony != null, "harmony must not be null.");
            _domain = domain;
            _harmony = harmony;
        }

        // Harmony resolves transpilers as static methods, so the domain cannot be a parameter of
        // one; the transpilers below read it from the gateway instead.
        private static HotReloadDomain TranspilerDomain => HotReloadTranspilerDomainGateway.Current;

        /// <summary>
        /// Patches <paramref name="method"/> with <paramref name="shimMethodInfo"/> using
        /// <paramref name="patchShape"/>. Re-applying the same method Unpatches the previous
        /// transpiler first so patches do not stack.
        /// Engine failures during apply never throw; they are contained as an
        /// <see cref="HotReloadPatchFailureReason.ApplyFailed"/> result for that method only.
        /// </summary>
        public HotReloadPatchResult Apply(
            MethodBase method,
            MethodInfo shimMethodInfo,
            HotReloadPatchShape patchShape,
            string filePath)
        {
            if (method == null)
            {
                return HotReloadPatchResult.Failure(
                    HotReloadPatchFailureReason.NullMethod,
                    "The target method is null.");
            }

            if (shimMethodInfo == null)
            {
                return HotReloadPatchResult.Failure(
                    HotReloadPatchFailureReason.NullShimMethod,
                    "The shim method is null.");
            }

            HotReloadPatchResult patchability = CheckPatchable(method);
            if (!patchability.Success)
            {
                return patchability;
            }

            HotReloadFileGeneration generation = _domain.FindGeneration(filePath);
            if (generation == null)
            {
                return HotReloadPatchResult.Failure(
                    HotReloadPatchFailureReason.ApplyFailed,
                    $"'{method}' has no hot-reload generation for '{filePath}' to patch into.");
            }

            // Why the owner is looked up across generations rather than asked of this file's
            // generation: a method's live patch belongs to whichever file's generation applied it,
            // and a rename or move re-applies the same method from a different path. Asking only
            // this generation would miss that patch and stack a second transpiler on the method.
            HotReloadFileGeneration patchOwner = _domain.FindGenerationForMethod(method);
            if (patchOwner != null && patchOwner.IsPatchActive(method))
            {
                // Why the patch is retired before Unpatch: same as Revert — during Unpatch Harmony
                // rebuilds the method and pause-point ChainJoin must see GetActiveShimForMethod ==
                // null, or it injects donor instruction indexes into the restored original IL
                // stream. Do not remove the shim registration here: ApplyEntry already registered
                // this method into the new generation before calling Apply.
                patchOwner.DeactivatePatch(method);
                HotReloadInvocationRegistry.Remove(HotReloadMethodKeys.FormatMethodLabel(method));
                _harmony.Unpatch(method, HarmonyPatchType.Transpiler, HotReloadConstants.HarmonyId);
                // Mirror the removal: if the re-Patch below fails, its contained Unpatch rebuilds
                // with markers restored (no live patch), and RevertAll can never reach this method
                // again — leaving suppress stuck true would make status lie forever.
                HotReloadPausePointCoordination.PausePointSide?.OnHotReloadPatchStateChanged(method, false);
            }

            MethodInfo transpilerMethodInfo = patchShape == HotReloadPatchShape.Delegation
                ? DelegationTranspilerMethodInfo
                : TransplantTranspilerMethodInfo;

            // The patch is committed only after Patch succeeds. During Patch the transpiler reads
            // the pending entry because Harmony resolves transpilers statically (no MethodInfo arg).
            generation.BeginPatch(method, shimMethodInfo);
            try
            {
                // Why Priority.First: same numeric priority sorts by registration index, and
                // Unpatch does not reindex — Patch/Unpatch cycles make same-priority order
                // unstable. pause-point must run after hot-reload so it sees the shim stream.
                _harmony.Patch(
                    method,
                    new HarmonyMethod(transpilerMethodInfo)
                    {
                        priority = Priority.First
                    });
                generation.CommitPatch(method);
                HotReloadPausePointCoordination.PausePointSide?.OnHotReloadPatchStateChanged(method, true);
            }
            catch (Exception exception)
            {
                // Why the entry goes before Unpatch, pending or live: the cleanup rebuild must see
                // "not patched" so pause-point markers re-instrument the restored original body.
                // Leaving it would make GetActiveShimForMethod return the failed shim and suppress
                // that re-instrumentation. Invoke(true) may already have committed the patch before
                // a later failure, so both states are dropped here. Losing the transplant state in
                // the same call is safe: with no live patch, pause-point never picks
                // TransplantChainJoin and so never reads it.
                generation.AbandonPatch(method);
                generation.DeactivatePatch(method);
                HotReloadInvocationRegistry.Remove(HotReloadMethodKeys.FormatMethodLabel(method));
                // User-approved exception to the no-try-catch policy: Harmony emit/JIT
                // failures cannot be pre-validated (the IL shape is only known inside
                // Harmony), and an escaping exception would abort the whole run while
                // silently leaving previously patched methods active. Contain it as this
                // method's Failed outcome so the per-method contract holds. Unpatch removes
                // the transpiler this call registered before failing and rebuilds the
                // wrapper, restoring the original body (verified by the extern-shim test).
                _harmony.Unpatch(method, HarmonyPatchType.Transpiler, HotReloadConstants.HarmonyId);
                // Why after Unpatch: retarget may have already replaced markers onto the shim;
                // restore them onto the original body now that GetActiveShim is null.
                HotReloadPausePointCoordination.PausePointSide?.OnHotReloadPatchStateChanged(method, false);
                Exception rootCause = exception;
                while (rootCause.InnerException != null)
                {
                    rootCause = rootCause.InnerException;
                }

                string rootCauseSuffix = ReferenceEquals(rootCause, exception)
                    ? string.Empty
                    : $" (root cause: {rootCause.GetType().Name}: {rootCause.Message})";
                return HotReloadPatchResult.Failure(
                    HotReloadPatchFailureReason.ApplyFailed,
                    $"Applying the patch to '{method}' failed: " +
                    $"{exception.GetType().Name}: {exception.Message}{rootCauseSuffix}");
            }

            return HotReloadPatchResult.SuccessResult(IsLikelyJitInlined(method));
        }

        /// <summary>
        /// Removes every hot-reload patch owned by this patcher and clears every
        /// domain-scoped store.
        /// </summary>
        public void RevertAll()
        {
            // Snapshot and empty the domain BEFORE UnpatchAll: Harmony rebuilds every patched
            // method during UnpatchAll, and the pause-point transpiler guard must see those
            // methods as unpatched so armed markers are re-instrumented into the restored
            // original IL. Shim registration clears in the same window so GetActiveShimForMethod
            // and GetShimLookupForFile agree during rebuild.
            IReadOnlyList<MethodBase> revertedMethods = _domain.RevertAll();
            _harmony.UnpatchAll(HotReloadConstants.HarmonyId);
            foreach (MethodBase revertedMethod in revertedMethods)
            {
                HotReloadPausePointCoordination.PausePointSide?.OnHotReloadPatchStateChanged(revertedMethod, false);
            }
        }

        /// <summary>
        /// Removes the hot-reload patch on <paramref name="method"/> when one is recorded.
        /// </summary>
        public HotReloadRevertOutcome Revert(MethodBase method, out string failureReason)
        {
            Debug.Assert(method != null, "method must not be null.");

            failureReason = null;
            // Why Remove before Unpatch: Harmony rebuilds the method during Unpatch, and the
            // pause-point guard sees GetActiveShimForMethod == null so surviving markers are
            // re-instrumented into the restored original IL. Registry removal stays in the same
            // pre-Unpatch window so lookup and ledger never disagree mid-rebuild.
            // The removed entry is kept for the failure path: status, the Auto Refresh hold, the
            // next apply's "unpatch the previous transpiler first" decision and pause-point's
            // chain-join offsets all read it, so a rebuild failure that leaves the patch live must
            // leave the whole entry describing that patch.
            HotReloadFileGeneration generation = _domain.FindGenerationForMethod(method);
            HotReloadActivePatchEntry removedEntry = generation?.DeactivatePatch(method);
            if (removedEntry == null)
            {
                return HotReloadRevertOutcome.NotPatched;
            }

            generation.RemoveShimMethod(method);
            string methodKey = HotReloadMethodKeys.FormatMethodLabel(method);
            HotReloadInvocationRegistry.Remove(methodKey);
            // Why here, not only RevertAll: RevertUnchangedPatches uses this path, and a
            // later apply of the same compiled key must not inherit a stale superseded Reason.
            generation.RemoveSupersededSignature(methodKey);
            try
            {
                Unpatch(method);
            }
            catch (Exception exception)
            {
                // Same approved exception as the apply path: a Harmony rebuild failure cannot be
                // pre-validated, and an escaping exception would abort the run while leaving the
                // other methods of the group unreverted. Unlike the apply path, which converges
                // on "no ledger entry, no patch" through its own cleanup Unpatch, a failed
                // rebuild here can leave the transpiler live: restore the ledger entry in that
                // case only, so status and the next apply still see the patch that is there.
                failureReason = "Reverting '" + methodKey + "' failed: " + exception.Message;
                HotReloadPatcherLog.LogHotReloadRevertFailed(methodKey, exception);
                if (HasLiveHotReloadTranspiler(method))
                {
                    generation.ReactivatePatch(removedEntry);
                    return HotReloadRevertOutcome.UnpatchFailed;
                }

                HotReloadPausePointCoordination.PausePointSide?.OnHotReloadPatchStateChanged(method, false);
                return HotReloadRevertOutcome.UnpatchFailed;
            }

            HotReloadPausePointCoordination.PausePointSide?.OnHotReloadPatchStateChanged(method, false);
            return HotReloadRevertOutcome.Reverted;
        }

        // Whether Harmony still holds this hot-reload transpiler, which decides if a failed
        // rebuild left the patch live.
        private static bool HasLiveHotReloadTranspiler(MethodBase method)
        {
            Patches patchInfo = Harmony.GetPatchInfo(method);
            if (patchInfo == null || patchInfo.Transpilers == null)
            {
                return false;
            }

            foreach (Patch transpiler in patchInfo.Transpilers)
            {
                if (string.Equals(transpiler.owner, HotReloadConstants.HarmonyId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private void Unpatch(MethodBase method)
        {
            _harmony.Unpatch(method, HarmonyPatchType.Transpiler, HotReloadConstants.HarmonyId);
        }

        /// <summary>
        /// How many methods currently have a live hot-reload patch.
        /// </summary>
        public int ActivePatchCount => _domain.ActivePatchCount;

        /// <summary>
        /// Harmony patches plus added-method shims. Domain reload drops both, so Play-entry
        /// warnings and ActivePatchTotal count this sum.
        /// </summary>
        public int ActiveChangeCount => _domain.ActiveChangeCount;

        /// <summary>
        /// Returns a sorted list of active patches (method key + source file path) for status
        /// reporting without applying or reverting patches.
        /// </summary>
        public IReadOnlyList<HotReloadActivePatchInfo> DescribeActivePatches()
        {
            return _domain.DescribeActivePatches();
        }

        // Why projectRelativePath, not DescribeActivePatches FilePath filtering by callers: a
        // patch belongs to the generation of the orchestrator's project-relative path.
        public IReadOnlyList<string> ListActiveMethodKeys(string projectRelativePath)
        {
            return _domain.ListActiveMethodKeys(projectRelativePath);
        }

        public IReadOnlyList<string> ListActiveFilePaths()
        {
            return _domain.ListActiveFilePaths();
        }

        private static IEnumerable<CodeInstruction> ReplaceWithTransplantSourceTranspiler(
            IEnumerable<CodeInstruction> instructions,
            ILGenerator generator,
            MethodBase original)
        {
            HotReloadFileGeneration generation = TranspilerDomain.FindGenerationForMethod(original);
            MethodInfo shimMethod = generation?.FindPatchShim(original);
            if (shimMethod == null)
            {
                throw new InvalidOperationException("Shim must be registered before Patch runs.");
            }

            // Discard the original (and any prior transpiler) instructions entirely — the shim IL
            // is the whole replacement body.
            // Read shim IL without letting MethodBodyReader declare locals on a throwaway path, then
            // declare locals on THIS patch ILGenerator and rebind short-form ldloc/stloc onto those
            // LocalBuilders. Numeric short-forms left as-is produce InvalidProgramException after
            // transplant when the shim body has locals (typical for object-initializer locals).
            // Labels need the same treatment: see HotReloadPatchIlRebind.RebindLabels.
            List<CodeInstruction> transplanted =
                new List<CodeInstruction>(PatchProcessor.GetOriginalInstructions(shimMethod));
            IReadOnlyList<LocalBuilder> transplantLocals =
                HotReloadPatchIlRebind.RebindShortFormLocals(shimMethod, generator, transplanted);
            generation.RecordTransplantLocals(original, transplantLocals);
            HotReloadPatchIlRebind.RebindLabels(generator, transplanted);
            PrependInvocationCountIncrement(transplanted, original, generation);
            return transplanted;
        }

        // Why discard the original instructions: the delegation body is a plain forward — load
        // every argument slot (slot 0 is `this` for instance methods), call the shim, return its
        // result. The shim was compiled without skip-visibility, so it JIT-compiles normally and
        // its accessor delegates reach the members this assembly boundary would otherwise forbid.
        private static IEnumerable<CodeInstruction> ReplaceWithDelegationTranspiler(
            IEnumerable<CodeInstruction> instructions,
            MethodBase original)
        {
            HotReloadFileGeneration generation = TranspilerDomain.FindGenerationForMethod(original);
            MethodInfo shimMethod = generation?.FindPatchShim(original);
            if (shimMethod == null)
            {
                throw new InvalidOperationException("Shim must be registered before Patch runs.");
            }

            int argumentSlotCount = original.GetParameters().Length + (original.IsStatic ? 0 : 1);
            if (argumentSlotCount != shimMethod.GetParameters().Length)
            {
                throw new InvalidOperationException(
                    "Shim parameter count must equal the original's argument slots (instance receiver included).");
            }

            List<CodeInstruction> forwarding = new List<CodeInstruction>(argumentSlotCount + 4);
            PrependInvocationCountIncrement(forwarding, original, generation);
            for (int slot = 0; slot < argumentSlotCount; slot++)
            {
                forwarding.Add(CreateLoadArgumentInstruction(slot));
            }

            forwarding.Add(new CodeInstruction(OpCodes.Call, shimMethod));
            forwarding.Add(new CodeInstruction(OpCodes.Ret));
            return forwarding;
        }

        // Why resolve through a method: every patched body calls this counter, so a signature the
        // transpiler cannot find has to stop the patch here rather than emit a call to null.
        private static MethodInfo ResolveIncrementInvocationMethod()
        {
            MethodInfo method = typeof(HotReloadInvocationRegistry).GetMethod(
                nameof(HotReloadInvocationRegistry.Increment),
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(string) },
                null);
            if (method == null)
            {
                throw new InvalidOperationException("Increment method must resolve.");
            }

            return method;
        }

        // What: records one invocation before the patched body runs (transplant or delegation).
        // Why move entry labels onto Ldstr: branches targeting the old first instruction must
        // still hit the counter when that instruction is no longer at offset 0.
        private static void PrependInvocationCountIncrement(
            List<CodeInstruction> instructions,
            MethodBase original,
            HotReloadFileGeneration generation)
        {
            CodeInstruction loadKey = new CodeInstruction(OpCodes.Ldstr, HotReloadMethodKeys.FormatMethodLabel(original));
            CodeInstruction increment = new CodeInstruction(OpCodes.Call, IncrementInvocationMethodInfo);
            if (instructions.Count > 0 && instructions[0].labels.Count > 0)
            {
                loadKey.labels.AddRange(instructions[0].labels);
                instructions[0].labels.Clear();
            }

            int countBeforeInsert = instructions.Count;
            instructions.Insert(0, increment);
            instructions.Insert(0, loadKey);
            generation.RecordTransplantPreambleLength(original, instructions.Count - countBeforeInsert);
        }

        private static CodeInstruction CreateLoadArgumentInstruction(int slot)
        {
            Debug.Assert(
                slot >= 0 && slot <= byte.MaxValue,
                "Argument slot must fit Ldarg_S's byte operand.");

            if (slot == 0)
            {
                return new CodeInstruction(OpCodes.Ldarg_0);
            }

            if (slot == 1)
            {
                return new CodeInstruction(OpCodes.Ldarg_1);
            }

            if (slot == 2)
            {
                return new CodeInstruction(OpCodes.Ldarg_2);
            }

            if (slot == 3)
            {
                return new CodeInstruction(OpCodes.Ldarg_3);
            }

            return new CodeInstruction(OpCodes.Ldarg_S, (byte)slot);
        }

        // Why internal: preflight validation must run these five checks before any
        // registry write or Harmony Patch. The method itself has no side effects.
        internal static HotReloadPatchResult CheckPatchable(MethodBase method)
        {
            if (method.IsAbstract)
            {
                return HotReloadPatchResult.Failure(
                    HotReloadPatchFailureReason.UnpatchableAbstract,
                    $"'{method}' is abstract and has no method body to patch.");
            }

            if (method.GetMethodBody() == null)
            {
                return HotReloadPatchResult.Failure(
                    HotReloadPatchFailureReason.UnpatchableExtern,
                    $"'{method}' has no IL method body (extern or an internal call) and cannot be patched.");
            }

            if (method.ContainsGenericParameters)
            {
                return HotReloadPatchResult.Failure(
                    HotReloadPatchFailureReason.UnpatchableOpenGeneric,
                    $"'{method}' is declared with unresolved generic type parameters and cannot be safely patched.");
            }

            if (HasBurstCompileAttribute(method) || HasBurstCompileAttribute(method.DeclaringType))
            {
                return HotReloadPatchResult.Failure(
                    HotReloadPatchFailureReason.UnpatchableBurstCompiled,
                    $"'{method}' (or its declaring type) is marked [BurstCompile] and cannot be patched.");
            }

            // Value-type instance transplant needs byref `this` semantics that transplant has not validated.
            if (method.DeclaringType != null && method.DeclaringType.IsValueType)
            {
                return HotReloadPatchResult.Failure(
                    HotReloadPatchFailureReason.UnpatchableValueType,
                    $"'{method}' is declared on a value type; struct method transplant is not supported.");
            }

            return HotReloadPatchResult.SuccessResult();
        }

        private static bool HasBurstCompileAttribute(MemberInfo member)
        {
            if (member == null)
            {
                return false;
            }

            foreach (object attribute in member.GetCustomAttributes(inherit: false))
            {
                if (attribute.GetType().FullName == HotReloadConstants.BurstCompileAttributeFullName)
                {
                    return true;
                }
            }

            return false;
        }

        // Heuristic only: [AggressiveInlining] is a hint, and IL size cannot predict Mono's real
        // inlining decision. Exists to surface a warning when HitCount-like symptoms appear.
        // Why inject codeOptimization: Debug mode does not inline tiny getters into warmed callers
        // (measured PR-4 To-Do 12), so the IL-size heuristic must stay Release-only.
        private static bool IsLikelyJitInlined(MethodBase method)
        {
            bool hasAggressiveInlining =
                (method.GetMethodImplementationFlags() & MethodImplAttributes.AggressiveInlining) != 0;
            byte[] ilBytes = method.GetMethodBody()?.GetILAsByteArray();
            int? ilByteLength = ilBytes == null ? (int?)null : ilBytes.Length;
            return HotReloadJitInliningRisk.Evaluate(
                hasAggressiveInlining,
                ilByteLength,
                CompilationPipeline.codeOptimization);
        }
    }
}
