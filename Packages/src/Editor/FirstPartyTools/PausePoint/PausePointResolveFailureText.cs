using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// What an enable request tells the caller when --line does not resolve: the message, the
    /// recommended next action, and the one warning that belongs to that failure.
    /// </summary>
    internal sealed class PausePointResolveFailureText
    {
        internal PausePointResolveFailureText(string message, string recommendedNextAction, string warning)
        {
            Message = message;
            RecommendedNextAction = recommendedNextAction;
            Warning = warning;
        }

        internal string Message { get; }

        internal string RecommendedNextAction { get; }

        // Empty when this failure carries no warning of its own.
        internal string Warning { get; }
    }

    /// <summary>
    /// Composes the resolve-failure wording, including the reads that only make sense when hot
    /// reload has patched the file.
    /// </summary>
    internal static class PausePointResolveFailureTextBuilder
    {
        internal static PausePointResolveFailureText Build(
            string file,
            int line,
            bool hasActiveHotReloadPatches,
            SourcePausePointResolveResult resolveResult,
            string patchedMethodPdbUnavailableWarning)
        {
            // Why a different next-action: resolve failure leaves ResolvedMethod and
            // ResolvedLineText empty, so the generic "compile then retry" text hides
            // the more likely cause — a line number taken from the edited file.
            string recommendedNextAction = hasActiveHotReloadPatches
                ? SourcePausePointConstants.HotReloadCompiledLineMapResolveFailureNextAction
                : SourcePausePointConstants.ResolveFailedRecommendedNextAction;
            IReadOnlyList<string> compiledSourceLinesOrNull = null;
            IReadOnlyList<SourcePausePointNearbyCompiledMethod> namedCompiledMethodSpans =
                Array.Empty<SourcePausePointNearbyCompiledMethod>();
            bool requestedLineReadOk = false;
            string requestedLineEditedText = string.Empty;
            // Why skip snapshot/edited-line IO without patches: Candidate is omitted on that
            // path, so those reads would change the historical no-patch failure for no gain.
            if (hasActiveHotReloadPatches)
            {
                string compiledSnapshotSource = PausePointCompiledSourceReader.LoadSnapshotOrEmpty(file);
                compiledSourceLinesOrNull = string.IsNullOrEmpty(compiledSnapshotSource)
                    ? null
                    : SourcePausePointSourceLineReader.SplitSourceLines(compiledSnapshotSource);
                namedCompiledMethodSpans = SourcePausePointResolver.FindNamedCompiledMethodSpansInFile(file);
                (requestedLineReadOk, requestedLineEditedText) =
                    PausePointCompiledLineComparisonWarnings.ReadEditedLineText(file, line);
            }

            string message = PausePointEnableWarnings.BuildResolveFailureMessage(
                resolveResult.ErrorMessage,
                resolveResult.NearbyCompiledMethods,
                hasActiveHotReloadPatches,
                line,
                requestedLineReadOk,
                requestedLineEditedText,
                compiledSourceLinesOrNull,
                namedCompiledMethodSpans);
            string warning = PausePointEnableWarnings.ChooseCompiledLineMapWarning(
                patchedMethodPdbUnavailableWarning,
                PausePointEnableWarnings.BuildCompiledLineMapResolveFailureWarningOrEmpty(
                    hasActiveHotReloadPatches,
                    file));
            return new PausePointResolveFailureText(message, recommendedNextAction, warning);
        }
    }
}
