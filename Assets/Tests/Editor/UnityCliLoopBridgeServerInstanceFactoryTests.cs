using System;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the bridge server factory creates independent, not-yet-started server instances.
    /// </summary>
    public sealed class UnityCliLoopBridgeServerInstanceFactoryTests
    {
        /// <summary>
        /// Verifies each Create call returns a new bridge server that is not running and has no endpoint yet.
        /// </summary>
        [Test]
        public void Create_WhenCalledTwice_ReturnsDistinctStoppedBridgeServers()
        {
            UnityCliLoopBridgeServerInstanceFactory factory = new UnityCliLoopBridgeServerInstanceFactory(
                new NoOpDomainReloadDetectionService(),
                CreateRegistrarService());

            IUnityCliLoopServerInstance first = factory.Create();
            IUnityCliLoopServerInstance second = factory.Create();

            Assert.That(first, Is.InstanceOf<UnityCliLoopBridgeServer>());
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(first.IsRunning, Is.False);
            Assert.That(first.Endpoint, Is.Empty);
        }

        private static UnityCliLoopToolRegistrarService CreateRegistrarService()
        {
            return new UnityCliLoopToolRegistrarService(
                new EmptyInternalToolNameProvider(),
                new AlwaysEnabledToolSettingsPort(),
                new UnityCliLoopToolExecutionService(new NoOpEditorRuntimeStatePort()),
                () => Array.Empty<IUnityCliLoopTool>());
        }

        private sealed class NoOpDomainReloadDetectionService : IDomainReloadDetectionService
        {
            public void RegisterForEditorStartup()
            {
            }

            public void StartDomainReload(string correlationId, bool serverIsRunning)
            {
            }

            public void CompleteDomainReload(string correlationId)
            {
            }

            public void RollbackDomainReloadStart(string correlationId)
            {
            }

            public bool ShouldShowReconnectingUI()
            {
                return false;
            }
        }
    }
}
