using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.CompositionRoot;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies the CompositionRoot tool discovery type-validation predicate.
    /// </summary>
    [TestFixture]
    public sealed class UnityCliLoopToolDiscoveryTests
    {
        // These fixtures are safe to compile into the editor: the discovery pre-filter rejects
        // them (missing attribute / abstract / not an IUnityCliLoopTool), so DiscoverTools()
        // never instantiates them or logs a skip-warning for them on domain reload.
        private sealed class MissingAttributeTool : IUnityCliLoopTool
        {
            public string ToolName => "missing-attribute-tool";
            public ToolParameterSchema ParameterSchema { get; } = new();

            public Task<UnityCliLoopToolResponse> ExecuteAsync(JToken paramsToken, CancellationToken ct)
            {
                return Task.FromResult<UnityCliLoopToolResponse>(null);
            }
        }

        [UnityCliLoopTool]
        private abstract class AbstractTool : IUnityCliLoopTool
        {
            public string ToolName => "abstract-tool";
            public ToolParameterSchema ParameterSchema { get; } = new();

            public abstract Task<UnityCliLoopToolResponse> ExecuteAsync(JToken paramsToken, CancellationToken ct);
        }

        [UnityCliLoopTool]
        private sealed class NotAToolAttributeTarget
        {
        }

        [Test]
        public void IsValidToolType_WhenTypeIsWellFormed_ReturnsTrue()
        {
            // Uses a real first-party tool instead of a local fixture: an attribute-tagged,
            // well-formed fixture type here would itself be discovered and registered into the
            // live editor registry by UnityCliLoopToolDiscovery.DiscoverTools().
            Assert.That(UnityCliLoopToolDiscovery.IsValidToolType(typeof(ClearConsoleTool)), Is.True);
        }

        [Test]
        public void IsValidToolType_WhenAttributeIsMissing_ReturnsFalse()
        {
            // Tests that a type without UnityCliLoopToolAttribute is rejected even if it implements the tool interface.
            Assert.That(UnityCliLoopToolDiscovery.IsValidToolType(typeof(MissingAttributeTool)), Is.False);
        }

        [Test]
        public void IsValidToolType_WhenTypeIsAbstract_ReturnsFalse()
        {
            // Tests that abstract types cannot be instantiated and are therefore rejected.
            Assert.That(UnityCliLoopToolDiscovery.IsValidToolType(typeof(AbstractTool)), Is.False);
        }

        [Test]
        public void IsValidToolType_WhenTypeDoesNotImplementToolInterface_ReturnsFalse()
        {
            // Tests that a type not implementing IUnityCliLoopTool is rejected regardless of the attribute.
            Assert.That(UnityCliLoopToolDiscovery.IsValidToolType(typeof(NotAToolAttributeTarget)), Is.False);
        }
    }
}
