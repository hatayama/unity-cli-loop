
namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    // Keeps keyboard simulation state recovery inside the simulate-keyboard tool module.
    /// <summary>
    /// Initializes Simulate Keyboard Editor editor startup behavior.
    /// </summary>
    internal static class SimulateKeyboardEditorStartup
    {
        internal static void Initialize()
        {
            KeyboardKeyState.InitializeForEditorStartup();
        }
    }
}
