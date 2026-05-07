
namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    // Keeps UI mouse simulation drag recovery inside the simulate-mouse-ui tool module.
    /// <summary>
    /// Initializes Simulate Mouse UI Editor editor startup behavior.
    /// </summary>
    internal static class SimulateMouseUiEditorStartup
    {
        internal static void Initialize()
        {
            MouseDragState.InitializeForEditorStartup();
        }
    }
}
