using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The inputs the edited-line resolver reads. The three functions are the only way it
    /// reaches the compiled resolver and hot reload, so the resolution logic stays testable
    /// without a compiled assembly.
    /// </summary>
    internal sealed class PausePointEditedLineResolveContext
    {
        internal PausePointEditedLineMap Map { get; }
        // Project-relative file, used only in refusal messages.
        internal string File { get; }
        internal EnablePausePointSchema Parameters { get; }
        internal Func<int, SourcePausePointResolveResult> ResolveAtCompiledLine { get; }
        internal Func<int, PausePointPatchedEditedSpan> PatchedSpanOrNull { get; }
        internal Func<int, HotReloadAddedMethodAtLine> AddedMethodOrNull { get; }
        // What the latest hot reload of the file says about it, which picks the next action of a
        // line-not-compiled refusal.
        internal PausePointHotReloadFileState FileState { get; }

        internal PausePointEditedLineResolveContext(
            PausePointEditedLineMap map,
            string file,
            EnablePausePointSchema parameters,
            Func<int, SourcePausePointResolveResult> resolveAtCompiledLine,
            Func<int, PausePointPatchedEditedSpan> patchedSpanOrNull,
            Func<int, HotReloadAddedMethodAtLine> addedMethodOrNull,
            PausePointHotReloadFileState fileState)
        {
            Debug.Assert(map != null, "map must not be null.");
            Debug.Assert(parameters != null && parameters.Line > 0, "parameters.Line must be a positive 1-based line number.");
            Debug.Assert(
                resolveAtCompiledLine != null && patchedSpanOrNull != null && addedMethodOrNull != null,
                "the resolve, patched-span, and added-method functions must not be null.");
            Debug.Assert(fileState != null, "fileState must not be null.");
            Map = map;
            File = file;
            Parameters = parameters;
            ResolveAtCompiledLine = resolveAtCompiledLine;
            PatchedSpanOrNull = patchedSpanOrNull;
            AddedMethodOrNull = addedMethodOrNull;
            FileState = fileState;
        }
    }

    /// <summary>
    /// The outcome of resolving an edited --line: either a refusal to return as is, or the
    /// compiled resolve result to patch with, plus the edited-file lines to report.
    /// </summary>
    internal sealed class PausePointEditedLineResolution
    {
        internal const string EditedFileLineBasis = "EditedFile";
        internal const string LastCompiledSourceLineBasis = "LastCompiledSource";

        // Kept in compiled coordinates because the patch is placed on the compiled PDB lines.
        // Null exactly when Refusal is set. A failed result is kept, not turned into a refusal,
        // so the use case builds the resolver's existing failure response.
        internal SourcePausePointResolveResult ResolveResult { get; }
        internal int EditedResolvedLine { get; }
        internal int EditedResolvedEndLine { get; }
        // 0 when the compiled method boundary has no edited counterpart.
        internal int EditedMethodStartLine { get; }
        internal int EditedMethodEndLine { get; }
        internal string LineBasis { get; }
        internal PausePointResponse Refusal { get; }
        internal string Warning { get; }

        private PausePointEditedLineResolution(
            SourcePausePointResolveResult resolveResult,
            int editedResolvedLine,
            int editedResolvedEndLine,
            int editedMethodStartLine,
            int editedMethodEndLine,
            string lineBasis,
            PausePointResponse refusal,
            string warning)
        {
            ResolveResult = resolveResult;
            EditedResolvedLine = editedResolvedLine;
            EditedResolvedEndLine = editedResolvedEndLine;
            EditedMethodStartLine = editedMethodStartLine;
            EditedMethodEndLine = editedMethodEndLine;
            LineBasis = lineBasis;
            Refusal = refusal;
            Warning = warning;
        }

        internal static PausePointEditedLineResolution Refused(PausePointResponse refusal)
        {
            Debug.Assert(refusal != null, "refusal must not be null.");
            return new PausePointEditedLineResolution(
                null, 0, 0, 0, 0, EditedFileLineBasis, refusal, string.Empty);
        }

        internal static PausePointEditedLineResolution Unresolved(SourcePausePointResolveResult failedResult)
        {
            Debug.Assert(failedResult != null && !failedResult.Success, "failedResult must be a failed resolve result.");
            return new PausePointEditedLineResolution(
                failedResult, 0, 0, 0, 0, EditedFileLineBasis, null, string.Empty);
        }

        internal static PausePointEditedLineResolution Resolved(
            SourcePausePointResolveResult resolvedResult,
            int editedResolvedLine,
            int editedResolvedEndLine,
            int editedMethodStartLine,
            int editedMethodEndLine)
        {
            Debug.Assert(resolvedResult != null && resolvedResult.Success, "resolvedResult must be a successful resolve result.");
            Debug.Assert(editedResolvedLine > 0, "editedResolvedLine must be a positive 1-based line number.");
            return new PausePointEditedLineResolution(
                resolvedResult,
                editedResolvedLine,
                editedResolvedEndLine,
                editedMethodStartLine,
                editedMethodEndLine,
                EditedFileLineBasis,
                null,
                string.Empty);
        }

        // Without a verified snapshot there is nothing to map through, so the lines stay
        // compiled lines and the warning says so instead of passing them off as edited lines.
        internal static PausePointEditedLineResolution Fallback(
            SourcePausePointResolveResult result,
            string file,
            int requestedLine)
        {
            Debug.Assert(result != null, "result must not be null.");
            if (!result.Success)
            {
                return new PausePointEditedLineResolution(
                    result, 0, 0, 0, 0, LastCompiledSourceLineBasis, null, string.Empty);
            }

            SourcePausePointResolution resolution = result.Resolution;
            return new PausePointEditedLineResolution(
                result,
                resolution.ResolvedLine,
                resolution.ResolvedEndLine,
                resolution.CompiledMethodStartLine,
                resolution.CompiledMethodEndLine,
                LastCompiledSourceLineBasis,
                null,
                string.Format(SourcePausePointConstants.NoVerifiedSnapshotLineBasisWarningFormat, file, requestedLine));
        }
    }

    /// <summary>
    /// Resolves a --line read from the edited file on disk: maps it to the last compiled source,
    /// resolves it there, and maps the resolved statement back. Lines that were added or changed
    /// since the last compile, and rounding that would skip such a line, are refused instead of
    /// arming a different statement.
    /// </summary>
    internal static class PausePointEditedLineResolver
    {
        // normalizedFile must already use forward slashes, and parameters.Line is validated by the use case.
        internal static PausePointEditedLineResolution Resolve(
            EnablePausePointSchema parameters,
            string normalizedFile,
            SourcePausePointSnapshotTiming timing)
        {
            Debug.Assert(parameters != null && parameters.Line > 0, "parameters.Line must be a positive 1-based line number.");
            Debug.Assert(!string.IsNullOrEmpty(normalizedFile), "normalizedFile must not be null or empty.");

            // Why before building the map: an added method has no compiled counterpart, so the
            // compiled-line fallback would round "on or after line N" into the next compiled
            // method, and no --method may pull the edited line onto a compiled twin either.
            HotReloadAddedMethodAtLine addedMethod =
                PausePointAddedMethodScope.FindAddedMethodContainingLineOrNull(normalizedFile, parameters.Line);
            if (addedMethod != null)
            {
                return PausePointEditedLineResolution.Refused(
                    PausePointResolveFailureResponse.CreateAddedMethodRefusal(parameters, parameters.Line, addedMethod.Label));
            }

            PausePointEditedLineMap map = BuildMapOrNull(normalizedFile);
            if (map == null)
            {
                return PausePointEditedLineResolution.Fallback(
                    SourcePausePointResolver.Resolve(normalizedFile, parameters.Line, parameters.Method, timing),
                    normalizedFile,
                    parameters.Line);
            }

            PausePointEditedLineResolveContext context = new PausePointEditedLineResolveContext(
                map,
                normalizedFile,
                parameters,
                compiledLine => SourcePausePointResolver.Resolve(normalizedFile, compiledLine, parameters.Method, timing),
                editedLine => PausePointPatchedEditedSpanLocator.FindPatchedSpanContainingEditedLineOrNull(normalizedFile, editedLine),
                editedLine => PausePointAddedMethodScope.FindAddedMethodContainingLineOrNull(normalizedFile, editedLine),
                PausePointHotReloadFileState.Read(normalizedFile));
            return ResolveThroughMap(context);
        }

        // Null means fall back to compiled lines: no compiled assembly (the resolver then reports
        // the same failure), no verified snapshot, no file on disk, or a diff too large to map.
        private static PausePointEditedLineMap BuildMapOrNull(string normalizedFile)
        {
            SourcePausePointCompiledAssemblyLocation location = SourcePausePointCompiledAssemblyLocator.Locate(normalizedFile);
            if (!location.Found)
            {
                return null;
            }

            // Why the dll-path port: the file-only port returns null until the first hot reload,
            // which would send every Editor that has not reloaded yet down the fallback.
            string compiledSource = HotReloadPausePointCoordination.HotReloadSide?.GetVerifiedSnapshotSource(
                normalizedFile,
                location.AssemblyPath);
            if (string.IsNullOrEmpty(compiledSource))
            {
                return null;
            }

            string editedFilePath = Path.Combine(UnityCliLoopPathResolver.GetProjectRoot(), normalizedFile);
            if (!File.Exists(editedFilePath))
            {
                return null;
            }

            return PausePointEditedLineMap.BuildOrNull(compiledSource, File.ReadAllText(editedFilePath));
        }

        internal static PausePointEditedLineResolution ResolveThroughMap(PausePointEditedLineResolveContext context)
        {
            Debug.Assert(context != null, "context must not be null.");
            PausePointEditedLineMap map = context.Map;
            int requestedLine = context.Parameters.Line;
            if (requestedLine > map.EditedLineCount)
            {
                return PausePointEditedLineResolution.Refused(
                    PausePointLineNotCompiledRefusal.BeyondEndOfFile(context.File, requestedLine, map.EditedLineCount));
            }

            int nextMappedLine = map.NextMappedEditedLineOrZero(requestedLine);
            if (nextMappedLine == 0)
            {
                return PausePointEditedLineResolution.Refused(
                    PausePointLineNotCompiledRefusal.NoCompiledLineAtOrAfter(context.File, requestedLine, context.FileState));
            }

            // Lines between the request and the next compiled line would be skipped by rounding;
            // one of them holding code means the caller pointed at code that is not compiled.
            int uncompiledLine = map.FirstUnmappedStatementLineOrZero(requestedLine, nextMappedLine);
            if (uncompiledLine > 0)
            {
                return PausePointEditedLineResolution.Refused(RefuseUncompiled(context, requestedLine, uncompiledLine));
            }

            SourcePausePointResolveResult result = context.ResolveAtCompiledLine(map.ToCompiledLineOrZero(nextMappedLine));
            if (!result.Success)
            {
                return MapFailureToEditedLines(context, requestedLine, nextMappedLine, result);
            }

            return MapResolvedStatementBack(context, requestedLine, nextMappedLine, result);
        }

        private static PausePointEditedLineResolution MapResolvedStatementBack(
            PausePointEditedLineResolveContext context,
            int requestedLine,
            int nextMappedLine,
            SourcePausePointResolveResult result)
        {
            SourcePausePointResolution resolution = result.Resolution;
            PausePointResponse landingRefusal = RefuseLandingOrNull(
                context, requestedLine, nextMappedLine, resolution.ResolvedLine);
            if (landingRefusal != null)
            {
                return PausePointEditedLineResolution.Refused(landingRefusal);
            }

            PausePointEditedLineMap map = context.Map;
            int editedResolvedLine = map.ToEditedLineOrZero(resolution.ResolvedLine);
            int editedResolvedEndLine = map.ToEditedLineOrZero(resolution.ResolvedEndLine);
            return PausePointEditedLineResolution.Resolved(
                result,
                editedResolvedLine,
                editedResolvedEndLine == 0 ? editedResolvedLine : editedResolvedEndLine,
                map.ToEditedLineOrZero(resolution.CompiledMethodStartLine),
                map.ToEditedLineOrZero(resolution.CompiledMethodEndLine));
        }

        // Refuses landing on a compiled statement the edited file no longer holds, or one that
        // rounding reaches by skipping an uncompiled statement; null when landing on it is sound.
        private static PausePointResponse RefuseLandingOrNull(
            PausePointEditedLineResolveContext context,
            int requestedLine,
            int nextMappedLine,
            int compiledStatementLine)
        {
            PausePointEditedLineMap map = context.Map;
            int editedStatementLine = map.ToEditedLineOrZero(compiledStatementLine);
            if (editedStatementLine == 0)
            {
                return PausePointLineNotCompiledRefusal.StatementRemoved(
                    context.File, requestedLine, map.CompiledLineTextOrEmpty(compiledStatementLine), context.FileState);
            }

            // Why a patched span skips the check: the patcher refuses a statement inside a
            // hot-reload patched method with guidance to arm its edited body, which is the right
            // answer there, whereas an uncompiled line inside that body is expected.
            if (context.PatchedSpanOrNull(editedStatementLine) != null)
            {
                return null;
            }

            int uncompiledLine = map.FirstUnmappedStatementLineOrZero(nextMappedLine, editedStatementLine);
            return uncompiledLine > 0 ? RefuseUncompiled(context, requestedLine, uncompiledLine) : null;
        }

        // Why map a failure instead of returning it as is: its lines are lines of the last
        // compiled source, while on this path the caller reads every line as a line of the file
        // on disk, so the unmapped lines would point at other code once the file changed.
        private static PausePointEditedLineResolution MapFailureToEditedLines(
            PausePointEditedLineResolveContext context,
            int requestedLine,
            int nextMappedLine,
            SourcePausePointResolveResult failed)
        {
            if (failed.FailureReason == SourcePausePointResolveFailureReason.NoSequencePointOnOrAfterLine)
            {
                return PausePointEditedLineResolution.Unresolved(ToEditedLineFailure(context, requestedLine, failed));
            }

            if (failed.FailureReason == SourcePausePointResolveFailureReason.PostLineAlwaysThrows)
            {
                return MapAlwaysThrowingStatement(context, requestedLine, nextMappedLine, failed);
            }

            // The other reasons name no line, so they read the same in either line numbering.
            return PausePointEditedLineResolution.Unresolved(failed);
        }

        // Why the requested line instead of the compiled line the resolver searched from: the
        // lines between them hold no code, so "on or after" reads the same from either, and only
        // the requested line is a line the caller knows.
        private static SourcePausePointResolveResult ToEditedLineFailure(
            PausePointEditedLineResolveContext context,
            int requestedLine,
            SourcePausePointResolveResult failed)
        {
            string methodFilter = context.Parameters.Method;
            string message = string.IsNullOrEmpty(methodFilter)
                ? string.Format(
                    SourcePausePointConstants.ResolveFailedNoCompiledStatementInEditedFileMessageFormat,
                    requestedLine,
                    context.File)
                : string.Format(
                    SourcePausePointConstants.ResolveFailedNoMethodNamedInEditedFileMessageFormat,
                    methodFilter,
                    requestedLine,
                    context.File);
            return SourcePausePointResolveResult.Failure(
                failed.FailureReason,
                message,
                MapNearbyToEditedLines(context.Map, failed.NearbyCompiledMethods));
        }

        // Why drop a span whose start or end has no edited counterpart: that end was removed or
        // changed since the last compile, so no line of the file on disk can stand for it.
        private static IReadOnlyList<SourcePausePointNearbyCompiledMethod> MapNearbyToEditedLines(
            PausePointEditedLineMap map,
            IReadOnlyList<SourcePausePointNearbyCompiledMethod> compiledNearby)
        {
            List<SourcePausePointNearbyCompiledMethod> editedNearby = new List<SourcePausePointNearbyCompiledMethod>();
            foreach (SourcePausePointNearbyCompiledMethod nearby in compiledNearby)
            {
                int editedStartLine = map.ToEditedLineOrZero(nearby.StartLine);
                int editedEndLine = map.ToEditedLineOrZero(nearby.EndLine);
                if (editedStartLine == 0 || editedEndLine == 0)
                {
                    continue;
                }

                editedNearby.Add(new SourcePausePointNearbyCompiledMethod(nearby.DisplayName, editedStartLine, editedEndLine));
            }

            return editedNearby;
        }

        // Why the landing checks come first: the always-throwing statement is where the request
        // lands, so when the file no longer holds it, or rounding onto it skips an uncompiled
        // statement, retrying with pre-line timing would arm a statement the edited line is not.
        private static PausePointEditedLineResolution MapAlwaysThrowingStatement(
            PausePointEditedLineResolveContext context,
            int requestedLine,
            int nextMappedLine,
            SourcePausePointResolveResult failed)
        {
            Debug.Assert(failed.StatementLine > 0, "an always-throws failure must name its statement line.");
            PausePointResponse landingRefusal = RefuseLandingOrNull(
                context, requestedLine, nextMappedLine, failed.StatementLine);
            if (landingRefusal != null)
            {
                return PausePointEditedLineResolution.Refused(landingRefusal);
            }

            // Why refuse inside a patched method: the compiled statement is not the code that
            // runs there, and the pre-line retry the always-throws next action asks for would
            // only reach the patcher's refusal of that method.
            int editedStatementLine = context.Map.ToEditedLineOrZero(failed.StatementLine);
            PausePointPatchedEditedSpan patchedSpan = context.PatchedSpanOrNull(editedStatementLine);
            if (patchedSpan != null)
            {
                return PausePointEditedLineResolution.Refused(
                    PausePointResolveFailureResponse.CreatePatchedMethodRefusal(context.Parameters, requestedLine, patchedSpan));
            }

            return PausePointEditedLineResolution.Unresolved(SourcePausePointResolveResult.Failure(
                SourcePausePointResolveFailureReason.PostLineAlwaysThrows,
                string.Format(SourcePausePointConstants.PostLineAlwaysThrowsMessageFormat, editedStatementLine, context.File),
                null,
                editedStatementLine));
        }

        private static PausePointResponse RefuseUncompiled(
            PausePointEditedLineResolveContext context,
            int requestedLine,
            int uncompiledLine)
        {
            HotReloadAddedMethodAtLine addedMethod = context.AddedMethodOrNull(uncompiledLine);
            if (addedMethod != null)
            {
                return PausePointResolveFailureResponse.CreateAddedMethodRefusal(
                    context.Parameters, uncompiledLine, addedMethod.Label);
            }

            // Why after the added-method check: an added method has no compiled twin to arm, so
            // pointing at its edited body would not help there; only a compile does.
            PausePointPatchedEditedSpan patchedSpan = context.PatchedSpanOrNull(uncompiledLine);
            if (patchedSpan != null)
            {
                return PausePointResolveFailureResponse.CreatePatchedMethodRefusal(
                    context.Parameters, uncompiledLine, patchedSpan);
            }

            string lineText = context.Map.EditedLineTextOrEmpty(uncompiledLine);
            if (uncompiledLine == requestedLine)
            {
                return PausePointLineNotCompiledRefusal.ChangedLine(context.File, requestedLine, lineText, context.FileState);
            }

            return PausePointLineNotCompiledRefusal.NextStatementUncompiled(
                context.File, requestedLine, uncompiledLine, lineText, context.FileState);
        }
    }
}
