
namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    // Keeps mouse input simulation state recovery inside the simulate-mouse-input tool module.
    /// <summary>
    /// Initializes Simulate Mouse Input Editor editor startup behavior.
    /// </summary>
    internal static class SimulateMouseInputEditorStartup
    {
        internal static void Initialize()
        {
            MouseInputState.InitializeForEditorStartup();
        }
    }
}
