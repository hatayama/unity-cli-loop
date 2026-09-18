using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds the call into a shim that a proxy makes when Unity delivers an added message, and
    /// the binding that holds one such call per message of a target type.
    /// </summary>
    internal static class HotReloadUnityMessageForwarderFactory
    {
        /// <summary>
        /// Builds the delegate that calls <paramref name="shim"/> on the receiver, taking the
        /// message's own arguments out of the object array the proxy filled.
        /// </summary>
        internal static Action<MonoBehaviour, object[]> CreateForwarder(MethodInfo shim)
        {
            Debug.Assert(shim != null, "shim must not be null.");
            ParameterInfo[] parameters = shim.GetParameters();
            Debug.Assert(parameters.Length > 0, "A forwarded shim must take the receiver parameter.");

            Type targetType = parameters[0].ParameterType;
            // Why a skip-visibility DynamicMethod and not a compiled call or a delegate over the
            // MethodInfo: the target type and the generated shim type are both routinely internal
            // to assemblies this one cannot see, and this Mono checks IL accessibility when it
            // jits. Skipping that check is what lets the same emitted shape serve every user type.
            DynamicMethod method = new DynamicMethod(
                "Forward_" + shim.Name,
                typeof(void),
                new[] { typeof(MonoBehaviour), typeof(object[]) },
                typeof(HotReloadUnityMessageProxy).Module,
                skipVisibility: true);
            ILGenerator il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, targetType);
            for (int index = 1; index < parameters.Length; index++)
            {
                Type parameterType = parameters[index].ParameterType;
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldc_I4, index - 1);
                il.Emit(OpCodes.Ldelem_Ref);
                if (parameterType.IsValueType)
                {
                    il.Emit(OpCodes.Unbox_Any, parameterType);
                }
                else
                {
                    il.Emit(OpCodes.Castclass, parameterType);
                }
            }

            il.Emit(OpCodes.Call, shim);
            il.Emit(OpCodes.Ret);
            return (Action<MonoBehaviour, object[]>)method.CreateDelegate(
                typeof(Action<MonoBehaviour, object[]>));
        }

        /// <summary>
        /// Builds the binding for one target type from the shims of its forwarded messages, in the
        /// order given.
        /// </summary>
        internal static HotReloadUnityMessageBinding CreateBinding(
            Type targetType,
            IReadOnlyList<MethodInfo> shims)
        {
            Debug.Assert(targetType != null, "targetType must not be null.");
            Debug.Assert(shims != null && shims.Count > 0, "A binding needs at least one shim.");

            int count = shims.Count;
            HashSet<string> seen = new HashSet<string>();
            string[] names = new string[count];
            bool[] gated = new bool[count];
            Action<MonoBehaviour, object[]>[] forwarders = new Action<MonoBehaviour, object[]>[count];
            Type[][] parameterTypes = new Type[count][];
            for (int slot = 0; slot < count; slot++)
            {
                MethodInfo shim = shims[slot];
                // One proxy type cannot declare the same message twice, and the same message added
                // to one type from two files is an edit the user has to resolve, not a shape this
                // can pick a winner for.
                if (!seen.Add(shim.Name))
                {
                    throw new InvalidOperationException(
                        $"Unity message '{shim.Name}' is added more than once to {targetType.FullName}.");
                }

                names[slot] = shim.Name;
                gated[slot] = HotReloadUnityMessageNames.IsGatedByTargetEnabled(shim.Name);
                forwarders[slot] = CreateForwarder(shim);
                parameterTypes[slot] = MessageParameterTypes(shim);
            }

            return new HotReloadUnityMessageBinding(targetType, names, gated, forwarders, parameterTypes);
        }

        // The shim's first parameter is the receiver the worker prepended; the message Unity
        // delivers to the proxy has only the ones after it.
        private static Type[] MessageParameterTypes(MethodInfo shim)
        {
            ParameterInfo[] parameters = shim.GetParameters();
            Type[] types = new Type[parameters.Length - 1];
            for (int index = 1; index < parameters.Length; index++)
            {
                types[index - 1] = parameters[index].ParameterType;
            }

            return types;
        }
    }
}
