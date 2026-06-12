using System;
using System.IO;
using System.Threading;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies the Unix domain socket listener's stop behavior.
    /// </summary>
    public class BridgeTransportListenerTests
    {
        private string _tempProjectRoot;
        private BridgeTransportEndpoint _endpoint;

        [SetUp]
        public void SetUp()
        {
            _tempProjectRoot = Path.Combine(Path.GetTempPath(), "uloop-listener-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempProjectRoot);
            _endpoint = BridgeTransportEndpoint.CreateProjectIpc(_tempProjectRoot);
            if (_endpoint.Kind != BridgeTransportKind.UnixDomainSocket)
            {
                Assert.Ignore("Unix domain socket listener tests only run on macOS/Linux.");
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (_endpoint != null && File.Exists(_endpoint.Path))
            {
                File.Delete(_endpoint.Path);
            }

            if (Directory.Exists(_tempProjectRoot))
            {
                Directory.Delete(_tempProjectRoot, recursive: true);
            }
        }

        [Test]
        public void Stop_WhenCalledTwice_DoesNotThrowAndRemovesSocketFile()
        {
            // Tests that Stop is idempotent: Dispose() also calls Stop, so double stops happen
            // routinely during shutdown and must not throw on the already-closed socket.
            UnixDomainSocketBridgeTransportListener listener = new(_endpoint);
            listener.Start();
            Assert.That(File.Exists(_endpoint.Path), Is.True);

            listener.Stop();
            Assert.DoesNotThrow(() => listener.Stop());
            Assert.That(File.Exists(_endpoint.Path), Is.False);
        }

        [Test]
        public void AcceptClient_AfterStop_ThrowsObjectDisposedException()
        {
            // Tests that accepting on a stopped listener surfaces as disposal, which the server
            // loop treats as an exit-and-recover signal, instead of NullReferenceException
            // which would make the accept loop spin.
            UnixDomainSocketBridgeTransportListener listener = new(_endpoint);
            listener.Start();
            listener.Stop();

            Assert.Throws<ObjectDisposedException>(
                () => listener.AcceptClient(CancellationToken.None));
        }
    }

    /// <summary>
    /// Test fixture that verifies the Windows named pipe listener's stopped-state behavior.
    /// These paths run before any pipe is created, so they are platform-independent.
    /// </summary>
    public class WindowsNamedPipeBridgeTransportListenerTests
    {
        [Test]
        public void Stop_WhenCalledTwice_DoesNotThrow()
        {
            // Tests that Stop is idempotent: Dispose() also calls Stop, so double stops happen
            // routinely during shutdown and only one caller may dispose the active pipe.
            BridgeTransportEndpoint endpoint = BridgeTransportEndpoint.CreateProjectIpc(
                System.IO.Path.GetTempPath());
            WindowsNamedPipeBridgeTransportListener listener = new(endpoint);
            listener.Start();

            listener.Stop();
            Assert.DoesNotThrow(() => listener.Stop());
        }

        [Test]
        public void AcceptClient_AfterStop_ThrowsObjectDisposedException()
        {
            // Tests that a stopped listener refuses to accept instead of creating a fresh
            // pipe nobody can wake, so the server loop exits and recovers.
            BridgeTransportEndpoint endpoint = BridgeTransportEndpoint.CreateProjectIpc(
                System.IO.Path.GetTempPath());
            WindowsNamedPipeBridgeTransportListener listener = new(endpoint);
            listener.Start();
            listener.Stop();

            Assert.Throws<ObjectDisposedException>(
                () => listener.AcceptClient(CancellationToken.None));
        }
    }
}
