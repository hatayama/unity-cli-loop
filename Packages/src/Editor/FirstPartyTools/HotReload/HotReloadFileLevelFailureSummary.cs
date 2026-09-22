using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Describes the file-level failures of a run for the top-level apply message.
    /// </summary>
    internal static class HotReloadFileLevelFailureSummary
    {
        private const string FileLevelMethodName = "(file)";

        /// <summary>
        /// Builds the sentence that names the first file-level failure, or none when the run has
        /// no such row.
        /// </summary>
        // Why only the first one: a file-level reason is a full sentence of its own, so listing
        // every row would push the rest of the message out of sight; the count tells the reader
        // that Methods holds more.
        public static bool TryDescribe(IReadOnlyList<HotReloadMethodOutcome> methods, out string sentence)
        {
            sentence = null;
            if (methods == null)
            {
                return false;
            }

            string firstReason = null;
            int failureCount = 0;
            for (int index = 0; index < methods.Count; index++)
            {
                HotReloadMethodOutcome outcome = methods[index];
                if (outcome.Kind != HotReloadMethodOutcomeKind.Failed
                    || outcome.Method != FileLevelMethodName
                    || string.IsNullOrEmpty(outcome.Reason))
                {
                    continue;
                }

                failureCount++;
                firstReason ??= outcome.Reason;
            }

            if (firstReason == null)
            {
                return false;
            }

            string prefix = failureCount > 1
                ? "First of " + failureCount + " file-level failures: "
                : "First file-level failure: ";
            sentence = prefix + EndWithPeriod(FirstLine(firstReason));
            return true;
        }

        // Why only the first line: a refusal reason can carry several lines - a parse error lists
        // every diagnostic, a failed shim compilation adds the compile errors and a hint - and
        // Message is read as one line. The remaining lines stay on the Methods row.
        private static string FirstLine(string reason)
        {
            string[] lines = reason.Split('\n');
            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index].Trim();
                if (line.Length > 0)
                {
                    return line;
                }
            }

            return reason;
        }

        // Why: the reason is free text written by whichever guard refused the file, so the
        // sentence that follows it in Message would otherwise run into it.
        private static string EndWithPeriod(string reason)
        {
            char last = reason[reason.Length - 1];
            if (last == '.' || last == '!' || last == '?')
            {
                return reason;
            }

            return reason + ".";
        }
    }
}
