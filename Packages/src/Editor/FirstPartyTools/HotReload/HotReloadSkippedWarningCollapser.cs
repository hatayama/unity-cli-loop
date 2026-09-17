using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Echoes the Skipped method outcomes of a run into Warnings, one line per reason, so a reader
    /// who only checks Warnings still sees that an edit was not applied without reading the same
    /// reason repeated once per method.
    /// </summary>
    internal static class HotReloadSkippedWarningCollapser
    {
        // Measured against a run that skipped 42 methods for one reason: every name in one line
        // made a single warning of about 2,900 characters, which a reader scrolls past rather
        // than reads. Five names say what kind of member was skipped; Methods carries the rest.
        private const int MAX_LISTED_METHOD_NAMES = 5;

        public static void Append(
            List<string> warnings,
            IReadOnlyList<HotReloadMethodOutcome> methods)
        {
            List<string> reasonOrder = new List<string>();
            Dictionary<string, List<string>> methodsByReason = new Dictionary<string, List<string>>();
            for (int index = 0; index < methods.Count; index++)
            {
                HotReloadMethodOutcome outcome = methods[index];
                if (outcome.Kind != HotReloadMethodOutcomeKind.Skipped)
                {
                    continue;
                }

                string reason = outcome.Reason ?? string.Empty;
                if (!methodsByReason.TryGetValue(reason, out List<string> methodNames))
                {
                    methodNames = new List<string>();
                    methodsByReason.Add(reason, methodNames);
                    reasonOrder.Add(reason);
                }

                methodNames.Add(outcome.Method);
            }

            foreach (string reason in reasonOrder)
            {
                List<string> methodNames = methodsByReason[reason];

                // Why a single method keeps the per-method wording: collapsing it would read as a
                // count of one and hide the method name behind a parenthesis.
                warnings.Add(
                    methodNames.Count == 1
                        ? string.Format(HotReloadConstants.SkippedMethodWarningFormat, methodNames[0], reason)
                        : string.Format(
                            HotReloadConstants.SkippedMethodsCollapsedWarningFormat,
                            methodNames.Count,
                            reason,
                            BuildMethodNameList(methodNames)));
            }
        }

        /// <summary>
        /// Names the first few skipped methods and counts the rest, so one warning stays readable
        /// however many methods share a reason.
        /// </summary>
        private static string BuildMethodNameList(List<string> methodNames)
        {
            if (methodNames.Count <= MAX_LISTED_METHOD_NAMES)
            {
                return string.Join(", ", methodNames);
            }

            List<string> listedNames = methodNames.GetRange(0, MAX_LISTED_METHOD_NAMES);
            int remainingCount = methodNames.Count - MAX_LISTED_METHOD_NAMES;
            return string.Join(", ", listedNames)
                + string.Format(HotReloadConstants.SkippedMethodsRemainderFormat, remainingCount);
        }
    }
}
