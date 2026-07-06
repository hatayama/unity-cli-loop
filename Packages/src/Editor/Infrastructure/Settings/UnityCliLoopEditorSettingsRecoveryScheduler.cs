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
        internal static void ScheduleForEditorStartup(IUnityCliLoopEditorSettingsPort editorSettingsPort)
        {
            System.Diagnostics.Debug.Assert(editorSettingsPort != null, "editorSettingsPort must not be null");

            if (AssetDatabase.IsAssetImportWorkerProcess())
            {
                return;
            }

            EditorApplication.delayCall += editorSettingsPort.RecoverSettingsFileIfNeeded;
        }
    }
}
