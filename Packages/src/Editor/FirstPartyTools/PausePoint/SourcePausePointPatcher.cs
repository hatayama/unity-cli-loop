using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

using HarmonyLib;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Runtime;

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
        private static readonly MethodInfo TranspilerMethodInfo =
            typeof(SourcePausePointPatcher).GetMethod(nameof(Transpiler), BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo CaptureMethodInfo =
            typeof(SourcePausePointCapture).GetMethod(nameof(SourcePausePointCapture.Capture));
        private static readonly MethodInfo IsArmedMethodInfo =
            typeof(UloopPausePointRegistry).GetMethod(nameof(UloopPausePointRegistry.IsArmed));

        private static readonly Dictionary<MethodBase, List<SourcePausePointPatchInjection>> InjectionsByMethod = new();
        private static readonly Dictionary<string, MethodBase> MethodById = new();

        public static SourcePausePointPatchResult Patch(string id, SourcePausePointResolution resolution)
        {
            Debug.Assert(!string.IsNullOrEmpty(id), "id must not be null or empty.");
            Debug.Assert(resolution != null, "resolution must not be null.");

            if (MethodById.ContainsKey(id))
            {
                // Re-enabling an id that is already patched is a no-op here: the call site is
                // already in the IL and always fires, gated only by the registry's armed state.
                return SourcePausePointPatchResult.SuccessResult();
            }

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

            SourcePausePointPatchInjection injection = new(
                id,
                resolution.InstructionIndex,
                resolution.IsStatic,
                resolution.IsDeclaringTypeValueType,
                resolution.Parameters,
                resolution.Locals);

            bool methodAlreadyPatched = InjectionsByMethod.TryGetValue(method, out List<SourcePausePointPatchInjection> injections);
            if (!methodAlreadyPatched)
            {
                injections = new List<SourcePausePointPatchInjection>();
                InjectionsByMethod[method] = injections;
            }

            injections.Add(injection);
            MethodById[id] = method;

            if (methodAlreadyPatched)
            {
                // Harmony only regenerates a method's replacement when Patch/Unpatch is called;
                // re-declaring the same transpiler risks double-registration, so drop and redo it
                // to force a clean rebuild from the (untouched) original IL against the new injection set.
                HarmonyInstance.Unpatch(method, HarmonyPatchType.Transpiler, SourcePausePointConstants.HarmonyId);
            }
            HarmonyInstance.Patch(method, transpiler: new HarmonyMethod(TranspilerMethodInfo));

            return SourcePausePointPatchResult.SuccessResult();
        }

        public static void Unpatch(string id)
        {
            Debug.Assert(!string.IsNullOrEmpty(id), "id must not be null or empty.");

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
            }
            else
            {
                HarmonyInstance.Patch(method, transpiler: new HarmonyMethod(TranspilerMethodInfo));
            }
        }

        public static void UnpatchAll()
        {
            HarmonyInstance.UnpatchAll(SourcePausePointConstants.HarmonyId);
            InjectionsByMethod.Clear();
            MethodById.Clear();
        }

        private static SourcePausePointPatchResult TryResolveMethod(SourcePausePointResolution resolution, out MethodBase method)
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

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions, ILGenerator generator, MethodBase original)
        {
            List<CodeInstruction> list = new(instructions);
            if (!InjectionsByMethod.TryGetValue(original, out List<SourcePausePointPatchInjection> injections))
            {
                return list;
            }

            foreach (SourcePausePointPatchInjection injection in injections.OrderByDescending(i => i.InstructionIndex))
            {
                Label skip = generator.DefineLabel();
                List<CodeInstruction> emitted = BuildInjection(injection, original, skip);

                // The instruction we insert before may be a branch target or a try/catch/finally
                // boundary marker; both must move to the new first instruction so control flow and
                // exception regions still land in the same place, now with our prefix folded in.
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

            ParameterInfo[] runtimeParameters = method.GetParameters();
            int argOffset = injection.IsStatic ? 0 : 1;
            AppendNameValueArray(
                emitted,
                injection.Parameters,
                p => CodeInstruction.LoadArgument(p.Index + argOffset, false),
                p => p.IsValueType ? runtimeParameters[p.Index].ParameterType : null,
                p => p.Name);

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

            emitted.Add(new CodeInstruction(OpCodes.Call, CaptureMethodInfo));
            return emitted;
        }

        private static void AppendInstanceLoad(List<CodeInstruction> emitted, SourcePausePointPatchInjection injection, MethodBase method)
        {
            if (injection.IsStatic)
            {
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
