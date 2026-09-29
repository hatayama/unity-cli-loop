using System;
using System.Collections.Generic;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Words the run-level warning for calls that live patches and registered added members still
    /// make into added members no generation registers any more.
    /// </summary>
    /// <remarks>
    /// Why from the state at the end of the run rather than from what the run changed: such a call
    /// is left behind by a caller that did not apply, in this run or an earlier one, and it keeps
    /// running until the caller applies again.
    /// </remarks>
    internal sealed class HotReloadStaleAddedMemberCallers
    {
        /// <summary>
        /// The warning naming each call into a member <paramref name="registeredMembers"/> does not
        /// hold, whose caller's file or member's declaring file is in <paramref name="pathsInRun"/>;
        /// null when there is none.
        /// </summary>
        /// <remarks>
        /// Why only calls with an end in this run: a reload of an unrelated file would otherwise
        /// repeat the warning on every run until the call is gone.
        /// </remarks>
        internal string DescribeOrNull(
            IReadOnlyList<HotReloadAddedMemberCall> calls,
            IReadOnlyList<HotReloadAddedMemberInfo> registeredMembers,
            IEnumerable<string> pathsInRun)
        {
            Debug.Assert(calls != null, "calls must not be null.");
            Debug.Assert(registeredMembers != null, "registeredMembers must not be null.");
            Debug.Assert(pathsInRun != null, "pathsInRun must not be null.");

            HashSet<string> registeredLabels = new HashSet<string>(StringComparer.Ordinal);
            foreach (HotReloadAddedMemberInfo member in registeredMembers)
            {
                registeredLabels.Add(member.MethodKey);
            }

            HashSet<string> touchedPaths = new HashSet<string>(
                pathsInRun,
                HotReloadSourcePathNormalizer.ProjectRelativePathComparer());
            SortedSet<string> staleCalls = new SortedSet<string>(StringComparer.Ordinal);
            foreach (HotReloadAddedMemberCall call in calls)
            {
                if (registeredLabels.Contains(call.Callee.Label))
                {
                    continue;
                }

                if (!touchedPaths.Contains(call.CallerFilePath) && !touchedPaths.Contains(call.Callee.DeclaringFilePath))
                {
                    continue;
                }

                staleCalls.Add(call.CallerLabel + " calls " + call.Callee.Label);
            }

            if (staleCalls.Count == 0)
            {
                return null;
            }

            return string.Format(
                HotReloadConstants.StaleAddedMemberCallsWarningFormat,
                string.Join(", ", staleCalls));
        }
    }
}
