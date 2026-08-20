using System;
using System.Collections.Generic;
using System.Linq;

using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Coordinates pause point tool validation and registry updates.
    /// </summary>
    internal sealed class PausePointUseCase
    {
        // Tracks which currently-armed source pause point ids carry a physics-callback warning,
        // and their declaring type, so a later expiry (LogExpired) can attribute the same
        // diagnostics snapshot to a miss that was never hit. Volatile by design: a domain reload
        // clears the Harmony patches this tracks anyway, so this dictionary does not need to
        // survive one, and entries are removed as soon as their pause point is cleared.
        private static readonly Dictionary<string, Type> PhysicsFlaggedDeclaringTypesById = new();

        // Why here rather than at the EnableBySourceLocation call site: PhysicsFlaggedDeclaringTypesById
        // can only become non-empty after EnableBySourceLocation has populated it at least once, and
        // by then this type has already been touched (it is the type EnableBySourceLocation is a
        // member of), so static type initialization has already run this constructor. The
        // subscription is therefore always wired before any Clear/bridge-Clear that could possibly
        // find a matching id in the dictionary.
        static PausePointUseCase()
        {
            UloopPausePointRegistry.OnClearResolved = OnRegistryClearResolved;
        }

        // Shared by both Clear callers (this use case's own --id path below, and the Infrastructure
        // CLI bridge's PausePointStatusBridgeCommand.Clear, which must not reference this Editor-only
        // tool assembly directly) via the UloopPausePointRegistry.OnClearResolved hook, so a zero-hit
        // clear of a physics-flagged marker is diagnosed the same way regardless of which caller
        // cleared it.
        private static void OnRegistryClearResolved(string id, int hitCount, string statusBeforeClear)
        {
            if (hitCount == 0 && PhysicsFlaggedDeclaringTypesById.TryGetValue(id, out Type declaringType))
            {
                LogPhysicsDispatchDiagnostics("pause_point_cleared_without_hit_physics", id, declaringType, statusBeforeClear);
            }
            PhysicsFlaggedDeclaringTypesById.Remove(id);
        }

        public PausePointResponse Enable(EnablePausePointSchema parameters)
        {
            string captureSettingsError = ValidateCaptureSettings(parameters);
            if (captureSettingsError != null)
            {
                return CreateValidationFailure(
                    captureSettingsError,
                    SourcePausePointConstants.ErrorCodeInvalidArgument,
                    "Fix the rejected capture argument described in Message and re-run; uloop enable-pause-point --help lists the accepted values.");
            }

            string modeError = ValidateEnableMode(parameters);
            if (modeError != null)
            {
                return CreateValidationFailure(
                    modeError,
                    SourcePausePointConstants.ErrorCodeInvalidArgument,
                    "Re-run with either --id alone, or --file and --line together.");
            }

            if (parameters.TimeoutSeconds <= 0)
            {
                return CreateValidationFailure(
                    "TimeoutSeconds must be greater than zero.",
                    SourcePausePointConstants.ErrorCodeInvalidArgument,
                    "Re-run with --timeout-seconds set to a positive integer.");
            }

            if (!string.IsNullOrWhiteSpace(parameters.File))
            {
                return EnableBySourceLocation(parameters);
            }

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.Enable(
                parameters.Id,
                parameters.TimeoutSeconds,
                parameters.Mode,
                parameters.MaxHistory,
                parameters.MaxPreviewElements,
                parameters.MaxCallerFrames);
            PausePointResponse response = PausePointResponse.FromSnapshot(snapshot);
            response.Warning = PausePointEnableWarnings.CreateEnableWarning();
            LogEnable(response.Id, resolvedMethod: string.Empty, fileLine: string.Empty, response.Mode, response.Warning);
            return response;
        }

        public PausePointResponse Clear(ClearPausePointSchema parameters)
        {
            if (parameters.All)
            {
                // Snapshot each physics-flagged marker before ClearAll resolves it away, so a
                // marker cleared without ever being hit still gets its diagnostics logged
                // regardless of whether it had already expired or was still Enabled. This is
                // the dominant field path (await timeout -> agent cleans up with --all), so
                // skipping it here would lose the primary evidence in the common case.
                //
                // This loop and OnRegistryClearResolved above are the only two places that log
                // this diagnostic; keep them in sync if the log shape or wording changes. This one
                // exists because ClearAll has no single-id equivalent to route through
                // UloopPausePointRegistry.Clear (and therefore OnClearResolved) - Registry.ClearAll
                // clears every entry in one bulk pass instead. Every id still in this dictionary at
                // this point is guaranteed to be un-cleared: a single Clear of any tracked id -
                // whichever caller made it - already removed it via OnRegistryClearResolved, so
                // there is no stale-Cleared-entry case left to guard against here.
                foreach (KeyValuePair<string, Type> tracked in PhysicsFlaggedDeclaringTypesById)
                {
                    UloopPausePointSnapshot trackedSnapshot = UloopPausePointRegistry.GetStatus(tracked.Key);
                    if (trackedSnapshot.HitCount == 0)
                    {
                        LogPhysicsDispatchDiagnostics(
                            "pause_point_cleared_without_hit_physics", tracked.Key, tracked.Value, trackedSnapshot.Status);
                    }
                }

                // Registry.ClearAll unpatches any source pause points via the hook
                // SourcePausePointPatcher wires into it; this use case never references the
                // Patcher directly.
                UloopPausePointClearAllResult clearAllResult = UloopPausePointRegistry.ClearAll();
                LogCleared("all", string.Empty);
                PhysicsFlaggedDeclaringTypesById.Clear();
                return PausePointResponse.FromClearAll(clearAllResult);
            }

            string idError = ValidateId(parameters.Id);
            if (idError != null)
            {
                return CreateValidationFailure(
                    idError,
                    SourcePausePointConstants.ErrorCodeInvalidArgument,
                    "Pass --id with the id returned by enable-pause-point, or use --all to clear every marker.");
            }

            (UloopPausePointSnapshot snapshot, bool resumedFromPause, int clearedCount) =
                UloopPausePointRegistry.Clear(parameters.Id);
            LogCleared(snapshot.Id, snapshot.StatusBeforeClear);
            if (snapshot.StatusBeforeClear == UloopPausePointStatus.Expired)
            {
                LogExpired(snapshot.Id, snapshot.ElapsedSinceEnabledMilliseconds);
            }

            // The zero-hit physics diagnostic (for any StatusBeforeClear, not just Expired - the
            // field incident that motivated it, Block.cs:29 2026-07-22, cleared while still
            // Enabled) and the PhysicsFlaggedDeclaringTypesById removal already happened inside
            // UloopPausePointRegistry.Clear above, via the OnClearResolved hook subscribed to
            // OnRegistryClearResolved. This keeps the direct-tool-call path in sync with the
            // Infrastructure CLI bridge's Clear path without duplicating the check here.
            PausePointResponse response = PausePointResponse.FromSnapshot(snapshot);
            response.ClearedCount = clearedCount;
            if (resumedFromPause)
            {
                response.Warning = SourcePausePointConstants.ClearResumedPlayModeWarning;
            }

            return response;
        }

        // Why: PausePointStatusBridgeCommand duplicates this instead of sharing it, since that
        // bridge must not reference this Editor-only tool assembly. Keep both in sync if the
        // log shape or wording changes.
        private static void LogCleared(string target, string statusBeforeClear)
        {
            VibeLogger.LogInfo(
                "pause_point_cleared",
                $"Pause point cleared: {target}",
                new { Target = target, StatusBeforeClear = statusBeforeClear });
        }

        // Why: PausePointStatusBridgeCommand duplicates this instead of sharing it, since that
        // bridge must not reference this Editor-only tool assembly. Keep both in sync if the
        // log shape or wording changes.
        private static void LogExpired(string id, long elapsedSinceEnabledMilliseconds)
        {
            VibeLogger.LogInfo(
                "pause_point_expired",
                $"Pause point expired before being cleared: {id}",
                new { Id = id, ElapsedSinceEnabledMilliseconds = elapsedSinceEnabledMilliseconds });
        }

        // Resolves File:Line to a patch location via the Resolver, patches it via Harmony, then
        // arms the same registry state machine the Id path uses, keyed by the derived source id.
        private static PausePointResponse EnableBySourceLocation(EnablePausePointSchema parameters)
        {
            if (CompilationPipeline.codeOptimization == CodeOptimization.Release)
            {
                return CreateValidationFailure(
                    SourcePausePointConstants.ReleaseCodeOptimizationRejectionMessage,
                    SourcePausePointConstants.ErrorCodeReleaseCodeOptimization,
                    SourcePausePointConstants.ReleaseCodeOptimizationRecommendedNextAction);
            }

            string normalizedFile = SourcePausePointPathNormalizer.ToForwardSlashes(parameters.File);
            string id = BuildSourcePausePointId(parameters.File, parameters.Line);
            string patchedMethodPdbUnavailableWarning = string.Empty;

            HotReloadShimFileLookup shimLookup =
                HotReloadPausePointCoordination.GetShimLookupForFile?.Invoke(normalizedFile);
            if (shimLookup != null)
            {
                SourcePausePointShimResolution shimResolution =
                    SourcePausePointShimResolver.Resolve(
                        shimLookup, normalizedFile, parameters.Line, parameters.Method);
                if (shimResolution.Kind == SourcePausePointShimResolveKind.TransplantChainJoin
                    || shimResolution.Kind == SourcePausePointShimResolveKind.ShimDirect)
                {
                    SourcePausePointPatchResult shimPatchResult = SourcePausePointPatcher.PatchShimTarget(
                        id,
                        shimResolution,
                        normalizedFile,
                        parameters.Line);
                    if (!shimPatchResult.Success)
                    {
                        return new PausePointResponse
                        {
                            Success = false,
                            ErrorCode = SourcePausePointConstants.ErrorCodePatchFailed,
                            Message = shimPatchResult.ErrorMessage,
                            RecommendedNextAction = shimPatchResult.Hint,
                            EditorState = PausePointEditorState.FromSnapshot(
                                UloopPausePointRegistry.CaptureEditorState()),
                        };
                    }

                    // Why the same resolved line twice: shim sequence points do not expose an
                    // end line distinct from the hit line. Edited method span is passed separately.
                    return FinishEnableBySourceLocation(
                        id,
                        parameters,
                        shimResolution.ResolvedLine,
                        shimResolution.ResolvedLine,
                        shimResolution.MethodDisplayName,
                        shimPatchResult,
                        retargetedToHotReloadPatch: true,
                        hasActiveHotReloadPatches: true,
                        editedMethodStartLine: shimResolution.SourceStartLine,
                        editedMethodEndLine: shimResolution.SourceEndLine);
                }

                if (shimResolution.Kind == SourcePausePointShimResolveKind.NoStatementInPatchedMethod)
                {
                    return CreateValidationFailure(
                        shimResolution.ErrorMessage,
                        SourcePausePointConstants.ErrorCodeResolveFailed,
                        "Pick a line with an executable statement inside the edited method body.");
                }

                patchedMethodPdbUnavailableWarning = PausePointEnableWarnings.BuildPatchedMethodPdbUnavailableWarningOrEmpty(
                    shimResolution.Kind == SourcePausePointShimResolveKind.PatchedMethodPdbUnavailable,
                    shimResolution.MethodDisplayName,
                    parameters.Line);
                // NotInPatchedMethod and PatchedMethodPdbUnavailable: fall through to the
                // compiled ScriptAssemblies resolver. The latter still uses the compiled line
                // map; only the warning text differs.
            }

            SourcePausePointResolveResult resolveResult = SourcePausePointResolver.Resolve(
                parameters.File, parameters.Line, parameters.Method);
            if (!resolveResult.Success)
            {
                bool hasActiveHotReloadPatches = shimLookup != null;
                // Why a different next-action: resolve failure leaves ResolvedMethod and
                // ResolvedLineText empty, so the generic "compile then retry" text hides
                // the more likely cause — a line number taken from the edited file.
                string recommendedNextAction = hasActiveHotReloadPatches
                    ? SourcePausePointConstants.HotReloadCompiledLineMapResolveFailureNextAction
                    : SourcePausePointConstants.ResolveFailedRecommendedNextAction;
                string message = PausePointEnableWarnings.AppendNearbyCompiledMethodsSuffix(
                    resolveResult.ErrorMessage,
                    resolveResult.NearbyCompiledMethods);
                if (hasActiveHotReloadPatches)
                {
                    message = AppendResolveFailureRequestedLineCandidateOrUnchanged(
                        message,
                        parameters);
                }

                PausePointResponse response = CreateValidationFailure(
                    message,
                    SourcePausePointConstants.ErrorCodeResolveFailed,
                    recommendedNextAction);
                response.Warning = PausePointEnableWarnings.ChooseCompiledLineMapWarning(
                    patchedMethodPdbUnavailableWarning,
                    PausePointEnableWarnings.BuildCompiledLineMapResolveFailureWarningOrEmpty(
                        hasActiveHotReloadPatches,
                        parameters.File));
                return response;
            }

            SourcePausePointPatchResult patchResult = SourcePausePointPatcher.Patch(
                id,
                resolveResult.Resolution,
                normalizedFile,
                parameters.Line);
            if (!patchResult.Success)
            {
                string errorCode =
                    patchResult.FailureReason == SourcePausePointPatchFailureReason.MethodPatchedByHotReload
                        ? SourcePausePointConstants.ErrorCodePausePointPatchedByHotReload
                        : SourcePausePointConstants.ErrorCodePatchFailed;
                return new PausePointResponse
                {
                    Success = false,
                    ErrorCode = errorCode,
                    Message = patchResult.ErrorMessage,
                    RecommendedNextAction = patchResult.Hint,
                    EditorState = PausePointEditorState.FromSnapshot(UloopPausePointRegistry.CaptureEditorState()),
                };
            }

            return FinishEnableBySourceLocation(
                id,
                parameters,
                resolveResult.Resolution.ResolvedLine,
                resolveResult.Resolution.ResolvedEndLine,
                resolveResult.Resolution.MethodDisplayName,
                patchResult,
                retargetedToHotReloadPatch: false,
                hasActiveHotReloadPatches: shimLookup != null,
                compiledMethodStartLine: resolveResult.Resolution.CompiledMethodStartLine,
                compiledMethodEndLine: resolveResult.Resolution.CompiledMethodEndLine,
                patchedMethodPdbUnavailableWarning: patchedMethodPdbUnavailableWarning);
        }

        private static PausePointResponse FinishEnableBySourceLocation(
            string id,
            EnablePausePointSchema parameters,
            int resolvedLine,
            int resolvedEndLine,
            string resolvedMethod,
            SourcePausePointPatchResult patchResult,
            bool retargetedToHotReloadPatch,
            bool hasActiveHotReloadPatches,
            int editedMethodStartLine = 0,
            int editedMethodEndLine = 0,
            int compiledMethodStartLine = 0,
            int compiledMethodEndLine = 0,
            string patchedMethodPdbUnavailableWarning = "")
        {
            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.Enable(
                id,
                parameters.TimeoutSeconds,
                parameters.Mode,
                parameters.MaxHistory,
                parameters.MaxPreviewElements,
                parameters.MaxCallerFrames);
            if (retargetedToHotReloadPatch)
            {
                UloopPausePointRegistry.SetRetargetedToHotReloadPatch(id, true);
                snapshot = UloopPausePointRegistry.GetStatus(id);
            }

            bool compareCompiledLineDrift = hasActiveHotReloadPatches && !retargetedToHotReloadPatch;
            string compiledSnapshotSource = compareCompiledLineDrift
                ? LoadCompiledSnapshotSourceOrEmpty(parameters.File)
                : string.Empty;
            // Why snapshot over disk: the editor file may already include unpatched-line drift, so
            // reading disk at the compiled ResolvedLine shows the wrong statement (FB9 empty/mismatch).
            // The snapshot read stays single-line because the verified snapshot has no end-line data;
            // the disk read spans resolvedLine..resolvedEndLine so a rounded-forward multi-line
            // statement returns its full text.
            string resolvedLineText = compareCompiledLineDrift
                ? SourcePausePointSourceLineReader.ReadLineTextFromSource(compiledSnapshotSource, resolvedLine)
                : PausePointLineTextReader.ReadResolvedLineText(parameters.File, resolvedLine, resolvedEndLine);
            UloopPausePointRegistry.SetResolvedLine(id, resolvedLine, resolvedLineText);

            PausePointResponse response = PausePointResponse.FromSnapshot(snapshot);
            // Why re-read after SetResolvedLine: FromSnapshot above used the pre-write snapshot.
            response.ResolvedLine = resolvedLine;
            response.ResolvedLineText = resolvedLineText;
            response.ResolvedMethod = resolvedMethod;
            response.SnapshotTiming = SourcePausePointConstants.PreLineSnapshotTimingNote;
            string enableWarning = PausePointEnableWarnings.CreateEnableWarning();
            enableWarning = PausePointEnableWarnings.MergeWarnings(
                enableWarning,
                PausePointEnableWarnings.BuildRetargetedToHotReloadPatchWarningOrEmpty(
                    retargetedToHotReloadPatch,
                    resolvedMethod,
                    parameters.Line,
                    editedMethodStartLine,
                    editedMethodEndLine));
            string compiledLineMapWarning = PausePointEnableWarnings.ChooseCompiledLineMapWarning(
                patchedMethodPdbUnavailableWarning,
                PausePointEnableWarnings.BuildCompiledLineMapWarningOrEmpty(
                    compareCompiledLineDrift,
                    parameters.File));
            enableWarning = PausePointEnableWarnings.MergeWarnings(enableWarning, compiledLineMapWarning);
            if (compareCompiledLineDrift)
            {
                (bool resolvedEditedReadOk, string resolvedEditedLineText) =
                    PausePointCompiledLineComparisonWarnings.ReadEditedLineText(parameters.File, resolvedLine);
                (bool requestedEditedReadOk, string requestedEditedLineText) =
                    PausePointCompiledLineComparisonWarnings.ReadEditedLineText(parameters.File, parameters.Line);
                string[] compiledSourceLines = SourcePausePointSourceLineReader.SplitSourceLines(compiledSnapshotSource);
                string driftWarning = PausePointCompiledLineComparisonWarnings.ComposeCompiledLineDriftAndSnapWarningOrEmpty(
                    parameters.File,
                    parameters.Line,
                    resolvedLine,
                    resolvedMethod,
                    resolvedLineText,
                    resolvedEditedReadOk,
                    resolvedEditedLineText,
                    requestedEditedReadOk,
                    requestedEditedLineText,
                    compiledMethodStartLine,
                    compiledMethodEndLine,
                    compiledSourceLines);
                enableWarning = PausePointEnableWarnings.MergeWarnings(enableWarning, driftWarning);
                if (driftWarning.Length > 0)
                {
                    response.RecommendedNextAction =
                        SourcePausePointConstants.HotReloadCompiledLineMapLineDriftNextAction;
                }
            }

            response.Warning = PausePointEnableWarnings.MergeWarnings(enableWarning, patchResult.Warning);
            response.Warning = PausePointEnableWarnings.MergeWarnings(
                response.Warning,
                PausePointEnableWarnings.BuildAddedFieldsNotCapturedWarningOrEmpty(patchResult.DeclaringType));
            response.Warning = PausePointEnableWarnings.MergeWarnings(
                response.Warning,
                PausePointEnableWarnings.BuildPerFrameTraceWarningOrEmpty(
                    parameters.Mode,
                    resolvedMethod,
                    snapshot.MaxHistory));
            LogEnable(response.Id, response.ResolvedMethod, $"{parameters.File}:{response.ResolvedLine}", response.Mode, response.Warning);

            if (patchResult.HasPhysicsCallbackWarning)
            {
                PhysicsFlaggedDeclaringTypesById[id] = patchResult.DeclaringType;
                LogPhysicsDispatchDiagnostics(
                    "pause_point_physics_dispatch_diagnostics", id, patchResult.DeclaringType, statusBeforeClear: string.Empty);
            }

            return response;
        }

        private static void LogEnable(string id, string resolvedMethod, string fileLine, string mode, string warning)
        {
            VibeLogger.LogInfo(
                "pause_point_enable",
                $"Pause point enabled: {id}",
                new { Id = id, ResolvedMethod = resolvedMethod, FileLine = fileLine, Mode = mode, HasWarning = !string.IsNullOrEmpty(warning) });
        }

        // Captures the state needed to diagnose a physics-callback dispatch miss if one recurs:
        // whether Play Mode is running, how long the current domain has been alive without a
        // reload (a suspected factor -- see docs/regression-harness.md), the declaring type, and
        // (for MonoBehaviour-derived types only) how many instances currently exist in the loaded
        // scenes. statusBeforeClear is empty at enable time (no clear has happened yet) and
        // Enabled/Expired at clear time.
        private static void LogPhysicsDispatchDiagnostics(string operation, string id, Type declaringType, string statusBeforeClear)
        {
            // Only reachable via PhysicsFlaggedDeclaringTypesById, which is populated solely from
            // a successful patch's method.DeclaringType -- a C#-sourced method always has one.
            Debug.Assert(declaringType != null, "declaringType must not be null");

            bool isMonoBehaviourDerived = typeof(MonoBehaviour).IsAssignableFrom(declaringType);
            // -1 signals "not applicable": counting instances only means something when the
            // declaring type is a MonoBehaviour (the physics dispatch miss this diagnostic exists
            // for is scoped to MonoBehaviour physics message methods).
#if UNITY_6000_4_OR_NEWER
            int instanceCount = isMonoBehaviourDerived
                ? UnityEngine.Object.FindObjectsByType(declaringType, FindObjectsInactive.Include).Length
                : -1;
#else
            int instanceCount = isMonoBehaviourDerived
                ? UnityEngine.Object.FindObjectsByType(declaringType, FindObjectsInactive.Include, FindObjectsSortMode.None).Length
                : -1;
#endif

            VibeLogger.LogInfo(
                operation,
                $"Physics-callback pause point dispatch diagnostics: {id}",
                new
                {
                    Id = id,
                    IsPlaying = EditorApplication.isPlaying,
                    IsPaused = EditorApplication.isPaused,
                    SecondsSinceLastDomainReload = PausePointDomainReloadTracker.SecondsSinceLoad(),
                    DeclaringType = declaringType.FullName,
                    InstanceCount = instanceCount,
                    StatusBeforeClear = statusBeforeClear
                });
        }

        private static string LoadCompiledSnapshotSourceOrEmpty(string requestedFile)
        {
            string normalizedFile = SourcePausePointPathNormalizer.ToForwardSlashes(requestedFile);
            string snapshotSource =
                HotReloadPausePointCoordination.GetVerifiedSnapshotSourceForFile?.Invoke(normalizedFile);
            return snapshotSource ?? string.Empty;
        }

        /// <summary>
        /// Appends a compiled-line Candidate to a resolve-failure Message when the edited
        /// --line text exists in the compiled snapshot.
        /// </summary>
        private static string AppendResolveFailureRequestedLineCandidateOrUnchanged(
            string message,
            EnablePausePointSchema parameters)
        {
            string compiledSnapshotSource = LoadCompiledSnapshotSourceOrEmpty(parameters.File);
            if (string.IsNullOrEmpty(compiledSnapshotSource))
            {
                return message;
            }

            (bool editedReadOk, string requestedLineEditedText) =
                PausePointCompiledLineComparisonWarnings.ReadEditedLineText(
                    parameters.File,
                    parameters.Line);
            if (!editedReadOk)
            {
                return message;
            }

            return PausePointEnableWarnings.AppendResolveFailureRequestedLineCandidateSuffixOrUnchanged(
                message,
                parameters.Line,
                requestedLineEditedText,
                SourcePausePointSourceLineReader.SplitSourceLines(compiledSnapshotSource));
        }

        // The derived id must use the originally requested file/line (not the resolved/rounded
        // line) so repeated calls at the same requested location stay idempotent.
        private static string BuildSourcePausePointId(string file, int line)
        {
            return SourcePausePointPathNormalizer.ToForwardSlashes(file) + ":" + line;
        }

        // Returns an error message when the Id/File/Line combination fails validation, or null
        // when exactly one of "Id" or "File"+"Line" is provided.
        private static string ValidateEnableMode(EnablePausePointSchema parameters)
        {
            bool hasId = !string.IsNullOrWhiteSpace(parameters.Id);
            bool hasFile = !string.IsNullOrWhiteSpace(parameters.File);
            bool hasLine = parameters.Line > 0;

            if (hasId && (hasFile || hasLine))
            {
                return "Specify either Id or File and Line, not both.";
            }

            if (!hasId && !hasFile && !hasLine)
            {
                return "Id must not be null or empty.";
            }

            if (!hasId && hasFile != hasLine)
            {
                return "File and Line must both be provided together.";
            }

            return null;
        }

        private static string ValidateCaptureSettings(EnablePausePointSchema parameters)
        {
            string[] supportedModes =
            {
                UloopPausePointCaptureMode.SingleShot,
                UloopPausePointCaptureMode.Continuous,
                UloopPausePointCaptureMode.Trace
            };
            if (!supportedModes.Contains(parameters.Mode))
            {
                return $"Mode must be one of: {string.Join(", ", supportedModes)}.";
            }

            if (parameters.MaxHistory <= 0 || parameters.MaxHistory > UloopPausePointRegistry.MaxHistoryLimit)
            {
                return $"MaxHistory must be between 1 and {UloopPausePointRegistry.MaxHistoryLimit}.";
            }

            if (parameters.MaxPreviewElements <= 0 ||
                parameters.MaxPreviewElements > UloopPausePointRegistry.MaxPreviewElementsLimit)
            {
                return $"MaxPreviewElements must be between 1 and {UloopPausePointRegistry.MaxPreviewElementsLimit}.";
            }

            if (parameters.MaxCallerFrames < 0 ||
                parameters.MaxCallerFrames > UloopPausePointRegistry.MaxCallerFramesLimit)
            {
                return $"MaxCallerFrames must be between 0 and {UloopPausePointRegistry.MaxCallerFramesLimit}.";
            }

            return null;
        }

        // Returns an error message when id fails validation, or null when it is valid.
        private static string ValidateId(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return "Id must not be null or empty.";
            }

            return null;
        }

        private static PausePointResponse CreateValidationFailure(
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
