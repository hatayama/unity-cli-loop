using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

using HarmonyLib;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Resolves the base-call stand-ins a shim calls to the base methods they stand for: checks
    /// before patching that every one resolves, and points each stand-in call in a transplanted
    /// body at its base method.
    /// </summary>
    internal static class HotReloadBaseCallStandIns
    {
        /// <summary>
        /// Why a patch whose shim calls a base method cannot be applied; null when the shim calls
        /// none or every call resolves to a base method of the patched method's type.
        /// </summary>
        internal static string DescribeUnresolvableCallOrNull(
            MethodInfo shimMethod,
            MethodBase original,
            HotReloadPatchShape patchShape)
        {
            // Fast path: most shim types declare no stand-in, and reading IL costs.
            bool shimTypeDeclaresStandIns = shimMethod.DeclaringType
                .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Any(method => method.Name.StartsWith(HotReloadBaseCallStandInNames.Prefix, StringComparison.Ordinal));
            if (!shimTypeDeclaresStandIns)
            {
                return null;
            }

            foreach (CodeInstruction instruction in PatchProcessor.GetOriginalInstructions(shimMethod))
            {
                MethodInfo standIn = AsStandInOrNull(instruction, shimMethod.DeclaringType);
                if (standIn == null)
                {
                    continue;
                }

                // Why refused: a delegating shim runs its own IL in the shim assembly, where the
                // stand-in call is never redirected and would throw.
                if (patchShape == HotReloadPatchShape.Delegation)
                {
                    return "A delegating shim cannot call a base method; run 'uloop compile'.";
                }

                if (ResolveBaseMethodOrNull(standIn, original) == null)
                {
                    HotReloadBaseCallStandInNames.TryParse(standIn.Name, out int depth, out string methodName);
                    return $"The base method '{methodName}' this body calls was not found {depth} type(s) above "
                        + $"'{original.DeclaringType}'; run 'uloop compile'.";
                }
            }

            return null;
        }

        /// <summary>Points every stand-in call in a transplanted body at the base method it stands for.</summary>
        internal static void RedirectToBaseMethods(
            List<CodeInstruction> transplanted,
            MethodInfo shimMethod,
            MethodBase original)
        {
            foreach (CodeInstruction instruction in transplanted)
            {
                MethodInfo standIn = AsStandInOrNull(instruction, shimMethod.DeclaringType);
                if (standIn == null)
                {
                    continue;
                }

                MethodInfo baseMethod = ResolveBaseMethodOrNull(standIn, original);
                if (baseMethod == null)
                {
                    throw new InvalidOperationException(
                        "A base-call stand-in passed the preflight but no longer resolves: " + standIn.Name);
                }

                // Why keep OpCodes.Call: a non-virtual call is what C# emits for base.M(); callvirt
                // would re-enter the override.
                instruction.operand = baseMethod;
            }
        }

        private static MethodInfo AsStandInOrNull(CodeInstruction instruction, Type shimType)
        {
            if (instruction.opcode != OpCodes.Call
                || instruction.operand is not MethodInfo method
                || method.DeclaringType != shimType
                || !method.Name.StartsWith(HotReloadBaseCallStandInNames.Prefix, StringComparison.Ordinal))
            {
                return null;
            }

            return method;
        }

        private static MethodInfo ResolveBaseMethodOrNull(MethodInfo standIn, MethodBase original)
        {
            if (!HotReloadBaseCallStandInNames.TryParse(standIn.Name, out int depth, out string methodName))
            {
                return null;
            }

            Type ancestor = original.DeclaringType;
            for (int hop = 0; hop < depth; hop++)
            {
                ancestor = ancestor.BaseType;
                if (ancestor == null)
                {
                    return null;
                }
            }

            // Why skip the first parameter: it is the stand-in's receiver, not a base method parameter.
            Type[] parameterTypes = standIn.GetParameters()
                .Skip(1)
                .Select(parameter => parameter.ParameterType)
                .ToArray();
            MethodInfo baseMethod = ancestor.GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null,
                parameterTypes,
                null);
            if (baseMethod == null || baseMethod.IsAbstract || baseMethod.ReturnType != standIn.ReturnType)
            {
                return null;
            }

            return baseMethod;
        }
    }
}
