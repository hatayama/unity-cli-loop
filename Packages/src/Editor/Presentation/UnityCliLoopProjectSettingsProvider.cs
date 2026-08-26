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
            bool currentValue = RegisteredProjectSettingsPort.GetSuppressSetupWizardAutoShow();
            GUIContent label = new(
                "Suppress Setup Wizard popup",
                "Stops the Setup Wizard from opening automatically after package install or update "
                    + "for everyone on this project. Saved to ProjectSettings/Packages and shared "
                    + "via version control.");
            bool updatedValue = EditorGUILayout.ToggleLeft(label, currentValue);
            if (updatedValue == currentValue) return;

            RegisteredProjectSettingsPort.SetSuppressSetupWizardAutoShow(updatedValue);
        }
    }
}
