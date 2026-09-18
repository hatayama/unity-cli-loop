using System;
using System.Reflection;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Decides, from a generated shim method alone, whether an added method is a Unity message a
    /// proxy component can forward to a live instance, a Unity message that needs a compile, or
    /// an ordinary added method hot reload treats as before.
    /// </summary>
    internal static class HotReloadUnityMessageDetector
    {
        // Keep in sync with TransformWorker~/TransformWorkerProgramMarker.InstanceParameterName.
        // The worker is built as a separate program and shares no file with this assembly, so the
        // receiver parameter name is the only contract between them.
        internal const string ReceiverParameterName = "__uloopInstance";

        // Keep in sync with TransformWorker~/ShimNameAllocator.NextShimMethodName. Every shim the
        // worker emits is named after the member plus this marker and a number, so the name Unity
        // dispatches on is only recoverable by taking that suffix back off.
        internal const string ShimMethodNameMarker = "__shim";

        /// <summary>What a shim method is, as far as Unity's message dispatch is concerned.</summary>
        internal enum Classification
        {
            NotUnityMessage,
            Forwarded,
            NotForwarded
        }

        /// <summary>
        /// The member name behind a shim method: the shim's own name without the marker and number
        /// the worker appended. A name the worker did not produce is returned unchanged.
        /// </summary>
        internal static string ResolveMemberName(MethodInfo shim)
        {
            Debug.Assert(shim != null, "shim must not be null.");
            string shimName = shim.Name;
            int markerStart = shimName.LastIndexOf(ShimMethodNameMarker, StringComparison.Ordinal);
            // Why a marker at index 0 is left alone: the member name before it would be empty, and
            // a method actually named "__shim0" is not one of ours to rewrite.
            if (markerStart <= 0)
            {
                return shimName;
            }

            int digitsStart = markerStart + ShimMethodNameMarker.Length;
            if (digitsStart == shimName.Length)
            {
                return shimName;
            }

            for (int index = digitsStart; index < shimName.Length; index++)
            {
                if (!char.IsDigit(shimName[index]))
                {
                    return shimName;
                }
            }

            return shimName.Substring(0, markerStart);
        }

        /// <summary>
        /// Classifies one shim method and reports the receiver type it was declared on. targetType
        /// is null exactly when the result is NotUnityMessage.
        /// </summary>
        internal static Classification Classify(MethodInfo shim, out Type targetType)
        {
            Debug.Assert(shim != null, "shim must not be null.");
            targetType = null;
            ParameterInfo[] parameters = shim.GetParameters();
            // Why the parameter name and not the type alone: a user's own static method that takes
            // the enclosing MonoBehaviour as its first argument has the same shape, and forwarding
            // it would call it with a receiver it never asked for. Only the worker emits this name.
            if (parameters.Length == 0
                || !string.Equals(parameters[0].Name, ReceiverParameterName, StringComparison.Ordinal))
            {
                return Classification.NotUnityMessage;
            }

            Type receiverType = parameters[0].ParameterType;
            if (!typeof(MonoBehaviour).IsAssignableFrom(receiverType))
            {
                return Classification.NotUnityMessage;
            }

            string memberName = ResolveMemberName(shim);
            if (!HotReloadUnityMessageNames.IsKnownMessage(memberName))
            {
                return Classification.NotUnityMessage;
            }

            targetType = receiverType;
            // Why a non-void message is reported as a Unity message that is not forwarded: Unity
            // still dispatches it once compiled, so the caller must not describe it as an ordinary
            // added method, but the proxy's emitted method returns void and cannot carry a result.
            if (shim.ReturnType != typeof(void))
            {
                return Classification.NotForwarded;
            }

            if (!CanCarryParameters(parameters))
            {
                return Classification.NotForwarded;
            }

            return HotReloadUnityMessageNames.IsForwarded(memberName)
                ? Classification.Forwarded
                : Classification.NotForwarded;
        }

        // The forwarded call travels through an object[], so a parameter that cannot live in one
        // has to compile instead: boxing a managed pointer and casting to a by-ref or open generic
        // type are both invalid IL, and the proxy would fail to verify rather than misbehave.
        private static bool CanCarryParameters(ParameterInfo[] parameters)
        {
            for (int index = 1; index < parameters.Length; index++)
            {
                Type parameterType = parameters[index].ParameterType;
                if (parameterType.IsByRef
                    || parameterType.IsPointer
                    || parameterType.ContainsGenericParameters)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
