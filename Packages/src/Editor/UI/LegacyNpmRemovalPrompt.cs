using System;
using UnityEditor;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop
{
    internal static class LegacyNpmRemovalPrompt
    {
        private const string DialogTitle = "Remove Old uLoop CLI?";
        private const string DialogMessage =
            "An older Node.js/npm uLoop CLI installation was found. It can shadow the native Go CLI and make the uloop command fail.\n\n"
            + "Remove the old npm CLI before installing the native CLI?";
        private const string ConfirmButtonText = "Remove Old CLI and Install";
        private const string CancelButtonText = "Cancel";

        public static bool ConfirmInstallCanProceed(RuntimePlatform platform)
        {
            bool hasLegacyNpmInstallation = NativeCliInstaller.HasLegacyNpmInstallation(platform);
            return ConfirmInstallCanProceed(
                hasLegacyNpmInstallation,
                (title, message, ok, cancel) => EditorUtility.DisplayDialog(title, message, ok, cancel));
        }

        internal static bool ConfirmInstallCanProceed(
            bool hasLegacyNpmInstallation,
            Func<string, string, string, string, bool> displayDialog)
        {
            Debug.Assert(displayDialog != null, "displayDialog must not be null");

            if (!hasLegacyNpmInstallation)
            {
                return true;
            }

            return displayDialog(
                DialogTitle,
                DialogMessage,
                ConfirmButtonText,
                CancelButtonText);
        }
    }
}
