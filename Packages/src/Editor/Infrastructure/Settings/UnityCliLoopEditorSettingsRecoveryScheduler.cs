using UnityEditor;

using io.github.hatayama.UnityCliLoop.Application;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    // Infrastructure scheduler for delayed settings file recovery during Editor startup.
    /// <summary>
    /// Schedules Unity CLI Loop Editor Settings Recovery work at the point the owning workflow expects.
    /// </summary>
    internal static class UnityCliLoopEditorSettingsRecoveryScheduler
    {
        internal static void ScheduleForEditorStartup()
        {
            if (AssetDatabase.IsAssetImportWorkerProcess())
            {
                return;
            }

            EditorApplication.delayCall += UnityCliLoopEditorSettings.RecoverSettingsFileIfNeeded;
        }
    }
}
