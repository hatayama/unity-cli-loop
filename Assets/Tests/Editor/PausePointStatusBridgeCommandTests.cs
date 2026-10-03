using Newtonsoft.Json.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies how the pause point status bridge command classifies request parameters.
    /// </summary>
    public sealed class PausePointStatusBridgeCommandTests
    {
        /// <summary>
        /// Verifies parameters that are not a JSON object carry no marker id and are treated as a list request.
        /// </summary>
        [Test]
        public void IsListRequest_WhenParamsAreNotAnObject_ReturnsTrue()
        {
            Assert.That(PausePointStatusBridgeCommand.IsListRequest(new JArray("marker-id")), Is.True);
            Assert.That(PausePointStatusBridgeCommand.IsListRequest(null), Is.True);
        }
    }
}
