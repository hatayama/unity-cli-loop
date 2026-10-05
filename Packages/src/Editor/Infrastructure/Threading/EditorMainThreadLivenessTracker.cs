using System;
using System.Threading;
using UnityEditor;

using io.github.hatayama.UnityCliLoop.InternalAPIBridge;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Records when the editor main thread last pumped EditorApplication.update or tick so
    /// background threads (the IPC heartbeat sender) can report main-thread stalls.
    /// </summary>
    internal static class EditorMainThreadLivenessTracker
    {
        // Initialized to "now" so stall reports stay sane before the first update tick.
        private static long _lastTickUtcTicks = DateTime.UtcNow.Ticks;

        internal static void RegisterForEditorStartup()
        {
            EditorApplication.update -= RecordTick;
            EditorApplication.update += RecordTick;
            // Why tick as well: EditorMainThreadDispatcher runs uloop's main-thread work on update and
            // on tick, and SignalTick wakes tick. Recording both keeps the counter about whether uloop
            // work can run now, so a loop woken by uloop status reads as alive.
            EditorApplicationTickBridge.RemoveTickHandler(RecordTick);
            EditorApplicationTickBridge.AddTickHandler(RecordTick);
            RecordTick();
        }

        private static void RecordTick()
        {
            Volatile.Write(ref _lastTickUtcTicks, DateTime.UtcNow.Ticks);
        }

        /// <summary>
        /// Returns how long the main thread has gone without an update or tick.
        /// Safe to call from any thread.
        /// </summary>
        internal static double SecondsSinceLastMainThreadTick()
        {
            long lastTickTicks = Volatile.Read(ref _lastTickUtcTicks);
            double seconds = (DateTime.UtcNow - new DateTime(lastTickTicks, DateTimeKind.Utc)).TotalSeconds;
            return seconds < 0 ? 0 : seconds;
        }
    }
}
