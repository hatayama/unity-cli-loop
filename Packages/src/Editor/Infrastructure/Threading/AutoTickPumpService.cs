using System.Diagnostics;
using UnityEditor;

using io.github.hatayama.UnityCliLoop.InternalAPIBridge;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Editor glue that calls SignalTick from the editor's own update/tick handlers for the whole
    /// editor session, throttled to one call per PUMP_INTERVAL_MS. The approach follows
    /// com.unity.pipeline's AutoTickCommand.
    /// What it does not do: it does not change the tick cadence of an unfocused editor. Unity
    /// runs update roughly every 100ms while the editor is in the background, and a SignalTick
    /// issued on the main thread from inside update/tick does not schedule an earlier tick.
    /// Measured unfocused on 2022.3 and 6000.3 (the same holds for AutoTickCommand, including its
    /// 0ms interval), so nothing should rely on this pump for faster continuations.
    /// Why keep it: the previous scoped pump (in-flight request + trailing window) let an
    /// unfocused editor go fully idle after the window expired; macOS then stopped scheduling
    /// the process, so the next IPC request could not even be accepted (pre_accept_timeout)
    /// and the CLI had to grab OS-level focus to wake Unity. This always-on pump was introduced
    /// against that parking. Whether it is what prevents parking has not been verified in a
    /// long-running measurement, so it stays until that is shown either way.
    /// </summary>
    internal static class AutoTickPumpService
    {
        private static Stopwatch _throttle;

        internal static void RegisterForEditorStartup()
        {
            // Why: leave unstarted so the first Pump after an external SignalTick is not throttled
            // and issues its own SignalTick right away.
            _throttle = new Stopwatch();

            // Same dual-registration pattern as EditorMainThreadDispatcher.Initialize:
            // update covers the normal editor loop; tick covers SignalTick-driven wakeups.
            EditorApplication.update -= Pump;
            EditorApplication.update += Pump;
            EditorApplicationTickBridge.RemoveTickHandler(Pump);
            EditorApplicationTickBridge.AddTickHandler(Pump);

            // Why: after domain reload the editor may already be unfocused and idle; one explicit
            // tick asks for the first update/tick after the reload without an OS focus kick.
            EditorApplicationTickBridge.SignalTick();
        }

        private static void Pump()
        {
            if (_throttle == null)
            {
                return;
            }

            // Why: !IsRunning covers the first tick after the Register wake-up, so the pump
            // signals on its first run instead of waiting out the interval gate.
            if (_throttle.IsRunning &&
                _throttle.ElapsedMilliseconds < AutoTickPumpConstants.PUMP_INTERVAL_MS)
            {
                return;
            }

            _throttle.Restart();
            EditorApplicationTickBridge.SignalTick();
        }
    }
}
