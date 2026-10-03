using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Verifies the execute-dynamic-code response keeps timings added after its list was cleared.
    /// </summary>
    public sealed class ExecuteDynamicCodeResponseTimingTests
    {
        /// <summary>
        /// Verifies a timing added after the list was set to null starts a new list.
        /// </summary>
        [Test]
        public void AddTiming_WhenTimingsWereCleared_StartsANewList()
        {
            ExecuteDynamicCodeResponse response = new ExecuteDynamicCodeResponse { Timings = null };

            response.AddTiming("compile_ms=3");

            Assert.That(response.Timings, Is.EqualTo(new[] { "compile_ms=3" }));
        }
    }
}
