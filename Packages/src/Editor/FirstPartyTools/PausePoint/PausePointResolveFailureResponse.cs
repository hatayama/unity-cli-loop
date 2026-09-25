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
        // compiled code here" the whole truth; otherwise the general guidance still applies
        // and the introduced type is one more thing to know about the file.
        internal static PausePointResponse Create(
            EnablePausePointSchema parameters,
            string normalizedFile,
            SourcePausePointResolveResult resolveResult)
        {
            bool declaresIntroducedType =
                HotReloadPausePointCoordination.HotReloadSide?.IsIntroducedTypeSourceFile(normalizedFile) == true;
            if (declaresIntroducedType
                && SourcePausePointResolver.FindNamedCompiledMethodSpansInFile(parameters.File).Count == 0)
            {
                return CreateIntroducedTypeResolveFailure(parameters);
            }

            // Why no hot-reload branch: --line is an edited-file line on every path, so a failure
            // in a patched file has the same causes as one in an unpatched file.
            PausePointResponse response = PausePointFailureResponse.Create(
                PausePointEnableWarnings.AppendNearbyCompiledMethodsSuffix(
                    resolveResult.ErrorMessage,
                    resolveResult.NearbyCompiledMethods),
                SourcePausePointConstants.ErrorCodeResolveFailed,
                SourcePausePointConstants.ResolveFailedRecommendedNextAction);
            List<string> resolveFailureWarnings = new List<string>();
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

        // Refuses a line whose statement is inside a method hot reload added. The compiled resolver
        // is not asked, so there is no resolver sentence to keep. addedMethodLine is the line
        // inside the added method, which differs from the requested line when the requested line
        // has no statement and the next one is inside the added method.
        internal static PausePointResponse CreateAddedMethodRefusal(
            EnablePausePointSchema parameters,
            int addedMethodLine,
            string addedMethodName)
        {
            string message = addedMethodLine == parameters.Line
                ? string.Format(
                    SourcePausePointConstants.AddedMethodResolveFailureMessageFormat,
                    parameters.Line,
                    addedMethodName)
                : string.Format(
                    SourcePausePointConstants.AddedMethodNextStatementResolveFailureMessageFormat,
                    parameters.Line,
                    addedMethodLine,
                    addedMethodName);
            return PausePointFailureResponse.Create(
                message,
                SourcePausePointConstants.ErrorCodeResolveFailed,
                SourcePausePointConstants.AddedMethodResolveFailureNextAction);
        }

        // Refuses a line whose statement, or whose next statement, is inside a method hot reload
        // patched. Why not LINE_NOT_COMPILED: its next action says to hot-reload and retry, but the
        // method is already patched, so that retry returns the same refusal. patchedLine is the
        // line inside the patched method, which differs from the requested line when the
        // requested line has no statement and the next one is inside the patched method.
        internal static PausePointResponse CreatePatchedMethodRefusal(
            EnablePausePointSchema parameters,
            int patchedLine,
            PausePointPatchedEditedSpan span)
        {
            string message = patchedLine == parameters.Line
                ? string.Format(
                    SourcePausePointConstants.HotReloadPatchedMethodRefusalMessageFormat,
                    parameters.Line,
                    span.Label,
                    span.StartLine,
                    span.EndLine)
                : string.Format(
                    SourcePausePointConstants.HotReloadPatchedMethodNextStatementRefusalMessageFormat,
                    parameters.Line,
                    patchedLine,
                    span.Label,
                    span.StartLine,
                    span.EndLine);
            return PausePointFailureResponse.Create(
                message,
                SourcePausePointConstants.ErrorCodePausePointPatchedByHotReload,
                string.Format(
                    SourcePausePointConstants.HotReloadPatchedMethodRefusalNextActionFormat,
                    parameters.Line,
                    span.Label,
                    span.StartLine,
                    span.EndLine));
        }

        // Why the resolver's own sentence is dropped: it names a line and reads as a second,
        // competing reason, so the caller retries with other line numbers instead of hot reloading
        // the method. The first sentence already covers every line in the file.
        private static PausePointResponse CreateIntroducedTypeResolveFailure(EnablePausePointSchema parameters)
        {
            string message = string.Format(
                SourcePausePointConstants.IntroducedTypeResolveFailureMessageFormat,
                parameters.File);
            return PausePointFailureResponse.Create(
                message,
                SourcePausePointConstants.ErrorCodeResolveFailed,
                SourcePausePointConstants.IntroducedTypeResolveFailureNextAction);
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
