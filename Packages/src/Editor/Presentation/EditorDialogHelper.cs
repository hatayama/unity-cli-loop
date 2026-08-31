using UnityEditor;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Provides helper operations for Editor Dialog behavior.
    /// </summary>
    public static class EditorDialogHelper
    {
        public static void ShowSkillsInstalledDialog()
        {
#if UNITY_6000_3_OR_NEWER
            EditorDialog.DisplayAlertDialog(
                "Skills Installed",
                "Skills have been installed successfully.",
                "OK",
                DialogIconType.Info);
#else
            EditorUtility.DisplayDialog(
                "Skills Installed",
                "Skills have been installed successfully.",
                "OK");
#endif
        }
    }
}
