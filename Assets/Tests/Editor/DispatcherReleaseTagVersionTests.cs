using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies dispatcher release tag to version extraction.
    /// </summary>
    public class DispatcherReleaseTagVersionTests
    {
        [TestCase("dispatcher-v3.4.0", "3.4.0")]
        [TestCase("dispatcher-v3.0.0-beta.31", "3.0.0-beta.31")]
        public void TryParse_WhenTagHasVersionSuffix_ReturnsVersionWithoutPrefix(
            string dispatcherReleaseTag,
            string expectedVersion)
        {
            // Verifies that a well-formed dispatcher release tag yields the bare semver string.
            bool parsed = DispatcherReleaseTagVersion.TryParse(dispatcherReleaseTag, out string version);

            Assert.That(parsed, Is.True);
            Assert.That(version, Is.EqualTo(expectedVersion));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("v3.4.0")]
        [TestCase("dispatcher-v")]
        public void TryParse_WhenTagIsEmptyOrMalformed_ReturnsFalse(string dispatcherReleaseTag)
        {
            // Verifies that empty, prefix-less, and version-less tags are rejected without producing a version.
            bool parsed = DispatcherReleaseTagVersion.TryParse(dispatcherReleaseTag, out string version);

            Assert.That(parsed, Is.False);
            Assert.That(version, Is.Null);
        }
    }
}
