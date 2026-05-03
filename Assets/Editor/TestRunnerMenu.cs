using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Class that provides menu items related to the Test Runner.
    /// </summary>
    public static class TestRunnerMenu
    {
        [MenuItem("UnityCliLoop/Tools/Test Runner/Run EditMode Tests/All Tests")]
        public static async void RunEditModeTests()
        {
            Debug.Log("Running EditMode tests!");

            SerializableTestResult result = await PlayModeTestExecuter.ExecuteEditModeTest();

            LogTestResult(result);
        }

        [MenuItem("UnityCliLoop/Tools/Test Runner/Run PlayMode Tests/All Tests")]
        public static async void RunPlayModeTests()
        {
            Debug.Log("Running PlayMode tests!");

            SerializableTestResult result = await PlayModeTestExecuter.ExecutePlayModeTest();

            LogTestResult(result);
        }
        
        [MenuItem("UnityCliLoop/Tools/Test Runner/Open Test Runner Window")]
        public static void OpenTestRunnerWindow()
        {
            // Open Unity's Test Runner Window
            EditorApplication.ExecuteMenuItem("Window/General/Test Runner");
            
            Debug.Log("Opened the Test Runner Window!");
        }
        
        // ===== Menu to run a specific test class =====
        
        [MenuItem("UnityCliLoop/Tools/Test Runner/Run Specific Tests/CompileCommandTests")]
        public static async void RunCompileCommandTests()
        {
            Debug.Log("Running only CompileCommandTests!");
            
            TestExecutionFilter filter = TestExecutionFilter.ByClassName("io.github.hatayama.UnityCliLoop.CompileCommandTests");
            SerializableTestResult result = await PlayModeTestExecuter.ExecuteEditModeTest(filter);
                
            LogTestResult(result);
        }
        
        [MenuItem("UnityCliLoop/Tools/Test Runner/Run Specific Tests/GetLogsCommandTests")]
        public static async void RunGetLogsCommandTests()
        {
            Debug.Log("Running only GetLogsCommandTests!");
            
            TestExecutionFilter filter = TestExecutionFilter.ByClassName("io.github.hatayama.UnityCliLoop.GetLogsCommandTests");
            SerializableTestResult result = await PlayModeTestExecuter.ExecuteEditModeTest(filter);
                
            LogTestResult(result);
        }
        
        [MenuItem("UnityCliLoop/Tools/Test Runner/Run Specific Tests/MainThreadSwitcherTests")]
        public static async void RunMainThreadSwitcherTests()
        {
            Debug.Log("Running only MainThreadSwitcherTests!");
            
            TestExecutionFilter filter = TestExecutionFilter.ByClassName("io.github.hatayama.UnityCliLoop.MainThreadSwitcherTests");
            SerializableTestResult result = await PlayModeTestExecuter.ExecuteEditModeTest(filter);
                
            LogTestResult(result);
        }
        
        [MenuItem("UnityCliLoop/Tools/Test Runner/Run Specific Tests/SampleEditModeTest")]
        public static async void RunSampleEditModeTest()
        {
            Debug.Log("Running only SampleEditModeTest!");
            
            TestExecutionFilter filter = TestExecutionFilter.ByClassName("Tests.SampleEditModeTest");
            SerializableTestResult result = await PlayModeTestExecuter.ExecuteEditModeTest(filter);
                
            LogTestResult(result);
        }
        
        /// <summary>
        /// Log test result
        /// </summary>
        private static void LogTestResult(SerializableTestResult result)
        {
            if (result.success)
            {
                Debug.Log($"Test completed successfully! " +
                         $"Passed: {result.passedCount}, " +
                         $"Failed: {result.failedCount}, " +
                         $"Skipped: {result.skippedCount}");
            }
            else
            {
                Debug.LogError($"Test failed: {result.message}");
            }
            
            if (!string.IsNullOrEmpty(result.xmlPath))
            {
                Debug.Log($"XML file saved to: {result.xmlPath}");
                
                // Select the file in the Project view if it exists
                Object xmlAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
                    result.xmlPath.Replace(Application.dataPath, "Assets"));
                if (xmlAsset != null)
                {
                    EditorGUIUtility.PingObject(xmlAsset);
                    Selection.activeObject = xmlAsset;
                }
            }
        }
    }
} 