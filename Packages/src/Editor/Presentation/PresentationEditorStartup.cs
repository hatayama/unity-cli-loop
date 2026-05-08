namespace io.github.hatayama.UnityCliLoop.Presentation
{
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
