using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.Dev
{
    /// <summary>
    /// Debug tool to analyze Unity Console mask values for different Clear settings
    /// </summary>
    public class ConsoleMaskDebugger : EditorWindow
    {
        [MenuItem("UnityCliLoop/Windows/Console Mask Debugger")]
        public static void ShowWindow()
        {
            GetWindow<ConsoleMaskDebugger>("Console Mask Debugger");
        }

        private void OnGUI()
        {
            GUILayout.Label("Unity Console Mask Analysis", EditorStyles.boldLabel);
            
            if (GUILayout.Button("Get Current Console Mask"))
            {
                int currentMask = GetCurrentConsoleMask();
                Debug.Log($"Current Console Mask: 0x{currentMask:X} ({currentMask})");
                
                // Analyze individual bits
                AnalyzeMaskBits(currentMask);
            }
            
            GUILayout.Space(10);
            GUILayout.Label("Instructions:", EditorStyles.boldLabel);
            GUILayout.Label("1. Change Console Clear settings manually");
            GUILayout.Label("2. Click 'Get Current Console Mask'");
            GUILayout.Label("3. Check the Console for bit analysis");
        }
        
        private int GetCurrentConsoleMask()
        {
            Assembly editorAssembly = Assembly.GetAssembly(typeof(EditorWindow));
            Type logEntriesType = editorAssembly.GetType("UnityEditor.LogEntries");
            
            if (logEntriesType == null)
            {
                Debug.LogError("LogEntries type not found");
                return 0;
            }
            
            PropertyInfo consoleFlagsProperty = logEntriesType.GetProperty("consoleFlags", 
                BindingFlags.Public | BindingFlags.Static);
            
            if (consoleFlagsProperty != null)
            {
                object result = consoleFlagsProperty.GetValue(null);
                return result != null ? (int)result : 0;
            }
            
            return 0;
        }
        
        private void AnalyzeMaskBits(int mask)
        {
            Debug.Log("=== Console Mask Bit Analysis ===");
            
            // Check individual bits
            for (int i = 0; i < 32; i++)
            {
                if ((mask & (1 << i)) != 0)
                {
                    Debug.Log($"Bit {i}: SET (0x{1 << i:X})");
                }
            }
            
            // Known bit patterns analysis
            Debug.Log("=== Known Pattern Analysis ===");
            Debug.Log($"Bit 0 (0x1): {((mask & 0x1) != 0 ? "SET" : "CLEAR")} - Suspected: Clear on Play");
            Debug.Log($"Bit 1 (0x2): {((mask & 0x2) != 0 ? "SET" : "CLEAR")} - Suspected: Collapse");
            Debug.Log($"Bit 2 (0x4): {((mask & 0x4) != 0 ? "SET" : "CLEAR")} - Suspected: Error Pause");
            Debug.Log($"Bit 7 (0x80): {((mask & 0x80) != 0 ? "SET" : "CLEAR")} - Log");
            Debug.Log($"Bit 8 (0x100): {((mask & 0x100) != 0 ? "SET" : "CLEAR")} - Warning");
            Debug.Log($"Bit 9 (0x200): {((mask & 0x200) != 0 ? "SET" : "CLEAR")} - Error");
        }
    }
}