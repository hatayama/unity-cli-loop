using io.github.hatayama.UnityCliLoop.Application;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    // Groups infrastructure startup behind one facade so outer boot order stays explicit.
    /// <summary>
    /// Initializes Infrastructure Editor editor startup behavior.
    /// </summary>
    internal static class InfrastructureEditorStartup
    {
        internal static void Initialize()
        {
            UnityCliLoopEditorSettingsRecoveryScheduler.ScheduleForEditorStartup();
            CompilationLockService.RegisterForEditorStartup();
            ProjectLocalCliAutoInstaller.ScheduleForEditorStartup();
        }
    }
}
