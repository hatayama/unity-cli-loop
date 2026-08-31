using System;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Application;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies the server lifecycle registry delivery paths:
    /// ServerStarted/ServerStopping via manual publish, ServerLoopExited via the registered source.
    /// </summary>
    public class UnityCliLoopServerLifecycleRegistryServiceTests
    {
        /// <summary>
        /// Fake lifecycle source that lets tests raise ServerLoopExited on demand.
        /// </summary>
        private sealed class FakeLifecycleSource : IUnityCliLoopServerLifecycleSource
        {
            public event Action ServerLoopExited;

            public void RaiseServerLoopExited()
            {
                ServerLoopExited?.Invoke();
            }

            public bool HasServerLoopExitedSubscribers => ServerLoopExited != null;
        }

        [Test]
        public void PublishServerStarted_WithAddedHandler_InvokesHandler()
        {
            // Verifies that ServerStarted handlers fire through the manual publish path.
            UnityCliLoopServerLifecycleRegistryService registry = new();
            int invocationCount = 0;
            registry.ServerStarted += () => invocationCount++;

            registry.PublishServerStarted();

            Assert.That(invocationCount, Is.EqualTo(1));
        }

        [Test]
        public void PublishServerStarted_AfterHandlerRemoved_DoesNotInvokeHandler()
        {
            // Verifies that removed ServerStarted handlers no longer receive publishes.
            UnityCliLoopServerLifecycleRegistryService registry = new();
            int invocationCount = 0;
            Action handler = () => invocationCount++;
            registry.ServerStarted += handler;
            registry.ServerStarted -= handler;

            registry.PublishServerStarted();

            Assert.That(invocationCount, Is.Zero);
        }

        [Test]
        public void PublishServerStopping_WithAddedHandler_InvokesHandler()
        {
            // Verifies that ServerStopping handlers fire through the manual publish path.
            UnityCliLoopServerLifecycleRegistryService registry = new();
            int invocationCount = 0;
            registry.ServerStopping += () => invocationCount++;

            registry.PublishServerStopping();

            Assert.That(invocationCount, Is.EqualTo(1));
        }

        [Test]
        public void ServerStateChanged_OnStartedAndStoppingPublishes_InvokesHandlerForEach()
        {
            // Verifies that ServerStateChanged aggregates both started and stopping publishes.
            UnityCliLoopServerLifecycleRegistryService registry = new();
            int invocationCount = 0;
            registry.ServerStateChanged += () => invocationCount++;

            registry.PublishServerStarted();
            registry.PublishServerStopping();

            Assert.That(invocationCount, Is.EqualTo(2));
        }

        [Test]
        public void ServerLoopExited_WhenHandlerAddedBeforeRegisterSource_InvokesHandlerOnSourceRaise()
        {
            // Verifies that handlers added before source registration are wired onto the source.
            UnityCliLoopServerLifecycleRegistryService registry = new();
            FakeLifecycleSource source = new();
            int invocationCount = 0;
            registry.ServerLoopExited += () => invocationCount++;

            registry.RegisterSource(source);
            source.RaiseServerLoopExited();

            Assert.That(invocationCount, Is.EqualTo(1));
        }

        [Test]
        public void ServerLoopExited_WhenHandlerAddedAfterRegisterSource_InvokesHandlerOnSourceRaise()
        {
            // Verifies that handlers added after source registration are wired onto the source.
            UnityCliLoopServerLifecycleRegistryService registry = new();
            FakeLifecycleSource source = new();
            registry.RegisterSource(source);
            int invocationCount = 0;

            registry.ServerLoopExited += () => invocationCount++;
            source.RaiseServerLoopExited();

            Assert.That(invocationCount, Is.EqualTo(1));
        }

        [Test]
        public void RegisterSource_WhenReplacingSource_UnwiresHandlersFromOldSource()
        {
            // Verifies that replacing the source moves ServerLoopExited wiring to the new source.
            UnityCliLoopServerLifecycleRegistryService registry = new();
            FakeLifecycleSource oldSource = new();
            FakeLifecycleSource newSource = new();
            int invocationCount = 0;
            registry.ServerLoopExited += () => invocationCount++;
            registry.RegisterSource(oldSource);

            registry.RegisterSource(newSource);

            Assert.That(oldSource.HasServerLoopExitedSubscribers, Is.False);
            oldSource.RaiseServerLoopExited();
            newSource.RaiseServerLoopExited();
            Assert.That(invocationCount, Is.EqualTo(1));
        }
    }
}
