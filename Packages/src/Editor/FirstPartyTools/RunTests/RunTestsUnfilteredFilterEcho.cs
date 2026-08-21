using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Copies unfiltered test names onto a NoTestsFound response when a filter was in effect.
    /// </summary>
    internal static class RunTestsUnfilteredFilterEcho
    {
        internal static void ApplyIfRetrieved(
            RunTestsResponse response,
            TestFilterType filterType,
            string filterValue,
            RunTestsUnfilteredTestListResult result)
        {
            Debug.Assert(response != null, "response must not be null");
            Debug.Assert(result != null, "result must not be null");
            if (!response.NoTestsFound || filterType == TestFilterType.all || !result.Retrieved)
            {
                return;
            }

            string echoFilterType = filterType.ToString();
            string echoFilterValue = filterValue ?? string.Empty;
            int totalCount = result.FullNames.Count;
            int listedCount = totalCount;
            if (listedCount > RunTestsConstants.UnfilteredTestNamesLimit)
            {
                listedCount = RunTestsConstants.UnfilteredTestNamesLimit;
            }

            List<string> listed = new List<string>(listedCount);
            for (int index = 0; index < listedCount; index++)
            {
                listed.Add(result.FullNames[index]);
            }

            response.FilterType = echoFilterType;
            response.FilterValue = echoFilterValue;
            response.UnfilteredTestNames = listed;
            response.UnfilteredTestCount = totalCount;
            response.Message = response.Message + string.Format(
                CultureInfo.InvariantCulture,
                RunTestsConstants.NoTestsFoundWithFilterMessageFormat,
                echoFilterType,
                echoFilterValue,
                totalCount);
        }
    }
}
