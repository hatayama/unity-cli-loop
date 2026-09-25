using System;
using System.Collections.Generic;

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
                PausePointUseCaseLogger.LogPhysicsDispatchDiagnostics("pause_point_cleared_without_hit_physics", id, declaringType, statusBeforeClear);
            }
            PhysicsFlaggedDeclaringTypesById.Remove(id);
            PausePointPersistRequestLedger.Remove(id);
        }

        public PausePointResponse Enable(EnablePausePointSchema parameters)
        {
            string hitWhen = string.IsNullOrWhiteSpace(parameters.HitWhen)
                ? string.Empty
                : parameters.HitWhen;
            UloopPausePointHitWhenParseResult hitWhenParseResult = string.IsNullOrEmpty(hitWhen)
                ? null
                : UloopPausePointHitWhenCondition.Parse(hitWhen);
            UloopPausePointHitWhenCondition hitWhenCondition = hitWhenParseResult == null
                ? null
                : hitWhenParseResult.Condition;
            string captureSettingsError = PausePointEnableValidation.ValidateCaptureSettings(
                parameters,
                hitWhen,
                hitWhenParseResult);
            if (captureSettingsError != null)
            {
                return PausePointFailureResponse.Create(
                    captureSettingsError,
                    SourcePausePointConstants.ErrorCodeInvalidArgument,
                    "Fix the rejected capture argument described in Message and re-run; uloop enable-pause-point --help lists the accepted values.");
            }

            string modeError = PausePointEnableValidation.ValidateEnableMode(parameters);
            if (modeError != null)
            {
                return PausePointFailureResponse.Create(
                    modeError,
                    SourcePausePointConstants.ErrorCodeInvalidArgument,
                    "Re-run with either --id alone, or --file and --line together.");
            }

            if (parameters.TimeoutSeconds <= 0)
            {
                return PausePointFailureResponse.Create(
                    "TimeoutSeconds must be greater than zero.",
                    SourcePausePointConstants.ErrorCodeInvalidArgument,
                    "Re-run with --timeout-seconds set to a positive integer.");
            }

            if (!string.IsNullOrWhiteSpace(parameters.File))
            {
                return EnableBySourceLocation(parameters, hitWhen, hitWhenCondition);
            }

            string rearmWarning = PausePointEnableWarnings.BuildRearmDiscardWarningOrEmpty(
                UloopPausePointRegistry.GetStatus(parameters.Id));
            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.Enable(
                parameters.Id,
                parameters.TimeoutSeconds,
                parameters.Mode,
                parameters.MaxHistory,
                parameters.MaxPreviewElements,
                parameters.MaxCallerFrames,
                hitWhen,
                hitWhenCondition);
            ApplyPersistRequest(snapshot.Id, parameters);
            snapshot = UloopPausePointRegistry.GetStatus(snapshot.Id);
            PausePointResponse response = PausePointResponse.FromSnapshot(snapshot);
            List<string> warningEntries = new List<string>();
            PausePointEnableWarningList.AddIfNotEmpty(
                warningEntries,
                PausePointEnableWarnings.CreateEnableWarning());
            PausePointEnableWarningList.AddIfNotEmpty(warningEntries, rearmWarning);
            PausePointEnableWarningList.Assign(response, warningEntries);
            response.RecommendedNextAction = PausePointEnableWarnings
                .ResolveSuccessEnableRecommendedNextAction(response.RecommendedNextAction, response.Id);
            PausePointUseCaseLogger.LogEnable(response.Id, resolvedMethod: string.Empty, fileLine: string.Empty, response.Mode, response.Warning);
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
                        PausePointUseCaseLogger.LogPhysicsDispatchDiagnostics(
                            "pause_point_cleared_without_hit_physics", tracked.Key, tracked.Value, trackedSnapshot.Status);
                    }
                }

                // Registry.ClearAll unpatches any source pause points via the hook
                // SourcePausePointPatcher wires into it; this use case never references the
                // Patcher directly.
                UloopPausePointClearAllResult clearAllResult = UloopPausePointRegistry.ClearAll();
                PausePointUseCaseLogger.LogCleared("all", string.Empty);
                PhysicsFlaggedDeclaringTypesById.Clear();
                return PausePointResponse.FromClearAll(clearAllResult);
            }

            string idError = PausePointEnableValidation.ValidateId(parameters.Id);
            if (idError != null)
            {
                return PausePointFailureResponse.Create(
                    idError,
                    SourcePausePointConstants.ErrorCodeInvalidArgument,
                    "Pass --id with the id returned by enable-pause-point, or use --all to clear every marker.");
            }

            (UloopPausePointSnapshot snapshot, bool resumedFromPause, int clearedCount) =
                UloopPausePointRegistry.Clear(parameters.Id);
            PausePointUseCaseLogger.LogCleared(snapshot.Id, snapshot.StatusBeforeClear);
            if (snapshot.StatusBeforeClear == UloopPausePointStatus.Expired)
            {
                PausePointUseCaseLogger.LogExpired(snapshot.Id, snapshot.ElapsedSinceEnabledMilliseconds);
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
                List<string> warningEntries = new List<string>();
                PausePointEnableWarningList.AddIfNotEmpty(
                    warningEntries,
                    SourcePausePointConstants.ClearReleasedOwnedPauseWarning);
                PausePointEnableWarningList.Assign(response, warningEntries);

                // FromSnapshot leaves this empty on a clear: the snapshot only carries a
                // recommendation while Status is Expired, and Status is Cleared by now. So this
                // fills the field rather than competing with an existing recommendation.
                response.RecommendedNextAction =
                    SourcePausePointConstants.ClearReleasedPauseRecommendedNextAction;
            }

            return response;
        }

        // Why the port as well as the shim lookup: the lookup lists patched methods only, and
        // a reload that only added methods moves edited lines off the compiled map just the same.
        private static bool HasActiveHotReloadChanges(HotReloadShimFileLookup shimLookup, string normalizedFile)
        {
            return shimLookup != null
                || HotReloadPausePointCoordination.HotReloadSide?.HasActiveHotReloadChangesInFile(normalizedFile) == true;
        }

        // Resolves File:Line to a patch location via the Resolver, patches it via Harmony, then
        // arms the same registry state machine the Id path uses, keyed by the derived source id.
        private static PausePointResponse EnableBySourceLocation(
            EnablePausePointSchema parameters,
            string hitWhen,
            UloopPausePointHitWhenCondition hitWhenCondition)
        {
            if (CompilationPipeline.codeOptimization == CodeOptimization.Release)
            {
                return PausePointFailureResponse.Create(
                    SourcePausePointConstants.ReleaseCodeOptimizationRejectionMessage,
                    SourcePausePointConstants.ErrorCodeReleaseCodeOptimization,
                    SourcePausePointConstants.ReleaseCodeOptimizationRecommendedNextAction);
            }

            string normalizedFile = SourcePausePointPathNormalizer.ToForwardSlashes(parameters.File);
            string id = BuildSourcePausePointId(parameters.File, parameters.Line);
            SourcePausePointSnapshotTiming snapshotTiming = ParseSnapshotTiming(parameters.SnapshotTiming);

            HotReloadShimFileLookup shimLookup =
                HotReloadPausePointCoordination.HotReloadSide?.GetShimLookupForFile(normalizedFile);
            if (shimLookup != null)
            {
                bool shimSourceChanged =
                    HotReloadPausePointCoordination.HotReloadSide.HasShimSourceChangedOnDisk(normalizedFile);
                SourcePausePointShimResolution shimResolution =
                    SourcePausePointShimResolver.Resolve(
                        shimLookup, normalizedFile, parameters.Line, parameters.Method, snapshotTiming);
                PausePointResponse staleSourceRefusal =
                    PausePointPatchedSourceGuard.RefuseWhenChangedOrNull(
                        shimSourceChanged, shimResolution, normalizedFile, parameters.Line);
                if (staleSourceRefusal != null)
                {
                    return staleSourceRefusal;
                }

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
                        hitWhen,
                        hitWhenCondition,
                        shimResolution.ResolvedLine,
                        shimResolution.ResolvedLine,
                        shimResolution.MethodDisplayName,
                        shimPatchResult,
                        "EditedFile",
                        retargetedToHotReloadPatch: true,
                        shimResolution.NotCapturableVariables,
                        editedMethodStartLine: shimResolution.SourceStartLine,
                        editedMethodEndLine: shimResolution.SourceEndLine);
                }

                if (shimResolution.Kind == SourcePausePointShimResolveKind.NoStatementInPatchedMethod)
                {
                    return PausePointFailureResponse.Create(
                        shimResolution.ErrorMessage,
                        SourcePausePointConstants.ErrorCodeResolveFailed,
                        "Pick a line with an executable statement inside the edited method body.");
                }

                // Why refuse: without shim debug symbols the edited body has no line map, and
                // falling through would arm compiled code that no longer runs.
                if (shimResolution.Kind == SourcePausePointShimResolveKind.PatchedMethodPdbUnavailable)
                {
                    return PausePointFailureResponse.Create(
                        string.Format(
                            SourcePausePointConstants.HotReloadPatchedMethodPdbUnavailableWarningFormat,
                            shimResolution.MethodDisplayName,
                            parameters.Line),
                        SourcePausePointConstants.ErrorCodePausePointPatchedByHotReload,
                        SourcePausePointConstants.HotReloadPatchedMethodPdbUnavailableNextAction);
                }

                // NotInPatchedMethod: fall through to the compiled ScriptAssemblies resolver.
            }

            // Asked separately from the shim lookup, which is null when the file has only added
            // methods and no patched ones.
            bool hasActiveHotReloadChanges = HasActiveHotReloadChanges(shimLookup, normalizedFile);

            PausePointEditedLineResolution resolution =
                PausePointEditedLineResolver.Resolve(parameters, normalizedFile, snapshotTiming);
            if (resolution.Refusal != null)
            {
                return resolution.Refusal;
            }

            SourcePausePointResolveResult resolveResult = resolution.ResolveResult;
            if (!resolveResult.Success)
            {
                return PausePointResolveFailureResponse.Create(
                    parameters,
                    normalizedFile,
                    hasActiveHotReloadPatches: hasActiveHotReloadChanges,
                    resolveResult,
                    string.Empty);
            }

            SourcePausePointPatchResult patchResult = SourcePausePointPatcher.Patch(
                id,
                resolveResult.Resolution,
                normalizedFile,
                parameters.Line);
            if (!patchResult.Success)
            {
                return CreateCompiledPatchFailureResponse(patchResult);
            }

            return FinishEnableBySourceLocation(
                id,
                parameters,
                hitWhen,
                hitWhenCondition,
                resolution.EditedResolvedLine,
                resolution.EditedResolvedEndLine,
                resolveResult.Resolution.MethodDisplayName,
                patchResult,
                resolution.LineBasis,
                retargetedToHotReloadPatch: false,
                resolveResult.Resolution.NotCapturableVariables,
                editedMethodStartLine: resolution.EditedMethodStartLine,
                editedMethodEndLine: resolution.EditedMethodEndLine,
                fallbackWarning: resolution.Warning);
        }

        private static PausePointResponse CreateCompiledPatchFailureResponse(SourcePausePointPatchResult patchResult)
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

        // Validation already rejected anything but the two accepted values.
        private static SourcePausePointSnapshotTiming ParseSnapshotTiming(string value)
        {
            if (value == SourcePausePointConstants.PostLineSnapshotTimingValue)
            {
                return SourcePausePointSnapshotTiming.PostLine;
            }

            Debug.Assert(
                value == SourcePausePointConstants.PreLineSnapshotTimingValue,
                "SnapshotTiming must have been validated before use.");
            return SourcePausePointSnapshotTiming.PreLine;
        }

        private static string DescribeSnapshotTiming(SourcePausePointSnapshotTiming snapshotTiming)
        {
            return snapshotTiming == SourcePausePointSnapshotTiming.PostLine
                ? SourcePausePointConstants.PostLineSnapshotTimingNote
                : SourcePausePointConstants.PreLineSnapshotTimingNote;
        }

        /// <summary>
        /// Records whether this enable asked for the pause point to be re-armed after a domain
        /// reload. Enabling without --persist rebuilds the entry, so persistence is already off;
        /// this only has to turn it on, and to drop any request the previous enable left behind.
        /// </summary>
        private static void ApplyPersistRequest(string registryId, EnablePausePointSchema parameters)
        {
            if (!parameters.Persist)
            {
                PausePointPersistRequestLedger.Remove(registryId);
                return;
            }

            UloopPausePointRegistry.SetPersisted(registryId, true);
            PausePointPersistRequestLedger.Upsert(
                PausePointPersistedRecord.FromSchema(registryId, parameters));
        }

        private static PausePointResponse FinishEnableBySourceLocation(
            string id,
            EnablePausePointSchema parameters,
            string hitWhen,
            UloopPausePointHitWhenCondition hitWhenCondition,
            int resolvedLine,
            int resolvedEndLine,
            string resolvedMethod,
            SourcePausePointPatchResult patchResult,
            string lineBasis,
            bool retargetedToHotReloadPatch,
            IReadOnlyList<string> notCapturableVariables,
            int editedMethodStartLine = 0,
            int editedMethodEndLine = 0,
            string fallbackWarning = "")
        {
            string rearmWarning = PausePointEnableWarnings.BuildRearmDiscardWarningOrEmpty(
                UloopPausePointRegistry.GetStatus(id));
            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.Enable(
                id,
                parameters.TimeoutSeconds,
                parameters.Mode,
                parameters.MaxHistory,
                parameters.MaxPreviewElements,
                parameters.MaxCallerFrames,
                hitWhen,
                hitWhenCondition,
                patchResult.HasPhysicsCallbackWarning);
            ApplyPersistRequest(snapshot.Id, parameters);
            snapshot = UloopPausePointRegistry.GetStatus(snapshot.Id);
            if (retargetedToHotReloadPatch)
            {
                UloopPausePointRegistry.SetRetargetedToHotReloadPatch(id, true);
                snapshot = UloopPausePointRegistry.GetStatus(id);
            }

            // Why empty on the compiled basis: a compiled line number read against the edited disk
            // file shows whatever statement drifted onto it. The disk read spans
            // resolvedLine..resolvedEndLine so a rounded-forward multi-line statement keeps its full text.
            string resolvedLineText = lineBasis == "EditedFile"
                ? PausePointLineTextReader.ReadResolvedLineText(parameters.File, resolvedLine, resolvedEndLine)
                : string.Empty;
            UloopPausePointRegistry.SetResolvedLine(id, resolvedLine, resolvedLineText);
            UloopPausePointRegistry.SetNotCapturableVariables(id, notCapturableVariables);

            PausePointResponse response = PausePointResponse.FromSnapshot(snapshot);
            // Why re-read after SetResolvedLine: FromSnapshot above used the pre-write snapshot.
            response.ResolvedLine = resolvedLine;
            response.ResolvedLineText = resolvedLineText;
            response.NotCapturableVariables =
                PausePointResponse.NormalizeNotCapturableVariables(notCapturableVariables);
            response.ResolvedMethod = resolvedMethod;
            response.SnapshotTiming = DescribeSnapshotTiming(ParseSnapshotTiming(parameters.SnapshotTiming));
            response.LineBasis = lineBasis;
            List<string> warningEntries = new List<string>();
            PausePointEnableWarningList.AddIfNotEmpty(
                warningEntries,
                PausePointEnableWarnings.CreateEnableWarning());
            PausePointEnableWarningList.AddIfNotEmpty(warningEntries, fallbackWarning);
            PausePointEnableWarningList.AddIfNotEmpty(
                warningEntries,
                PausePointEnableWarnings.BuildRetargetedToHotReloadPatchWarningOrEmpty(
                    retargetedToHotReloadPatch,
                    resolvedMethod,
                    parameters.Line,
                    editedMethodStartLine,
                    editedMethodEndLine));
            PausePointEnableWarningList.AddRangeIfNotEmpty(warningEntries, patchResult.Warnings);
            PausePointEnableWarningList.AddIfNotEmpty(
                warningEntries,
                PausePointEnableWarnings.BuildAddedFieldsNotCapturedWarningOrEmpty(patchResult.DeclaringType));
            PausePointEnableWarningList.AddRangeIfNotEmpty(
                warningEntries,
                PausePointPerFrameEnableWarnings.CollectPerFrameEnableWarnings(
                    parameters.Mode,
                    resolvedMethod,
                    snapshot.MaxHistory));
            PausePointEnableWarningList.AddIfNotEmpty(
                warningEntries,
                PausePointNotCapturableWarnings.BuildNotCapturableParametersWarningOrEmpty(
                    notCapturableVariables));
            PausePointEnableWarningList.AddIfNotEmpty(warningEntries, rearmWarning);
            PausePointEnableWarningList.AddIfNotEmpty(
                warningEntries,
                PausePointEnableWarnings.BuildClosingBraceWarningOrEmpty(
                    resolvedLineText,
                    resolvedLine,
                    resolvedMethod,
                    editedMethodEndLine));
            PausePointEnableWarningList.Assign(response, warningEntries);
            response.RecommendedNextAction = PausePointEnableWarnings
                .ResolveSuccessEnableRecommendedNextAction(response.RecommendedNextAction, id);
            PausePointUseCaseLogger.LogEnable(response.Id, response.ResolvedMethod, $"{parameters.File}:{response.ResolvedLine}", response.Mode, response.Warning);

            if (patchResult.HasPhysicsCallbackWarning)
            {
                PhysicsFlaggedDeclaringTypesById[id] = patchResult.DeclaringType;
                PausePointUseCaseLogger.LogPhysicsDispatchDiagnostics(
                    "pause_point_physics_dispatch_diagnostics", id, patchResult.DeclaringType, statusBeforeClear: string.Empty);
            }

            return response;
        }

        // The derived id must use the originally requested file/line (not the resolved/rounded
        // line) so repeated calls at the same requested location stay idempotent.
        private static string BuildSourcePausePointId(string file, int line)
        {
            return SourcePausePointPathNormalizer.ToForwardSlashes(file) + ":" + line;
        }
    }
}
