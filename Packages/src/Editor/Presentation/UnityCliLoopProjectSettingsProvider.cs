using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// Shows Unity CLI Loop project-scoped (git-shared) settings under Edit > Project Settings.
    /// </summary>
    internal static class UnityCliLoopProjectSettingsProvider
    {
        private const string SETTINGS_MENU_PATH = "Project/Unity CLI Loop";

        private static IUnityCliLoopProjectSettingsPort RegisteredProjectSettingsPort;

        internal static void InitializeEditorServices(IUnityCliLoopProjectSettingsPort projectSettingsPort)
        {
            Debug.Assert(projectSettingsPort != null, "projectSettingsPort must not be null");

            RegisteredProjectSettingsPort = projectSettingsPort
                ?? throw new System.ArgumentNullException(nameof(projectSettingsPort));
        }

        [SettingsProvider]
        private static SettingsProvider CreateProjectSettingsProvider()
        {
            return new SettingsProvider(SETTINGS_MENU_PATH, SettingsScope.Project)
            {
                guiHandler = _ => DrawSettings(),
                keywords = new[] { "uloop", "CLI", "Setup", "Wizard", "popup", "suppress" }
            };
        }

        private static void DrawSettings()
        {
            // The provider can be drawn before editor startup wiring runs (e.g. while scripts
            // are still initializing), so a missing port is reported instead of asserted.
            if (RegisteredProjectSettingsPort == null)
            {
                EditorGUILayout.HelpBox("Unity CLI Loop is still initializing.", MessageType.Info);
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Setup Wizard", EditorStyles.boldLabel);

            bool currentValue = RegisteredProjectSettingsPort.GetSuppressSetupWizardAutoShow();
            bool updatedValue = DrawSuppressToggle(currentValue);

            EditorGUILayout.Space(2f);
            EditorGUILayout.HelpBox(
                "When enabled, the Setup Wizard window no longer opens automatically after this "
                    + "package is installed or updated. This applies to everyone on the project: "
                    + "the value is saved to ProjectSettings/Packages/"
                    + ToolContracts.UnityCliLoopConstants.PACKAGE_NAME
                    + "/settings.json, so commit that file to share the setting with your team.",
                MessageType.Info);

            if (updatedValue == currentValue) return;

            RegisteredProjectSettingsPort.SetSuppressSetupWizardAutoShow(updatedValue);
        }

        // ToggleLeft draws the label flush against the checkbox, so the toggle and label are
        // laid out manually to leave a readable gap between them.
        private static bool DrawSuppressToggle(bool currentValue)
        {
            EditorGUILayout.BeginHorizontal();
            bool updatedValue = EditorGUILayout.Toggle(currentValue, GUILayout.Width(16f));
            GUILayout.Space(4f);
            GUILayout.Label("Suppress Setup Wizard popup");
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            return updatedValue;
        }
    }
}
