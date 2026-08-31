using Newtonsoft.Json.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.CompositionRoot;
using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    public sealed class UnityCliLoopFirstPartyServerLifecycleBindingTests
    {
        [Test]
        public void CreateGetVersionReadinessRequestJson_UsesInternalHealthCheckWithCliMetadata()
        {
            // Tests that the internal readiness probe does not depend on user-toggleable tools.
            string requestJson =
                UnityCliLoopFirstPartyServerLifecycleBinding.CreateGetVersionReadinessRequestJson();

            JObject request = JObject.Parse(requestJson);

            Assert.That(request["method"]?.ToString(), Is.EqualTo("get-version"));
            Assert.That(
                request["uloop"]?["protocolVersion"]?.ToObject<int>(),
                Is.EqualTo(CliConstants.REQUIRED_CLI_PROTOCOL_VERSION));
        }
    }
}
