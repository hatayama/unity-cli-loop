using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

using HarmonyLib;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Injects a call to <see cref="SourcePausePointCapture.Capture"/> at a resolved instruction
    /// index via a Harmony transpiler, and tracks the resulting patches so they can be removed.
    /// The injected IL calls an internal method across assembly boundaries; this works because
    /// Harmony/MonoMod builds the replacement method as a skip-visibility DynamicMethod, so the
    /// CLR's normal accessibility check for the call site never runs (verified in PR 4's design
    /// discussion against the vendored Harmony's own private-nested-class smoke test).
    /// </summary>
    internal static class SourcePausePointPatcher
    {
        private static readonly Harmony HarmonyInstance = new(SourcePausePointConstants.HarmonyId);
        private static readonly MethodInfo TranspilerMethodInfo =
            typeof(SourcePausePointPatcher).GetMethod(nameof(Transpiler), BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo CaptureMethodInfo =
            typeof(SourcePausePointCapture).GetMethod(nameof(SourcePausePointCapture.Capture));
        private static readonly MethodInfo IsArmedMethodInfo =
            typeof(UloopPausePointRegistry).GetMethod(nameof(UloopPausePointRegistry.IsArmed));

        private static readonly Dictionary<MethodBase, List<SourcePausePointPatchInjection>> InjectionsByMethod = new();
        private static readonly Dictionary<string, MethodBase> MethodById = new();
        private static readonly Dictionary<string, MethodBase> LogicalOwnerById = new();
        // Why store file:line separately: auto-retarget must re-resolve with the original enable
        // request, and parsing the id string would couple us to id formatting.
        private static readonly Dictionary<string, (string NormalizedFile, int Line)> RequestById = new();
        private static readonly List<RetargetLineDriftWarning> PendingRetargetLineDriftWarnings =
            new List<RetargetLineDriftWarning>();
        private static readonly List<string> PendingExpiredNotRetargetedIds = new List<string>();

        // The registry lives in a Runtime assembly this Editor-only tool assembly may depend on,
        // but not the reverse (patching is an outer/implementation concern the registry's inner
        // layer must not know about). Wiring these hooks here - rather than having every Clear
        // caller reference this class directly - lets the Infrastructure CLI bridge call
        // UloopPausePointRegistry.Clear/ClearAll without ever referencing this assembly.
        static SourcePausePointPatcher()
        {
            UloopPausePointRegistry.OnCleared = Unpatch;
            UloopPausePointRegistry.OnClearedAll = UnpatchAll;
            HotReloadPausePointCoordination.GetArmedMarkerIdsOnMethod = GetArmedMarkerIds;
            HotReloadPausePointCoordination.GetSuppressedMarkerIdsOnMethod = GetSuppressedMarkerIds;
            HotReloadPausePointCoordination.ConsumeExpiredNotRetargetedMarkerIds =
                ConsumeExpiredNotRetargetedMarkerIds;
            HotReloadPausePointCoordination.OnHotReloadPatchStateChanged = HandleHotReloadPatchStateChanged;
            HotReloadPausePointCoordination.ConsumeRetargetLineDriftWarnings = ConsumeRetargetLineDriftWarnings;
        }

        /// <summary>
        /// Drains retarget line-drift warnings recorded during the latest hot-reload patch apply.
        /// </summary>
        private static IReadOnlyList<(string Id, string OldText, string NewText)> ConsumeRetargetLineDriftWarnings()
        {
            if (PendingRetargetLineDriftWarnings.Count == 0)
            {
                return Array.Empty<(string, string, string)>();
            }

            List<(string Id, string OldText, string NewText)> copy =
                new List<(string, string, string)>(PendingRetargetLineDriftWarnings.Count);
            for (int index = 0; index < PendingRetargetLineDriftWarnings.Count; index++)
            {
                RetargetLineDriftWarning warning = PendingRetargetLineDriftWarnings[index];
                copy.Add((warning.Id, warning.OldText, warning.NewText));
            }

            PendingRetargetLineDriftWarnings.Clear();
            return copy;
        }

        /// <summary>
        /// Drains expired-marker ids recorded during the latest hot-reload patch transition
        /// (skipped for retarget because they were not armed).
        /// </summary>
        private static IReadOnlyList<string> ConsumeExpiredNotRetargetedMarkerIds()
        {
            if (PendingExpiredNotRetargetedIds.Count == 0)
            {
                return Array.Empty<string>();
            }

            List<string> copy = new List<string>(PendingExpiredNotRetargetedIds);
            PendingExpiredNotRetargetedIds.Clear();
            return copy;
        }

        private readonly struct RetargetLineDriftWarning
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
        private static IReadOnlyList<string> GetArmedMarkerIds(MethodBase method)
        {
            List<string> ids = new List<string>();
            foreach (KeyValuePair<string, MethodBase> pair in LogicalOwnerById)
            {
                if (pair.Value.Equals(method) && UloopPausePointRegistry.IsArmed(pair.Key))
                {
                    ids.Add(pair.Key);
                }
            }

            return ids;
        }

        // What: ids that stayed armed on the logical owner but could not be re-targeted.
        private static IReadOnlyList<string> GetSuppressedMarkerIds(MethodBase method)
        {
            List<string> ids = new List<string>();
            foreach (KeyValuePair<string, MethodBase> pair in LogicalOwnerById)
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
        private static void HandleHotReloadPatchStateChanged(MethodBase method, bool isPatched)
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
            foreach (KeyValuePair<string, MethodBase> pair in LogicalOwnerById)
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
                LogicalOwnerById.Remove(id);
                RequestById.Remove(id);
                if (!PendingExpiredNotRetargetedIds.Contains(id))
                {
                    PendingExpiredNotRetargetedIds.Add(id);
                }
            }
        }

        private static List<string> CollectMarkerIdsForLogicalOwner(MethodBase method)
        {
            List<string> ids = new List<string>();
            foreach (KeyValuePair<string, MethodBase> ownerPair in LogicalOwnerById)
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
                if (!RequestById.TryGetValue(id, out (string NormalizedFile, int Line) request))
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

                UnpatchInstrumentationOnly(id);
                // Why try-finally (not catch): Fail Fast on Harmony emit failures, but keep
                // LogicalOwner/Request and suppress flags honest when CommitPatch rolls back.
                bool committed = false;
                try
                {
                    SourcePausePointPatchResult patchResult = PatchShimTarget(
                        id,
                        shimResolution,
                        request.NormalizedFile,
                        request.Line);
                    committed = patchResult.Success;
                    if (committed)
                    {
                        string previousLineText = UloopPausePointRegistry.GetStatus(id).ResolvedLineText;
                        string newLineText = PausePointLineTextReader.ReadResolvedLineText(
                            request.NormalizedFile,
                            shimResolution.ResolvedLine);
                        UloopPausePointRegistry.SetResolvedLine(
                            id,
                            shimResolution.ResolvedLine,
                            newLineText);
                        if (!string.IsNullOrEmpty(previousLineText)
                            && !string.Equals(previousLineText, newLineText, StringComparison.Ordinal))
                        {
                            PendingRetargetLineDriftWarnings.Add(
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
                if (!RequestById.TryGetValue(id, out (string NormalizedFile, int Line) request))
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
                    TryResolveMethod(resolveResult.Resolution, out MethodBase restoredMethod);
                if (!methodResolve.Success
                    || restoredMethod == null
                    || !IsSameLogicalMethodOrItsStateMachine(restoredMethod, logicalOwner))
                {
                    UloopPausePointRegistry.SetResolvedLine(id, 0, null);
                    SuppressMarkerRestoreFailed(id);
                    continue;
                }

                UnpatchInstrumentationOnly(id);
                bool committed = false;
                try
                {
                    // Why logicalOwner override: async/iterator PDB resolve returns MoveNext, but
                    // hot-reload keys and later transitions use the compiled wrapper as owner.
                    SourcePausePointPatchResult patchResult = Patch(
                        id,
                        resolveResult.Resolution,
                        request.NormalizedFile,
                        request.Line,
                        logicalOwner);
                    committed = patchResult.Success;
                    if (committed)
                    {
                        // Why update after restore: status must show the line that will fire next.
                        string restoredLineText = PausePointLineTextReader.ReadResolvedLineText(
                            request.NormalizedFile,
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
            LogicalOwnerById[id] = logicalOwner;
            RememberRequest(id, request.NormalizedFile, request.Line);
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

        public static SourcePausePointPatchResult Patch(
            string id,
            SourcePausePointResolution resolution,
            string normalizedFile = "",
            int requestedLine = 0,
            MethodBase logicalOwnerOverride = null)
        {
            Debug.Assert(!string.IsNullOrEmpty(id), "id must not be null or empty.");
            Debug.Assert(resolution != null, "resolution must not be null.");

            SourcePausePointPatchResult resolveResult = TryResolveMethod(resolution, out MethodBase method);
            if (!resolveResult.Success)
            {
                return resolveResult;
            }

            SourcePausePointPatchResult patchabilityResult = CheckPatchable(method);
            if (!patchabilityResult.Success)
            {
                return patchabilityResult;
            }

            bool patchedByHotReload =
                HotReloadPausePointCoordination.GetActiveShimForMethod?.Invoke(method) != null;
            if (patchedByHotReload)
            {
                string typeName = method.DeclaringType != null ? method.DeclaringType.Name : "?";
                return SourcePausePointPatchResult.Failure(
                    SourcePausePointPatchFailureReason.MethodPatchedByHotReload,
                    $"'{typeName}.{method.Name}' is currently hot-reload patched and line {requestedLine} "
                    + "does not fall inside any hot-reload patched method's current body, so the marker "
                    + "cannot be placed reliably. Either the compiled line map for this file is stale, "
                    + "or the method's active patch belongs to a superseded hot-reload generation.",
                    "Pick a line inside the edited method body, run 'uloop hot-reload --revert-all' to "
                    + "restore compiled bodies, or run 'uloop compile' to realign line numbers.");
            }

            MethodBase logicalOwner = logicalOwnerOverride ?? method;

            // Why conditional no-op: ShouldInject can leave a prior injection inert (e.g. stale
            // OriginalBody under an active shim). Re-enable must replace mismatched ledger state
            // instead of reporting success while the call site never fires.
            if (TryReuseExistingPatch(
                    id,
                    SourcePausePointPatchInjectionTargetKind.OriginalBody,
                    method,
                    donorShim: null))
            {
                RememberRequest(id, normalizedFile, requestedLine);
                LogicalOwnerById[id] = logicalOwner;
                return SourcePausePointPatchResult.SuccessResult();
            }

            SourcePausePointPatchInjection injection = new(
                id,
                resolution.InstructionIndex,
                resolution.IsStatic,
                resolution.IsDeclaringTypeValueType,
                resolution.Parameters,
                resolution.Locals,
                SourcePausePointPatchInjectionTargetKind.OriginalBody);

            return CommitPatch(id, method, logicalOwner, injection, normalizedFile, requestedLine);
        }

        /// <summary>
        /// Patches a pause point onto a hot-reload shim target (transplant chain-join or
        /// shim-direct) without AppDomain name/MVID resolution.
        /// </summary>
        public static SourcePausePointPatchResult PatchShimTarget(
            string id,
            SourcePausePointShimResolution shim,
            string normalizedFile,
            int requestedLine)
        {
            Debug.Assert(!string.IsNullOrEmpty(id), "id must not be null or empty.");
            Debug.Assert(shim != null, "shim must not be null.");
            Debug.Assert(
                shim.Kind == SourcePausePointShimResolveKind.TransplantChainJoin
                || shim.Kind == SourcePausePointShimResolveKind.ShimDirect,
                "PatchShimTarget requires a successful shim resolution.");

            // Why skip TryResolveMethod: shim assemblies are all named HotReloadShim across
            // generations, so name+MVID lookup cannot identify the loaded generation; the
            // resolver already handed us the MethodBase from that generation's LoadedAssembly.
            MethodBase method = shim.TargetMethod;
            SourcePausePointPatchResult patchabilityResult = CheckPatchable(method);
            if (!patchabilityResult.Success)
            {
                return patchabilityResult;
            }

            SourcePausePointPatchInjectionTargetKind targetKind =
                shim.Kind == SourcePausePointShimResolveKind.TransplantChainJoin
                    ? SourcePausePointPatchInjectionTargetKind.TransplantChainJoin
                    : SourcePausePointPatchInjectionTargetKind.ShimDirect;

            if (TryReuseExistingPatch(id, targetKind, method, shim.DonorShim))
            {
                RememberRequest(id, normalizedFile, requestedLine);
                return SourcePausePointPatchResult.SuccessResult();
            }

            bool isStatic = !shim.InstanceFromFirstArgument && method.IsStatic;
            // Why physical DeclaringType: shim compiles with -optimize+, so async MoveNext state
            // machines are structs even when the user type is a class. Boxing decisions must
            // follow the method we actually patch, not the logical owner.
            bool isDeclaringTypeValueType =
                method.DeclaringType != null && method.DeclaringType.IsValueType;

            SourcePausePointPatchInjection injection = new(
                id,
                shim.InstructionIndex,
                isStatic,
                isDeclaringTypeValueType,
                shim.Parameters,
                shim.Locals,
                targetKind,
                shim.DonorShim,
                shim.InstanceFromFirstArgument);

            return CommitPatch(id, method, shim.LogicalOwner, injection, normalizedFile, requestedLine);
        }

        private static bool TryReuseExistingPatch(
            string id,
            SourcePausePointPatchInjectionTargetKind targetKind,
            MethodBase physicalTarget,
            MethodBase donorShim)
        {
            if (!MethodById.TryGetValue(id, out MethodBase existingPhysical)
                || !InjectionsByMethod.TryGetValue(existingPhysical, out List<SourcePausePointPatchInjection> injections))
            {
                return false;
            }

            SourcePausePointPatchInjection existing = FindInjectionById(injections, id);
            if (existing == null)
            {
                return false;
            }

            bool sameKind = existing.TargetKind == targetKind;
            bool sameTarget = existingPhysical.Equals(physicalTarget);
            bool sameDonor = targetKind != SourcePausePointPatchInjectionTargetKind.TransplantChainJoin
                || (existing.DonorShim != null && donorShim != null && existing.DonorShim.Equals(donorShim));
            if (sameKind && sameTarget && sameDonor)
            {
                return true;
            }

            UnpatchInstrumentationOnly(id);
            return false;
        }

        private static SourcePausePointPatchInjection FindInjectionById(
            List<SourcePausePointPatchInjection> injections,
            string id)
        {
            for (int index = 0; index < injections.Count; index++)
            {
                if (injections[index].Id == id)
                {
                    return injections[index];
                }
            }

            return null;
        }

        private static void RememberRequest(string id, string normalizedFile, int requestedLine)
        {
            RequestById[id] = (normalizedFile, requestedLine);
        }

        private static SourcePausePointPatchResult CommitPatch(
            string id,
            MethodBase method,
            MethodBase logicalOwner,
            SourcePausePointPatchInjection injection,
            string normalizedFile,
            int requestedLine)
        {
            bool methodAlreadyPatched = InjectionsByMethod.TryGetValue(method, out List<SourcePausePointPatchInjection> injections);
            if (!methodAlreadyPatched)
            {
                injections = new List<SourcePausePointPatchInjection>();
                InjectionsByMethod[method] = injections;
            }

            injections.Add(injection);
            MethodById[id] = method;
            LogicalOwnerById[id] = logicalOwner;
            RememberRequest(id, normalizedFile, requestedLine);

            // The ledger above is written before Harmony actually rebuilds the method. If
            // Unpatch/Patch throws (e.g. a byref-like `this` produces invalid IL at JIT time), a
            // plain rethrow would leave this id's ledger entry claiming success while Harmony
            // never attached the transpiler; a later caller would then see a false "already
            // patched" and silently miss the pause point. try-finally (not catch) restores the
            // ledger to its honest pre-call shape while letting the original exception propagate
            // unchanged, keeping this Fail Fast rather than swallowing the failure.
            bool committed = false;
            try
            {
                if (methodAlreadyPatched)
                {
                    // Harmony only regenerates a method's replacement when Patch/Unpatch is called;
                    // re-declaring the same transpiler risks double-registration, so drop and redo it
                    // to force a clean rebuild from the (untouched) original IL against the new injection set.
                    HarmonyInstance.Unpatch(method, HarmonyPatchType.Transpiler, SourcePausePointConstants.HarmonyId);
                }
                HarmonyInstance.Patch(method, transpiler: new HarmonyMethod(TranspilerMethodInfo));
                committed = true;
            }
            finally
            {
                if (!committed)
                {
                    injections.Remove(injection);
                    if (injections.Count == 0)
                    {
                        InjectionsByMethod.Remove(method);
                    }
                    MethodById.Remove(id);
                    LogicalOwnerById.Remove(id);
                    RequestById.Remove(id);
                }
            }

            (string warning, bool hasPhysicsCallbackWarning) = BuildPatchWarning(method);
            return SourcePausePointPatchResult.SuccessResult(warning, method.DeclaringType, hasPhysicsCallbackWarning);
        }

        // What: removes Harmony instrumentation and patcher ledgers for one id without touching
        // the runtime registry. Used by auto-retarget (keep armed/hit) and by registry clear.
        // Why keep Unpatch as the OnCleared hook name: the registry wires OnCleared = Unpatch.
        public static void Unpatch(string id)
        {
            UnpatchInstrumentationOnly(id);
        }

        public static void UnpatchInstrumentationOnly(string id)
        {
            Debug.Assert(!string.IsNullOrEmpty(id), "id must not be null or empty.");

            // Why before MethodById guard: ReanchorMarkerWithoutInjection leaves MethodById
            // empty while LogicalOwnerById/RequestById still hold the id; clear must scrub
            // those ledgers so a later transition cannot revive a cleared marker's request.
            LogicalOwnerById.Remove(id);
            RequestById.Remove(id);

            if (!MethodById.TryGetValue(id, out MethodBase method))
            {
                return;
            }
            MethodById.Remove(id);

            List<SourcePausePointPatchInjection> injections = InjectionsByMethod[method];
            injections.RemoveAll(injection => injection.Id == id);

            HarmonyInstance.Unpatch(method, HarmonyPatchType.Transpiler, SourcePausePointConstants.HarmonyId);
            if (injections.Count == 0)
            {
                InjectionsByMethod.Remove(method);
                return;
            }

            // If re-patching the remaining injections throws, Harmony is left with no transpiler
            // attached at all, yet the ledger would still claim the other ids are patched. Wipe
            // this method's ledger entries entirely on failure so a later call sees the honest
            // "not patched" state instead of a silently stale success (see the same reasoning in Patch).
            bool committed = false;
            try
            {
                HarmonyInstance.Patch(method, transpiler: new HarmonyMethod(TranspilerMethodInfo));
                committed = true;
            }
            finally
            {
                if (!committed)
                {
                    foreach (string remainingId in injections.Select(remaining => remaining.Id).ToList())
                    {
                        MethodById.Remove(remainingId);
                        LogicalOwnerById.Remove(remainingId);
                        RequestById.Remove(remainingId);
                    }
                    InjectionsByMethod.Remove(method);
                }
            }
        }

        public static void UnpatchAll()
        {
            HarmonyInstance.UnpatchAll(SourcePausePointConstants.HarmonyId);
            InjectionsByMethod.Clear();
            MethodById.Clear();
            LogicalOwnerById.Clear();
            RequestById.Clear();
            PendingExpiredNotRetargetedIds.Clear();
            PendingRetargetLineDriftWarnings.Clear();
        }

        private static SourcePausePointPatchResult TryResolveMethod(SourcePausePointResolution resolution, out MethodBase method)
        {
            method = null;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name != resolution.AssemblyName)
                {
                    continue;
                }

                if (assembly.ManifestModule.ModuleVersionId.ToString() != resolution.Mvid)
                {
                    return SourcePausePointPatchResult.Failure(
                        SourcePausePointPatchFailureReason.StaleAssembly,
                        $"The loaded assembly '{resolution.AssemblyName}' no longer matches the "
                        + "compiled assembly this resolution was taken from.",
                        SourcePausePointConstants.StaleAssemblyHint);
                }

                method = assembly.ManifestModule.ResolveMethod(resolution.MetadataToken);
                return SourcePausePointPatchResult.SuccessResult();
            }

            return SourcePausePointPatchResult.Failure(
                SourcePausePointPatchFailureReason.AssemblyNotLoaded,
                $"Assembly '{resolution.AssemblyName}' is not currently loaded in the AppDomain.",
                SourcePausePointConstants.AssemblyNotLoadedHint);
        }

        private static SourcePausePointPatchResult CheckPatchable(MethodBase method)
        {
            if (method.IsAbstract)
            {
                return SourcePausePointPatchResult.Failure(
                    SourcePausePointPatchFailureReason.UnpatchableAbstract,
                    $"'{method}' is abstract and has no method body to patch.",
                    SourcePausePointConstants.ManualMarkerFallbackHint);
            }

            if (method.GetMethodBody() == null)
            {
                return SourcePausePointPatchResult.Failure(
                    SourcePausePointPatchFailureReason.UnpatchableExtern,
                    $"'{method}' has no IL method body (extern or an internal call) and cannot be patched.",
                    SourcePausePointConstants.ManualMarkerFallbackHint);
            }

            if (method.ContainsGenericParameters)
            {
                return SourcePausePointPatchResult.Failure(
                    SourcePausePointPatchFailureReason.UnpatchableOpenGeneric,
                    $"'{method}' is declared with unresolved generic type parameters and cannot be safely patched.",
                    SourcePausePointConstants.ManualMarkerFallbackHint);
            }

            if (HasBurstCompileAttribute(method) || HasBurstCompileAttribute(method.DeclaringType))
            {
                return SourcePausePointPatchResult.Failure(
                    SourcePausePointPatchFailureReason.UnpatchableBurstCompiled,
                    $"'{method}' (or its declaring type) is marked [BurstCompile] and cannot be patched.",
                    SourcePausePointConstants.ManualMarkerFallbackHint);
            }

            return SourcePausePointPatchResult.SuccessResult();
        }

        private static bool HasBurstCompileAttribute(MemberInfo member)
        {
            foreach (object attribute in member.GetCustomAttributes(inherit: false))
            {
                if (attribute.GetType().FullName == SourcePausePointConstants.BurstCompileAttributeFullName)
                {
                    return true;
                }
            }

            return false;
        }

        private static (string Warning, bool HasPhysicsCallbackWarning) BuildPatchWarning(MethodBase method)
        {
            List<string> warnings = new();
            bool hasPhysicsCallbackWarning = false;

            if (!method.IsStatic && IsByRefLikeType(method.DeclaringType))
            {
                warnings.Add(SourcePausePointConstants.RefStructInstanceNotCapturedWarning);
            }

            if (SourcePausePointPhysicalMessageMethods.IsPhysicalMessageMethod(method.Name) &&
                typeof(MonoBehaviour).IsAssignableFrom(method.DeclaringType))
            {
                warnings.Add(SourcePausePointConstants.PhysicalCallbackMayMissExistingInstanceWarning);
                hasPhysicsCallbackWarning = true;
            }
            else if (SourcePausePointPhysicalCallbackCallSiteScanner.IsCalledFromPhysicalMessageMethod(method))
            {
                warnings.Add(SourcePausePointConstants.PhysicalCallbackIndirectCallMayMissExistingInstanceWarning);
                hasPhysicsCallbackWarning = true;
            }

            if (hasPhysicsCallbackWarning)
            {
                warnings.Add(SourcePausePointConstants.PhysicalCallbackMidSolverValuesWarning);
            }

            if (IsLikelyJitInlined(method))
            {
                warnings.Add(SourcePausePointConstants.SmallMethodInliningRiskWarning);
            }

            return (string.Join(" ", warnings), hasPhysicsCallbackWarning);
        }

        private static bool IsByRefLikeType(Type type)
        {
            foreach (object attribute in type.GetCustomAttributes(inherit: false))
            {
                if (attribute.GetType().FullName == SourcePausePointConstants.IsByRefLikeAttributeFullName)
                {
                    return true;
                }
            }

            return false;
        }

        // Heuristic, not a guarantee: [AggressiveInlining] is only a hint the JIT may ignore, and
        // IL body size alone cannot predict Mono's actual inlining decision (call-site count, caller
        // size, and tiering all matter too). Both false positives (flagged but never inlined) and
        // false negatives (inlined despite exceeding the threshold) are possible; this exists solely
        // to explain a HitCount=0 symptom, not to predict it precisely.
        // Harmony detours the JIT-compiled native code and never rewrites the metadata IL, so
        // measuring after Patch still reads the original method body size.
        private static bool IsLikelyJitInlined(MethodBase method)
        {
            if ((method.GetMethodImplementationFlags() & MethodImplAttributes.AggressiveInlining) != 0)
            {
                return true;
            }

            byte[] ilBytes = method.GetMethodBody()?.GetILAsByteArray();
            return ilBytes != null && ilBytes.Length <= SourcePausePointConstants.SmallMethodInliningRiskThresholdBytes;
        }

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions, ILGenerator generator, MethodBase original)
        {
            List<CodeInstruction> list = new(instructions);
            if (!InjectionsByMethod.TryGetValue(original, out List<SourcePausePointPatchInjection> injections))
            {
                return list;
            }

            MethodBase activeShim =
                HotReloadPausePointCoordination.GetActiveShimForMethod?.Invoke(original);

            foreach (SourcePausePointPatchInjection injection in injections.OrderByDescending(i => i.InstructionIndex))
            {
                if (!ShouldInject(injection, activeShim))
                {
                    continue;
                }

                Label skip = generator.DefineLabel();
                List<CodeInstruction> emitted = BuildInjection(injection, original, skip);

                // The instruction we insert before may be a branch target or a try/catch/finally
                // boundary marker; both must move to the new first instruction so control flow and
                // exception regions still land in the same place, now with our prefix folded in.
                Debug.Assert(
                    injection.InstructionIndex < list.Count,
                    "An injection's index must exist in the current instruction stream.");
                CodeInstruction displaced = list[injection.InstructionIndex];
                emitted[0].labels.AddRange(displaced.labels);
                displaced.labels.Clear();
                emitted[0].blocks.AddRange(displaced.blocks);
                displaced.blocks.Clear();

                // The not-armed guard above skips straight to the original displaced instruction,
                // so it needs its own label there to land on.
                displaced.labels.Add(skip);

                list.InsertRange(injection.InstructionIndex, emitted);
            }

            return list;
        }

        private static bool ShouldInject(SourcePausePointPatchInjection injection, MethodBase activeShim)
        {
            switch (injection.TargetKind)
            {
                case SourcePausePointPatchInjectionTargetKind.OriginalBody:
                    // Pre-hot-reload body indexes are only valid when no shim is active.
                    return activeShim == null;
                case SourcePausePointPatchInjectionTargetKind.TransplantChainJoin:
                    // Inject only while the donor shim that produced these indexes is still active.
                    return activeShim != null && activeShim.Equals(injection.DonorShim);
                case SourcePausePointPatchInjectionTargetKind.ShimDirect:
                    // ShimDirect injections live on the shim-side method's InjectionsByMethod entry;
                    // they never share a ledger key with an original-body method.
                    return true;
                default:
                    return false;
            }
        }

        private static List<CodeInstruction> BuildInjection(SourcePausePointPatchInjection injection, MethodBase method, Label skip)
        {
            // Checking IsArmed before building the parameter/local object array (rather than
            // relying on Capture's own check) keeps the overwhelmingly common not-armed hit
            // allocation-free: the array-build and boxing instructions below never execute unless armed.
            List<CodeInstruction> emitted = new()
            {
                new CodeInstruction(OpCodes.Ldstr, injection.Id),
                new CodeInstruction(OpCodes.Call, IsArmedMethodInfo),
                new CodeInstruction(OpCodes.Brfalse, skip),
                new CodeInstruction(OpCodes.Ldstr, injection.Id),
            };

            AppendInstanceLoad(emitted, injection, method);

            // InstanceFromFirstArgument stores absolute GetParameters() indexes (including the
            // hole left by skipping __uloopInstance at 0), so argOffset stays 0. Ordinary
            // instance methods still need +1 to skip the hidden `this` argument.
            int argOffset = injection.InstanceFromFirstArgument
                ? 0
                : (injection.IsStatic ? 0 : 1);

            // Fully qualify: ToolContracts also defines ParameterInfo (tool schema DTO).
            System.Reflection.ParameterInfo[] runtimeParameters = method.GetParameters();
            AppendNameValueArray(
                emitted,
                injection.Parameters,
                p => CodeInstruction.LoadArgument(p.Index + argOffset, false),
                p => p.IsValueType ? runtimeParameters[p.Index].ParameterType : null,
                p => p.Name);

            AppendLocalsLoad(emitted, injection, method);

            emitted.Add(new CodeInstruction(OpCodes.Call, CaptureMethodInfo));
            return emitted;
        }

        private static void AppendLocalsLoad(
            List<CodeInstruction> emitted,
            SourcePausePointPatchInjection injection,
            MethodBase method)
        {
            if (injection.TargetKind == SourcePausePointPatchInjectionTargetKind.TransplantChainJoin)
            {
                IReadOnlyList<LocalBuilder> transplantLocals =
                    HotReloadPausePointCoordination.GetTransplantLocals?.Invoke(method);
                MethodBody donorBody = injection.DonorShim != null
                    ? injection.DonorShim.GetMethodBody()
                    : null;
                List<SourcePausePointLocalVariable> capturable = new List<SourcePausePointLocalVariable>();
                Dictionary<int, Type> boxTypeBySlot = new Dictionary<int, Type>();
                foreach (SourcePausePointLocalVariable local in injection.Locals)
                {
                    if (transplantLocals == null
                        || local.SlotIndex < 0
                        || local.SlotIndex >= transplantLocals.Count
                        || donorBody == null
                        || local.SlotIndex >= donorBody.LocalVariables.Count)
                    {
                        // Why skip (not fail): a missing LocalBuilder mid-rebuild must not abort
                        // the whole injection; capture the locals that are still addressable.
                        Debug.Assert(
                            false,
                            "Transplant local slot must exist on the donor shim and LocalBuilder list.");
                        continue;
                    }

                    capturable.Add(local);
                    boxTypeBySlot[local.SlotIndex] = local.IsValueType
                        ? donorBody.LocalVariables[local.SlotIndex].LocalType
                        : null;
                }

                AppendNameValueArray(
                    emitted,
                    capturable,
                    l => new CodeInstruction(OpCodes.Ldloc, transplantLocals[l.SlotIndex]),
                    l => boxTypeBySlot[l.SlotIndex],
                    l => l.Name);
                return;
            }

            IList<LocalVariableInfo> runtimeLocals = method.GetMethodBody().LocalVariables;
            foreach (SourcePausePointLocalVariable local in injection.Locals)
            {
                Debug.Assert(
                    runtimeLocals[local.SlotIndex].LocalIndex == local.SlotIndex,
                    "LocalVariableInfo order must match the resolved slot index.");
            }
            AppendNameValueArray(
                emitted,
                injection.Locals,
                l => CodeInstruction.LoadLocal(l.SlotIndex, false),
                l => l.IsValueType ? runtimeLocals[l.SlotIndex].LocalType : null,
                l => l.Name);
        }

        private static void AppendInstanceLoad(List<CodeInstruction> emitted, SourcePausePointPatchInjection injection, MethodBase method)
        {
            if (injection.InstanceFromFirstArgument)
            {
                // Why no box: hot-reload rejects value-type instance methods as UnpatchableValueType,
                // so a delegation shim's __uloopInstance is always a reference-type receiver here.
                emitted.Add(CodeInstruction.LoadArgument(0, false));
                return;
            }

            if (injection.IsStatic)
            {
                emitted.Add(new CodeInstruction(OpCodes.Ldnull));
                return;
            }

            if (injection.IsDeclaringTypeValueType && IsByRefLikeType(method.DeclaringType))
            {
                // A byref-like (ref struct) `this` cannot be boxed at all (illegal IL); degrade to
                // a null instance so locals and parameters can still be captured, rather than
                // rejecting the whole patch over the instance fields alone.
                emitted.Add(new CodeInstruction(OpCodes.Ldnull));
                return;
            }

            emitted.Add(CodeInstruction.LoadArgument(0, false));
            if (injection.IsDeclaringTypeValueType)
            {
                // ldarg.0 on a value-type instance method yields a managed pointer (this is how
                // the CLR always passes "this" for struct instance methods); Capture takes
                // `object`, so it must be dereferenced to the value and boxed explicitly.
                emitted.Add(new CodeInstruction(OpCodes.Ldobj, method.DeclaringType));
                emitted.Add(new CodeInstruction(OpCodes.Box, method.DeclaringType));
            }
        }

        private static void AppendNameValueArray<T>(
            List<CodeInstruction> emitted,
            IReadOnlyList<T> items,
            Func<T, CodeInstruction> loadValue,
            Func<T, Type> boxTypeOrNull,
            Func<T, string> nameOf)
        {
            emitted.Add(new CodeInstruction(OpCodes.Ldc_I4, items.Count * 2));
            emitted.Add(new CodeInstruction(OpCodes.Newarr, typeof(object)));

            for (int i = 0; i < items.Count; i++)
            {
                T item = items[i];

                emitted.Add(new CodeInstruction(OpCodes.Dup));
                emitted.Add(new CodeInstruction(OpCodes.Ldc_I4, i * 2));
                emitted.Add(new CodeInstruction(OpCodes.Ldstr, nameOf(item)));
                emitted.Add(new CodeInstruction(OpCodes.Stelem_Ref));

                emitted.Add(new CodeInstruction(OpCodes.Dup));
                emitted.Add(new CodeInstruction(OpCodes.Ldc_I4, i * 2 + 1));
                emitted.Add(loadValue(item));
                Type boxType = boxTypeOrNull(item);
                if (boxType != null)
                {
                    emitted.Add(new CodeInstruction(OpCodes.Box, boxType));
                }
                emitted.Add(new CodeInstruction(OpCodes.Stelem_Ref));
            }
        }
    }
}
