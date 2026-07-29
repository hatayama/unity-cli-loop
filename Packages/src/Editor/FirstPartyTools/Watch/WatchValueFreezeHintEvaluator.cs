using System.Collections.Generic;
using System.Linq;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Detects a watch expression whose recent evaluations returned the same value, so
    /// get-watch-values can explain that watches only refresh on a changed, paused frame -
    /// a value that looks stuck usually means the linked pause point has not been hit again.
    /// </summary>
    internal static class WatchValueFreezeHintEvaluator
    {
        internal const int MinIdenticalEvaluationsForHint = 3;

        private const string FreezeHintMessageFormat =
            "Value has not changed across the last {0} evaluations. Watch values only refresh " +
            "when the Editor is paused on a changed frame, so confirm the linked pause point has " +
            "been hit again if you expect this value to be different (a marker on a conditional " +
            "line freezes after its first hit). A custom ToString() that omits changing fields " +
            "can also make a changing value appear frozen; in that case watch a more specific " +
            "field or property instead.";

        public static string EvaluateFreezeHint(IReadOnlyList<WatchHistoryResponse> history)
        {
            if (history == null || history.Count < MinIdenticalEvaluationsForHint)
            {
                return string.Empty;
            }

            List<WatchHistoryResponse> recent = history
                .Skip(history.Count - MinIdenticalEvaluationsForHint)
                .ToList();
            if (recent.Any(entry => !entry.Success))
            {
                return string.Empty;
            }

            string firstValue = recent[0].Value;
            bool allIdentical = recent.All(entry => entry.Value == firstValue);
            return allIdentical
                ? string.Format(FreezeHintMessageFormat, MinIdenticalEvaluationsForHint)
                : string.Empty;
        }
    }
}
