using UnityEditor;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    // Infrastructure scheduler for delayed settings file recovery during Editor startup.
    /// <summary>
    /// Schedules Unity CLI Loop Editor Settings Recovery work at the point the owning workflow expects.
    /// </summary>
    internal static class UnityCliLoopEditorSettingsRecoveryScheduler
    {
        internal static void ScheduleForEditorStartup(UnityCliLoopEditorSettingsService editorSettingsService)
        {
            System.Diagnostics.Debug.Assert(editorSettingsService != null, "editorSettingsService must not be null");

            if (AssetDatabase.IsAssetImportWorkerProcess())
            {
                return;
            }

            EditorApplication.delayCall += editorSettingsService.RecoverSettingsFileIfNeeded;
        }
    }
}
