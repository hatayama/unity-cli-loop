using System;
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

        // Verifies that --max-public-candidates parses integers and defaults to unlimited (-1).
        [Test]
        public void Parse_WhenMaxPublicCandidatesIsProvidedOrOmitted_ShouldSetLimitOrLeaveUnlimited()
        {
            ScanOptions limitedOptions = CommandLineOptions.Parse(new[]
            {
                "--max-public-candidates",
                "22"
            });
            ScanOptions defaultOptions = CommandLineOptions.Parse(Array.Empty<string>());
            ScanOptions negativeOptions = CommandLineOptions.Parse(new[]
            {
                "--max-public-candidates",
                "-1"
            });

            Assert.That(limitedOptions.MaxPublicCandidates, Is.EqualTo(22));
            Assert.That(defaultOptions.MaxPublicCandidates, Is.EqualTo(-1));
            Assert.That(negativeOptions.MaxPublicCandidates, Is.EqualTo(-1));
        }

        // Verifies that a non-integer --max-public-candidates value is rejected.
        [Test]
        public void Parse_WhenMaxPublicCandidatesIsNotInteger_ShouldThrow()
        {
            Assert.That(
                () => CommandLineOptions.Parse(new[]
                {
                    "--max-public-candidates",
                    "abc"
                }),
                Throws.ArgumentException);
        }
    }
}
