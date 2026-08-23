using System.Threading;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

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
            CompileController compileController = new(
                UnityCliLoopCompileResultSessionRepositoryFacade.Repository,
                UnityCliLoopPendingCompileSessionRepositoryFacade.Repository);

            try
            {
                if (!ValidateCompilationStateBeforeControllerExecution())
                {
                    return;
                }

                CompileResult result = await compileController.TryCompileAsync(
                    forceRecompile: false,
                    playModeStopWarning: null,
                    ct: CancellationToken.None);
                CompilerMessage[] err = result.Errors;
                CompilerMessage[] warning = result.Warnings;

                Debug.Log($"Compilation result: Success={result.Success}");
                Debug.Log($"Number of errors: {err.Length}");
                Debug.Log($"Number of warnings: {warning.Length}");

                foreach (CompilerMessage error in err)
                {
                    Debug.LogError($"Error: {error.message} at {error.file}:{error.line}");
                }

                foreach (CompilerMessage warn in warning)
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
            CompileController compileController = new(
                UnityCliLoopCompileResultSessionRepositoryFacade.Repository,
                UnityCliLoopPendingCompileSessionRepositoryFacade.Repository);

            try
            {
                if (!ValidateCompilationStateBeforeControllerExecution())
                {
                    return;
                }

                // Example of forced re-compilation
                CompileResult result = await compileController.TryCompileAsync(
                    forceRecompile: true,
                    playModeStopWarning: null,
                    ct: CancellationToken.None);
                CompilerMessage[] err = result.Errors;
                CompilerMessage[] warning = result.Warnings;

                Debug.Log($"Forced compilation result: Success={result.Success}");
                Debug.Log($"Number of errors: {err.Length}, Number of warnings: {warning.Length}");
            }
            finally
            {
                compileController.Dispose();
            }
        }

        private static bool ValidateCompilationStateBeforeControllerExecution()
        {
            CompilationStateValidationService validationService = new();
            ValidationResult validation = validationService.ValidateCompilationState();
            if (validation.IsValid)
            {
                return true;
            }

            Debug.LogWarning(validation.ErrorMessage);
            return false;
        }
    }
}
