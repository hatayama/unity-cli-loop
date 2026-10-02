using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies the tool execution security gate reads tool metadata from the registry.
    /// </summary>
    public sealed class UnityCliLoopSecurityCheckerTests
    {
        private const string RegisteredToolName = "security-checker-test-tool";

        /// <summary>
        /// Verifies that a registered tool with the default security setting is allowed.
        /// </summary>
        [Test]
        public void IsToolAllowed_WhenToolIsRegisteredWithDefaultSetting_ReturnsTrue()
        {
            UnityCliLoopToolRegistry registry = CreateRegistryWithTool();

            Assert.That(UnityCliLoopSecurityChecker.IsToolAllowed(registry, RegisteredToolName), Is.True);
        }

        /// <summary>
        /// Verifies that the gate fails closed when no registry is available.
        /// </summary>
        [Test]
        public void IsToolAllowed_WhenRegistryIsNull_ReturnsFalse()
        {
            Assert.That(UnityCliLoopSecurityChecker.IsToolAllowed(null, RegisteredToolName), Is.False);
        }

        /// <summary>
        /// Verifies that the gate fails closed for a missing tool name instead of querying the registry.
        /// </summary>
        [Test]
        public void IsToolAllowed_WhenToolNameIsNull_ReturnsFalse()
        {
            UnityCliLoopToolRegistry registry = CreateRegistryWithTool();

            Assert.That(UnityCliLoopSecurityChecker.IsToolAllowed(registry, null), Is.False);
        }

        /// <summary>
        /// Verifies that the gate fails closed for a tool the registry does not know.
        /// </summary>
        [Test]
        public void IsToolAllowed_WhenToolIsNotRegistered_ReturnsFalse()
        {
            UnityCliLoopToolRegistry registry = CreateRegistryWithTool();

            Assert.That(UnityCliLoopSecurityChecker.IsToolAllowed(registry, "missing-tool"), Is.False);
        }

        private static UnityCliLoopToolRegistry CreateRegistryWithTool()
        {
            UnityCliLoopToolRegistry registry = new UnityCliLoopToolRegistry(
                new AlwaysEnabledToolSettingsPort(),
                internalToolNameProvider: null,
                toolDiscovery: null);
            registry.RegisterTool(new SecurityCheckerTestTool());
            return registry;
        }

        [UnityCliLoopTool]
        private sealed class SecurityCheckerTestTool : IUnityCliLoopTool
        {
            public string ToolName => RegisteredToolName;
            public ToolParameterSchema ParameterSchema { get; } = new();

            public Task<UnityCliLoopToolResponse> ExecuteAsync(JToken paramsToken, CancellationToken ct)
            {
                UnityCliLoopToolResponse response = new SecurityCheckerTestResponse();
                return Task.FromResult(response);
            }
        }

        private sealed class SecurityCheckerTestResponse : UnityCliLoopToolResponse
        {
        }
    }
}
