using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies the bind-retry loop of server recovery without waiting in real time.
    /// </summary>
    public sealed class UnityCliLoopServerRecoveryExecutorTests
    {
        private UnityCliLoopSessionFlagsRepository _sessionFlagsRepository;
        private UnityCliLoopEditorSessionStateSnapshot _originalSessionState;
        private List<int> _waitedDelays;
        private IUnityCliLoopServerInstance _bridgeServer;

        [SetUp]
        public void SetUp()
        {
            _sessionFlagsRepository = UnityCliLoopEditorSessionStateTestFactory.CreateSessionFlagsRepository();
            _originalSessionState = UnityCliLoopEditorSessionStateTestFactory.CaptureSnapshot();
            UnityCliLoopEditorSessionStateTestFactory.ClearAll();
            _waitedDelays = new List<int>();
            _bridgeServer = null;
        }

        [TearDown]
        public void TearDown()
        {
            _originalSessionState.Restore();
        }

        /// <summary>
        /// Verifies recovery retries a failed bind through the injected wait, one 250 ms step per failure, and then
        /// marks the server started with the instance that bound.
        /// </summary>
        [Test]
        public async Task StartRecoveryIfNeededAsync_WhenBindFailsTwiceThenSucceeds_RetriesThroughInjectedWait()
        {
            SequencedServerInstanceFactory factory = new SequencedServerInstanceFactory(failuresBeforeSuccess: 2);
            _sessionFlagsRepository.SetIsReconnecting(true);
            _sessionFlagsRepository.SetShowPostCompileReconnectingUI(true);

            await CreateExecutor(factory).StartRecoveryIfNeededAsync(isAfterCompile: false, CancellationToken.None);

            Assert.That(_waitedDelays, Is.EqualTo(new[] { 250, 250 }));
            Assert.That(factory.CreateCount, Is.EqualTo(3));
            Assert.That(_bridgeServer, Is.SameAs(factory.LastCreated));
            Assert.That(_bridgeServer.IsRunning, Is.True);
            Assert.That(_sessionFlagsRepository.GetIsServerRunning(), Is.True);
            Assert.That(_sessionFlagsRepository.GetIsReconnecting(), Is.False);
            Assert.That(_sessionFlagsRepository.GetShowPostCompileReconnectingUI(), Is.False);
        }

        /// <summary>
        /// Verifies recovery that never binds spends the whole 5000 ms budget in 250 ms steps, clears the server
        /// and reconnecting flags, logs an error, and throws.
        /// </summary>
        [Test]
        public void StartRecoveryIfNeededAsync_WhenBindNeverSucceeds_ClearsSessionFlagsAndThrows()
        {
            SequencedServerInstanceFactory factory = new SequencedServerInstanceFactory(failuresBeforeSuccess: int.MaxValue);
            _sessionFlagsRepository.SetIsServerRunning(true);
            _sessionFlagsRepository.SetIsReconnecting(true);
            _sessionFlagsRepository.SetShowReconnectingUI(true);
            UnityCliLoopServerRecoveryExecutor executor = CreateExecutor(factory);
            LogAssert.Expect(LogType.Error, new Regex("could not be bound within 5000ms"));

            InvalidOperationException exception = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await executor.StartRecoveryIfNeededAsync(isAfterCompile: false, CancellationToken.None));

            Assert.That(exception.Message, Does.Contain("could not be bound within 5000ms"));
            Assert.That(_waitedDelays.Count, Is.EqualTo(20));
            Assert.That(_waitedDelays, Is.All.EqualTo(250));
            Assert.That(factory.CreateCount, Is.EqualTo(21));
            Assert.That(_bridgeServer, Is.Null);
            Assert.That(_sessionFlagsRepository.GetIsServerRunning(), Is.False);
            Assert.That(_sessionFlagsRepository.GetIsReconnecting(), Is.False);
            Assert.That(_sessionFlagsRepository.GetShowReconnectingUI(), Is.False);
        }

        private UnityCliLoopServerRecoveryExecutor CreateExecutor(IUnityCliLoopServerInstanceFactory factory)
        {
            UnityCliLoopServerReadinessService readinessService = new UnityCliLoopServerReadinessService(
                new UnityCliLoopServerLifecycleRegistryService(),
                new CompletedReadinessProbe(),
                () => false);
            return new UnityCliLoopServerRecoveryExecutor(
                factory,
                readinessService,
                new UnityCliLoopServerStartupProtectionService(),
                _sessionFlagsRepository,
                UnityCliLoopToolRegistrarTestFactory.Create(() => Array.Empty<IUnityCliLoopTool>()),
                () => _bridgeServer,
                server => _bridgeServer = server,
                (delayMilliseconds, ct) =>
                {
                    _waitedDelays.Add(delayMilliseconds);
                    return Task.CompletedTask;
                });
        }

        /// <summary>
        /// Readiness probe that succeeds at once so recovery reaches its ready state without real I/O.
        /// </summary>
        private sealed class CompletedReadinessProbe : IUnityCliLoopServerReadinessProbe
        {
            public Task ProbeAsync(CancellationToken ct)
            {
                return Task.CompletedTask;
            }
        }

        /// <summary>
        /// Server factory whose first instances fail to bind with an address-in-use error and whose later ones bind.
        /// </summary>
        private sealed class SequencedServerInstanceFactory : IUnityCliLoopServerInstanceFactory
        {
            private readonly int _failuresBeforeSuccess;

            public SequencedServerInstanceFactory(int failuresBeforeSuccess)
            {
                _failuresBeforeSuccess = failuresBeforeSuccess;
            }

            public int CreateCount { get; private set; }

            public FakeServerInstance LastCreated { get; private set; }

            public IUnityCliLoopServerInstance Create()
            {
                CreateCount++;
                LastCreated = new FakeServerInstance(failsToStart: CreateCount <= _failuresBeforeSuccess);
                return LastCreated;
            }
        }

        /// <summary>
        /// Server instance that either binds or throws the way an occupied endpoint does.
        /// </summary>
        private sealed class FakeServerInstance : IUnityCliLoopServerInstance
        {
            private readonly bool _failsToStart;

            public FakeServerInstance(bool failsToStart)
            {
                _failsToStart = failsToStart;
            }

            public bool IsRunning { get; private set; }

            public string Endpoint => "test";

            public void StartServer()
            {
                if (_failsToStart)
                {
                    throw new InvalidOperationException(
                        "bind failed",
                        new SocketException((int)SocketError.AddressAlreadyInUse));
                }

                IsRunning = true;
            }

            public void StopServer()
            {
                IsRunning = false;
            }

            public void Dispose()
            {
                IsRunning = false;
            }
        }
    }
}
