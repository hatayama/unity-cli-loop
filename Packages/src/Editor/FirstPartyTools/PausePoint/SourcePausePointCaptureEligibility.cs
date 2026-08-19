using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;

using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Collects capturable locals and parameters, excluding byref, pointer, and ref-struct types.
    /// </summary>
    internal static class SourcePausePointCaptureEligibility
    {
        internal static List<SourcePausePointLocalVariable> CollectCapturableLocals(
            MethodDefinition method,
            int offset)
        {
            List<SourcePausePointLocalVariable> results = new List<SourcePausePointLocalVariable>();
            ScopeDebugInformation rootScope = method.DebugInformation.Scope;
            if (rootScope != null)
            {
                CollectInScopeVariables(rootScope, offset, method.Body.Variables, results);
            }

            return results;
        }

        /// <summary>
        /// Collects every named capturable local in the method's debug scopes, ignoring IL offset.
        /// Used when a shim sequence point lands on Ret (or similar) after local scopes close.
        /// Keep-first dedupe by slot and by name avoids duplicate capture entries when scopes
        /// reuse slots or nest identically named locals.
        /// </summary>
        internal static List<SourcePausePointLocalVariable> CollectAllCapturableLocals(
            MethodDefinition method)
        {
            List<SourcePausePointLocalVariable> results = new List<SourcePausePointLocalVariable>();
            ScopeDebugInformation rootScope = method.DebugInformation.Scope;
            if (rootScope == null)
            {
                return results;
            }

            HashSet<int> seenSlots = new HashSet<int>();
            HashSet<string> seenNames = new HashSet<string>(StringComparer.Ordinal);
            CollectAllScopeVariables(rootScope, method.Body.Variables, results, seenSlots, seenNames);
            return results;
        }

        private static void CollectAllScopeVariables(
            ScopeDebugInformation scope,
            Collection<VariableDefinition> methodVariables,
            List<SourcePausePointLocalVariable> results,
            HashSet<int> seenSlots,
            HashSet<string> seenNames)
        {
            if (scope.HasVariables)
            {
                foreach (VariableDebugInformation variableDebugInformation in scope.Variables)
                {
                    int beforeCount = results.Count;
                    AppendLocalIfCapturable(variableDebugInformation, methodVariables, results);
                    if (results.Count == beforeCount)
                    {
                        continue;
                    }

                    SourcePausePointLocalVariable added = results[results.Count - 1];
                    if (seenSlots.Contains(added.SlotIndex) || seenNames.Contains(added.Name))
                    {
                        results.RemoveAt(results.Count - 1);
                    }
                    else
                    {
                        seenSlots.Add(added.SlotIndex);
                        seenNames.Add(added.Name);
                    }
                }
            }

            if (!scope.HasScopes)
            {
                return;
            }

            foreach (ScopeDebugInformation childScope in scope.Scopes)
            {
                CollectAllScopeVariables(childScope, methodVariables, results, seenSlots, seenNames);
            }
        }

        private static void CollectInScopeVariables(
            ScopeDebugInformation scope,
            int offset,
            Collection<VariableDefinition> methodVariables,
            List<SourcePausePointLocalVariable> results)
        {
            if (!IsOffsetWithinScope(scope, offset))
            {
                return;
            }

            if (scope.HasVariables)
            {
                foreach (VariableDebugInformation variableDebugInformation in scope.Variables)
                {
                    AppendLocalIfCapturable(variableDebugInformation, methodVariables, results);
                }
            }

            if (scope.HasScopes)
            {
                foreach (ScopeDebugInformation childScope in scope.Scopes)
                {
                    CollectInScopeVariables(childScope, offset, methodVariables, results);
                }
            }
        }

        private static bool IsOffsetWithinScope(ScopeDebugInformation scope, int offset)
        {
            if (offset < scope.Start.Offset)
            {
                return false;
            }

            return scope.End.IsEndOfMethod || offset < scope.End.Offset;
        }

        private static void AppendLocalIfCapturable(
            VariableDebugInformation variableDebugInformation,
            Collection<VariableDefinition> methodVariables,
            List<SourcePausePointLocalVariable> results)
        {
            string name = variableDebugInformation.Name;
            if (string.IsNullOrEmpty(name) || name.StartsWith("<", StringComparison.Ordinal))
            {
                // Unnamed or compiler-generated hoisted locals (e.g. "<>u__1") carry no source meaning to capture.
                return;
            }

            int slotIndex = variableDebugInformation.Index;
            if (slotIndex < 0 || slotIndex >= methodVariables.Count)
            {
                return;
            }

            TypeReference variableType = methodVariables[slotIndex].VariableType;
            if (IsCaptureExcluded(variableType))
            {
                return;
            }

            results.Add(new SourcePausePointLocalVariable(name, slotIndex, variableType.FullName, variableType.IsValueType));
        }

        private static bool IsCaptureExcluded(TypeReference type)
        {
            // byref locals/parameters (ref, out, in) and pointers cannot be boxed; ref structs
            // (Span<T>, and any user-defined "ref struct") cannot be boxed either.
            return type.IsByReference || type.IsPointer || IsRefStructType(type);
        }

        private static bool IsRefStructType(TypeReference type)
        {
            if (IsKnownFrameworkRefStructType(type))
            {
                return true;
            }

            // A generic instance (e.g. MyRefStruct<int>) must be unwrapped to its open generic
            // definition before the TypeDefinition check below, the same way the framework check
            // above unwraps Span<T>/ReadOnlySpan<T>.
            TypeReference elementType = type.IsGenericInstance ? type.GetElementType() : type;

            // Only inspect user-defined ref structs when the reference already points directly at
            // a TypeDefinition, i.e. it is declared in the very assembly being read. Resolving an
            // external TypeReference (e.g. any corlib value type such as System.Int32) requires
            // Mono.Cecil's assembly resolver to locate that assembly, which is not reliably
            // resolvable against the Unity Editor's Mono/.NET layout and would throw for ordinary
            // primitives that are not ref structs at all.
            if (!(elementType is TypeDefinition typeDefinition) || !typeDefinition.IsValueType || !typeDefinition.HasCustomAttributes)
            {
                return false;
            }

            foreach (CustomAttribute attribute in typeDefinition.CustomAttributes)
            {
                if (attribute.AttributeType.FullName == SourcePausePointConstants.IsByRefLikeAttributeFullName)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsKnownFrameworkRefStructType(TypeReference type)
        {
            // Span<T>/ReadOnlySpan<T> are ref structs defined in corlib; checking their FullName
            // avoids forcing assembly resolution for a framework type on the hot path.
            TypeReference elementType = type.IsGenericInstance ? type.GetElementType() : type;
            return elementType.FullName == "System.Span`1" || elementType.FullName == "System.ReadOnlySpan`1";
        }

        internal static List<SourcePausePointParameter> CollectParameters(MethodDefinition method)
        {
            List<SourcePausePointParameter> parameters = new List<SourcePausePointParameter>();
            foreach (ParameterDefinition parameter in method.Parameters)
            {
                if (IsCaptureExcluded(parameter.ParameterType))
                {
                    continue;
                }

                parameters.Add(new SourcePausePointParameter(
                    parameter.Name, parameter.Index, parameter.ParameterType.FullName, parameter.ParameterType.IsValueType));
            }

            return parameters;
        }

        /// <summary>
        /// Collects capturable parameters from a reflection MethodBase (transplant chain-join
        /// uses the original method; shim-direct uses the resolved shim-side method).
        /// </summary>
        internal static List<SourcePausePointParameter> CollectParametersFromReflection(
            MethodBase method,
            bool skipFirstParameter)
        {
            Debug.Assert(method != null, "method must not be null.");

            // Fully qualify: ToolContracts also defines ParameterInfo (tool schema DTO).
            System.Reflection.ParameterInfo[] runtimeParameters = method.GetParameters();
            List<SourcePausePointParameter> parameters = new List<SourcePausePointParameter>();
            int startIndex = skipFirstParameter ? 1 : 0;
            for (int index = startIndex; index < runtimeParameters.Length; index++)
            {
                System.Reflection.ParameterInfo parameter = runtimeParameters[index];
                Type parameterType = parameter.ParameterType;
                if (IsCaptureExcludedReflection(parameterType))
                {
                    continue;
                }

                // Keep GetParameters() position so LoadArgument hits the right slot even when
                // earlier parameters were excluded from capture (same as Cecil Parameter.Index).
                parameters.Add(
                    new SourcePausePointParameter(
                        parameter.Name,
                        index,
                        parameterType.FullName,
                        parameterType.IsValueType));
            }

            return parameters;
        }

        private static bool IsCaptureExcludedReflection(Type type)
        {
            return type.IsByRef || type.IsPointer || IsByRefLikeTypeReflection(type);
        }

        private static bool IsByRefLikeTypeReflection(Type type)
        {
            Type elementType = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
            if (elementType.FullName == "System.Span`1" || elementType.FullName == "System.ReadOnlySpan`1")
            {
                return true;
            }

            foreach (object attribute in type.GetCustomAttributes(inherit: false))
            {
                if (attribute.GetType().FullName == SourcePausePointConstants.IsByRefLikeAttributeFullName)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
