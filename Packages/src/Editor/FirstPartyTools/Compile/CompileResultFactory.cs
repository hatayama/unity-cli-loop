using System;
using System.Linq;
using UnityEditor.Compilation;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Creates raw CompileResult instances before CompileSessionResultService shapes them into CompileResponse DTOs.
    /// </summary>
    internal static class CompileResultFactory
    {
        internal static CompileResult CreateCompileResult(
            CompilerMessage[] compileMessages,
            bool isForceCompile)
        {
            int errorCount = compileMessages.Count(m => m.type == CompilerMessageType.Error);
            int warningCount = compileMessages.Count(m => m.type == CompilerMessageType.Warning);

            // Why: Unity does not expose reliable detailed issue data for this clean compile path.
            if (isForceCompile)
            {
                return new CompileResult(
                    success: null,
                    errorCount: errorCount,
                    warningCount: warningCount,
                    completedAt: DateTime.Now,
                    messages: new CompilerMessage[0],
                    errors: new CompilerMessage[0],
                    warnings: new CompilerMessage[0],
                    isIndeterminate: true,
                    message: null
                );
            }

            CompilerMessage[] errors = compileMessages.Where(m => m.type == CompilerMessageType.Error).ToArray();
            CompilerMessage[] warnings = compileMessages.Where(m => m.type == CompilerMessageType.Warning).ToArray();

            return new CompileResult(
                success: errorCount == 0,
                errorCount: errorCount,
                warningCount: warningCount,
                completedAt: DateTime.Now,
                messages: compileMessages,
                errors: errors,
                warnings: warnings
            );
        }

        /// <summary>
        /// Creates the result used when Unity stops compiling before the finish callback is received.
        /// </summary>
        internal static CompileResult CreateStoppedWithoutFinishResult(
            AssemblyDefinitionConsoleErrorResult assemblyDefinitionErrors,
            CompilerMessage[] compileMessages,
            bool isForceCompile,
            string message)
        {
            UnityEngine.Debug.Assert(assemblyDefinitionErrors != null, "assemblyDefinitionErrors must not be null");
            UnityEngine.Debug.Assert(compileMessages != null, "compileMessages must not be null");

            if (assemblyDefinitionErrors.HasErrors)
            {
                return CreateAssemblyDefinitionFailureResult(assemblyDefinitionErrors);
            }

            return CreateIndeterminateCompileResultFromMessages(compileMessages, isForceCompile, message);
        }

        /// <summary>
        /// Creates an unknown compile result from the compiler messages already observed by this request.
        /// </summary>
        private static CompileResult CreateIndeterminateCompileResultFromMessages(
            CompilerMessage[] compileMessages,
            bool isForceCompile,
            string message)
        {
            UnityEngine.Debug.Assert(compileMessages != null, "compileMessages must not be null");

            CompilerMessage[] errors = compileMessages.Where(m => m.type == CompilerMessageType.Error).ToArray();
            CompilerMessage[] warnings = compileMessages.Where(m => m.type == CompilerMessageType.Warning).ToArray();
            CompilerMessage[] messages = isForceCompile ? Array.Empty<CompilerMessage>() : compileMessages;
            CompilerMessage[] resultErrors = isForceCompile ? Array.Empty<CompilerMessage>() : errors;
            CompilerMessage[] resultWarnings = isForceCompile ? Array.Empty<CompilerMessage>() : warnings;
            return new CompileResult(
                success: null,
                errorCount: errors.Length,
                warningCount: warnings.Length,
                completedAt: DateTime.Now,
                messages: messages,
                errors: resultErrors,
                warnings: resultWarnings,
                isIndeterminate: true,
                message: message
            );
        }

        /// <summary>
        /// Creates a failed compile result from Assembly Definition and Assembly Reference Console errors.
        /// </summary>
        internal static CompileResult CreateAssemblyDefinitionFailureResult(
            AssemblyDefinitionConsoleErrorResult assemblyDefinitionErrors)
        {
            CompilerMessage[] errors = CreateAssemblyDefinitionCompilerMessages(assemblyDefinitionErrors.Errors);
            return new CompileResult(
                success: false,
                errorCount: errors.Length,
                warningCount: 0,
                completedAt: DateTime.Now,
                messages: errors,
                errors: errors,
                warnings: Array.Empty<CompilerMessage>(),
                message: assemblyDefinitionErrors.Message
            );
        }

        /// <summary>
        /// Creates a failed compile result for external Scene changes that cannot be auto-resolved.
        /// </summary>
        internal static CompileResult CreateExternalSceneChangeFailureResult(
            (bool CanProceed, string Message, string[] ScenePaths) sceneChangeResult)
        {
            UnityEngine.Debug.Assert(!sceneChangeResult.CanProceed, "sceneChangeResult must be a failure");

            CompilerMessage[] errors = CreateExternalSceneChangeCompilerMessages(sceneChangeResult);
            return new CompileResult(
                success: false,
                errorCount: errors.Length,
                warningCount: 0,
                completedAt: DateTime.Now,
                messages: errors,
                errors: errors,
                warnings: Array.Empty<CompilerMessage>(),
                message: sceneChangeResult.Message,
                preserveDetailsWhenForceRecompile: true
            );
        }

        /// <summary>
        /// Converts unresolved external Scene changes into compiler-shaped errors for compile responses.
        /// </summary>
        private static CompilerMessage[] CreateExternalSceneChangeCompilerMessages(
            (bool CanProceed, string Message, string[] ScenePaths) sceneChangeResult)
        {
            UnityEngine.Debug.Assert(sceneChangeResult.ScenePaths != null, "scene paths must not be null");
            UnityEngine.Debug.Assert(sceneChangeResult.ScenePaths.Length > 0, "scene paths must not be empty");

            CompilerMessage[] errors = new CompilerMessage[sceneChangeResult.ScenePaths.Length];
            for (int i = 0; i < sceneChangeResult.ScenePaths.Length; i++)
            {
                errors[i] = new CompilerMessage
                {
                    type = CompilerMessageType.Error,
                    message = sceneChangeResult.Message,
                    file = sceneChangeResult.ScenePaths[i],
                    line = 0
                };
            }

            return errors;
        }

        /// <summary>
        /// Converts Assembly Definition and Assembly Reference Console errors into compiler messages.
        /// </summary>
        private static CompilerMessage[] CreateAssemblyDefinitionCompilerMessages(
            AssemblyDefinitionConsoleError[] assemblyDefinitionErrors)
        {
            CompilerMessage[] messages = new CompilerMessage[assemblyDefinitionErrors.Length];
            for (int i = 0; i < assemblyDefinitionErrors.Length; i++)
            {
                AssemblyDefinitionConsoleError error = assemblyDefinitionErrors[i];
                messages[i] = new CompilerMessage
                {
                    type = CompilerMessageType.Error,
                    message = error.Message,
                    file = error.File,
                    line = error.Line
                };
            }

            return messages;
        }
    }
}
