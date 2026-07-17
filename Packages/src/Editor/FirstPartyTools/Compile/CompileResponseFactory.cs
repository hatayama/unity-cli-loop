using System;
using System.Diagnostics;
using System.Linq;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Creates CompileResponse DTOs from raw CompileResult values produced by CompileResultFactory.
    /// </summary>
    internal static class CompileResponseFactory
    {
        private const string MissingTestFrameworkReferenceHint =
            "Possible test asmdef issue: Unity test framework symbols are missing. Make sure com.unity.test-framework is installed and add optionalUnityReferences: [\"TestAssemblies\"] or enable testAssemblies on the test asmdef.";

        private static readonly string[] ExternalSceneChangeNextActions =
        {
            "Reload each changed Scene with execute-dynamic-code, for example: " +
            "`uloop execute-dynamic-code --code 'using UnityEditor.SceneManagement; using UnityEngine.SceneManagement; " +
            "EditorSceneManager.OpenScene(\"<SCENE_ASSET_PATH>\", OpenSceneMode.Single);'` " +
            "using the Scene path from Errors[].File. OpenSceneMode.Single discards unsaved in-editor Scene changes.",
            "If the Scene has unsaved in-editor changes that conflict with the external change, decide first: " +
            "save the Scene to keep editor changes, or reload it to take the external content."
        };

        internal static CompileResponse CreateResponse(
            CompileResult result,
            bool forceRecompile)
        {
            Debug.Assert(result != null, "result must not be null");

            if (forceRecompile && !result.PreserveDetailsWhenForceRecompile)
            {
                return CreateForceCompileResult(result);
            }

            if (result.IsIndeterminate)
            {
                return new CompileResponse(
                    success: result.Success == true,
                    errorCount: result.ErrorCount,
                    warningCount: result.WarningCount,
                    errors: null,
                    warnings: null,
                    message: result.Message ?? "Compilation status is unknown. Use get-logs to inspect the compiler output.");
            }

            CompileResponse response = new CompileResponse(
                success: result.Success == true,
                errorCount: result.Errors?.Length ?? 0,
                warningCount: result.Warnings?.Length ?? 0,
                errors: ToIssues(result.Errors),
                warnings: ToIssues(result.Warnings),
                message: AddMissingTestFrameworkReferenceHint(result.Message, result.Errors));
            response.NextActions = CreateExternalSceneChangeNextActions(result.Message);
            return response;
        }

        private static string[] CreateExternalSceneChangeNextActions(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return null;
            }

            if (!message.Contains("externally changed Scene files", StringComparison.Ordinal))
            {
                return null;
            }

            return ExternalSceneChangeNextActions;
        }

        private static CompileResponse CreateForceCompileResult(CompileResult result)
        {
            ForceCompileUnknownResult unknownResult = ForceCompileUnknownResult.Create();
            CompileResponse response = new CompileResponse(
                success: unknownResult.Success,
                errorCount: unknownResult.ErrorCount,
                warningCount: unknownResult.WarningCount,
                errors: null,
                warnings: null,
                message: unknownResult.Message);
            response.ErrorCode = ForceCompileUnknownResult.ErrorCodeText;
            response.NextActions = new[] { ForceCompileUnknownResult.NextActionText };
            return response;
        }

        private static CompileIssue[] ToIssues(UnityEditor.Compilation.CompilerMessage[] messages)
        {
            if (messages == null)
            {
                return null;
            }

            return messages.Select(message => new CompileIssue(message.message, message.file, message.line)).ToArray();
        }

        private static string AddMissingTestFrameworkReferenceHint(
            string message,
            UnityEditor.Compilation.CompilerMessage[] errors)
        {
            if (!ContainsMissingTestFrameworkReference(errors))
            {
                return message;
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                return MissingTestFrameworkReferenceHint;
            }

            if (message.Contains(MissingTestFrameworkReferenceHint, StringComparison.Ordinal))
            {
                return message;
            }

            return $"{message} {MissingTestFrameworkReferenceHint}";
        }

        private static bool ContainsMissingTestFrameworkReference(
            UnityEditor.Compilation.CompilerMessage[] errors)
        {
            if (errors == null)
            {
                return false;
            }

            foreach (UnityEditor.Compilation.CompilerMessage error in errors)
            {
                string errorMessage = error.message ?? string.Empty;
                if (errorMessage.Contains("UnityTestAttribute", StringComparison.Ordinal)
                    || errorMessage.Contains("UnityEngine.TestTools", StringComparison.Ordinal)
                    || errorMessage.Contains("NUnit.Framework", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
