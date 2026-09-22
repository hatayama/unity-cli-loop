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
    /// <remarks>
    /// The owner-file ledger has a second writer despite its name: revert-all records the owner
    /// files of the introduced types it leaves loaded. A revert drops what later reloads added to
    /// those types, and a file that was never compiled is not a changed file, so without that row
    /// an omitted --files run would leave every caller of an added member failing to compile.
    /// </remarks>
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

        // Why apart from the identities: a revert-all may already have recorded an owner row for
        // a type Play entry records again. A cancelled entry takes back only the rows it added,
        // or the revert's row would go and the next omitted --files run would miss that file.
        private static List<string> _pendingSourceIdentitiesRecordedInThisDomain;

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

            ClearLedgers();
            // Why the companions go here but not with revert-all: the compiled assembly now holds
            // what they were given for, while a revert leaves the next reload needing them again.
            HotReloadCompanionSourceSessionStore.Clear();
            GetServices?.Invoke().Domain.CompanionSources.Clear();
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

            RemoveFromLedgers(recoveredIdentities);
        }

        // Why only the owner-file ledger takes the surviving types: the identity ledger reports
        // what Play entry discarded, and the revert discarded none of these types.
        internal static void NotifyRevertAll(IReadOnlyList<HotReloadPlayModeEntryDropSource> survivingIntroducedSources)
        {
            Debug.Assert(survivingIntroducedSources != null, "survivingIntroducedSources must not be null");
            ClearLedgers();
            HotReloadPlayModeEntryDropSourceLedger.Record(survivingIntroducedSources);
        }

        internal static void ResetPendingForTesting()
        {
            _pendingIdentitiesRecordedInThisDomain = null;
            _pendingSourceIdentitiesRecordedInThisDomain = null;
        }

        internal static void NotifyPlayModeStateChanged(
            PlayModeStateChange state,
            IReadOnlyList<string> identities,
            IReadOnlyList<HotReloadPlayModeEntryDropSource> introducedSources,
            bool isDomainReloadDisabledOnEnterPlayMode)
        {
            Debug.Assert(identities != null, "identities must not be null");
            Debug.Assert(introducedSources != null, "introducedSources must not be null");
            DiscardPendingIfSameDomainSurvived();
            if (!ShouldRecord(state, isDomainReloadDisabledOnEnterPlayMode, identities.Count))
            {
                return;
            }

            List<string> newSourceIdentities = ListSourceIdentitiesNotYetRecorded(introducedSources);
            HotReloadPlayModeEntryDropLedger.Record(identities);
            HotReloadPlayModeEntryDropSourceLedger.Record(introducedSources);
            RememberPending(identities, newSourceIdentities);
        }

        private static List<string> ListSourceIdentitiesNotYetRecorded(
            IReadOnlyList<HotReloadPlayModeEntryDropSource> introducedSources)
        {
            HashSet<string> recorded = new HashSet<string>(
                HotReloadPlayModeEntryDropSourceLedger.GetIdentities(),
                StringComparer.Ordinal);
            List<string> notYetRecorded = new List<string>();
            for (int index = 0; index < introducedSources.Count; index++)
            {
                string identity = introducedSources[index].Identity;
                if (!recorded.Contains(identity))
                {
                    notYetRecorded.Add(identity);
                }
            }

            return notYetRecorded;
        }

        // Why both ledgers move together: a source line names the type identity it was recorded
        // under, so a type that leaves the identity ledger must take its owner file along, or an
        // omitted --files run would keep selecting a file whose type is no longer discarded.
        private static void RemoveFromLedgers(IReadOnlyList<string> identities)
        {
            HotReloadPlayModeEntryDropLedger.Remove(identities);
            HotReloadPlayModeEntryDropSourceLedger.Remove(identities);
        }

        private static void ClearLedgers()
        {
            HotReloadPlayModeEntryDropLedger.Clear();
            HotReloadPlayModeEntryDropSourceLedger.Clear();
        }

        private static void HandlePlayModeStateChanged(PlayModeStateChange state)
        {
            NotifyPlayModeStateChanged(
                state,
                CollectActiveIdentities(),
                CollectActiveIntroducedSources(GetServices().Domain),
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
            HotReloadPlayModeEntryDropSourceLedger.Remove(_pendingSourceIdentitiesRecordedInThisDomain);
            _pendingIdentitiesRecordedInThisDomain = null;
            _pendingSourceIdentitiesRecordedInThisDomain = null;
        }

        private static void RememberPending(IReadOnlyList<string> identities, List<string> newSourceIdentities)
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
            _pendingSourceIdentitiesRecordedInThisDomain = newSourceIdentities;
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

        /// <summary>
        /// The owner file of each introduced type the domain holds, keyed by the same identity
        /// CollectActiveIdentities gives that type. Read at Play entry for the types the domain
        /// reload is about to discard, and after revert-all for the types the revert left loaded.
        /// </summary>
        internal static IReadOnlyList<HotReloadPlayModeEntryDropSource> CollectActiveIntroducedSources(
            HotReloadDomain domain)
        {
            Debug.Assert(domain != null, "domain must not be null");
            IReadOnlyList<HotReloadIntroducedTypeDescriptor> introducedTypes =
                domain.IntroducedTypes.DescribeActive();
            List<HotReloadPlayModeEntryDropSource> sources =
                new List<HotReloadPlayModeEntryDropSource>(introducedTypes.Count);
            for (int index = 0; index < introducedTypes.Count; index++)
            {
                HotReloadIntroducedTypeDescriptor descriptor = introducedTypes[index];
                // A type with no owner file has nothing an omitted --files run could select again.
                if (string.IsNullOrEmpty(descriptor.OwnerProjectRelativePath))
                {
                    continue;
                }

                sources.Add(new HotReloadPlayModeEntryDropSource(
                    HotReloadPlayModeEntryDropIdentity.ForType(
                        descriptor.OriginalAssemblyName,
                        descriptor.MetadataName.Value),
                    descriptor.OwnerProjectRelativePath));
            }

            return sources;
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
