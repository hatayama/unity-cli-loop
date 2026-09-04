using System;
using System.Reflection;
using System.Text;

using UnityEngine;

using ReflectionParameterInfo = System.Reflection.ParameterInfo;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Single Unity-side formatter for the two method identifiers hot reload uses: the wire key
    /// (Type::Method`N(params), exchanged with the worker and the call-site scanner) and the
    /// display label (Type.Method`N(params), shown in Methods[].Method and --status rows).
    /// Mirrored on the worker side by WorkerMethodKeys — keep the two files in sync.
    /// </summary>
    internal static class HotReloadMethodKeys
    {
        internal static string BuildMethodKey(TransformWorkerEntryDto entry)
        {
            return BuildMethodKeyParts(
                entry.typeMetadataName,
                entry.methodName,
                entry.parameterTypeFullNames,
                entry.genericArity);
        }

        // Keep in sync with WorkerMethodKeys.BuildMethodKey.
        // Why arity suffix: Caller(int) and Caller<T>(int) must not share a wire key.
        // Arity 0 keeps the bare name so existing non-generic keys stay stable.
        internal static string BuildMethodKeyParts(
            string typeMetadataName,
            string methodName,
            string[] parameterTypeFullNames,
            int genericArity)
        {
            string nameWithArity = methodName;
            if (genericArity > 0)
            {
                nameWithArity = methodName + "`" + genericArity.ToString();
            }

            return typeMetadataName + "::" + nameWithArity + "("
                + string.Join(",", parameterTypeFullNames ?? Array.Empty<string>()) + ")";
        }

        // What: status / counter label from a resolved MethodBase (apply outcomes use the same
        // helper after Resolve so --status rows match Patched Methods[].Method).
        // Why parameter ToString (+ generic arity): MethodBase ledger entries distinguish
        // overloads, so a name-only key would merge counts and let Revert of one overload
        // zero the other's counter. FullName embeds assembly Version/PublicKeyToken for
        // constructed generics (List`1[[Int32, mscorlib, ...]]), which bloated labels.
        internal static string FormatMethodLabel(MethodBase method)
        {
            Debug.Assert(method != null, "method must not be null.");
            Debug.Assert(method.DeclaringType != null, "Patched methods must have a declaring type.");

            // Why alias: ToolContracts.ParameterInfo also exists in callers' usings.
            ReflectionParameterInfo[] parameters = method.GetParameters();
            string[] parameterTypeFullNames = new string[parameters.Length];
            for (int index = 0; index < parameters.Length; index++)
            {
                parameterTypeFullNames[index] = parameters[index].ParameterType.ToString();
            }

            int genericArity = 0;
            if (method.IsGenericMethodDefinition || method.IsGenericMethod)
            {
                genericArity = method.GetGenericArguments().Length;
            }

            return FormatMethodLabelParts(
                method.DeclaringType.FullName,
                method.Name,
                parameterTypeFullNames,
                genericArity);
        }

        // What: display label from worker DTO fields (pre-Resolve failures) using the same shape
        // as FormatMethodLabel. Cecil nested separators ('/') are normalized to reflection ('+').
        // Keep in sync with WorkerMethodKeys.FormatMethodLabelParts.
        internal static string FormatMethodLabelParts(
            string typeMetadataName,
            string methodName,
            string[] parameterTypeFullNames,
            int genericArity)
        {
            Debug.Assert(!string.IsNullOrEmpty(typeMetadataName), "typeMetadataName must not be null or empty.");
            Debug.Assert(!string.IsNullOrEmpty(methodName), "methodName must not be null or empty.");
            Debug.Assert(parameterTypeFullNames != null, "parameterTypeFullNames must not be null.");
            Debug.Assert(genericArity >= 0, "genericArity must not be negative.");

            StringBuilder builder = new StringBuilder();
            builder.Append(NormalizeNestedTypeSeparators(typeMetadataName));
            builder.Append('.');
            builder.Append(methodName);
            if (genericArity > 0)
            {
                builder.Append('`');
                builder.Append(genericArity);
            }

            builder.Append('(');
            for (int index = 0; index < parameterTypeFullNames.Length; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                string parameterTypeFullName = parameterTypeFullNames[index];
                Debug.Assert(
                    !string.IsNullOrEmpty(parameterTypeFullName),
                    "parameterTypeFullNames entries must not be null or empty.");
                builder.Append(NormalizeNestedTypeSeparators(parameterTypeFullName));
            }

            builder.Append(')');
            return builder.ToString();
        }

        // Why: Cecil metadata names use '/' for nested types; Type.FullName uses '+'.
        private static string NormalizeNestedTypeSeparators(string metadataName)
        {
            return metadataName.Replace('/', '+');
        }
    }
}
