using System.Globalization;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Formats the no-tests Message suffix that names predefined-assembly test methods.
    /// </summary>
    internal static class RunTestsPredefinedAssemblyTestNoticeFormatter
    {
        internal static string AppendIfNeeded(string message, RunTestsPredefinedAssemblyTestFindings findings)
        {
            Debug.Assert(message != null, "message must not be null");
            Debug.Assert(findings != null, "findings must not be null");

            if (findings.TotalCount == 0)
            {
                return message;
            }

            return message + FormatNotice(findings);
        }

        internal static string FormatNotice(RunTestsPredefinedAssemblyTestFindings findings)
        {
            Debug.Assert(findings != null, "findings must not be null");
            Debug.Assert(findings.TotalCount > 0, "FormatNotice requires at least one finding");

            string listed = string.Join(", ", findings.SampleEntries);
            if (findings.TotalCount > RunTestsConstants.PredefinedAssemblyTestSampleLimit)
            {
                int omittedCount = findings.TotalCount - RunTestsConstants.PredefinedAssemblyTestSampleLimit;
                listed += " (+" + omittedCount.ToString(CultureInfo.InvariantCulture) + " more)";
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                RunTestsConstants.PredefinedAssemblyTestNoticeFormat,
                findings.TotalCount,
                listed);
        }
    }
}
