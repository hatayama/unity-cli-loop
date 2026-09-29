using System;
using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Builds and finds the run-level warning about calls left running into retired added members,
    /// for tests that pin whether a run reports it.
    /// </summary>
    internal static class HotReloadStaleAddedMemberCallsWarnings
    {
        internal static string Expected(params string[] callerCallsMemberPairs)
        {
            return string.Format(
                HotReloadConstants.StaleAddedMemberCallsWarningFormat,
                string.Join(", ", callerCallsMemberPairs));
        }

        internal static string Pair(string callerLabel, string memberLabel)
        {
            return callerLabel + " calls " + memberLabel;
        }

        /// <summary>
        /// Fails when any warning is this one, whatever calls it names; the deactivation filters
        /// other tests use would let it through.
        /// </summary>
        internal static void AssertNone(IReadOnlyList<string> warnings)
        {
            string format = HotReloadConstants.StaleAddedMemberCallsWarningFormat;
            string prefix = format.Substring(0, format.IndexOf("{0}", StringComparison.Ordinal));
            foreach (string warning in warnings)
            {
                Assert.That(warning, Does.Not.StartWith(prefix), string.Join("\n", warnings));
            }
        }
    }
}
