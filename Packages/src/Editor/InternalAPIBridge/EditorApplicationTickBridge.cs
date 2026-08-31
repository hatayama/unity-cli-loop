using UnityEditor;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.InternalAPIBridge
{
    /// <summary>
    /// SignalTick is Unity's thread-safe route for waking editor ticks from background IPC without OS run-loop APIs.
    /// </summary>
    public static class EditorApplicationTickBridge
    {
        public static void AddTickHandler(EditorApplication.CallbackFunction callback)
        {
            Debug.Assert(callback != null, "callback must not be null");

            EditorApplication.tick -= callback;
            EditorApplication.tick += callback;
        }

        public static void RemoveTickHandler(EditorApplication.CallbackFunction callback)
        {
            Debug.Assert(callback != null, "callback must not be null");

            EditorApplication.tick -= callback;
        }

        public static void SignalTick()
        {
            EditorApplication.SignalTick();
        }
    }
}
