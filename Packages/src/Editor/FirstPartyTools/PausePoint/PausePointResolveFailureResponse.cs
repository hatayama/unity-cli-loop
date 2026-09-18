using System.Collections.Generic;

using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds the response an enable request returns when --line does not resolve, including
    /// which of the two explanations the file earns.
    /// </summary>
    internal static class PausePointResolveFailureResponse
    {
        // Why the compiled method spans decide: a file can hold both compiled types and a type
        // hot reload introduced. Only when the file has no compiled method at all is "there is no
        // compiled line map here" the whole truth; otherwise the general guidance still applies
        // and the introduced type is one more thing to know about the file.
        internal static PausePointResponse Create(
            EnablePausePointSchema parameters,
            string normalizedFile,
            bool hasActiveHotReloadPatches,
            SourcePausePointResolveResult resolveResult,
            string patchedMethodPdbUnavailableWarning)
        {
            bool declaresIntroducedType =
                HotReloadPausePointCoordination.HotReloadSide?.IsIntroducedTypeSourceFile(normalizedFile) == true;
            if (declaresIntroducedType
                && SourcePausePointResolver.FindNamedCompiledMethodSpansInFile(parameters.File).Count == 0)
            {
                return CreateIntroducedTypeResolveFailure(
                    parameters,
                    resolveResult,
                    patchedMethodPdbUnavailableWarning);
            }

            PausePointResolveFailureText failureText = PausePointResolveFailureTextBuilder.Build(
                parameters.File,
                parameters.Line,
                hasActiveHotReloadPatches,
                resolveResult,
                patchedMethodPdbUnavailableWarning);
            PausePointResponse response = PausePointFailureResponse.Create(
                failureText.Message,
                SourcePausePointConstants.ErrorCodeResolveFailed,
                failureText.RecommendedNextAction);
            List<string> resolveFailureWarnings = new List<string>();
            PausePointEnableWarningList.AddIfNotEmpty(resolveFailureWarnings, failureText.Warning);
            PausePointEnableWarningList.AddIfNotEmpty(
                resolveFailureWarnings,
                declaresIntroducedType
                    ? string.Format(
                        SourcePausePointConstants.IntroducedTypeInFileWarningFormat,
                        parameters.File)
                    : string.Empty);
            PausePointEnableWarningList.Assign(response, resolveFailureWarnings);
            return response;
        }

        // Refuses a line inside a method hot reload added. The compiled resolver is not asked,
        // so there is no resolver sentence to keep.
        internal static PausePointResponse CreateAddedMethodRefusal(
            EnablePausePointSchema parameters,
            string addedMethodName,
            bool methodFilterIsAmbiguous)
        {
            return PausePointFailureResponse.Create(
                string.Format(
                    SourcePausePointConstants.AddedMethodResolveFailureMessageFormat,
                    parameters.Line,
                    addedMethodName),
                SourcePausePointConstants.ErrorCodeResolveFailed,
                methodFilterIsAmbiguous
                    ? SourcePausePointConstants.AmbiguousAddedMethodResolveFailureNextAction
                    : SourcePausePointConstants.AddedMethodResolveFailureNextAction);
        }

        // Keeps the resolver's own sentence so the caller still sees which line failed, under a
        // first line that says why no line in this file can resolve yet. The pdb warning travels
        // along because "hot reload it and try again" hides the real cause when the method the
        // caller means is already patched and its pdb is the part that is missing.
        private static PausePointResponse CreateIntroducedTypeResolveFailure(
            EnablePausePointSchema parameters,
            SourcePausePointResolveResult resolveResult,
            string patchedMethodPdbUnavailableWarning)
        {
            string message = string.Format(
                    SourcePausePointConstants.IntroducedTypeResolveFailureMessageFormat,
                    parameters.File)
                + "\n"
                + resolveResult.ErrorMessage;
            PausePointResponse response = PausePointFailureResponse.Create(
                message,
                SourcePausePointConstants.ErrorCodeResolveFailed,
                SourcePausePointConstants.IntroducedTypeResolveFailureNextAction);
            List<string> warnings = new List<string>();
            PausePointEnableWarningList.AddIfNotEmpty(warnings, patchedMethodPdbUnavailableWarning);
            PausePointEnableWarningList.Assign(response, warnings);
            return response;
        }
    }

    /// <summary>
    /// Builds the response shape every pause point rejection shares.
    /// </summary>
    internal static class PausePointFailureResponse
    {
        internal static PausePointResponse Create(
            string message,
            string errorCode,
            string recommendedNextAction)
        {
            return new PausePointResponse
            {
                Success = false,
                Message = message,
                ErrorCode = errorCode,
                RecommendedNextAction = recommendedNextAction,
                EditorState = PausePointEditorState.FromSnapshot(UloopPausePointRegistry.CaptureEditorState()),
            };
        }
    }
}
