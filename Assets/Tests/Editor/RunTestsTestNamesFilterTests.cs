using System;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests the preconditions of the filter that runs a list of tests by full name.
    /// </summary>
    public sealed class RunTestsTestNamesFilterTests
    {
        /// <summary>
        /// What: ByTestNames rejects a null name list.
        /// </summary>
        [Test]
        public void ByTestNames_WithNullNames_ThrowsArgumentException()
        {
            Assert.That(() => TestExecutionFilter.ByTestNames(null), Throws.InstanceOf<ArgumentException>());
        }

        /// <summary>
        /// What: ByTestNames rejects an empty name list, which would otherwise run every test.
        /// </summary>
        [Test]
        public void ByTestNames_WithNoNames_ThrowsArgumentException()
        {
            Assert.That(
                () => TestExecutionFilter.ByTestNames(Array.Empty<string>()),
                Throws.InstanceOf<ArgumentException>());
        }

        /// <summary>
        /// What: ByTestNames rejects a whitespace-only name in the list.
        /// </summary>
        [Test]
        public void ByTestNames_WithWhitespaceName_ThrowsArgumentException()
        {
            Assert.That(
                () => TestExecutionFilter.ByTestNames(new[] { "Ns.C.M", " " }),
                Throws.InstanceOf<ArgumentException>());
        }
    }
}
