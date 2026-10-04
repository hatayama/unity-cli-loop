using System;
using System.IO;
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
    /// Verifies the bridge server's start failures, accept-loop exits, unexpected-exit cleanup, and stop path
    /// with a fake listener and a fake accept. No endpoint is bound, the accept loop is awaited directly,
    /// and the fake accept never returns a client, so no client handler or thread pool work is started.
    /// </summary>
    public sealed class UnityCliLoopBridgeServerLoopTests
    {
        /// <summary>
        /// Verifies an address-in-use failure from the listener is reported as an in-use endpoint and leaves the server stopped.
        /// </summary>
        [Test]
        public void StartServer_WhenListenerReportsAddressInUse_ThrowsInUseAndStaysStopped()
        {
            FakeListener listener = new FakeListener
            {
                StartException = new SocketException((int)SocketError.AddressAlreadyInUse)
            };
            UnityCliLoopBridgeServer server = CreateServerWithListener(listener, AcceptNever);

            try
            {
                server.StartServer();
                Assert.Fail("StartServer should report the in-use endpoint.");
            }
            catch (InvalidOperationException e)
            {
                Assert.That(e.Message, Does.StartWith("Project IPC endpoint is already in use: "));
                Assert.That(e.InnerException, Is.SameAs(listener.StartException));
            }

            Assert.That(server.IsRunning, Is.False);
            server.Dispose();
        }

        /// <summary>
        /// Verifies any other listener start failure is rethrown unchanged and leaves the server stopped.
        /// </summary>
        [Test]
        public void StartServer_WhenListenerStartFailsOtherwise_RethrowsAndStaysStopped()
        {
            FakeListener listener = new FakeListener
            {
                StartException = new IOException("bind failed")
            };
            UnityCliLoopBridgeServer server = CreateServerWithListener(listener, AcceptNever);

            try
            {
                server.StartServer();
                Assert.Fail("StartServer should rethrow the listener failure.");
            }
            catch (IOException e)
            {
                Assert.That(e, Is.SameAs(listener.StartException));
            }

            Assert.That(server.IsRunning, Is.False);
            server.Dispose();
        }

        /// <summary>
        /// Verifies StartServer does nothing while the server is already running.
        /// </summary>
        [Test]
        public void StartServer_WhenAlreadyRunning_DoesNotCreateAnotherListener()
        {
            FakeListener listener = new FakeListener();
            int createCount = 0;
            UnityCliLoopBridgeServer server = CreateServer(
                endpoint =>
                {
                    createCount++;
                    return listener;
                },
                AcceptNever);
            server.AttachListenerForTesting(listener);

            server.StartServer();

            Assert.That(createCount, Is.EqualTo(0));
            Assert.That(server.IsRunning, Is.True);
            server.Dispose();
        }

        /// <summary>
        /// Verifies a listener disposed under a running server ends the loop as an unexpected exit:
        /// the listener is stopped, the server is marked stopped, and ServerLoopExited fires once.
        /// </summary>
        [Test]
        public async Task ServerLoopAsync_WhenAcceptReportsDisposedListener_CleansUpAndRaisesLoopExited()
        {
            FakeListener listener = new FakeListener();
            UnityCliLoopBridgeServer server = CreateServerWithListener(
                listener,
                (_, _) => Task.FromException<BridgeClientConnection>(new ObjectDisposedException("listener")));
            int exitedCount = 0;
            server.ServerLoopExited += () => exitedCount++;
            server.AttachListenerForTesting(listener);

            await AwaitLoopWithoutCancellationAsync(server.ServerLoopAsync(CancellationToken.None));

            Assert.That(exitedCount, Is.EqualTo(1));
            Assert.That(listener.StopCount, Is.EqualTo(1));
            Assert.That(server.IsRunning, Is.False);
            Assert.That(server.Endpoint, Is.Empty);
        }

        /// <summary>
        /// Verifies an unexpected accept failure is logged as an error and hands the server to the recovery path
        /// instead of retrying the accept.
        /// </summary>
        [Test]
        public async Task ServerLoopAsync_WhenAcceptFails_LogsErrorAndExitsOnce()
        {
            FakeListener listener = new FakeListener();
            int acceptCount = 0;
            IBridgeTransportListener acceptedListener = null;
            UnityCliLoopBridgeServer server = CreateServerWithListener(
                listener,
                (acceptListener, _) =>
                {
                    acceptCount++;
                    acceptedListener = acceptListener;
                    return Task.FromException<BridgeClientConnection>(new IOException("accept failed"));
                });
            int exitedCount = 0;
            server.ServerLoopExited += () => exitedCount++;
            server.AttachListenerForTesting(listener);
            LogAssert.Expect(LogType.Error, new Regex("Server accept loop failed; restarting the IPC server\\..*accept failed"));

            await AwaitLoopWithoutCancellationAsync(server.ServerLoopAsync(CancellationToken.None));

            Assert.That(acceptCount, Is.EqualTo(1));
            Assert.That(acceptedListener, Is.SameAs(listener));
            Assert.That(exitedCount, Is.EqualTo(1));
            Assert.That(server.IsRunning, Is.False);
        }

        /// <summary>
        /// Verifies an accept failure after the loop token was canceled exits quietly without the error log,
        /// and is still treated as an unexpected exit because the server was not stopped.
        /// </summary>
        [Test]
        public async Task ServerLoopAsync_WhenAcceptFailsAfterCancellation_ExitsWithoutErrorLog()
        {
            FakeListener listener = new FakeListener();
            using CancellationTokenSource loopCancellation = new CancellationTokenSource();
            UnityCliLoopBridgeServer server = CreateServerWithListener(
                listener,
                (_, _) =>
                {
                    loopCancellation.Cancel();
                    return Task.FromException<BridgeClientConnection>(new IOException("accept canceled"));
                });
            int exitedCount = 0;
            server.ServerLoopExited += () => exitedCount++;
            server.AttachListenerForTesting(listener);

            await AwaitLoopWithoutCancellationAsync(server.ServerLoopAsync(loopCancellation.Token));

            LogAssert.NoUnexpectedReceived();
            Assert.That(exitedCount, Is.EqualTo(1));
            Assert.That(server.IsRunning, Is.False);
        }

        /// <summary>
        /// Verifies a canceled accept that returns no client ends the loop through the loop condition.
        /// </summary>
        [Test]
        public async Task ServerLoopAsync_WhenAcceptReturnsNoClientAfterCancellation_ExitsLoop()
        {
            FakeListener listener = new FakeListener();
            using CancellationTokenSource loopCancellation = new CancellationTokenSource();
            int acceptCount = 0;
            UnityCliLoopBridgeServer server = CreateServerWithListener(
                listener,
                (_, _) =>
                {
                    acceptCount++;
                    // Why cancel first: a completed null accept with an uncanceled token would loop forever
                    // on the test thread, because the loop has no other await or yield.
                    loopCancellation.Cancel();
                    return Task.FromResult<BridgeClientConnection>(null);
                });
            int exitedCount = 0;
            server.ServerLoopExited += () => exitedCount++;
            server.AttachListenerForTesting(listener);

            await AwaitLoopWithoutCancellationAsync(server.ServerLoopAsync(loopCancellation.Token));

            Assert.That(acceptCount, Is.EqualTo(1));
            Assert.That(exitedCount, Is.EqualTo(1));
            Assert.That(listener.StopCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies a loop that starts after StopServer neither accepts nor reports an unexpected exit.
        /// </summary>
        [Test]
        public async Task ServerLoopAsync_AfterStopServer_ExitsWithoutAcceptOrLoopExited()
        {
            FakeListener listener = new FakeListener();
            int acceptCount = 0;
            UnityCliLoopBridgeServer server = CreateServerWithListener(
                listener,
                (_, _) =>
                {
                    acceptCount++;
                    return Task.FromException<BridgeClientConnection>(new ObjectDisposedException("listener"));
                });
            int exitedCount = 0;
            server.ServerLoopExited += () => exitedCount++;
            server.AttachListenerForTesting(listener);
            server.StopServer();

            await AwaitLoopWithoutCancellationAsync(server.ServerLoopAsync(CancellationToken.None));

            Assert.That(acceptCount, Is.EqualTo(0));
            Assert.That(exitedCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies StopServer marks the server stopped, stops and releases the listener, and is a no-op when repeated.
        /// </summary>
        [Test]
        public void StopServer_AfterAttach_StopsListenerAndClearsEndpoint()
        {
            FakeListener listener = new FakeListener();
            UnityCliLoopBridgeServer server = CreateServerWithListener(listener, AcceptNever);
            server.AttachListenerForTesting(listener);
            Assert.That(server.Endpoint, Is.Not.Empty);

            server.StopServer();
            server.StopServer();

            Assert.That(server.IsRunning, Is.False);
            Assert.That(server.Endpoint, Is.Empty);
            Assert.That(listener.StopCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies a listener whose Stop throws is still released and the failure reaches the caller.
        /// </summary>
        [Test]
        public void StopServer_WhenListenerStopThrows_ReleasesListenerAndRethrows()
        {
            FakeListener listener = new FakeListener
            {
                StopException = new IOException("stop failed")
            };
            UnityCliLoopBridgeServer server = CreateServerWithListener(listener, AcceptNever);
            server.AttachListenerForTesting(listener);

            try
            {
                server.StopServer();
                Assert.Fail("StopServer should rethrow the listener failure.");
            }
            catch (IOException e)
            {
                Assert.That(e, Is.SameAs(listener.StopException));
            }

            Assert.That(server.IsRunning, Is.False);
            Assert.That(server.Endpoint, Is.Empty);
        }

        /// <summary>
        /// Verifies Dispose stops a running server and releases its listener.
        /// </summary>
        [Test]
        public void Dispose_AfterAttach_StopsServer()
        {
            FakeListener listener = new FakeListener();
            UnityCliLoopBridgeServer server = CreateServerWithListener(listener, AcceptNever);
            server.AttachListenerForTesting(listener);

            server.Dispose();

            Assert.That(server.IsRunning, Is.False);
            Assert.That(server.Endpoint, Is.Empty);
            Assert.That(listener.StopCount, Is.EqualTo(1));
        }

        /// <summary>
        /// What: attaching a second listener to a running server is rejected, so the first listener is still the one Stop releases.
        /// </summary>
        [Test]
        public void AttachListenerForTesting_WhenAlreadyRunning_RejectsAndKeepsFirstListener()
        {
            FakeListener listener = new FakeListener();
            FakeListener replacement = new FakeListener();
            UnityCliLoopBridgeServer server = CreateServerWithListener(listener, AcceptNever);
            server.AttachListenerForTesting(listener);

            try
            {
                server.AttachListenerForTesting(replacement);
                Assert.Fail("Attaching a listener to a running server should be rejected.");
            }
            catch (InvalidOperationException)
            {
            }

            server.StopServer();

            Assert.That(listener.StopCount, Is.EqualTo(1));
            Assert.That(replacement.StopCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Awaits one loop run that must exit normally. Unity Test Framework records an async test that ends
        /// Canceled as passed, so a cancellation leaking out of the loop is turned into a failure here.
        /// </summary>
        private static async Task AwaitLoopWithoutCancellationAsync(Task loop)
        {
            try
            {
                await loop;
            }
            catch (OperationCanceledException)
            {
                Assert.Fail("The server loop leaked a cancellation instead of exiting normally.");
            }
        }

        private static UnityCliLoopBridgeServer CreateServerWithListener(
            FakeListener listener,
            Func<IBridgeTransportListener, CancellationToken, Task<BridgeClientConnection>> acceptClient)
        {
            return CreateServer(endpoint => listener, acceptClient);
        }

        private static UnityCliLoopBridgeServer CreateServer(
            Func<BridgeTransportEndpoint, IBridgeTransportListener> createListener,
            Func<IBridgeTransportListener, CancellationToken, Task<BridgeClientConnection>> acceptClient)
        {
            UnityCliLoopToolRegistrarService registrarService = new UnityCliLoopToolRegistrarService(
                new EmptyInternalToolNameProvider(),
                new AlwaysEnabledToolSettingsPort(),
                new UnityCliLoopToolExecutionService(new NoOpEditorRuntimeStatePort()),
                () => Array.Empty<IUnityCliLoopTool>());
            return new UnityCliLoopBridgeServer(
                new NoOpDomainReloadDetectionService(),
                new JsonRpcRequestProcessor(new UnityCliLoopExecutionRouter(registrarService)),
                new UnityCliLoopBridgeHeartbeatService(),
                new UnityCliLoopBridgeClientDisconnectMonitor(),
                createListener,
                acceptClient);
        }

        private static Task<BridgeClientConnection> AcceptNever(IBridgeTransportListener listener, CancellationToken ct)
        {
            throw new InvalidOperationException("These tests never run the accept loop.");
        }

        /// <summary>
        /// Listener that binds nothing; Start and Stop only record calls or throw the scripted failure.
        /// </summary>
        private sealed class FakeListener : IBridgeTransportListener
        {
            public BridgeTransportEndpoint Endpoint { get; } =
                BridgeTransportEndpoint.CreateProjectIpc(Path.GetTempPath());
            public Exception StartException { get; set; }
            public Exception StopException { get; set; }
            public int StopCount { get; private set; }

            public void Start()
            {
                if (StartException != null)
                {
                    throw StartException;
                }

                throw new InvalidOperationException("A successful Start would start the accept loop on the thread pool.");
            }

            public BridgeClientConnection AcceptClient(CancellationToken ct)
            {
                throw new InvalidOperationException("Accept goes through the injected accept delegate.");
            }

            public void Stop()
            {
                StopCount++;
                if (StopException != null)
                {
                    throw StopException;
                }
            }

            public void Dispose()
            {
            }
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
