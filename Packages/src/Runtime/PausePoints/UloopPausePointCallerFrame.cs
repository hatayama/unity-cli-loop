#if UNITY_EDITOR
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.Runtime
{
    /// <summary>
    /// One managed caller stack frame captured at a pause point hit, nearest caller first.
    /// </summary>
    internal sealed class UloopPausePointCallerFrame
    {
        public UloopPausePointCallerFrame(string method, string file, int line)
        {
            Debug.Assert(!string.IsNullOrEmpty(method), "method must not be null or empty");
            // A frame without debug symbols must not report a stale line number.
            Debug.Assert(file != null || line == 0, "line must be 0 when file is null");

            Method = method;
            File = file;
            Line = line;
        }

        public string Method { get; }

        // Null when debug symbols are unavailable for the frame (for example a caller whose
        // body is currently hot-reload patched and executes as a Harmony dynamic method).
        public string File { get; }

        public int Line { get; }
    }
}
#endif
