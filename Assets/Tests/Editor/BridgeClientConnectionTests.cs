using System;
using System.IO;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    public sealed class BridgeClientConnectionTests
    {
        [Test]
        public void IsConnected_WhenConnectionStateProviderIsDisposed_ShouldReturnFalse()
        {
            using MemoryStream stream = new();
            BridgeClientConnection connection = new(
                "test-endpoint",
                stream,
                () => throw new ObjectDisposedException("test-client"));

            bool isConnected = connection.IsConnected;

            Assert.That(isConnected, Is.False);
        }

        /// <summary>
        /// Verifies disposing the connection disposes the underlying client stream.
        /// </summary>
        [Test]
        public void Dispose_WhenCalled_DisposesClientStream()
        {
            MemoryStream stream = new MemoryStream();
            BridgeClientConnection connection = new("test-endpoint", stream, () => true);

            connection.Dispose();

            Assert.That(stream.CanWrite, Is.False);
            Assert.Throws<ObjectDisposedException>(() => stream.WriteByte(1));
        }
    }
}
