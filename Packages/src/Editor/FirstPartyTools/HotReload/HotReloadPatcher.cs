using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

using HarmonyLib;

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
    internal static class HotReloadPatcher
    {
        private static readonly Harmony HarmonyInstance = new Harmony(HotReloadConstants.HarmonyId);
        private static readonly MethodInfo TransplantTranspilerMethodInfo =
            typeof(HotReloadPatcher).GetMethod(
                nameof(ReplaceWithTransplantSourceTranspiler),
                BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo DelegationTranspilerMethodInfo =
            typeof(HotReloadPatcher).GetMethod(
                nameof(ReplaceWithDelegationTranspiler),
                BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo IncrementInvocationMethodInfo =
            typeof(HotReloadInvocationRegistry).GetMethod(
                nameof(HotReloadInvocationRegistry.Increment),
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(string) },
                null);

        // Ledger: key = patched original method, value = the shim whose call replaced it.
        private static readonly Dictionary<MethodBase, MethodInfo> ShimByMethod =
            new Dictionary<MethodBase, MethodInfo>();

        // Transplant LocalBuilders (shim slot order) from the latest rebuild of each original.
        private static readonly Dictionary<MethodBase, IReadOnlyList<LocalBuilder>> TransplantLocalsByMethod =
            new Dictionary<MethodBase, IReadOnlyList<LocalBuilder>>();

        // Harmony resolves transpilers as static methods, so the shim cannot be a parameter.
        // Production looks up the target method in the ledger; the apply path also stashes the
        // pending shim here so the first Patch call can read it before the ledger entry
        // exists (Patch invokes the transpiler synchronously on the main thread).
        private static MethodInfo _pendingShimMethod;
        private static MethodBase _pendingOriginalMethod;

        static HotReloadPatcher()
        {
            // Why also expose _pending*: with Priority.First, hot-reload runs before pause-point
            // during Apply. The ledger is written only after Patch returns, so without the pending
            // shim pause-point would try to inject original-body indexes into the shim stream.
            HotReloadPausePointCoordination.GetActiveShimForMethod = method =>
            {
                if (ShimByMethod.TryGetValue(method, out MethodInfo shimMethod))
                {
                    return shimMethod;
                }

                // Why Equals (not ReferenceEquals): match MethodBase equality used by
                // ShimByMethod.TryGetValue rather than Harmony's instance identity.
                if (_pendingShimMethod != null
                    && _pendingOriginalMethod != null
                    && method != null
                    && method.Equals(_pendingOriginalMethod))
                {
                    return _pendingShimMethod;
                }

                return null;
            };
            HotReloadPausePointCoordination.GetTransplantLocals = method =>
                TransplantLocalsByMethod.TryGetValue(method, out IReadOnlyList<LocalBuilder> locals)
                    ? locals
                    : null;
        }

        /// <summary>
        /// Patches <paramref name="method"/> with <paramref name="shimMethodInfo"/> using
        /// <paramref name="patchShape"/>. Re-applying the same method Unpatches the previous
        /// transpiler first so patches do not stack.
        /// Engine failures during apply never throw; they are contained as an
        /// <see cref="HotReloadPatchFailureReason.ApplyFailed"/> result for that method only.
        /// </summary>
        public static HotReloadPatchResult Apply(
            MethodBase method,
            MethodInfo shimMethodInfo,
            HotReloadPatchShape patchShape)
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

            if (ShimByMethod.ContainsKey(method))
            {
                // Why ledger before Unpatch: same as Revert — during Unpatch Harmony rebuilds
                // the method and pause-point ChainJoin must see GetActiveShimForMethod == null,
                // or it injects donor instruction indexes into the restored original IL stream.
                // Do not RemoveMethod from the shim registry here: ApplyEntry already registered
                // this method into the new generation before calling Apply.
                ShimByMethod.Remove(method);
                TransplantLocalsByMethod.Remove(method);
                HotReloadInvocationRegistry.Remove(FormatMethodKey(method));
                HarmonyInstance.Unpatch(method, HarmonyPatchType.Transpiler, HotReloadConstants.HarmonyId);
                // Mirror the ledger removal: if the re-Patch below fails, its contained Unpatch
                // rebuilds with markers restored (ledger empty), and RevertAll can never reach
                // this method again — leaving suppress stuck true would make status lie forever.
                HotReloadPausePointCoordination.OnHotReloadPatchStateChanged?.Invoke(method, false);
            }

            MethodInfo transpilerMethodInfo = patchShape == HotReloadPatchShape.Delegation
                ? DelegationTranspilerMethodInfo
                : TransplantTranspilerMethodInfo;

            // Ledger is updated after Patch succeeds. During Patch the transpiler reads the
            // pending shim because Harmony resolves transpilers statically (no MethodInfo arg).
            _pendingShimMethod = shimMethodInfo;
            _pendingOriginalMethod = method;
            try
            {
                // Why Priority.First: same numeric priority sorts by registration index, and
                // Unpatch does not reindex — Patch/Unpatch cycles make same-priority order
                // unstable. pause-point must run after hot-reload so it sees the shim stream.
                HarmonyInstance.Patch(
                    method,
                    transpiler: new HarmonyMethod(transpilerMethodInfo)
                    {
                        priority = Priority.First
                    });
                ShimByMethod[method] = shimMethodInfo;
                HotReloadPausePointCoordination.OnHotReloadPatchStateChanged?.Invoke(method, true);
            }
            catch (Exception exception)
            {
                // Why clear pending before Unpatch: cleanup rebuild must see "not patched"
                // so pause-point markers re-instrument the restored original body. Leaving
                // pending set would make GetActiveShimForMethod return the failed shim and
                // suppress that re-instrumentation (regression vs the old ContainsKey probe).
                _pendingShimMethod = null;
                _pendingOriginalMethod = null;
                // Why Remove before Unpatch: Invoke(true) may have written ShimByMethod before
                // a later failure; rebuild must not see an active shim (same as re-apply path).
                ShimByMethod.Remove(method);
                HotReloadInvocationRegistry.Remove(FormatMethodKey(method));
                // User-approved exception to the no-try-catch policy: Harmony emit/JIT
                // failures cannot be pre-validated (the IL shape is only known inside
                // Harmony), and an escaping exception would abort the whole run while
                // silently leaving previously patched methods active. Contain it as this
                // method's Failed outcome so the per-method contract holds. Unpatch removes
                // the transpiler this call registered before failing and rebuilds the
                // wrapper, restoring the original body (verified by the extern-shim test).
                HarmonyInstance.Unpatch(method, HarmonyPatchType.Transpiler, HotReloadConstants.HarmonyId);
                TransplantLocalsByMethod.Remove(method);
                // Why after Unpatch: retarget may have already replaced markers onto the shim;
                // restore them onto the original body now that GetActiveShim is null.
                HotReloadPausePointCoordination.OnHotReloadPatchStateChanged?.Invoke(method, false);
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
                    $"{exception.GetType().Name}: {exception.Message}{rootCauseSuffix} " +
                    "Other methods in this run are unaffected.");
            }
            finally
            {
                _pendingShimMethod = null;
                _pendingOriginalMethod = null;
            }

            return HotReloadPatchResult.SuccessResult(IsLikelyJitInlined(method));
        }

        /// <summary>
        /// Removes every hot-reload patch owned by this patcher and clears the ledger.
        /// </summary>
        public static void RevertAll()
        {
            // Snapshot and clear the ledger BEFORE UnpatchAll: Harmony rebuilds every
            // patched method during UnpatchAll, and the pause-point transpiler guard
            // must see those methods as unpatched so armed markers are re-instrumented
            // into the restored original IL. Shim registration clears in the same window
            // so GetActiveShimForMethod / GetShimLookupForFile agree during rebuild.
            List<MethodBase> revertedMethods = new List<MethodBase>(ShimByMethod.Keys);
            ShimByMethod.Clear();
            HotReloadShimRegistry.Clear();
            TransplantLocalsByMethod.Clear();
            HotReloadInvocationRegistry.Clear();
            _pendingShimMethod = null;
            _pendingOriginalMethod = null;
            HarmonyInstance.UnpatchAll(HotReloadConstants.HarmonyId);
            foreach (MethodBase revertedMethod in revertedMethods)
            {
                HotReloadPausePointCoordination.OnHotReloadPatchStateChanged?.Invoke(revertedMethod, false);
            }
        }

        /// <summary>
        /// Removes the hot-reload patch on <paramref name="method"/> when one is recorded.
        /// Returns false when the method was not patched.
        /// </summary>
        public static bool Revert(MethodBase method)
        {
            Debug.Assert(method != null, "method must not be null.");

            if (!ShimByMethod.Remove(method))
            {
                return false;
            }

            // Why Remove before Unpatch: Harmony rebuilds the method during Unpatch, and the
            // pause-point guard sees GetActiveShimForMethod == null so surviving markers are
            // re-instrumented into the restored original IL. Registry removal stays in the same
            // pre-Unpatch window so lookup and ledger never disagree mid-rebuild.
            HotReloadShimRegistry.RemoveMethod(method);
            TransplantLocalsByMethod.Remove(method);
            HotReloadInvocationRegistry.Remove(FormatMethodKey(method));
            HarmonyInstance.Unpatch(method, HarmonyPatchType.Transpiler, HotReloadConstants.HarmonyId);
            HotReloadPausePointCoordination.OnHotReloadPatchStateChanged?.Invoke(method, false);
            return true;
        }

        /// <summary>
        /// How many methods currently have an active patch recorded in the ledger.
        /// </summary>
        public static int ActivePatchCount => ShimByMethod.Count;

        /// <summary>
        /// Returns a sorted list of labels for every method currently recorded in the
        /// patch ledger, for status reporting without applying or reverting patches.
        /// </summary>
        public static IReadOnlyList<string> DescribeActivePatches()
        {
            List<string> labels = new List<string>(ShimByMethod.Count);
            foreach (MethodBase method in ShimByMethod.Keys)
            {
                labels.Add(FormatMethodKey(method));
            }

            labels.Sort(StringComparer.Ordinal);
            return labels;
        }

        // What: status label shared by DescribeActivePatches and the IL-injected counter key.
        internal static string FormatMethodKey(MethodBase method)
        {
            Debug.Assert(method != null, "method must not be null.");
            Debug.Assert(method.DeclaringType != null, "Patched methods must have a declaring type.");
            return method.DeclaringType.FullName + "." + method.Name;
        }

        private static IEnumerable<CodeInstruction> ReplaceWithTransplantSourceTranspiler(
            IEnumerable<CodeInstruction> instructions,
            ILGenerator generator,
            MethodBase original)
        {
            MethodInfo shimMethod = ResolveShimMethod(original);
            Debug.Assert(shimMethod != null, "Shim must be registered before Patch runs.");
            // Discard the original (and any prior transpiler) instructions entirely — the shim IL
            // is the whole replacement body.
            // Read shim IL without letting MethodBodyReader declare locals on a throwaway path, then
            // declare locals on THIS patch ILGenerator and rebind short-form ldloc/stloc onto those
            // LocalBuilders. Numeric short-forms left as-is produce InvalidProgramException after
            // transplant when the shim body has locals (typical for object-initializer locals).
            // Labels need the same treatment: see RebindLabels below.
            List<CodeInstruction> transplanted =
                new List<CodeInstruction>(PatchProcessor.GetOriginalInstructions(shimMethod));
            IReadOnlyList<LocalBuilder> transplantLocals =
                RebindShortFormLocals(shimMethod, generator, transplanted);
            TransplantLocalsByMethod[original] = transplantLocals;
            RebindLabels(generator, transplanted);
            PrependInvocationCountIncrement(transplanted, original);
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
            MethodInfo shimMethod = ResolveShimMethod(original);
            Debug.Assert(shimMethod != null, "Shim must be registered before Patch runs.");

            int argumentSlotCount = original.GetParameters().Length + (original.IsStatic ? 0 : 1);
            Debug.Assert(
                argumentSlotCount == shimMethod.GetParameters().Length,
                "Shim parameter count must equal the original's argument slots (instance receiver included).");

            List<CodeInstruction> forwarding = new List<CodeInstruction>(argumentSlotCount + 4);
            PrependInvocationCountIncrement(forwarding, original);
            for (int slot = 0; slot < argumentSlotCount; slot++)
            {
                forwarding.Add(CreateLoadArgumentInstruction(slot));
            }

            forwarding.Add(new CodeInstruction(OpCodes.Call, shimMethod));
            forwarding.Add(new CodeInstruction(OpCodes.Ret));
            return forwarding;
        }

        // What: records one invocation before the patched body runs (transplant or delegation).
        // Why move entry labels onto Ldstr: branches targeting the old first instruction must
        // still hit the counter when that instruction is no longer at offset 0.
        private static void PrependInvocationCountIncrement(
            List<CodeInstruction> instructions,
            MethodBase original)
        {
            Debug.Assert(IncrementInvocationMethodInfo != null, "Increment method must resolve.");
            CodeInstruction loadKey = new CodeInstruction(OpCodes.Ldstr, FormatMethodKey(original));
            CodeInstruction increment = new CodeInstruction(OpCodes.Call, IncrementInvocationMethodInfo);
            if (instructions.Count > 0 && instructions[0].labels.Count > 0)
            {
                loadKey.labels.AddRange(instructions[0].labels);
                instructions[0].labels.Clear();
            }

            instructions.Insert(0, increment);
            instructions.Insert(0, loadKey);
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

        private static MethodInfo ResolveShimMethod(MethodBase original)
        {
            if (ShimByMethod.TryGetValue(original, out MethodInfo shimMethod))
            {
                return shimMethod;
            }

            return _pendingShimMethod;
        }

        private static HotReloadPatchResult CheckPatchable(MethodBase method)
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

            // Value-type instance transplant needs byref `this` semantics that v1 has not validated.
            if (method.DeclaringType != null && method.DeclaringType.IsValueType)
            {
                return HotReloadPatchResult.Failure(
                    HotReloadPatchFailureReason.UnpatchableValueType,
                    $"'{method}' is declared on a value type; struct method transplant is out of scope for v1.");
            }

            return HotReloadPatchResult.SuccessResult();
        }

        private static IReadOnlyList<LocalBuilder> RebindShortFormLocals(
            MethodInfo shimMethod,
            ILGenerator generator,
            List<CodeInstruction> instructions)
        {
            Debug.Assert(shimMethod != null, "shimMethod must not be null.");
            Debug.Assert(generator != null, "generator must not be null.");
            Debug.Assert(instructions != null, "instructions must not be null.");

            MethodBody methodBody = shimMethod.GetMethodBody();
            if (methodBody == null || methodBody.LocalVariables.Count == 0)
            {
                return Array.Empty<LocalBuilder>();
            }

            LocalBuilder[] locals = new LocalBuilder[methodBody.LocalVariables.Count];
            for (int localIndex = 0; localIndex < locals.Length; localIndex++)
            {
                LocalVariableInfo localVariable = methodBody.LocalVariables[localIndex];
                locals[localIndex] = generator.DeclareLocal(localVariable.LocalType, localVariable.IsPinned);
            }

            for (int instructionIndex = 0; instructionIndex < instructions.Count; instructionIndex++)
            {
                CodeInstruction instruction = instructions[instructionIndex];
                int localIndex;
                bool isAddress;
                bool isStore;
                if (!TryGetLocalOpcodeShape(instruction, out localIndex, out isStore, out isAddress))
                {
                    continue;
                }

                Debug.Assert(
                    localIndex >= 0 && localIndex < locals.Length,
                    "Local index from shim IL must fall within declared locals.");

                OpCode opCode = isStore
                    ? OpCodes.Stloc
                    : (isAddress ? OpCodes.Ldloca : OpCodes.Ldloc);
                instructions[instructionIndex] = new CodeInstruction(opCode, locals[localIndex])
                {
                    labels = instruction.labels,
                    blocks = instruction.blocks
                };
            }

            return locals;
        }

        // Labels read from the shim without this patch's ILGenerator belong to a throwaway
        // generator (same failure family as the LocalBuilder rebinding above): the real
        // CecilILGenerator resolves Label structs against its own table, so a foreign label
        // NREs at emit — or silently branches to the wrong target when indices happen to
        // collide with labels it did define.
        private static void RebindLabels(ILGenerator generator, List<CodeInstruction> instructions)
        {
            Debug.Assert(generator != null, "RebindLabels requires the patch method ILGenerator.");
            Debug.Assert(instructions != null, "RebindLabels requires the transplanted instructions.");

            Dictionary<Label, Label> ownedLabelByForeign = new Dictionary<Label, Label>();

            for (int instructionIndex = 0; instructionIndex < instructions.Count; instructionIndex++)
            {
                CodeInstruction instruction = instructions[instructionIndex];
                if (instruction.operand is Label foreignTarget)
                {
                    instruction.operand = RemapLabel(generator, ownedLabelByForeign, foreignTarget);
                }
                else if (instruction.operand is Label[] foreignTargets)
                {
                    Label[] ownedTargets = new Label[foreignTargets.Length];
                    for (int targetIndex = 0; targetIndex < foreignTargets.Length; targetIndex++)
                    {
                        ownedTargets[targetIndex] =
                            RemapLabel(generator, ownedLabelByForeign, foreignTargets[targetIndex]);
                    }

                    instruction.operand = ownedTargets;
                }

                for (int labelIndex = 0; labelIndex < instruction.labels.Count; labelIndex++)
                {
                    instruction.labels[labelIndex] =
                        RemapLabel(generator, ownedLabelByForeign, instruction.labels[labelIndex]);
                }
            }
        }

        private static Label RemapLabel(
            ILGenerator generator,
            Dictionary<Label, Label> ownedLabelByForeign,
            Label foreignLabel)
        {
            if (ownedLabelByForeign.TryGetValue(foreignLabel, out Label ownedLabel))
            {
                return ownedLabel;
            }

            ownedLabel = generator.DefineLabel();
            ownedLabelByForeign[foreignLabel] = ownedLabel;
            return ownedLabel;
        }

        private static bool TryGetLocalOpcodeShape(
            CodeInstruction instruction,
            out int localIndex,
            out bool isStore,
            out bool isAddress)
        {
            localIndex = -1;
            isStore = false;
            isAddress = false;

            OpCode opCode = instruction.opcode;
            if (opCode == OpCodes.Ldloc_0 || opCode == OpCodes.Stloc_0)
            {
                localIndex = 0;
                isStore = opCode == OpCodes.Stloc_0;
                return true;
            }

            if (opCode == OpCodes.Ldloc_1 || opCode == OpCodes.Stloc_1)
            {
                localIndex = 1;
                isStore = opCode == OpCodes.Stloc_1;
                return true;
            }

            if (opCode == OpCodes.Ldloc_2 || opCode == OpCodes.Stloc_2)
            {
                localIndex = 2;
                isStore = opCode == OpCodes.Stloc_2;
                return true;
            }

            if (opCode == OpCodes.Ldloc_3 || opCode == OpCodes.Stloc_3)
            {
                localIndex = 3;
                isStore = opCode == OpCodes.Stloc_3;
                return true;
            }

            if (opCode == OpCodes.Ldloc_S || opCode == OpCodes.Stloc_S || opCode == OpCodes.Ldloca_S)
            {
                localIndex = ReadLocalIndexOperand(instruction.operand);
                isStore = opCode == OpCodes.Stloc_S;
                isAddress = opCode == OpCodes.Ldloca_S;
                return localIndex >= 0;
            }

            if (opCode == OpCodes.Ldloc || opCode == OpCodes.Stloc || opCode == OpCodes.Ldloca)
            {
                // Rebind even when the operand is already a LocalBuilder: GetOriginalInstructions
                // without this patch's ILGenerator yields builders that are not owned by it.
                localIndex = ReadLocalIndexOperand(instruction.operand);
                isStore = opCode == OpCodes.Stloc;
                isAddress = opCode == OpCodes.Ldloca;
                return localIndex >= 0;
            }

            return false;
        }

        private static int ReadLocalIndexOperand(object operand)
        {
            if (operand is byte byteIndex)
            {
                return byteIndex;
            }

            if (operand is ushort ushortIndex)
            {
                return ushortIndex;
            }

            if (operand is int intIndex)
            {
                return intIndex;
            }

            if (operand is LocalBuilder localBuilder)
            {
                return localBuilder.LocalIndex;
            }

            return -1;
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
        private static bool IsLikelyJitInlined(MethodBase method)
        {
            if ((method.GetMethodImplementationFlags() & MethodImplAttributes.AggressiveInlining) != 0)
            {
                return true;
            }

            byte[] ilBytes = method.GetMethodBody()?.GetILAsByteArray();
            return ilBytes != null && ilBytes.Length <= HotReloadConstants.SmallMethodInliningRiskThresholdBytes;
        }
    }
}
