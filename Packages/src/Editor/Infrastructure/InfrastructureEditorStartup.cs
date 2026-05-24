using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    // Groups infrastructure startup behind one facade so outer boot order stays explicit.
    /// <summary>
    /// Initializes Infrastructure Editor editor startup behavior.
    /// </summary>
    internal static class InfrastructureEditorStartup
    {
        internal static void Initialize(UnityCliLoopEditorSettingsService editorSettingsService)
        {
            UnityCliLoopPackageRemovalSettingsResetter packageRemovalSettingsResetter = new(editorSettingsService);
            packageRemovalSettingsResetter.RegisterForEditorStartup();
            UnityCliLoopEditorSettingsRecoveryScheduler.ScheduleForEditorStartup(editorSettingsService);
        }
    }
}
