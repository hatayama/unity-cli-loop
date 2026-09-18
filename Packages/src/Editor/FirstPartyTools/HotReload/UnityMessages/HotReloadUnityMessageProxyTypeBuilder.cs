using System;
using System.Reflection;
using System.Reflection.Emit;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Emits the component type Unity delivers the added messages to: one void method per message
    /// of a binding, each handing the call straight back to the proxy base.
    /// </summary>
    /// <remarks>
    /// Why a generated type at all: Unity finds a component's messages by reflecting over the
    /// compiled type, so a method a hot reload added exists only as a shim Unity never sees. A
    /// component that really declares the message is the only thing the engine will call.
    /// </remarks>
    internal sealed class HotReloadUnityMessageProxyTypeBuilder
    {
        private const string BindingFieldName = "__binding";
        private const string ModuleName = "UloopHotReloadUnityMessageProxies";

        private ModuleBuilder _module;
        // The counter belongs to the module, not to a run: DefineType refuses a name the module
        // already holds, and a counter restarted for a rebuilt binding would collide with the type
        // an earlier binding of the same target left behind.
        private int _sequence;

        /// <summary>Builds the proxy type that forwards every message of <paramref name="binding"/>.</summary>
        internal Type Build(HotReloadUnityMessageBinding binding)
        {
            Debug.Assert(binding != null, "binding must not be null.");
            Debug.Assert(binding.Count > 0, "A proxy type needs at least one message to forward.");

            ModuleBuilder module = GetModule();
            _sequence++;
            string typeName = "HotReloadUnityMessageProxy_" + binding.TargetType.Name + "_" + _sequence;
            TypeBuilder typeBuilder = module.DefineType(
                typeName,
                TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed,
                typeof(HotReloadUnityMessageProxy));
            FieldBuilder bindingField = typeBuilder.DefineField(
                BindingFieldName,
                typeof(HotReloadUnityMessageBinding),
                FieldAttributes.Public | FieldAttributes.Static);
            // Unity constructs a component through its parameterless constructor, and the base one
            // is protected, so the generated type needs a public one that chains to it.
            typeBuilder.DefineDefaultConstructor(MethodAttributes.Public);

            MethodInfo forward = typeof(HotReloadUnityMessageProxy).GetMethod(
                nameof(HotReloadUnityMessageProxy.Forward),
                BindingFlags.Public | BindingFlags.Instance);
            Debug.Assert(forward != null, "HotReloadUnityMessageProxy.Forward must be public.");
            for (int slot = 0; slot < binding.Count; slot++)
            {
                EmitMessageMethod(typeBuilder, bindingField, forward, binding, slot);
            }

            Type proxyType = typeBuilder.CreateType();
            FieldInfo createdField = proxyType.GetField(
                BindingFieldName,
                BindingFlags.Public | BindingFlags.Static);
            createdField.SetValue(null, binding);
            return proxyType;
        }

        /// <summary>
        /// Points a proxy type this builder emitted at <paramref name="binding"/>, so the proxies
        /// already attached forward to the new shims without being replaced.
        /// </summary>
        /// <remarks>
        /// Why swapping the field is enough: the emitted methods read the binding from it on every
        /// call, and one reference assignment is never seen half-done by Unity's message dispatch.
        /// The caller has to have checked the shapes match; a proxy type only declares the messages
        /// of the binding it was built for.
        /// </remarks>
        internal void Rebind(Type proxyType, HotReloadUnityMessageBinding binding)
        {
            Debug.Assert(proxyType != null, "proxyType must not be null.");
            Debug.Assert(binding != null, "binding must not be null.");
            FieldInfo bindingField = proxyType.GetField(
                BindingFieldName,
                BindingFlags.Public | BindingFlags.Static);
            if (bindingField == null || bindingField.FieldType != typeof(HotReloadUnityMessageBinding))
            {
                throw new InvalidOperationException(
                    proxyType.FullName + " was not emitted by this builder and holds no binding.");
            }

            bindingField.SetValue(null, binding);
        }

        // The emitted body reads nothing but its own arguments and one static field of a public
        // type, because the target type and the shim it ends up calling are often internal and
        // this IL is checked for accessibility like compiled code.
        private static void EmitMessageMethod(
            TypeBuilder typeBuilder,
            FieldBuilder bindingField,
            MethodInfo forward,
            HotReloadUnityMessageBinding binding,
            int slot)
        {
            Type[] parameterTypes = binding.GetParameterTypes(slot);
            MethodBuilder method = typeBuilder.DefineMethod(
                binding.GetName(slot),
                MethodAttributes.Public | MethodAttributes.HideBySig,
                typeof(void),
                parameterTypes);
            ILGenerator il = method.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldsfld, bindingField);
            il.Emit(OpCodes.Ldc_I4, slot);
            if (parameterTypes.Length == 0)
            {
                // Why null and not an empty array: the per-frame messages take no arguments, and
                // allocating an array for them would cost a garbage collection every few frames.
                il.Emit(OpCodes.Ldnull);
            }
            else
            {
                il.Emit(OpCodes.Ldc_I4, parameterTypes.Length);
                il.Emit(OpCodes.Newarr, typeof(object));
                for (int index = 0; index < parameterTypes.Length; index++)
                {
                    il.Emit(OpCodes.Dup);
                    il.Emit(OpCodes.Ldc_I4, index);
                    il.Emit(OpCodes.Ldarg_S, (byte)(index + 1));
                    if (parameterTypes[index].IsValueType)
                    {
                        il.Emit(OpCodes.Box, parameterTypes[index]);
                    }

                    il.Emit(OpCodes.Stelem_Ref);
                }
            }

            il.Emit(OpCodes.Call, forward);
            il.Emit(OpCodes.Ret);
        }

        private ModuleBuilder GetModule()
        {
            if (_module != null)
            {
                return _module;
            }

            AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(
                new AssemblyName(ModuleName),
                AssemblyBuilderAccess.Run);
            _module = assembly.DefineDynamicModule(ModuleName);
            return _module;
        }
    }
}
