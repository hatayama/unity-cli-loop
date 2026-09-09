using System;
using System.Collections.Generic;

using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Records Play-entry domain-reload drops of every kind of hot-reload change (patched
    /// methods, added members, introduced types) and clears them on successful compile,
    /// revert-all, or recovered apply outcomes. Event handlers stay thin; decisions are
    /// tested through the Notify/Should methods.
    /// </summary>
    internal static class HotReloadPlayModeEntryDropRecorder
    {
        private static int _currentCompilationErrorCount;

        /// <summary>
        /// Reads the installed services when Play entry collects what it would drop.
        /// </summary>
        /// <remarks>
        /// Why a provider and not a captured value: the collectors run from Editor callbacks that
        /// take no argument, and a replacement scope installs another domain while those callbacks
        /// stay registered, so a value captured at startup would list the wrong domain's changes.
        /// </remarks>
        internal static Func<HotReloadServices> GetServices { get; set; }

        // Why static: a domain reload wipes this list. The next playModeStateChanged
        // in the same domain therefore means Play entry was cancelled and the just-recorded
        // identities must leave the ledger so live patches are not reported as dropped.
        private static List<string> _pendingIdentitiesRecordedInThisDomain;

        public static void Initialize()
        {
            EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
            CompilationPipeline.compilationStarted -= HandleCompilationStarted;
            CompilationPipeline.compilationStarted += HandleCompilationStarted;
            CompilationPipeline.assemblyCompilationFinished -= HandleAssemblyCompilationFinished;
            CompilationPipeline.assemblyCompilationFinished += HandleAssemblyCompilationFinished;
            CompilationPipeline.compilationFinished -= HandleCompilationFinished;
            CompilationPipeline.compilationFinished += HandleCompilationFinished;
        }

        internal static bool ShouldRecord(
            PlayModeStateChange state,
            bool isDomainReloadDisabledOnEnterPlayMode,
            int activeIdentityCount)
        {
            if (state != PlayModeStateChange.ExitingEditMode)
            {
                return false;
            }

            if (isDomainReloadDisabledOnEnterPlayMode)
            {
                return false;
            }

            return activeIdentityCount > 0;
        }

        internal static bool ShouldClearAfterCompilation(int errorCount)
        {
            return errorCount == 0;
        }

        internal static void NotifyCompilationFinished(int errorCount)
        {
            if (!ShouldClearAfterCompilation(errorCount))
            {
                return;
            }

            HotReloadPlayModeEntryDropLedger.Clear();
        }

        internal static void NotifyApplyRecovered(
            IReadOnlyList<HotReloadMethodOutcome> methods,
            IReadOnlyList<HotReloadIntroducedTypeOutcome> introducedTypes)
        {
            Debug.Assert(methods != null, "methods must not be null");
            Debug.Assert(introducedTypes != null, "introducedTypes must not be null");
            List<string> recoveredIdentities = new List<string>();
            for (int index = 0; index < methods.Count; index++)
            {
                HotReloadMethodOutcome outcome = methods[index];
                if (outcome.Kind != HotReloadMethodOutcomeKind.Patched
                    && outcome.Kind != HotReloadMethodOutcomeKind.Added)
                {
                    continue;
                }

                recoveredIdentities.Add(outcome.Method);
            }

            for (int index = 0; index < introducedTypes.Count; index++)
            {
                HotReloadIntroducedTypeOutcome outcome = introducedTypes[index];
                // Why AlreadyActive counts as recovered too: a type the domain still holds was
                // not discarded, so a ledger row naming it is a stale record of an earlier drop.
                if (outcome.Kind != HotReloadIntroducedTypeOutcomeKind.Introduced
                    && outcome.Kind != HotReloadIntroducedTypeOutcomeKind.AlreadyActive)
                {
                    continue;
                }

                recoveredIdentities.Add(HotReloadPlayModeEntryDropIdentity.ForType(
                    outcome.OriginalAssemblyName,
                    outcome.MetadataName));
            }

            HotReloadPlayModeEntryDropLedger.Remove(recoveredIdentities);
        }

        internal static void NotifyRevertAll()
        {
            HotReloadPlayModeEntryDropLedger.Clear();
        }

        internal static void ResetPendingForTesting()
        {
            _pendingIdentitiesRecordedInThisDomain = null;
        }

        internal static void NotifyPlayModeStateChanged(
            PlayModeStateChange state,
            IReadOnlyList<string> identities,
            bool isDomainReloadDisabledOnEnterPlayMode)
        {
            Debug.Assert(identities != null, "identities must not be null");
            DiscardPendingIfSameDomainSurvived();
            if (!ShouldRecord(state, isDomainReloadDisabledOnEnterPlayMode, identities.Count))
            {
                return;
            }

            HotReloadPlayModeEntryDropLedger.Record(identities);
            RememberPending(identities);
        }

        private static void HandlePlayModeStateChanged(PlayModeStateChange state)
        {
            NotifyPlayModeStateChanged(
                state,
                CollectActiveIdentities(),
                IsDomainReloadDisabledOnEnterPlayMode());
        }

        private static void DiscardPendingIfSameDomainSurvived()
        {
            if (_pendingIdentitiesRecordedInThisDomain == null
                || _pendingIdentitiesRecordedInThisDomain.Count == 0)
            {
                return;
            }

            HotReloadPlayModeEntryDropLedger.Remove(_pendingIdentitiesRecordedInThisDomain);
            _pendingIdentitiesRecordedInThisDomain = null;
        }

        private static void RememberPending(IReadOnlyList<string> identities)
        {
            List<string> pending = new List<string>();
            for (int index = 0; index < identities.Count; index++)
            {
                string identity = identities[index];
                if (string.IsNullOrEmpty(identity))
                {
                    continue;
                }

                pending.Add(identity);
            }

            _pendingIdentitiesRecordedInThisDomain = pending;
        }

        private static void HandleCompilationStarted(object context)
        {
            _currentCompilationErrorCount = 0;
        }

        private static void HandleAssemblyCompilationFinished(
            string assemblyPath,
            CompilerMessage[] compilerMessages)
        {
            if (compilerMessages == null)
            {
                return;
            }

            for (int index = 0; index < compilerMessages.Length; index++)
            {
                if (compilerMessages[index].type == CompilerMessageType.Error)
                {
                    _currentCompilationErrorCount++;
                }
            }
        }

        private static void HandleCompilationFinished(object context)
        {
            NotifyCompilationFinished(_currentCompilationErrorCount);
        }

        /// <summary>
        /// Every hot-reload change the next domain reload would discard, in ledger identity form:
        /// patched methods, added members, and the types this domain introduced.
        /// </summary>
        internal static IReadOnlyList<string> CollectActiveIdentities()
        {
            Debug.Assert(GetServices != null, "GetServices must be set before identities are collected.");
            HotReloadServices services = GetServices();
            IReadOnlyList<HotReloadActivePatchInfo> patches = services.Patcher.DescribeActivePatches();
            IReadOnlyList<HotReloadAddedMemberInfo> addedMembers =
                services.Domain.DescribeAddedMembers();
            List<string> identities = new List<string>(patches.Count + addedMembers.Count);
            for (int index = 0; index < patches.Count; index++)
            {
                identities.Add(patches[index].MethodKey);
            }

            for (int index = 0; index < addedMembers.Count; index++)
            {
                identities.Add(addedMembers[index].MethodKey);
            }

            // Why the types belong here: they live in artifact assemblies only a domain reload
            // unloads, so Play entry discards them exactly as it discards a patch.
            IReadOnlyList<HotReloadIntroducedTypeDescriptor> introducedTypes =
                services.Domain.IntroducedTypes.DescribeActive();
            for (int index = 0; index < introducedTypes.Count; index++)
            {
                HotReloadIntroducedTypeDescriptor descriptor = introducedTypes[index];
                identities.Add(HotReloadPlayModeEntryDropIdentity.ForType(
                    descriptor.OriginalAssemblyName,
                    descriptor.MetadataName.Value));
            }

            return identities;
        }

        private static bool IsDomainReloadDisabledOnEnterPlayMode()
        {
            if (!EditorSettings.enterPlayModeOptionsEnabled)
            {
                return false;
            }

            return (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) != 0;
        }
    }
}
