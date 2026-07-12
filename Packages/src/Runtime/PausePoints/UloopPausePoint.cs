using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.Runtime
{
    /// <summary>
    /// Provides the Editor-only pause point marker API for Unity CLI Loop workflows.
    /// </summary>
    public static class UloopPausePoint
    {
        /// <summary>
        /// Pauses at a named marker when the Editor has enabled the same id.
        /// </summary>
        [Conditional("UNITY_EDITOR")]
        public static void Pause(string id)
        {
#if UNITY_EDITOR
            UloopPausePointRegistry.Hit(id);
#endif
        }

        /// <summary>
        /// Tries to read a raw captured variable from the latest pause-point hit while Unity is paused.
        /// </summary>
        public static (bool Found, object Value) TryGetCapturedValue(string name)
        {
#if UNITY_EDITOR
            return UloopPausePointRawCaptureHolder.TryGetCapturedValue(name);
#else
            return (false, null);
#endif
        }

        /// <summary>
        /// Returns captured variable names from the latest pause-point hit, or empty when none is held.
        /// </summary>
        public static IReadOnlyList<string> GetCapturedNames()
        {
#if UNITY_EDITOR
            return UloopPausePointRawCaptureHolder.GetCapturedNames();
#else
            return Array.Empty<string>();
#endif
        }

        /// <summary>
        /// Returns the pause-point id for the latest raw capture snapshot, or empty when none is held.
        /// </summary>
        public static string GetCapturedPausePointId()
        {
#if UNITY_EDITOR
            return UloopPausePointRawCaptureHolder.GetCapturedPausePointId();
#else
            return string.Empty;
#endif
        }
    }
}
