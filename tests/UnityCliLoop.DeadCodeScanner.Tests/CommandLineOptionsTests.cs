using NUnit.Framework;
using UnityCliLoop.DeadCodeScanner;

namespace UnityCliLoop.DeadCodeScanner.Tests
{
    [TestFixture]
    public sealed class CommandLineOptionsTests
    {
        // Verifies that --fail-on high-confidence enables the CI fail flag and --fail-on none leaves it off.
        [Test]
        public void Parse_WhenFailOnIsHighConfidenceOrNone_ShouldSetFailOnHighConfidenceFlag()
        {
            ScanOptions highConfidenceOptions = CommandLineOptions.Parse(new[]
            {
                "--fail-on",
                "high-confidence"
            });
            ScanOptions noneOptions = CommandLineOptions.Parse(new[]
            {
                "--fail-on",
                "none"
            });

            Assert.That(highConfidenceOptions.FailOnHighConfidence, Is.True);
            Assert.That(noneOptions.FailOnHighConfidence, Is.False);
        }
    }
}
