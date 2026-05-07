
namespace io.github.hatayama.UnityCliLoop.Presentation
{
    // Groups presentation startup behind one facade so UI boot decisions stay in the presentation layer.
    /// <summary>
    /// Initializes Presentation Editor editor startup behavior.
    /// </summary>
    internal static class PresentationEditorStartup
    {
        internal static void Initialize()
        {
            SetupWizardWindow.InitializeForEditorStartup();
        }
    }
}
