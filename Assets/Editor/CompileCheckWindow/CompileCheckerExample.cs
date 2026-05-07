using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Dev
{
    /// <summary>
    /// Provides Compile Checker Example behavior for Unity CLI Loop.
    /// </summary>
    public class CompileCheckerExample
    {
        [MenuItem("UnityCliLoop/Debug/Compile Tests/Compile Checker Usage Example")]
        public static async void TestCompileChecker()
        {
            CompileController compileController = new();
            
            try
            {
                // How Masamichi requested to use it
                CompileResult result = await compileController.TryCompileAsync();
                var err = result.error;
                var warning = result.warning;

                Debug.Log($"Compilation result: Success={result.Success}");
                Debug.Log($"Number of errors: {err.Length}");
                Debug.Log($"Number of warnings: {warning.Length}");

                // Display error details
                foreach (var error in err)
                {
                    Debug.LogError($"Error: {error.message} at {error.file}:{error.line}");
                }

                // Display warning details
                foreach (var warn in warning)
                {
                    Debug.LogWarning($"Warning: {warn.message} at {warn.file}:{warn.line}");
                }
            }
            finally
            {
                compileController.Dispose();
            }
        }

        [MenuItem("UnityCliLoop/Debug/Compile Tests/Force Compile Checker Usage Example")]
        public static async void TestForceCompileChecker()
        {
            CompileController compileController = new();
            
            try
            {
                // Example of forced re-compilation
                CompileResult result = await compileController.TryCompileAsync(forceRecompile: true);
                var err = result.error;
                var warning = result.warning;

                Debug.Log($"Forced compilation result: Success={result.Success}");
                Debug.Log($"Number of errors: {err.Length}, Number of warnings: {warning.Length}");
            }
            finally
            {
                compileController.Dispose();
            }
        }
    }
} 