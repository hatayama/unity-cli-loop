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
    /// Both spell parameter types the way metadata does (List`1&lt;System.Int32&gt;), whether they
    /// are built from worker fields or from a resolved MethodBase.
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
        // Parameter types (+ generic arity) are part of the label because MethodBase ledger
        // entries distinguish overloads, so a name-only key would merge counts and let Revert
        // of one overload zero the other's counter. They are spelled the way metadata spells
        // them because worker rows and call-site hits carry that spelling and the ledgers match
        // labels by ordinal equality: Type.ToString writes List`1[System.Int32] and
        // System.Int32[,] where Cecil writes List`1<System.Int32> and System.Int32[0...,0...],
        // and FullName embeds assembly Version/PublicKeyToken for constructed generics.
        internal static string FormatMethodLabel(MethodBase method)
        {
            Debug.Assert(method != null, "method must not be null.");
            Debug.Assert(method.DeclaringType != null, "Patched methods must have a declaring type.");

            // Why alias: ToolContracts.ParameterInfo also exists in callers' usings.
            ReflectionParameterInfo[] parameters = method.GetParameters();
            string[] parameterTypeFullNames = new string[parameters.Length];
            for (int index = 0; index < parameters.Length; index++)
            {
                parameterTypeFullNames[index] = FormatParameterTypeName(parameters[index].ParameterType);
            }

            int genericArity = 0;
            if (method.IsGenericMethodDefinition || method.IsGenericMethod)
            {
                genericArity = method.GetGenericArguments().Length;
            }

            return FormatMethodLabelFromReflection(
                new HotReloadReflectionTypeName(method.DeclaringType.FullName),
                method.Name,
                parameterTypeFullNames,
                genericArity);
        }

        // Spells a parameter type the way Cecil's TypeReference.FullName spells it, except that a
        // nested type keeps reflection's '+', which is what every label turns Cecil's '/' into.
        // A structural walk rather than a rewrite of Type.ToString, because a '[' in its output
        // opens either type arguments or an array rank and only the Type knows which.
        private static string FormatParameterTypeName(Type type)
        {
            if (type.IsByRef)
            {
                return FormatParameterTypeName(type.GetElementType()) + "&";
            }

            if (type.IsPointer)
            {
                return FormatParameterTypeName(type.GetElementType()) + "*";
            }

            if (type.IsArray)
            {
                return FormatParameterTypeName(type.GetElementType()) + FormatArrayRankSuffix(type.GetArrayRank());
            }

            if (type.IsGenericParameter)
            {
                return type.Name;
            }

            if (type.IsGenericType)
            {
                return FormatGenericTypeName(type);
            }

            return type.FullName;
        }

        // Rank alone decides, as it does in the worker's CecilTypeNames: C# declares a rank-1
        // array only as a vector, and metadata gives each dimension of a C# multidimensional
        // array a zero lower bound, which Cecil spells "0...".
        private static string FormatArrayRankSuffix(int rank)
        {
            if (rank == 1)
            {
                return "[]";
            }

            string[] dimensions = new string[rank];
            for (int index = 0; index < rank; index++)
            {
                dimensions[index] = "0...";
            }

            return "[" + string.Join(",", dimensions) + "]";
        }

        // Every generic type takes this path, definitions included: FullName is null for a
        // constructed type that still holds a type parameter (List<T>), and a definition, which
        // is what a generic type's method gets for its own type or a type nested in it, has a
        // FullName without the type parameters metadata still spells (Box`1<T>).
        // GetGenericArguments lists a nested type's arguments outer type first, which is the
        // order metadata writes them in.
        private static string FormatGenericTypeName(Type genericType)
        {
            Type[] arguments = genericType.GetGenericArguments();
            string[] argumentNames = new string[arguments.Length];
            for (int index = 0; index < arguments.Length; index++)
            {
                argumentNames[index] = FormatParameterTypeName(arguments[index]);
            }

            return genericType.GetGenericTypeDefinition().FullName + "<" + string.Join(",", argumentNames) + ">";
        }

        // What: display label from worker DTO fields (pre-Resolve failures) using the same shape
        // as FormatMethodLabel. Cecil nested separators ('/') are normalized to reflection ('+').
        // Keep in sync with WorkerMethodKeys.FormatMethodLabelParts.
        internal static string FormatMethodLabelParts(
            HotReloadMetadataTypeName typeMetadataName,
            string methodName,
            string[] parameterTypeFullNames,
            int genericArity)
        {
            return FormatMethodLabelFromReflection(
                typeMetadataName.ToReflectionName(),
                methodName,
                parameterTypeFullNames,
                genericArity);
        }

        // What: the shared assembly of a label once every name it holds is in reflection form.
        // Why a parameter type is converted here too: a worker row spells a nested parameter type
        // in metadata form, while FormatParameterTypeName already spells it as reflection does, so
        // the conversion is a no-op on the reflection path and both paths produce one string.
        private static string FormatMethodLabelFromReflection(
            HotReloadReflectionTypeName typeReflectionName,
            string methodName,
            string[] parameterTypeFullNames,
            int genericArity)
        {
            Debug.Assert(!string.IsNullOrEmpty(methodName), "methodName must not be null or empty.");
            Debug.Assert(parameterTypeFullNames != null, "parameterTypeFullNames must not be null.");
            Debug.Assert(genericArity >= 0, "genericArity must not be negative.");

            StringBuilder builder = new StringBuilder();
            builder.Append(typeReflectionName.Value);
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
                builder.Append(new HotReloadMetadataTypeName(parameterTypeFullName).ToReflectionName().Value);
            }

            builder.Append(')');
            return builder.ToString();
        }
    }
}
