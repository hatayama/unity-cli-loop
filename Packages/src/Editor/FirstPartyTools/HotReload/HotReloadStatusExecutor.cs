using System.Collections.Generic;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds --status and --revert-all responses from the live hot-reload ledgers.
    /// </summary>
    internal static class HotReloadStatusExecutor
    {
        public static HotReloadResponse ExecuteRevertAll()
        {
            int clearedCount = HotReloadPatcher.ActiveChangeCount;
            HotReloadPatcher.RevertAll();
            HotReloadPlayModeEntryDropRecorder.NotifyRevertAll();
            return new HotReloadResponse
            {
                Success = true,
                ClearedCount = clearedCount,
                ActivePatchTotal = HotReloadPatcher.ActiveChangeCount,
                Message = clearedCount == 0
                    ? "No active hot-reload changes to revert."
                    : "Reverted all active hot-reload changes."
            };
        }

        public static HotReloadResponse ExecuteStatus()
        {
            IReadOnlyList<HotReloadActivePatchInfo> active = HotReloadPatcher.DescribeActivePatches();
            IReadOnlyList<HotReloadAddedMemberInfo> addedMembers = HotReloadAddedMemberRegistry.Describe();
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
                HotReloadAddedFieldRegistry.DescribeAll();
            AppendAddedFieldStatusRows(methods, addedFields);
            string message = $"{count} change(s) currently active.";
            if (neverInvokedCount > 0)
            {
                message += " " + string.Format(
                    HotReloadConstants.NeverInvokedActiveAggregatedMessageFormat,
                    neverInvokedCount);
            }

            int droppedCount = HotReloadPlayModeEntryDropLedger.Count;
            string dropMessage = HotReloadPlayModeEntryDropStatusMessageBuilder.Build(
                count,
                droppedCount);
            if (dropMessage != null)
            {
                message = dropMessage;
            }

            return new HotReloadResponse
            {
                Success = true,
                Methods = methods,
                ActivePatchTotal = count,
                AddedFieldTotal = addedFields.Count,
                Message = message,
                DroppedByPlayModeEntryCount = droppedCount
            };
        }

        private static string ResolveActiveStatusReason(string methodKey, long invocationCount)
        {
            if (HotReloadSupersededSignatureRegistry.TryGetReplacement(
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

        private static void AppendAddedFieldStatusRows(
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
