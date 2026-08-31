using System.Diagnostics;
using UnityEditor;

using io.github.hatayama.UnityCliLoop.InternalAPIBridge;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Editor glue that keeps a SignalTick pump running for the whole editor session,
    /// mirroring com.unity.pipeline's AutoTickCommand (unconditional 16ms pump).
    /// Why always-on: the previous scoped pump (in-flight request + trailing window) let an
    /// unfocused editor go fully idle after the window expired; macOS then stopped scheduling
    /// the process, so the next IPC request could not even be accepted (pre_accept_timeout)
    /// and the CLI had to grab OS-level focus to wake Unity. Continuous ticking keeps the
    /// process from ever being parked, so requests are served without a focus kick.
    /// </summary>
    internal static class AutoTickPumpService
    {
        private static Stopwatch _throttle;

        internal static void RegisterForEditorStartup()
        {
            // Why: leave unstarted so the first Pump after an external SignalTick is not throttled.
            // If that first tick were swallowed, an unfocused editor would never start the pump chain.
            _throttle = new Stopwatch();

            // Same dual-registration pattern as EditorMainThreadDispatcher.Initialize:
            // update covers the normal editor loop; tick covers SignalTick-driven wakeups.
            EditorApplication.update -= Pump;
            EditorApplication.update += Pump;
            EditorApplicationTickBridge.RemoveTickHandler(Pump);
            EditorApplicationTickBridge.AddTickHandler(Pump);

            // Why: after domain reload the editor may already be unfocused and idle; one explicit
            // tick starts the self-sustaining pump chain without an OS focus kick.
            EditorApplicationTickBridge.SignalTick();
        }

        private static void Pump()
        {
            if (_throttle == null)
            {
                return;
            }

            // Why: !IsRunning covers the first tick after the Register wake-up. Swallowing
            // that tick under the interval gate would leave an unfocused editor without a
            // follow-up SignalTick, so the self-sustaining pump chain would never start.
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
