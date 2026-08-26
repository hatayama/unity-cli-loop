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
        private const float HELP_BOX_ICON_SIZE = 32f;
        private const float HELP_BOX_TEXT_WIDTH_MARGIN = 80f;
        private const float MINIMUM_HELP_BOX_TEXT_WIDTH = 120f;

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
            DrawSelectableHelpBox(
                "When enabled, the Setup Wizard window no longer opens automatically after this "
                    + "package is installed or updated. This applies to everyone on the project: "
                    + "the value is saved to ProjectSettings/Packages/"
                    + ToolContracts.UnityCliLoopConstants.PACKAGE_NAME
                    + "/settings.json, so commit that file to share the setting with your team.");

            if (updatedValue == currentValue) return;

            RegisteredProjectSettingsPort.SetSuppressSetupWizardAutoShow(updatedValue);
        }

        // EditorGUILayout.HelpBox renders its message as a plain label, which cannot be selected
        // or copied. The box is drawn manually so the text can be a SelectableLabel while the
        // icon and framing still match a stock info HelpBox.
        private static void DrawSelectableHelpBox(string message)
        {
            Debug.Assert(!string.IsNullOrEmpty(message), "message must not be null or empty");

            GUIStyle textStyle = EditorStyles.wordWrappedMiniLabel;
            float textWidth = Mathf.Max(
                MINIMUM_HELP_BOX_TEXT_WIDTH,
                EditorGUIUtility.currentViewWidth - HELP_BOX_TEXT_WIDTH_MARGIN);
            // SelectableLabel is a text field underneath, so it never grows to fit wrapped
            // content the way a Label does and needs its height measured up front.
            float textHeight = textStyle.CalcHeight(new GUIContent(message), textWidth);

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            GUILayout.Label(
                EditorGUIUtility.IconContent("console.infoicon"),
                GUILayout.Width(HELP_BOX_ICON_SIZE),
                GUILayout.Height(HELP_BOX_ICON_SIZE));
            EditorGUILayout.SelectableLabel(
                message,
                textStyle,
                GUILayout.Height(textHeight),
                GUILayout.ExpandWidth(true));
            EditorGUILayout.EndHorizontal();
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
