using Newtonsoft.Json.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.CompositionRoot;
using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    public sealed class UnityCliLoopFirstPartyServerLifecycleBindingTests
    {
        [Test]
        public void CreateExecuteDynamicCodeReadinessRequestJson_IncludesCliVersionMetadata()
        {
            // Tests that the internal readiness probe uses the same CLI metadata contract as native CLI requests.
            string requestJson =
                UnityCliLoopFirstPartyServerLifecycleBinding.CreateExecuteDynamicCodeReadinessRequestJson("return \"ready\";");

            JObject request = JObject.Parse(requestJson);

            Assert.That(request["uloop"]?["cliVersion"]?.ToString(), Is.EqualTo(CliConstants.MINIMUM_REQUIRED_CLI_VERSION));
        }
    }
}
