using System;
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

        internal PausePointEditedLineResolveContext(
            PausePointEditedLineMap map,
            string file,
            EnablePausePointSchema parameters,
            Func<int, SourcePausePointResolveResult> resolveAtCompiledLine,
            Func<int, PausePointPatchedEditedSpan> patchedSpanOrNull,
            Func<int, HotReloadAddedMethodAtLine> addedMethodOrNull)
        {
            Debug.Assert(map != null, "map must not be null.");
            Debug.Assert(parameters != null && parameters.Line > 0, "parameters.Line must be a positive 1-based line number.");
            Debug.Assert(
                resolveAtCompiledLine != null && patchedSpanOrNull != null && addedMethodOrNull != null,
                "the resolve, patched-span, and added-method functions must not be null.");
            Map = map;
            File = file;
            Parameters = parameters;
            ResolveAtCompiledLine = resolveAtCompiledLine;
            PatchedSpanOrNull = patchedSpanOrNull;
            AddedMethodOrNull = addedMethodOrNull;
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
        internal bool UsedFallback { get; }

        private PausePointEditedLineResolution(
            SourcePausePointResolveResult resolveResult,
            int editedResolvedLine,
            int editedResolvedEndLine,
            int editedMethodStartLine,
            int editedMethodEndLine,
            string lineBasis,
            PausePointResponse refusal,
            string warning,
            bool usedFallback)
        {
            ResolveResult = resolveResult;
            EditedResolvedLine = editedResolvedLine;
            EditedResolvedEndLine = editedResolvedEndLine;
            EditedMethodStartLine = editedMethodStartLine;
            EditedMethodEndLine = editedMethodEndLine;
            LineBasis = lineBasis;
            Refusal = refusal;
            Warning = warning;
            UsedFallback = usedFallback;
        }

        internal static PausePointEditedLineResolution Refused(PausePointResponse refusal)
        {
            Debug.Assert(refusal != null, "refusal must not be null.");
            return new PausePointEditedLineResolution(
                null, 0, 0, 0, 0, EditedFileLineBasis, refusal, string.Empty, usedFallback: false);
        }

        internal static PausePointEditedLineResolution Unresolved(SourcePausePointResolveResult failedResult)
        {
            Debug.Assert(failedResult != null && !failedResult.Success, "failedResult must be a failed resolve result.");
            return new PausePointEditedLineResolution(
                failedResult, 0, 0, 0, 0, EditedFileLineBasis, null, string.Empty, usedFallback: false);
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
                string.Empty,
                usedFallback: false);
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
                    result, 0, 0, 0, 0, LastCompiledSourceLineBasis, null, string.Empty, usedFallback: true);
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
                string.Format(SourcePausePointConstants.NoVerifiedSnapshotLineBasisWarningFormat, file, requestedLine),
                usedFallback: true);
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
                    PausePointResolveFailureResponse.CreateAddedMethodRefusal(parameters, addedMethod.Label));
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
                editedLine => PausePointAddedMethodScope.FindAddedMethodContainingLineOrNull(normalizedFile, editedLine));
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
                    PausePointLineNotCompiledRefusal.NoCompiledLineAtOrAfter(context.File, requestedLine));
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
                return PausePointEditedLineResolution.Unresolved(result);
            }

            return MapResolvedStatementBack(context, requestedLine, nextMappedLine, result);
        }

        private static PausePointEditedLineResolution MapResolvedStatementBack(
            PausePointEditedLineResolveContext context,
            int requestedLine,
            int nextMappedLine,
            SourcePausePointResolveResult result)
        {
            PausePointEditedLineMap map = context.Map;
            SourcePausePointResolution resolution = result.Resolution;
            int editedResolvedLine = map.ToEditedLineOrZero(resolution.ResolvedLine);
            if (editedResolvedLine == 0)
            {
                return PausePointEditedLineResolution.Refused(PausePointLineNotCompiledRefusal.StatementRemoved(
                    context.File, requestedLine, map.CompiledLineTextOrEmpty(resolution.ResolvedLine)));
            }

            // Why a patched span skips the check: the patcher refuses a statement inside a
            // hot-reload patched method with guidance to arm its edited body, which is the right
            // answer there, whereas an uncompiled line inside that body is expected.
            if (context.PatchedSpanOrNull(editedResolvedLine) == null)
            {
                int uncompiledLine = map.FirstUnmappedStatementLineOrZero(nextMappedLine, editedResolvedLine);
                if (uncompiledLine > 0)
                {
                    return PausePointEditedLineResolution.Refused(RefuseUncompiled(context, requestedLine, uncompiledLine));
                }
            }

            int editedResolvedEndLine = map.ToEditedLineOrZero(resolution.ResolvedEndLine);
            return PausePointEditedLineResolution.Resolved(
                result,
                editedResolvedLine,
                editedResolvedEndLine == 0 ? editedResolvedLine : editedResolvedEndLine,
                map.ToEditedLineOrZero(resolution.CompiledMethodStartLine),
                map.ToEditedLineOrZero(resolution.CompiledMethodEndLine));
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
                    context.Parameters, addedMethod.Label);
            }

            string lineText = context.Map.EditedLineTextOrEmpty(uncompiledLine);
            if (uncompiledLine == requestedLine)
            {
                return PausePointLineNotCompiledRefusal.ChangedLine(context.File, requestedLine, lineText);
            }

            return PausePointLineNotCompiledRefusal.NextStatementUncompiled(
                context.File, requestedLine, uncompiledLine, lineText);
        }
    }
}
