using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Retargets and restores armed pause-point markers across hot-reload patch apply and revert.
    /// </summary>
    internal static class SourcePausePointHotReloadRetarget
    {
        /// <summary>
        /// Drains retarget line-drift warnings recorded during the latest hot-reload patch apply.
        /// </summary>
        internal static IReadOnlyList<(string Id, string OldText, string NewText)> ConsumeRetargetLineDriftWarnings()
        {
            if (SourcePausePointPatcher.PendingRetargetLineDriftWarnings.Count == 0)
            {
                return Array.Empty<(string, string, string)>();
            }

            List<(string Id, string OldText, string NewText)> copy =
                new List<(string, string, string)>(SourcePausePointPatcher.PendingRetargetLineDriftWarnings.Count);
            for (int index = 0; index < SourcePausePointPatcher.PendingRetargetLineDriftWarnings.Count; index++)
            {
                RetargetLineDriftWarning warning = SourcePausePointPatcher.PendingRetargetLineDriftWarnings[index];
                copy.Add((warning.Id, warning.OldText, warning.NewText));
            }

            SourcePausePointPatcher.PendingRetargetLineDriftWarnings.Clear();
            return copy;
        }

        /// <summary>
        /// Drains expired-marker ids recorded during the latest hot-reload patch transition
        /// (skipped for retarget because they were not armed).
        /// </summary>
        internal static IReadOnlyList<string> ConsumeExpiredNotRetargetedMarkerIds()
        {
            if (SourcePausePointPatcher.PendingExpiredNotRetargetedIds.Count == 0)
            {
                return Array.Empty<string>();
            }

            List<string> copy = new List<string>(SourcePausePointPatcher.PendingExpiredNotRetargetedIds);
            SourcePausePointPatcher.PendingExpiredNotRetargetedIds.Clear();
            return copy;
        }

        internal readonly struct RetargetLineDriftWarning
        {
            public RetargetLineDriftWarning(string id, string oldText, string newText)
            {
                Id = id;
                OldText = oldText;
                NewText = newText;
            }

            public string Id { get; }
            public string OldText { get; }
            public string NewText { get; }
        }

        // What: lets the hot-reload tool list marker ids by logical owner (user method),
        // including markers whose physical injection lives on a shim-side MoveNext/closure.
        // Why IsArmed: hit/expired SingleShot markers keep instrumentation until clear, but
        // transitions and apply warnings must track only currently armed markers.
        internal static IReadOnlyList<string> GetArmedMarkerIds(MethodBase method)
        {
            List<string> ids = new List<string>();
            foreach (KeyValuePair<string, MethodBase> pair in SourcePausePointPatcher.LogicalOwnerById)
            {
                if (pair.Value.Equals(method) && UloopPausePointRegistry.IsArmed(pair.Key))
                {
                    ids.Add(pair.Key);
                }
            }

            return ids;
        }

        // What: ids that stayed armed on the logical owner but could not be re-targeted.
        internal static IReadOnlyList<string> GetSuppressedMarkerIds(MethodBase method)
        {
            List<string> ids = new List<string>();
            foreach (KeyValuePair<string, MethodBase> pair in SourcePausePointPatcher.LogicalOwnerById)
            {
                if (!pair.Value.Equals(method) || !UloopPausePointRegistry.IsArmed(pair.Key))
                {
                    continue;
                }

                if (UloopPausePointRegistry.GetStatus(pair.Key).SuppressedByHotReload)
                {
                    ids.Add(pair.Key);
                }
            }

            return ids;
        }

        // What: re-targets or restores armed markers when a logical owner's hot-reload patch
        // state changes. Succeeds before mutating registry flags; never clears the marker.
        internal static void HandleHotReloadPatchStateChanged(MethodBase method, bool isPatched)
        {
            if (isPatched)
            {
                // Why before Collect: expired markers are not armed, so they never enter the
                // retarget list — record them as a transition event (pending-drain) instead of
                // scanning residual Expired ledger state on every later apply.
                EnqueueExpiredNotRetargetedForLogicalOwner(method);
                List<string> markerIds = CollectMarkerIdsForLogicalOwner(method);
                RetargetMarkersOntoHotReloadPatch(markerIds, method);
                return;
            }

            List<string> restoreIds = CollectMarkerIdsForLogicalOwner(method);
            RestoreMarkersAfterHotReloadRevert(restoreIds, method);
        }

        // What: records expired owned markers skipped by this patch transition, then detaches
        // them from the retarget owner ledger so later hot-reloads do not re-warn forever.
        // Why keep MethodById/injections: Clear → Unpatch still needs to remove Harmony patches.
        private static void EnqueueExpiredNotRetargetedForLogicalOwner(MethodBase method)
        {
            List<string> expiredIds = new List<string>();
            foreach (KeyValuePair<string, MethodBase> pair in SourcePausePointPatcher.LogicalOwnerById)
            {
                if (!pair.Value.Equals(method) || UloopPausePointRegistry.IsArmed(pair.Key))
                {
                    continue;
                }

                UloopPausePointSnapshot status = UloopPausePointRegistry.GetStatus(pair.Key);
                if (!status.Expired && status.Status != UloopPausePointStatus.Expired)
                {
                    continue;
                }

                expiredIds.Add(pair.Key);
            }

            for (int index = 0; index < expiredIds.Count; index++)
            {
                string id = expiredIds[index];
                SourcePausePointPatcher.LogicalOwnerById.Remove(id);
                SourcePausePointPatcher.RequestById.Remove(id);
                if (!SourcePausePointPatcher.PendingExpiredNotRetargetedIds.Contains(id))
                {
                    SourcePausePointPatcher.PendingExpiredNotRetargetedIds.Add(id);
                }
            }
        }

        private static List<string> CollectMarkerIdsForLogicalOwner(MethodBase method)
        {
            List<string> ids = new List<string>();
            foreach (KeyValuePair<string, MethodBase> ownerPair in SourcePausePointPatcher.LogicalOwnerById)
            {
                // Why IsArmed: hit/expired instrumentation remains until clear, but retarget /
                // restore must not rewrite disarmed markers back to RetargetedToHotReloadPatch.
                if (ownerPair.Value.Equals(method) && UloopPausePointRegistry.IsArmed(ownerPair.Key))
                {
                    ids.Add(ownerPair.Key);
                }
            }

            return ids;
        }

        private static void RetargetMarkersOntoHotReloadPatch(List<string> markerIds, MethodBase logicalOwner)
        {
            for (int index = 0; index < markerIds.Count; index++)
            {
                string id = markerIds[index];
                if (!SourcePausePointPatcher.RequestById.TryGetValue(id, out (string NormalizedFile, int Line) request))
                {
                    SuppressMarkerRetargetFailed(id);
                    continue;
                }

                HotReloadShimFileLookup lookup =
                    HotReloadPausePointCoordination.GetShimLookupForFile?.Invoke(request.NormalizedFile);
                if (lookup == null)
                {
                    SuppressMarkerRetargetFailed(id);
                    continue;
                }

                SourcePausePointShimResolution shimResolution = SourcePausePointShimResolver.Resolve(
                    lookup,
                    request.NormalizedFile,
                    request.Line);
                if (shimResolution.Kind != SourcePausePointShimResolveKind.TransplantChainJoin
                    && shimResolution.Kind != SourcePausePointShimResolveKind.ShimDirect)
                {
                    SuppressMarkerRetargetFailed(id);
                    continue;
                }

                SourcePausePointPatcher.UnpatchInstrumentationOnly(id);
                // Why try-finally (not catch): Fail Fast on Harmony emit failures, but keep
                // LogicalOwner/Request and suppress flags honest when CommitPatch rolls back.
                bool committed = false;
                try
                {
                    SourcePausePointPatchResult patchResult = SourcePausePointPatcher.PatchShimTarget(
                        id,
                        shimResolution,
                        request.NormalizedFile,
                        request.Line);
                    committed = patchResult.Success;
                    if (committed)
                    {
                        string previousLineText = UloopPausePointRegistry.GetStatus(id).ResolvedLineText;
                        // Shim resolutions carry no end line, so the span degenerates to the
                        // single resolved line.
                        string newLineText = PausePointLineTextReader.ReadResolvedLineText(
                            request.NormalizedFile,
                            shimResolution.ResolvedLine,
                            shimResolution.ResolvedLine);
                        UloopPausePointRegistry.SetResolvedLine(
                            id,
                            shimResolution.ResolvedLine,
                            newLineText);
                        if (!string.IsNullOrEmpty(previousLineText)
                            && !string.Equals(previousLineText, newLineText, StringComparison.Ordinal))
                        {
                            SourcePausePointPatcher.PendingRetargetLineDriftWarnings.Add(
                                new RetargetLineDriftWarning(id, previousLineText, newLineText));
                        }

                        UloopPausePointRegistry.SetSuppressedByHotReload(id, false, null);
                        UloopPausePointRegistry.SetRetargetedToHotReloadPatch(id, true);
                    }
                }
                finally
                {
                    if (!committed)
                    {
                        ReanchorMarkerWithoutInjection(id, logicalOwner, request);
                        SuppressMarkerRetargetFailed(id);
                    }
                }
            }
        }

        private static void RestoreMarkersAfterHotReloadRevert(List<string> markerIds, MethodBase logicalOwner)
        {
            for (int index = 0; index < markerIds.Count; index++)
            {
                string id = markerIds[index];
                if (!SourcePausePointPatcher.RequestById.TryGetValue(id, out (string NormalizedFile, int Line) request))
                {
                    UloopPausePointRegistry.SetResolvedLine(id, 0, null);
                    SuppressMarkerRestoreFailed(id);
                    continue;
                }

                SourcePausePointResolveResult resolveResult =
                    SourcePausePointResolver.Resolve(request.NormalizedFile, request.Line);
                if (!resolveResult.Success)
                {
                    UloopPausePointRegistry.SetResolvedLine(id, 0, null);
                    SuppressMarkerRestoreFailed(id);
                    continue;
                }

                SourcePausePointPatchResult methodResolve =
                    SourcePausePointPatcher.TryResolveMethod(resolveResult.Resolution, out MethodBase restoredMethod);
                if (!methodResolve.Success
                    || restoredMethod == null
                    || !IsSameLogicalMethodOrItsStateMachine(restoredMethod, logicalOwner))
                {
                    UloopPausePointRegistry.SetResolvedLine(id, 0, null);
                    SuppressMarkerRestoreFailed(id);
                    continue;
                }

                SourcePausePointPatcher.UnpatchInstrumentationOnly(id);
                bool committed = false;
                try
                {
                    // Why logicalOwner override: async/iterator PDB resolve returns MoveNext, but
                    // hot-reload keys and later transitions use the compiled wrapper as owner.
                    SourcePausePointPatchResult patchResult = SourcePausePointPatcher.Patch(
                        id,
                        resolveResult.Resolution,
                        request.NormalizedFile,
                        request.Line,
                        logicalOwner);
                    committed = patchResult.Success;
                    if (committed)
                    {
                        // Why snapshot: restore uses compiled line numbers; the disk file may
                        // still be the edited source, so disk text would lie the same way enable did.
                        string dllPath = restoredMethod.DeclaringType != null
                            ? restoredMethod.DeclaringType.Assembly.Location
                            : null;
                        string restoredLineText = PausePointLineTextReader.ReadCompiledSnapshotLineText(
                            request.NormalizedFile,
                            dllPath,
                            resolveResult.Resolution.ResolvedLine);
                        UloopPausePointRegistry.SetResolvedLine(
                            id,
                            resolveResult.Resolution.ResolvedLine,
                            restoredLineText);
                        UloopPausePointRegistry.SetSuppressedByHotReload(id, false, null);
                        UloopPausePointRegistry.SetRetargetedToHotReloadPatch(id, false);
                    }
                }
                finally
                {
                    if (!committed)
                    {
                        // Why clear: restore failed, so previously stored shim line is no longer trustworthy.
                        UloopPausePointRegistry.SetResolvedLine(id, 0, null);
                        ReanchorMarkerWithoutInjection(id, logicalOwner, request);
                        SuppressMarkerRestoreFailed(id);
                    }
                }
            }
        }

        // Why StateMachineAttribute: async/iterator hot-reload events key the compiled wrapper,
        // while compiled PDB resolve returns MoveNext on the state-machine type.
        private static bool IsSameLogicalMethodOrItsStateMachine(
            MethodBase restoredMethod,
            MethodBase logicalOwner)
        {
            if (restoredMethod.Equals(logicalOwner))
            {
                return true;
            }

            StateMachineAttribute attribute = logicalOwner.GetCustomAttribute<StateMachineAttribute>();
            return attribute != null
                && attribute.StateMachineType != null
                && attribute.StateMachineType == restoredMethod.DeclaringType;
        }

        private static void ReanchorMarkerWithoutInjection(
            string id,
            MethodBase logicalOwner,
            (string NormalizedFile, int Line) request)
        {
            SourcePausePointPatcher.LogicalOwnerById[id] = logicalOwner;
            SourcePausePointPatcher.RequestById[id] = (request.NormalizedFile, request.Line);
        }

        private static void SuppressMarkerRetargetFailed(string id)
        {
            UloopPausePointRegistry.SetSuppressedByHotReload(
                id,
                true,
                SourcePausePointConstants.RetargetOntoHotReloadFailedReason);
            UloopPausePointRegistry.SetRetargetedToHotReloadPatch(id, false);
        }

        private static void SuppressMarkerRestoreFailed(string id)
        {
            UloopPausePointRegistry.SetSuppressedByHotReload(
                id,
                true,
                SourcePausePointConstants.RestoreAfterHotReloadRevertFailedReason);
            UloopPausePointRegistry.SetRetargetedToHotReloadPatch(id, false);
        }
    }
}
