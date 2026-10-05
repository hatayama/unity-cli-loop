using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Finds the closure structs a local function receives by reference, so a pause point in it
    /// can capture the variables and the instance it shares with its enclosing scopes.
    /// </summary>
    /// <remarks>
    /// A local function that captures variables but is not converted to a delegate is compiled to a
    /// method taking one by-ref struct argument per enclosing scope it uses, after its own declared
    /// parameters. This happens whether the local function itself is static, an instance method of
    /// the declaring class, or an instance method of a lambda's closure class. The instance often
    /// travels inside one of those structs, as `<>4__this` or as a hot-reload shim's receiver field.
    /// </remarks>
    internal static class SourcePausePointClosureFrameArgument
    {
        /// <summary>The GetParameters() indexes of every closure struct argument, in declaration order.</summary>
        internal static List<int> FindIndexes(MethodBase method)
        {
            System.Reflection.ParameterInfo[] parameters = method.GetParameters();
            List<int> indexes = new List<int>();
            for (int index = 0; index < parameters.Length; index++)
            {
                if (FrameTypeOrNull(parameters[index].ParameterType) != null)
                {
                    indexes.Add(index);
                }
            }

            return indexes;
        }

        /// <summary>The struct type behind a by-ref closure argument, or null for any other parameter.</summary>
        internal static Type FrameTypeOrNull(Type parameterType)
        {
            if (!parameterType.IsByRef)
            {
                return null;
            }

            Type elementType = parameterType.GetElementType();
            if (elementType == null || !elementType.IsValueType)
            {
                return null;
            }

            return Attribute.IsDefined(elementType, typeof(CompilerGeneratedAttribute)) ? elementType : null;
        }
    }
}
