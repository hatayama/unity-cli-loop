using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

using HarmonyLib;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Rebinds transplanted shim locals and labels onto the patch method's ILGenerator.
    /// </summary>
    internal static class HotReloadPatchIlRebind
    {
        internal static IReadOnlyList<LocalBuilder> RebindShortFormLocals(
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
        internal static void RebindLabels(ILGenerator generator, List<CodeInstruction> instructions)
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
    }
}
