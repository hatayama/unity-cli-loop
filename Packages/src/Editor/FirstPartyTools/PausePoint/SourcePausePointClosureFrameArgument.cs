using System;
using System.Reflection;
using System.Runtime.CompilerServices;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Finds the closure struct a static local function receives by reference, so a pause point in
    /// it can capture the variables the local function shares with its enclosing method.
    /// </summary>
    /// <remarks>
    /// A local function that captures variables but is not converted to a delegate is compiled to a
    /// method taking its enclosing scopes as by-ref struct arguments. When the enclosing method is a
    /// hot-reload shim, the instance travels in that struct as the shim's receiver field, so the
    /// local function is static and has no `this` of its own.
    /// </remarks>
    internal static class SourcePausePointClosureFrameArgument
    {
        private const string OuterThisFieldName = "<>4__this";

        /// <summary>
        /// The GetParameters() index of the closure struct to capture, or -1 when there is none.
        /// The struct that holds the instance wins, because the instance is what the pause point
        /// most needs; otherwise the first one.
        /// </summary>
        internal static int FindIndexOrMinusOne(MethodBase method)
        {
            System.Reflection.ParameterInfo[] parameters = method.GetParameters();
            int firstFrameIndex = -1;
            for (int index = 0; index < parameters.Length; index++)
            {
                Type frameType = FrameTypeOrNull(parameters[index].ParameterType);
                if (frameType == null)
                {
                    continue;
                }

                if (HoldsInstance(frameType))
                {
                    return index;
                }

                if (firstFrameIndex < 0)
                {
                    firstFrameIndex = index;
                }
            }

            return firstFrameIndex;
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

        private static bool HoldsInstance(Type frameType)
        {
            const BindingFlags instanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            return frameType.GetField(HotReloadShimMethodLookup.ShimReceiverParameterName, instanceFields) != null
                || frameType.GetField(OuterThisFieldName, instanceFields) != null;
        }
    }
}
