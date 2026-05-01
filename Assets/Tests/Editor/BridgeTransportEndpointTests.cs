using System.IO;
using NUnit.Framework;

namespace io.github.hatayama.uLoopMCP
{
    public class BridgeTransportEndpointTests
    {
        [Test]
        public void CanonicalizeProjectRoot_WhenPathIsFilesystemRoot_ShouldPreserveRoot()
        {
            string filesystemRoot = Path.GetPathRoot(Directory.GetCurrentDirectory());

            string canonicalProjectRoot = BridgeTransportEndpoint.CanonicalizeProjectRoot(filesystemRoot);

            Assert.That(canonicalProjectRoot, Is.EqualTo(filesystemRoot));
        }
    }
}
