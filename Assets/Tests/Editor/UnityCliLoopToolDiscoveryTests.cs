using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.CompositionRoot;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies the CompositionRoot tool discovery type-validation predicate.
    /// </summary>
    [TestFixture]
    public sealed class UnityCliLoopToolDiscoveryTests
    {
        [UnityCliLoopTool]
        private sealed class ValidTool : IUnityCliLoopTool
        {
            public string ToolName => "valid-tool";
            public ToolParameterSchema ParameterSchema { get; } = new();

            public Task<UnityCliLoopToolResponse> ExecuteAsync(JToken paramsToken, CancellationToken ct)
            {
                return Task.FromResult<UnityCliLoopToolResponse>(null);
            }
        }

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
        private sealed class NoParameterlessConstructorTool : IUnityCliLoopTool
        {
            public NoParameterlessConstructorTool(string toolName)
            {
                ToolName = toolName;
            }

            public string ToolName { get; }
            public ToolParameterSchema ParameterSchema { get; } = new();

            public Task<UnityCliLoopToolResponse> ExecuteAsync(JToken paramsToken, CancellationToken ct)
            {
                return Task.FromResult<UnityCliLoopToolResponse>(null);
            }
        }

        [UnityCliLoopTool]
        private sealed class NotAToolAttributeTarget
        {
        }

        // Tests that a concrete IUnityCliLoopTool with the attribute and a parameterless constructor is accepted.
        [Test]
        public void IsValidToolType_WhenTypeIsWellFormed_ReturnsTrue()
        {
            Assert.That(UnityCliLoopToolDiscovery.IsValidToolType(typeof(ValidTool)), Is.True);
        }

        // Tests that a type without UnityCliLoopToolAttribute is rejected even if it implements the tool interface.
        [Test]
        public void IsValidToolType_WhenAttributeIsMissing_ReturnsFalse()
        {
            Assert.That(UnityCliLoopToolDiscovery.IsValidToolType(typeof(MissingAttributeTool)), Is.False);
        }

        // Tests that abstract types cannot be instantiated and are therefore rejected.
        [Test]
        public void IsValidToolType_WhenTypeIsAbstract_ReturnsFalse()
        {
            Assert.That(UnityCliLoopToolDiscovery.IsValidToolType(typeof(AbstractTool)), Is.False);
        }

        // Tests that types without a parameterless constructor are rejected since discovery cannot instantiate them.
        [Test]
        public void IsValidToolType_WhenParameterlessConstructorIsMissing_ReturnsFalse()
        {
            Assert.That(UnityCliLoopToolDiscovery.IsValidToolType(typeof(NoParameterlessConstructorTool)), Is.False);
        }

        // Tests that a type not implementing IUnityCliLoopTool is rejected regardless of the attribute.
        [Test]
        public void IsValidToolType_WhenTypeDoesNotImplementToolInterface_ReturnsFalse()
        {
            Assert.That(UnityCliLoopToolDiscovery.IsValidToolType(typeof(NotAToolAttributeTarget)), Is.False);
        }
    }
}
