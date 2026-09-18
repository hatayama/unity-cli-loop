using System;
using System.Collections.Generic;
using System.Reflection;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The sentence the deactivated-added-members warning gains when a deactivated member was a
    /// Unity message a proxy was forwarding, since that message stops running on live instances.
    /// </summary>
    internal static class HotReloadDeactivatedUnityMessageNote
    {
        /// <summary>
        /// The method keys of the members whose shims a proxy forwards to live instances.
        /// </summary>
        internal static HashSet<string> CollectForwardedLabels(IReadOnlyList<HotReloadAddedMemberInfo> members)
        {
            Debug.Assert(members != null, "members must not be null.");
            HashSet<string> labels = new HashSet<string>(StringComparer.Ordinal);
            foreach (HotReloadAddedMemberInfo member in members)
            {
                MethodInfo shim = member.ShimMethod;
                if (shim == null)
                {
                    continue;
                }

                if (HotReloadUnityMessageDetector.Classify(shim, out Type _)
                    != HotReloadUnityMessageDetector.Classification.Forwarded)
                {
                    continue;
                }

                labels.Add(member.MethodKey);
            }

            return labels;
        }

        /// <summary>
        /// The sentence naming the deactivated labels that were forwarded messages, or null when
        /// none of them was.
        /// </summary>
        internal static string Describe(IReadOnlyList<string> deactivatedAddedLabels, HashSet<string> forwardedLabels)
        {
            Debug.Assert(deactivatedAddedLabels != null, "deactivatedAddedLabels must not be null.");
            Debug.Assert(forwardedLabels != null, "forwardedLabels must not be null.");
            List<string> deactivatedMessages = new List<string>();
            foreach (string label in deactivatedAddedLabels)
            {
                if (forwardedLabels.Contains(label))
                {
                    deactivatedMessages.Add(label);
                }
            }

            if (deactivatedMessages.Count == 0)
            {
                return null;
            }

            deactivatedMessages.Sort(string.CompareOrdinal);
            return string.Format(
                HotReloadUnityMessageNotes.DeactivatedForwardedFormat,
                string.Join(", ", deactivatedMessages));
        }
    }
}
