using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Assembly = System.Reflection.Assembly;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Resolves Compiled Command Entry Point values from the available runtime context.
    /// </summary>
    internal sealed class CompiledCommandEntryPointResolver
    {
        private const string WrappedExecuteMethodName = "Execute";
        private const string WrappedExecuteAsyncMethodName = "ExecuteAsync";

        public (Type targetType, MethodInfo executeMethod) TryFindExecuteMethod(Assembly assembly)
        {
            Type wrappedType = assembly.GetType(
                $"{DynamicCodeConstants.DEFAULT_NAMESPACE}.{DynamicCodeConstants.DEFAULT_CLASS_NAME}",
                false);
            if (wrappedType != null)
            {
                MethodInfo directMethod = FindPreferredExecuteMethod(wrappedType);
                if (directMethod != null)
                {
                    return (wrappedType, directMethod);
                }
            }

            Type[] types = assembly.GetTypes();
            foreach (Type type in types)
            {
                MethodInfo method = FindPreferredExecuteMethod(type);
                if (method != null)
                {
                    return (type, method);
                }
            }

            return (null, null);
        }

        private static MethodInfo FindPreferredExecuteMethod(Type type)
        {
            MethodInfo methodWithParametersAndCancellation = type.GetMethod(
                WrappedExecuteMethodName,
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new Type[] { typeof(Dictionary<string, object>), typeof(CancellationToken) },
                null);
            if (methodWithParametersAndCancellation != null)
            {
                return methodWithParametersAndCancellation;
            }

            MethodInfo methodWithParameters = type.GetMethod(
                WrappedExecuteMethodName,
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new Type[] { typeof(Dictionary<string, object>) },
                null);
            if (methodWithParameters != null)
            {
                return methodWithParameters;
            }

            MethodInfo methodWithCancellation = type.GetMethod(
                WrappedExecuteMethodName,
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new Type[] { typeof(CancellationToken) },
                null);
            if (methodWithCancellation != null)
            {
                return methodWithCancellation;
            }

            return type.GetMethod(
                WrappedExecuteMethodName,
                BindingFlags.Public | BindingFlags.Instance,
                null,
                Type.EmptyTypes,
                null);
        }

        public (Type targetType, MethodInfo executeAsyncMethod) TryFindExecuteAsyncMethod(Assembly assembly)
        {
            Type wrappedType = assembly.GetType(
                $"{DynamicCodeConstants.DEFAULT_NAMESPACE}.{DynamicCodeConstants.DEFAULT_CLASS_NAME}",
                false);
            if (wrappedType != null)
            {
                MethodInfo directMethod = FindPreferredExecuteAsyncMethod(wrappedType);
                if (directMethod != null)
                {
                    return (wrappedType, directMethod);
                }
            }

            Type[] types = assembly.GetTypes();
            foreach (Type type in types)
            {
                MethodInfo method = FindPreferredExecuteAsyncMethod(type);
                if (method != null)
                {
                    return (type, method);
                }
            }

            return (null, null);
        }

        private static MethodInfo FindPreferredExecuteAsyncMethod(Type type)
        {
            MethodInfo methodWithParametersAndCancellation = type.GetMethod(
                WrappedExecuteAsyncMethodName,
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new Type[] { typeof(Dictionary<string, object>), typeof(CancellationToken) },
                null);
            if (methodWithParametersAndCancellation != null)
            {
                return methodWithParametersAndCancellation;
            }

            MethodInfo methodWithParameters = type.GetMethod(
                WrappedExecuteAsyncMethodName,
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new Type[] { typeof(Dictionary<string, object>) },
                null);
            if (methodWithParameters != null)
            {
                return methodWithParameters;
            }

            MethodInfo methodWithCancellation = type.GetMethod(
                WrappedExecuteAsyncMethodName,
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new Type[] { typeof(CancellationToken) },
                null);
            if (methodWithCancellation != null)
            {
                return methodWithCancellation;
            }

            return type.GetMethod(
                WrappedExecuteAsyncMethodName,
                BindingFlags.Public | BindingFlags.Instance,
                null,
                Type.EmptyTypes,
                null);
        }
    }
}
