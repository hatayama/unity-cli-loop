#nullable enable
#if ULOOP_HAS_INPUT_SYSTEM

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Reports whether input observation finished normally, paused, or exceeded the wall-clock guard.
    /// </summary>
    internal enum InputSimulationWaitOutcome
    {
        Completed = 0,
        Paused = 1,
        TimedOut = 2
    }

}
#endif
