using System;
using System.Collections.Generic;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Named NUnit test methods compiled into Unity predefined assemblies.
    /// </summary>
    internal sealed class RunTestsPredefinedAssemblyTestFindings
    {
        public int TotalCount { get; }

        public IReadOnlyList<string> SampleEntries { get; }

        private RunTestsPredefinedAssemblyTestFindings(int totalCount, IReadOnlyList<string> sampleEntries)
        {
            TotalCount = totalCount;
            SampleEntries = sampleEntries;
        }

        public static RunTestsPredefinedAssemblyTestFindings None()
        {
            return new RunTestsPredefinedAssemblyTestFindings(0, Array.Empty<string>());
        }

        public static RunTestsPredefinedAssemblyTestFindings Create(
            int totalCount,
            IReadOnlyList<string> sampleEntries)
        {
            Debug.Assert(totalCount >= 0, "totalCount must be >= 0");
            Debug.Assert(sampleEntries != null, "sampleEntries must not be null");
            Debug.Assert(
                sampleEntries.Count <= RunTestsConstants.PredefinedAssemblyTestSampleLimit,
                "sampleEntries must already be capped");
            Debug.Assert(
                totalCount == 0 || sampleEntries.Count > 0,
                "non-zero totalCount must include at least one sample entry");

            return new RunTestsPredefinedAssemblyTestFindings(totalCount, sampleEntries);
        }
    }
}
