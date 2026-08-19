using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

using HarmonyLib;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Emits the Harmony transpiler that injects SourcePausePointCapture.Capture into patched methods.
    /// </summary>
    internal static class SourcePausePointInjectionEmitter
    {
        internal static readonly MethodInfo TranspilerMethodInfo =
            typeof(SourcePausePointInjectionEmitter).GetMethod(nameof(Transpiler), BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo CaptureMethodInfo =
            typeof(SourcePausePointCapture).GetMethod(nameof(SourcePausePointCapture.Capture));
        private static readonly MethodInfo IsArmedMethodInfo =
            typeof(UloopPausePointRegistry).GetMethod(nameof(UloopPausePointRegistry.IsArmed));

        internal static bool IsByRefLikeType(Type type)
        {
            foreach (object attribute in type.GetCustomAttributes(inherit: false))
            {
                if (attribute.GetType().FullName == SourcePausePointConstants.IsByRefLikeAttributeFullName)
                {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions, ILGenerator generator, MethodBase original)
        {
            List<CodeInstruction> list = new(instructions);
            if (!SourcePausePointPatcher.InjectionsByMethod.TryGetValue(original, out List<SourcePausePointPatchInjection> injections))
            {
                return list;
            }

            MethodBase activeShim =
                HotReloadPausePointCoordination.GetActiveShimForMethod?.Invoke(original);

            foreach (SourcePausePointPatchInjection injection in injections.OrderByDescending(i => i.InstructionIndex))
            {
                if (!ShouldInject(injection, activeShim))
                {
                    continue;
                }

                Label skip = generator.DefineLabel();
                List<CodeInstruction> emitted = BuildInjection(injection, original, skip);

                // The instruction we insert before may be a branch target or a try/catch/finally
                // boundary marker; both must move to the new first instruction so control flow and
                // exception regions still land in the same place, now with our prefix folded in.
                Debug.Assert(
                    injection.InstructionIndex < list.Count,
                    "An injection's index must exist in the current instruction stream.");
                CodeInstruction displaced = list[injection.InstructionIndex];
                emitted[0].labels.AddRange(displaced.labels);
                displaced.labels.Clear();
                emitted[0].blocks.AddRange(displaced.blocks);
                displaced.blocks.Clear();

                // The not-armed guard above skips straight to the original displaced instruction,
                // so it needs its own label there to land on.
                displaced.labels.Add(skip);

                list.InsertRange(injection.InstructionIndex, emitted);
            }

            return list;
        }

        private static bool ShouldInject(SourcePausePointPatchInjection injection, MethodBase activeShim)
        {
            switch (injection.TargetKind)
            {
                case SourcePausePointPatchInjectionTargetKind.OriginalBody:
                    // Pre-hot-reload body indexes are only valid when no shim is active.
                    return activeShim == null;
                case SourcePausePointPatchInjectionTargetKind.TransplantChainJoin:
                    // Inject only while the donor shim that produced these indexes is still active.
                    return activeShim != null && activeShim.Equals(injection.DonorShim);
                case SourcePausePointPatchInjectionTargetKind.ShimDirect:
                    // ShimDirect injections live on the shim-side method's SourcePausePointPatcher.InjectionsByMethod entry;
                    // they never share a ledger key with an original-body method.
                    return true;
                default:
                    return false;
            }
        }

        private static List<CodeInstruction> BuildInjection(SourcePausePointPatchInjection injection, MethodBase method, Label skip)
        {
            // Checking IsArmed before building the parameter/local object array (rather than
            // relying on Capture's own check) keeps the overwhelmingly common not-armed hit
            // allocation-free: the array-build and boxing instructions below never execute unless armed.
            List<CodeInstruction> emitted = new()
            {
                new CodeInstruction(OpCodes.Ldstr, injection.Id),
                new CodeInstruction(OpCodes.Call, IsArmedMethodInfo),
                new CodeInstruction(OpCodes.Brfalse, skip),
                new CodeInstruction(OpCodes.Ldstr, injection.Id),
            };

            AppendInstanceLoad(emitted, injection, method);

            // InstanceFromFirstArgument stores absolute GetParameters() indexes (including the
            // hole left by skipping __uloopInstance at 0), so argOffset stays 0. Ordinary
            // instance methods still need +1 to skip the hidden `this` argument.
            int argOffset = injection.InstanceFromFirstArgument
                ? 0
                : (injection.IsStatic ? 0 : 1);

            // Fully qualify: ToolContracts also defines ParameterInfo (tool schema DTO).
            System.Reflection.ParameterInfo[] runtimeParameters = method.GetParameters();
            AppendNameValueArray(
                emitted,
                injection.Parameters,
                p => CodeInstruction.LoadArgument(p.Index + argOffset, false),
                p => p.IsValueType ? runtimeParameters[p.Index].ParameterType : null,
                p => p.Name);

            AppendLocalsLoad(emitted, injection, method);

            emitted.Add(new CodeInstruction(OpCodes.Call, CaptureMethodInfo));
            return emitted;
        }

        private static void AppendLocalsLoad(
            List<CodeInstruction> emitted,
            SourcePausePointPatchInjection injection,
            MethodBase method)
        {
            if (injection.TargetKind == SourcePausePointPatchInjectionTargetKind.TransplantChainJoin)
            {
                IReadOnlyList<LocalBuilder> transplantLocals =
                    HotReloadPausePointCoordination.GetTransplantLocals?.Invoke(method);
                MethodBody donorBody = injection.DonorShim != null
                    ? injection.DonorShim.GetMethodBody()
                    : null;
                List<SourcePausePointLocalVariable> capturable = new List<SourcePausePointLocalVariable>();
                Dictionary<int, Type> boxTypeBySlot = new Dictionary<int, Type>();
                foreach (SourcePausePointLocalVariable local in injection.Locals)
                {
                    if (transplantLocals == null
                        || local.SlotIndex < 0
                        || local.SlotIndex >= transplantLocals.Count
                        || donorBody == null
                        || local.SlotIndex >= donorBody.LocalVariables.Count)
                    {
                        // Why skip (not fail): a missing LocalBuilder mid-rebuild must not abort
                        // the whole injection; capture the locals that are still addressable.
                        Debug.Assert(
                            false,
                            "Transplant local slot must exist on the donor shim and LocalBuilder list.");
                        continue;
                    }

                    capturable.Add(local);
                    boxTypeBySlot[local.SlotIndex] = local.IsValueType
                        ? donorBody.LocalVariables[local.SlotIndex].LocalType
                        : null;
                }

                AppendNameValueArray(
                    emitted,
                    capturable,
                    l => new CodeInstruction(OpCodes.Ldloc, transplantLocals[l.SlotIndex]),
                    l => boxTypeBySlot[l.SlotIndex],
                    l => l.Name);
                return;
            }

            IList<LocalVariableInfo> runtimeLocals = method.GetMethodBody().LocalVariables;
            foreach (SourcePausePointLocalVariable local in injection.Locals)
            {
                Debug.Assert(
                    runtimeLocals[local.SlotIndex].LocalIndex == local.SlotIndex,
                    "LocalVariableInfo order must match the resolved slot index.");
            }
            AppendNameValueArray(
                emitted,
                injection.Locals,
                l => CodeInstruction.LoadLocal(l.SlotIndex, false),
                l => l.IsValueType ? runtimeLocals[l.SlotIndex].LocalType : null,
                l => l.Name);
        }

        private static void AppendInstanceLoad(List<CodeInstruction> emitted, SourcePausePointPatchInjection injection, MethodBase method)
        {
            if (injection.InstanceFromFirstArgument)
            {
                // Why no box: hot-reload rejects value-type instance methods as UnpatchableValueType,
                // so a delegation shim's __uloopInstance is always a reference-type receiver here.
                emitted.Add(CodeInstruction.LoadArgument(0, false));
                return;
            }

            if (injection.IsStatic)
            {
                emitted.Add(new CodeInstruction(OpCodes.Ldnull));
                return;
            }

            if (injection.IsDeclaringTypeValueType && IsByRefLikeType(method.DeclaringType))
            {
                // A byref-like (ref struct) `this` cannot be boxed at all (illegal IL); degrade to
                // a null instance so locals and parameters can still be captured, rather than
                // rejecting the whole patch over the instance fields alone.
                emitted.Add(new CodeInstruction(OpCodes.Ldnull));
                return;
            }

            emitted.Add(CodeInstruction.LoadArgument(0, false));
            if (injection.IsDeclaringTypeValueType)
            {
                // ldarg.0 on a value-type instance method yields a managed pointer (this is how
                // the CLR always passes "this" for struct instance methods); Capture takes
                // `object`, so it must be dereferenced to the value and boxed explicitly.
                emitted.Add(new CodeInstruction(OpCodes.Ldobj, method.DeclaringType));
                emitted.Add(new CodeInstruction(OpCodes.Box, method.DeclaringType));
            }
        }

        private static void AppendNameValueArray<T>(
            List<CodeInstruction> emitted,
            IReadOnlyList<T> items,
            Func<T, CodeInstruction> loadValue,
            Func<T, Type> boxTypeOrNull,
            Func<T, string> nameOf)
        {
            emitted.Add(new CodeInstruction(OpCodes.Ldc_I4, items.Count * 2));
            emitted.Add(new CodeInstruction(OpCodes.Newarr, typeof(object)));

            for (int i = 0; i < items.Count; i++)
            {
                T item = items[i];

                emitted.Add(new CodeInstruction(OpCodes.Dup));
                emitted.Add(new CodeInstruction(OpCodes.Ldc_I4, i * 2));
                emitted.Add(new CodeInstruction(OpCodes.Ldstr, nameOf(item)));
                emitted.Add(new CodeInstruction(OpCodes.Stelem_Ref));

                emitted.Add(new CodeInstruction(OpCodes.Dup));
                emitted.Add(new CodeInstruction(OpCodes.Ldc_I4, i * 2 + 1));
                emitted.Add(loadValue(item));
                Type boxType = boxTypeOrNull(item);
                if (boxType != null)
                {
                    emitted.Add(new CodeInstruction(OpCodes.Box, boxType));
                }
                emitted.Add(new CodeInstruction(OpCodes.Stelem_Ref));
            }
        }
    }
}
