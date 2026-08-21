using System;
using System.Collections.Generic;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Outcome of listing tests in a TestMode with no filter applied.
    /// </summary>
    internal sealed class RunTestsUnfilteredTestListResult
    {
        public bool Retrieved { get; }

        public IReadOnlyList<string> FullNames { get; }

        private RunTestsUnfilteredTestListResult(bool retrieved, IReadOnlyList<string> fullNames)
        {
            Retrieved = retrieved;
            FullNames = fullNames;
        }

        public static RunTestsUnfilteredTestListResult NotRetrieved()
        {
            return new RunTestsUnfilteredTestListResult(false, Array.Empty<string>());
        }

        public static RunTestsUnfilteredTestListResult Success(IReadOnlyList<string> fullNames)
        {
            Debug.Assert(fullNames != null, "fullNames must not be null");
            return new RunTestsUnfilteredTestListResult(true, fullNames);
        }
    }
}
