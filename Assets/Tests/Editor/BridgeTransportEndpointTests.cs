using System.IO;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies Bridge Transport Endpoint behavior.
    /// </summary>
    public class BridgeTransportEndpointTests
    {
        [Test]
        public void CanonicalizeProjectRoot_WhenPathIsFilesystemRoot_ShouldPreserveRoot()
        {
            string filesystemRoot = Path.GetPathRoot(Directory.GetCurrentDirectory());

            // Tests that root path canonicalization keeps the filesystem root stable.
            string canonicalProjectRoot = ProjectRootCanonicalizer.Canonicalize(filesystemRoot);

            Assert.That(canonicalProjectRoot, Is.EqualTo(filesystemRoot));
        }
    }
}
