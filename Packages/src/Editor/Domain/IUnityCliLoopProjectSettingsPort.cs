namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Defines the persistence boundary for project-scoped (git-shared) Unity CLI Loop settings,
    /// owned by Infrastructure. Unlike <see cref="IUnityCliLoopEditorSettingsPort"/>, values behind
    /// this port apply to every team member working on the project.
    /// </summary>
    public interface IUnityCliLoopProjectSettingsPort
    {
        bool GetSuppressSetupWizardAutoShow();
        void SetSuppressSetupWizardAutoShow(bool suppressAutoShow);
    }
}
