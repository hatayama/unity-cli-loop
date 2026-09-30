using System;
using System.Collections.Generic;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The added members one group's worker output declares, keyed by the wire key its entries use
    /// in calledAddedMethodKeys, so each entry's calls are recorded in the ledger's labels.
    /// </summary>
    internal sealed class HotReloadAddedCalleeIndex
    {
        private readonly Dictionary<string, HotReloadCalledAddedMember> _calleesByWireKey =
            new Dictionary<string, HotReloadCalledAddedMember>(StringComparer.Ordinal);

        internal HotReloadAddedCalleeIndex(TransformWorkerEntryDto[] groupEntries)
        {
            Debug.Assert(groupEntries != null, "groupEntries must not be null.");

            foreach (TransformWorkerEntryDto entry in groupEntries)
            {
                // Why only added entries: an entry that patches an existing method shares the key
                // shape, but a call to it is a call to compiled code, not to an added member.
                if (entry.patchKind != HotReloadConstants.PatchKindAddedMethod)
                {
                    continue;
                }

                _calleesByWireKey[HotReloadMethodKeys.BuildMethodKey(entry)] =
                    new HotReloadCalledAddedMember(
                        HotReloadEntryResolution.FormatEntryLabel(entry),
                        entry.sourceProjectRelativePath);
            }
        }

        /// <summary>
        /// The added members <paramref name="entry"/> calls, or the error naming the first call
        /// this group declares no added member for.
        /// </summary>
        internal (IReadOnlyList<HotReloadCalledAddedMember> Callees, string ErrorMessage) Resolve(
            TransformWorkerEntryDto entry)
        {
            Debug.Assert(entry != null, "entry must not be null.");

            string[] calledKeys = entry.calledAddedMethodKeys ?? Array.Empty<string>();
            List<HotReloadCalledAddedMember> callees = new List<HotReloadCalledAddedMember>(calledKeys.Length);
            foreach (string calledKey in calledKeys)
            {
                if (!_calleesByWireKey.TryGetValue(calledKey ?? string.Empty, out HotReloadCalledAddedMember callee))
                {
                    return (null, "Called added member not found among this reload's added members: " + calledKey);
                }

                callees.Add(callee);
            }

            return (callees, null);
        }
    }
}
