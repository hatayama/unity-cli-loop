using System;
using UnityEditor;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop
{
    internal static class CliUninstallPrompt
    {
        private const string DialogTitle = "Uninstall uLoop CLI?";
        private const string DialogMessage =
            "This removes the native uLoop CLI command from this user account and removes package-owned PATH entries when applicable.\n\n"
            + "Project-local files are not removed.";
        private const string ConfirmButtonText = "OK";
        private const string CancelButtonText = "Cancel";

        public static bool ConfirmUninstall()
        {
            return ConfirmUninstall(
                (title, message, ok, cancel) => EditorUtility.DisplayDialog(title, message, ok, cancel));
        }

        internal static bool ConfirmUninstall(Func<string, string, string, string, bool> displayDialog)
        {
            Debug.Assert(displayDialog != null, "displayDialog must not be null");

            return displayDialog(
                DialogTitle,
                DialogMessage,
                ConfirmButtonText,
                CancelButtonText);
        }
    }
}
