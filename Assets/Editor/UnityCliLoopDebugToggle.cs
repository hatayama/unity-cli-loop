using UnityEditor;
using UnityEngine;
using System.Linq;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Dev
{
    /// <summary>
    /// Unity Editor menu items for toggling ULOOP_DEBUG and debug-only Roslyn support.
    ///
    /// This file is intended for internal debugging convenience:
    /// - It lives under Assets/Editor/ outside Packages, so it is NOT included in the distributed UnityCliLoop package.
    /// - In production, Roslyn define symbols are managed centrally via UnityCliLoopEditorSettings (see UpdateRoslynDefineSymbol).
    ///   These menus operate only on the currently selected BuildTargetGroup and may temporarily diverge from global policy.
    ///
    /// Related classes:
    /// - UnityCliLoopSettingsWindow: Uses ULOOP_DEBUG to show/hide developer tools
    /// - McpLogger: Debug logging behavior controlled by this symbol
    /// </summary>
    public static class UnityCliLoopDebugToggle
    {
        private const string MENU_PATH_ENABLE = "UnityCliLoop/Tools/Debug Settings/Enable Debug Mode";
        private const string MENU_PATH_DISABLE = "UnityCliLoop/Tools/Debug Settings/Disable Debug Mode";

        /// <summary>
        /// Check if ULOOP_DEBUG symbol is currently defined
        /// </summary>
        private static bool IsDebugModeEnabled()
        {
            BuildTargetGroup targetGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
            string defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(targetGroup);
            return defines.Split(';').Contains(UnityCliLoopConstants.SCRIPTING_DEFINE_ULOOP_DEBUG);
        }

        /// <summary>
        /// Enable ULOOP_DEBUG scripting define symbol
        /// </summary>
        [MenuItem(MENU_PATH_ENABLE)]
        public static void EnableDebugMode()
        {
            if (IsDebugModeEnabled())
            {
                Debug.Log("[UnityCliLoop] Debug mode is already enabled");
                return;
            }

            BuildTargetGroup targetGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
            string defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(targetGroup);
            
            if (string.IsNullOrEmpty(defines))
            {
                defines = UnityCliLoopConstants.SCRIPTING_DEFINE_ULOOP_DEBUG;
            }
            else
            {
                defines += ";" + UnityCliLoopConstants.SCRIPTING_DEFINE_ULOOP_DEBUG;
            }
            
            PlayerSettings.SetScriptingDefineSymbolsForGroup(targetGroup, defines);
            Debug.Log("[UnityCliLoop] Debug mode enabled. Unity will recompile scripts.");
        }

        /// <summary>
        /// Disable ULOOP_DEBUG scripting define symbol
        /// </summary>
        [MenuItem(MENU_PATH_DISABLE)]
        public static void DisableDebugMode()
        {
            if (!IsDebugModeEnabled())
            {
                Debug.Log("[UnityCliLoop] Debug mode is already disabled");
                return;
            }

            BuildTargetGroup targetGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
            string defines = PlayerSettings.GetScriptingDefineSymbolsForGroup(targetGroup);
            
            string[] defineArray = defines.Split(';');
            defineArray = defineArray.Where(d => d != UnityCliLoopConstants.SCRIPTING_DEFINE_ULOOP_DEBUG).ToArray();
            defines = string.Join(";", defineArray);
            
            PlayerSettings.SetScriptingDefineSymbolsForGroup(targetGroup, defines);
            Debug.Log("[UnityCliLoop] Debug mode disabled. Unity will recompile scripts.");
        }

        /// <summary>
        /// Validate menu item - only show Enable when debug mode is disabled
        /// </summary>
        [MenuItem(MENU_PATH_ENABLE, true)]
        public static bool ValidateEnableDebugMode()
        {
            return !IsDebugModeEnabled();
        }

        /// <summary>
        /// Validate menu item - only show Disable when debug mode is enabled
        /// </summary>
        [MenuItem(MENU_PATH_DISABLE, true)]
        public static bool ValidateDisableDebugMode()
        {
            return IsDebugModeEnabled();
        }

    }
}