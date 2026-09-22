#if ULOOP_HAS_INPUT_SYSTEM
using System;
using System.Diagnostics;
using System.Reflection;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Decides from the current call stack whether a messageless assertion was raised by the Input System's
    /// monitor-removal path rather than by user code running inside an input callback.
    /// </summary>
    internal sealed class InputSystemMonitorRemovalAssertionOrigin
    {
        private const string UnityEngineNamespace = "UnityEngine";
        private const string DynamicBitfieldTypeName = "UnityEngine.InputSystem.DynamicBitfield";
        private const string ClearBitMethodName = "ClearBit";
        private const string InputManagerTypeName = "UnityEngine.InputSystem.InputManager";
        private const string InputManagerStateMonitorsTypeName = "UnityEngine.InputSystem.InputManagerStateMonitors";
        private const string FireStateChangeNotificationsMethodName = "FireStateChangeNotifications";

        /// <summary>
        /// Returns true only when the first frame below the logging frames is DynamicBitfield.ClearBit,
        /// or FireStateChangeNotifications in case the JIT inlined ClearBit into it. That caller is
        /// InputManager.FireStateChangeNotifications in Input System 1.14 and
        /// InputManagerStateMonitors.FireStateChangeNotifications in Input System 1.20, which moved it,
        /// so both declaring types are accepted.
        /// A user callback's bare assert always has the user's method there, so it is never matched.
        /// </summary>
        public bool IsCurrentAssertionFromMonitorRemoval()
        {
            StackTrace stackTrace = new StackTrace(false);
            StackFrame[] frames = stackTrace.GetFrames();
            if (frames == null)
            {
                return false;
            }

            foreach (StackFrame frame in frames)
            {
                MethodBase method = frame.GetMethod();
                Type declaringType = method?.DeclaringType;
                // Unknown frames fail closed so a log is never hidden without a positive origin match.
                if (declaringType == null)
                {
                    return false;
                }

                if (IsLoggingFrame(declaringType))
                {
                    continue;
                }

                return IsMonitorRemovalFrame(declaringType.FullName, method.Name);
            }

            return false;
        }

        private static bool IsLoggingFrame(Type declaringType)
        {
            if (declaringType == typeof(InputSystemMonitorRemovalAssertionOrigin)
                || declaringType == typeof(InputSystemMonitorRemovalAssertionLogFilter))
            {
                return true;
            }

            return declaringType.Namespace == UnityEngineNamespace;
        }

        internal static bool IsMonitorRemovalFrame(string typeName, string methodName)
        {
            if (typeName == DynamicBitfieldTypeName && methodName == ClearBitMethodName)
            {
                return true;
            }

            if (methodName != FireStateChangeNotificationsMethodName)
            {
                return false;
            }

            return typeName == InputManagerTypeName || typeName == InputManagerStateMonitorsTypeName;
        }
    }
}
#endif
