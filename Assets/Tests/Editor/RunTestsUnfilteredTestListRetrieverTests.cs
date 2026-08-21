using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies TestRunnerApi.RetrieveTestList through the production unfiltered-list retriever.
    /// </summary>
    public sealed class RunTestsUnfilteredTestListRetrieverTests
    {
        private const string KnownLeafFullName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.RunTestsUnfilteredFilterEchoTests.ApplyIfRetrieved_WhenFilterMisses_AppendsExactMessageAndEchoFields";

        /// <summary>
        /// What: RetrieveTestList for EditMode returns a catalog that includes a known leaf test full name.
        /// </summary>
        [Test]
        public async Task RetrieveAsync_WhenEditModeCatalogIsAvailable_IncludesKnownLeafFullName()
        {
            await MainThreadSwitcher.SwitchToMainThread(CancellationToken.None);
            RunTestsUnfilteredTestListResult result =
                await RunTestsUnfilteredTestListRetriever.RetrieveAsync(
                    UnityCliLoopTestMode.EditMode,
                    CancellationToken.None);

            Assert.That(result.Retrieved, Is.EqualTo(true));
            bool found = false;
            for (int index = 0; index < result.FullNames.Count; index++)
            {
                if (result.FullNames[index] == KnownLeafFullName)
                {
                    found = true;
                    break;
                }
            }

            Assert.That(found, Is.EqualTo(true));
        }
    }
}
