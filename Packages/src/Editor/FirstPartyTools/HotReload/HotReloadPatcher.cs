using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

using HarmonyLib;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Applies Harmony transplant patches that discard a target method's IL and emit a shim
    /// method's IL instead, so the body runs inside Harmony's skip-visibility DynamicMethod.
    /// </summary>
    internal static class HotReloadPatcher
    {
        private static readonly Harmony HarmonyInstance = new Harmony(HotReloadConstants.HarmonyId);
        private static readonly MethodInfo TransplantTranspilerMethodInfo =
            typeof(HotReloadPatcher).GetMethod(
                nameof(ReplaceWithTransplantSourceTranspiler),
                BindingFlags.NonPublic | BindingFlags.Static);

        // key = patched original method, value = transplant source shim.
        private static readonly Dictionary<MethodBase, MethodInfo> ShimByMethod =
            new Dictionary<MethodBase, MethodInfo>();

        // Harmony resolves transpilers as static methods, so the transplant source cannot be a
        // parameter. Production looks up the target method in the ledger; the apply path also
        // stashes the pending shim here so the first Patch call can read it before the ledger
        // entry exists (Patch invokes the transpiler synchronously on the main thread).
        private static MethodInfo _pendingTransplantSourceMethod;

        /// <summary>
        /// Transplants <paramref name="shimMethodInfo"/>'s IL into <paramref name="method"/>.
        /// Re-applying the same method Unpatches the previous transpiler first so patches do not stack.
        /// </summary>
        public static HotReloadPatchResult Apply(MethodBase method, MethodInfo shimMethodInfo)
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
                // A second hot reload of the same method must replace the previous transplant;
                // leaving it in place would stack transpilers and run discarded IL chains.
                HarmonyInstance.Unpatch(method, HarmonyPatchType.Transpiler, HotReloadConstants.HarmonyId);
                ShimByMethod.Remove(method);
            }

            // Ledger is updated after Patch succeeds. During Patch the transpiler reads the
            // pending field because Harmony resolves transpilers statically (no MethodInfo arg).
            _pendingTransplantSourceMethod = shimMethodInfo;
            try
            {
                HarmonyInstance.Patch(
                    method,
                    transpiler: new HarmonyMethod(TransplantTranspilerMethodInfo));
                ShimByMethod[method] = shimMethodInfo;
            }
            finally
            {
                _pendingTransplantSourceMethod = null;
            }

            string warning = IsLikelyJitInlined(method)
                ? HotReloadConstants.SmallMethodInliningRiskWarning
                : string.Empty;
            return HotReloadPatchResult.SuccessResult(warning);
        }

        /// <summary>
        /// Removes every hot-reload transplant owned by this patcher and clears the ledger.
        /// </summary>
        public static void RevertAll()
        {
            HarmonyInstance.UnpatchAll(HotReloadConstants.HarmonyId);
            ShimByMethod.Clear();
            _pendingTransplantSourceMethod = null;
        }

        /// <summary>
        /// How many methods currently have an active transplant recorded in the ledger.
        /// </summary>
        public static int ActivePatchCount => ShimByMethod.Count;

        private static IEnumerable<CodeInstruction> ReplaceWithTransplantSourceTranspiler(
            IEnumerable<CodeInstruction> instructions,
            ILGenerator generator,
            MethodBase original)
        {
            MethodInfo shimMethod = ResolveTransplantSource(original);
            Debug.Assert(shimMethod != null, "Transplant source must be registered before Patch runs.");
            // Discard the original (and any prior transpiler) instructions entirely — the shim IL
            // is the whole replacement body.
            // Read shim IL without letting MethodBodyReader declare locals on a throwaway path, then
            // declare locals on THIS patch ILGenerator and rebind short-form ldloc/stloc onto those
            // LocalBuilders. Numeric short-forms left as-is produce InvalidProgramException after
            // transplant when the shim body has locals (typical for object-initializer locals).
            List<CodeInstruction> transplanted =
                new List<CodeInstruction>(PatchProcessor.GetOriginalInstructions(shimMethod));
            RebindShortFormLocals(shimMethod, generator, transplanted);
            return transplanted;
        }

        private static void RebindShortFormLocals(
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
                return;
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

        private static MethodInfo ResolveTransplantSource(MethodBase original)
        {
            if (ShimByMethod.TryGetValue(original, out MethodInfo shimMethod))
            {
                return shimMethod;
            }

            return _pendingTransplantSourceMethod;
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
