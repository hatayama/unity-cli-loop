using System;
using System.IO;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

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

        /// <summary>
        /// Verifies listener startup leaves an untrusted regular file untouched instead of deleting it.
        /// </summary>
        [Test]
        public void Start_WhenEndpointPathIsRegularFile_FailsWithoutDeletingFile()
        {
            string endpointDirectory = Path.GetDirectoryName(_endpoint.Path);
            UnixEndpointSecurityResult directoryResult = new UnixEndpointSecurityPolicy(new UnixNativeFileSystem())
                .EnsureEndpointDirectory(endpointDirectory);
            Assert.That(directoryResult.Success, Is.True, directoryResult.ErrorMessage);
            File.WriteAllText(_endpoint.Path, "untrusted");
            UnixDomainSocketBridgeTransportListener listener = new(_endpoint);

            Assert.Throws<IOException>(() => listener.Start());
            Assert.That(File.Exists(_endpoint.Path), Is.True);
        }

        /// <summary>
        /// Verifies listener startup uses a 0700 directory and creates a 0600 Unix socket.
        /// </summary>
        [Test]
        public void Start_WhenSuccessful_UsesOwnerOnlyDirectoryAndSocket()
        {
            UnixDomainSocketBridgeTransportListener listener = new(_endpoint);
            listener.Start();
            UnixNativeFileSystem fileSystem = new();

            UnixFileMetadata directoryMetadata = fileSystem.ReadMetadata(
                Path.GetDirectoryName(_endpoint.Path),
                followSymbolicLinks: false);
            UnixFileMetadata socketMetadata = fileSystem.ReadMetadata(
                _endpoint.Path,
                followSymbolicLinks: false);

            Assert.That(directoryMetadata.Kind, Is.EqualTo(UnixFileKind.Directory));
            Assert.That(directoryMetadata.Mode, Is.EqualTo(0x1C0));
            Assert.That(socketMetadata.Kind, Is.EqualTo(UnixFileKind.Socket));
            Assert.That(socketMetadata.Mode, Is.EqualTo(0x180));
            listener.Stop();
        }

        /// <summary>
        /// Verifies a failed socket restriction closes the listener and removes its bound socket file.
        /// </summary>
        [Test]
        public void Start_WhenSocketRestrictionFails_RemovesBoundSocketFileBeforeThrowing()
        {
            UnixEndpointSecurityPolicy securityPolicy = new(new FailingSocketModeFileSystem());
            UnixDomainSocketBridgeTransportListener listener =
                UnixDomainSocketBridgeTransportListener.CreateForTesting(_endpoint, securityPolicy);

            Assert.Throws<IOException>(() => listener.Start());
            Assert.That(File.Exists(_endpoint.Path), Is.False);
        }

        /// <summary>
        /// Verifies teardown warns and preserves an untrusted replacement instead of throwing or deleting it.
        /// </summary>
        [Test]
        public void Stop_WhenSocketWasReplacedWithRegularFile_WarnsAndPreservesFile()
        {
            UnixDomainSocketBridgeTransportListener listener = new(_endpoint);
            listener.Start();
            File.Delete(_endpoint.Path);
            File.WriteAllText(_endpoint.Path, "replacement");
            LogAssert.Expect(
                LogType.Warning,
                new System.Text.RegularExpressions.Regex("Refusing to remove untrusted existing Unix endpoint"));

            Assert.DoesNotThrow(() => listener.Stop());
            Assert.That(File.Exists(_endpoint.Path), Is.True);
        }

        private sealed class FailingSocketModeFileSystem : IUnixNativeFileSystem
        {
            private readonly IUnixNativeFileSystem _inner = new UnixNativeFileSystem();

            public uint GetEffectiveUserId()
            {
                return _inner.GetEffectiveUserId();
            }

            public UnixFileMetadata ReadMetadata(string path, bool followSymbolicLinks)
            {
                return _inner.ReadMetadata(path, followSymbolicLinks);
            }

            public UnixNativeOperationResult CreateDirectory(string path, uint mode)
            {
                return _inner.CreateDirectory(path, mode);
            }

            public UnixNativeOperationResult ChangeMode(string path, uint mode)
            {
                return UnixNativeOperationResult.Failure(5);
            }
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
