using System.Collections.Generic;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds --status and --revert-all responses from the live hot-reload ledgers.
    /// </summary>
    internal sealed class HotReloadStatusExecutor
    {
        private readonly HotReloadDomain _domain;
        private readonly HotReloadPatcher _patcher;

        internal HotReloadStatusExecutor(HotReloadDomain domain, HotReloadPatcher patcher)
        {
            Debug.Assert(domain != null, "domain must not be null.");
            Debug.Assert(patcher != null, "patcher must not be null.");
            _domain = domain;
            _patcher = patcher;
        }

        public HotReloadResponse ExecuteRevertAll()
        {
            int clearedCount = _patcher.ActiveChangeCount;
            _patcher.RevertAll();
            HotReloadPlayModeEntryDropRecorder.NotifyRevertAll();
            HotReloadAutoRefreshHoldSyncResult hold =
                HotReloadAutoRefreshHold.SyncToActiveChanges();
            List<string> warnings = new List<string>();
            HotReloadAutoRefreshHoldResponseEnricher.AppendDeferredWarning(
                warnings,
                hold.ReleaseDeferred);
            HotReloadAutoRefreshHoldResponseEnricher.AppendSceneRefreshWarning(
                warnings,
                hold.SceneRefreshWarning);
            // Why one snapshot: the total and the sentence that names it must agree, and a second
            // read could answer after another reload activated a type.
            HotReloadActiveChangeSnapshot snapshot = _domain.CountActiveChanges();
            return new HotReloadResponse
            {
                Success = true,
                ClearedCount = clearedCount,
                ActivePatchTotal = snapshot.PatchAndAddedMemberCount,
                AutoRefreshHeld = hold.Held,
                Warnings = warnings,
                // Why the rows too: a total without them names nothing, so a caller told that
                // types stayed loaded could not tell which ones a revert left behind.
                IntroducedTypes = HotReloadIntroducedTypeStatusSection.BuildActiveRows(_domain),
                ActiveIntroducedTypeTotal = snapshot.IntroducedTypeCount,
                Message = HotReloadIntroducedTypeStatusSection.AppendRevertAllNote(
                    clearedCount == 0
                        ? "No active hot-reload changes to revert."
                        : "Reverted all active hot-reload changes.",
                    snapshot.IntroducedTypeCount,
                    hold.Held)
            };
        }

        public HotReloadResponse ExecuteStatus()
        {
            IReadOnlyList<HotReloadActivePatchInfo> active = _patcher.DescribeActivePatches();
            IReadOnlyList<HotReloadAddedMemberInfo> addedMembers =
                _domain.DescribeAddedMembers();
            List<HotReloadMethodResult> methods =
                new List<HotReloadMethodResult>(active.Count + addedMembers.Count);
            int neverInvokedCount = 0;
            for (int index = 0; index < active.Count; index++)
            {
                HotReloadActivePatchInfo patch = active[index];
                long invocationCount = HotReloadInvocationRegistry.GetCount(patch.MethodKey);
                HotReloadMethodResult row = new HotReloadMethodResult
                {
                    Kind = "Active",
                    Method = patch.MethodKey,
                    FilePath = patch.FilePath,
                    InvocationCount = invocationCount
                };
                row.Reason = ResolveActiveStatusReason(patch.MethodKey, invocationCount);
                if (row.Reason == HotReloadConstants.ActivePatchNeverInvokedReason)
                {
                    neverInvokedCount++;
                }

                methods.Add(row);
            }

            for (int index = 0; index < addedMembers.Count; index++)
            {
                HotReloadAddedMemberInfo added = addedMembers[index];
                methods.Add(
                    new HotReloadMethodResult
                    {
                        Kind = HotReloadConstants.AddedMemberStatusKind,
                        Method = added.MethodKey,
                        FilePath = added.FilePath,
                        // Why: --status does not compare source, so the AlreadyActive first
                        // sentence would be a lie after a post-reload edit; only the
                        // not-instrumented fact is always true.
                        Reason = HotReloadConstants.AddedMemberNotInstrumentedReason
                    });
            }

            int count = methods.Count;
            IReadOnlyList<HotReloadAddedFieldDescription> addedFields =
                _domain.DescribeAddedFields();
            AppendAddedFieldStatusRows(methods, addedFields);
            // Why one snapshot for the heading, the drop decision, and the reported total: a
            // domain still holding an introduced type has not lost it, and a caller told three
            // different numbers for "what is active" cannot tell which one answers the question.
            HotReloadActiveChangeSnapshot snapshot = _domain.CountActiveChanges();
            string message = $"{snapshot.RuntimeChangeTotal} change(s) currently active.";
            if (neverInvokedCount > 0)
            {
                message += " " + string.Format(
                    HotReloadConstants.NeverInvokedActiveAggregatedMessageFormat,
                    neverInvokedCount);
            }

            int droppedCount = HotReloadPlayModeEntryDropLedger.Count;
            string dropMessage = HotReloadPlayModeEntryDropStatusMessageBuilder.Build(
                snapshot.RuntimeChangeTotal,
                droppedCount);
            if (dropMessage != null)
            {
                message = dropMessage;
            }

            HotReloadAutoRefreshHoldSyncResult hold =
                HotReloadAutoRefreshHold.SyncToActiveChanges();
            List<string> warnings = new List<string>();
            HotReloadAutoRefreshHoldResponseEnricher.AppendDeferredWarning(
                warnings,
                hold.ReleaseDeferred);
            HotReloadAutoRefreshHoldResponseEnricher.AppendSceneRefreshWarning(
                warnings,
                hold.SceneRefreshWarning);
            return new HotReloadResponse
            {
                Success = true,
                Methods = methods,
                Warnings = warnings,
                IntroducedTypes = HotReloadIntroducedTypeStatusSection.BuildActiveRows(_domain),
                ActiveIntroducedTypeTotal = snapshot.IntroducedTypeCount,
                ActivePatchTotal = count,
                AddedFieldTotal = addedFields.Count,
                AutoRefreshHeld = hold.Held,
                Message = message,
                DroppedByPlayModeEntryCount = droppedCount
            };
        }

        private string ResolveActiveStatusReason(string methodKey, long invocationCount)
        {
            if (_domain.TryGetSupersededReplacement(
                    methodKey,
                    out string replacementDisplayName))
            {
                return string.Format(
                    HotReloadConstants.ActivePatchSupersededReasonFormat,
                    replacementDisplayName);
            }

            if (invocationCount == 0L)
            {
                return HotReloadConstants.ActivePatchNeverInvokedReason;
            }

            return string.Empty;
        }

        private void AppendAddedFieldStatusRows(
            List<HotReloadMethodResult> methods,
            IReadOnlyList<HotReloadAddedFieldDescription> addedFields)
        {
            for (int index = 0; index < addedFields.Count; index++)
            {
                HotReloadAddedFieldDescription field = addedFields[index];
                methods.Add(
                    new HotReloadMethodResult
                    {
                        Kind = HotReloadConstants.AddedFieldKind,
                        Method = field.TypeName + "." + field.FieldName,
                        FilePath = field.ProjectRelativePath,
                        Reason = string.Empty
                    });
            }
        }
    }
}
