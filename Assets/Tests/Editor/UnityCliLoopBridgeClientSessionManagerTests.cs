using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the bridge client session loop reads Content-Length frames from a client stream, answers requests,
    /// reports corrupted framing, and removes the client when the session ends, by awaiting the handler directly on
    /// an in-memory stream that ends after its scripted bytes. Also covers which exceptions count as a normal
    /// client disconnection.
    /// </summary>
    public sealed class UnityCliLoopBridgeClientSessionManagerTests
    {
        private const string Endpoint = "session-test-endpoint";

        private CountingPauseController _pauseController;

        [SetUp]
        public void SetUp()
        {
            _pauseController = new CountingPauseController();
            UloopPausePointRegistry.ConfigureForTests(_pauseController, () => DateTime.UtcNow);
        }

        [TearDown]
        public void TearDown()
        {
            UloopPausePointRegistry.ResetForTests();
        }

        /// <summary>
        /// Verifies a request frame is extracted from the stream and answered with a Content-Length framed response
        /// that carries the request id.
        /// </summary>
        [Test]
        public async Task HandleClientAsync_WithARequestFrame_WritesAFramedResponse()
        {
            ScriptedDuplexStream stream = new ScriptedDuplexStream(
                Frame("{\"jsonrpc\":\"2.0\",\"method\":\"no-such-tool\",\"id\":7}"));
            UnityCliLoopBridgeClientSessionManager manager = CreateManager();

            await manager.HandleClientAsync(new BridgeClientConnection(Endpoint, stream, () => true), CancellationToken.None);

            string written = stream.WrittenText;
            Assert.That(written, Does.StartWith("Content-Length: "));
            Assert.That(written, Does.Contain("\"id\":7"));
        }

        /// <summary>
        /// Verifies a notification frame is processed without writing any response.
        /// </summary>
        [Test]
        public async Task HandleClientAsync_WithANotificationFrame_WritesNothing()
        {
            ScriptedDuplexStream stream = new ScriptedDuplexStream(
                Frame("{\"jsonrpc\":\"2.0\",\"method\":\"focus-window\"}"));
            UnityCliLoopBridgeClientSessionManager manager = CreateManager();

            await manager.HandleClientAsync(new BridgeClientConnection(Endpoint, stream, () => true), CancellationToken.None);

            Assert.That(stream.WrittenText, Is.Empty);
            Assert.That(stream.ReadCallCount, Is.EqualTo(2));
        }

        /// <summary>
        /// Verifies a frame whose header has no usable Content-Length ends the session with an error log.
        /// </summary>
        [Test]
        public async Task HandleClientAsync_WithCorruptedFraming_LogsTheSessionFailure()
        {
            ScriptedDuplexStream stream = new ScriptedDuplexStream(Encoding.UTF8.GetBytes("Content-Length: abc\r\n\r\n{}"));
            UnityCliLoopBridgeClientSessionManager manager = CreateManager();
            LogAssert.Expect(LogType.Error, new Regex("Client session for " + Endpoint + " failed: .*InvalidOperationException"));

            await manager.HandleClientAsync(new BridgeClientConnection(Endpoint, stream, () => true), CancellationToken.None);

            Assert.That(stream.WrittenText, Is.Empty);
        }

        /// <summary>
        /// Verifies a finished session removes its client, so disconnecting every client afterwards finds none and
        /// does not ask a paused Editor to resume.
        /// </summary>
        [Test]
        public async Task HandleClientAsync_WhenTheSessionEnds_RemovesTheClient()
        {
            _pauseController.Pause();
            ScriptedDuplexStream stream = new ScriptedDuplexStream(Array.Empty<byte>());
            UnityCliLoopBridgeClientSessionManager manager = CreateManager();

            await manager.HandleClientAsync(new BridgeClientConnection(Endpoint, stream, () => true), CancellationToken.None);
            manager.DisconnectAllClients();
            UloopPausePointRegistry.ApplyPendingClientDisconnectResume();

            Assert.That(_pauseController.ResumeCount, Is.EqualTo(0));
            Assert.That(_pauseController.IsPaused, Is.True);
        }

        /// <summary>
        /// Verifies which exceptions are treated as a normal client disconnection, including socket errors wrapped in
        /// an IOException, Windows disconnect HResults, and disconnects found deeper in the inner exceptions.
        /// </summary>
        [Test]
        public void IsNormalDisconnectionException_ClassifiesDisconnectsAndOtherFailures()
        {
            Assert.That(UnityCliLoopBridgeClientSessionManager.IsNormalDisconnectionException(
                new SocketException((int)SocketError.ConnectionReset)), Is.True);
            Assert.That(UnityCliLoopBridgeClientSessionManager.IsNormalDisconnectionException(
                new SocketException((int)SocketError.AccessDenied)), Is.False);
            Assert.That(UnityCliLoopBridgeClientSessionManager.IsNormalDisconnectionException(
                new ObjectDisposedException("stream")), Is.True);
            Assert.That(UnityCliLoopBridgeClientSessionManager.IsNormalDisconnectionException(
                new IOException("wrapped", new SocketException((int)SocketError.Shutdown))), Is.True);
            Assert.That(UnityCliLoopBridgeClientSessionManager.IsNormalDisconnectionException(
                new IOException("wrapped", new SocketException((int)SocketError.TimedOut))), Is.False);
            Assert.That(UnityCliLoopBridgeClientSessionManager.IsNormalDisconnectionException(
                new IOException("netname deleted", unchecked((int)0x80070040))), Is.True);
            Assert.That(UnityCliLoopBridgeClientSessionManager.IsNormalDisconnectionException(
                new IOException("outer", new InvalidOperationException("middle", new ObjectDisposedException("stream")))), Is.True);
            Assert.That(UnityCliLoopBridgeClientSessionManager.IsNormalDisconnectionException(
                new IOException("plain")), Is.False);
            Assert.That(UnityCliLoopBridgeClientSessionManager.IsNormalDisconnectionException(
                new InvalidOperationException("other")), Is.False);
        }

        private static UnityCliLoopBridgeClientSessionManager CreateManager()
        {
            UnityCliLoopToolRegistrarService registrarService = new UnityCliLoopToolRegistrarService(
                new EmptyInternalToolNameProvider(),
                new AllToolsEnabledSettingsPort(),
                new UnityCliLoopToolExecutionService(new IdleEditorRuntimeStatePort()),
                () => Array.Empty<IUnityCliLoopTool>());
            JsonRpcRequestProcessor processor = new JsonRpcRequestProcessor(new UnityCliLoopExecutionRouter(registrarService, new EditorExecutionActivity(new InertProcessActivityApi())));
            return new UnityCliLoopBridgeClientSessionManager(
                processor,
                new UnityCliLoopBridgeHeartbeatService(),
                new UnityCliLoopBridgeClientDisconnectMonitor());
        }

        private static byte[] Frame(string json)
        {
            byte[] body = Encoding.UTF8.GetBytes(json);
            byte[] header = Encoding.ASCII.GetBytes("Content-Length: " + body.Length + "\r\n\r\n");
            byte[] frame = new byte[header.Length + body.Length];
            Buffer.BlockCopy(header, 0, frame, 0, header.Length);
            Buffer.BlockCopy(body, 0, frame, header.Length, body.Length);
            return frame;
        }

        /// <summary>
        /// A client stream that hands out its scripted bytes, then reports the end of the stream, and records what
        /// is written to it. Reads and writes complete synchronously so no thread pool work is started.
        /// </summary>
        private sealed class ScriptedDuplexStream : Stream
        {
            private readonly byte[] _incoming;
            private readonly MemoryStream _written = new MemoryStream();
            private int _readPosition;
            private bool _disposed;

            public ScriptedDuplexStream(byte[] incoming)
            {
                _incoming = incoming;
            }

            public int ReadCallCount { get; private set; }

            public string WrittenText => Encoding.UTF8.GetString(_written.ToArray());

            public override bool CanRead => !_disposed;
            public override bool CanSeek => false;
            public override bool CanWrite => !_disposed;
            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                ReadCallCount++;
                int available = Math.Min(count, _incoming.Length - _readPosition);
                Buffer.BlockCopy(_incoming, _readPosition, buffer, offset, available);
                _readPosition += available;
                return available;
            }

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                return Task.FromResult(Read(buffer, offset, count));
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                _written.Write(buffer, offset, count);
            }

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                Write(buffer, offset, count);
                return Task.CompletedTask;
            }

            public override void Flush()
            {
            }

            public override Task FlushAsync(CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            protected override void Dispose(bool disposing)
            {
                _disposed = true;
                base.Dispose(disposing);
            }
        }

        private sealed class CountingPauseController : IUloopPausePointPauseController
        {
            public bool IsPlaying => true;
            public bool IsPaused { get; private set; }
            public int ResumeCount { get; private set; }

            public void Pause()
            {
                IsPaused = true;
            }

            public void Resume()
            {
                ResumeCount++;
                IsPaused = false;
            }
        }

        private sealed class AllToolsEnabledSettingsPort : IToolSettingsPort
        {
            public bool IsToolEnabled(string toolName)
            {
                return true;
            }

            public void SetToolEnabled(string toolName, bool enabled)
            {
            }

            public string[] GetDisabledTools()
            {
                return Array.Empty<string>();
            }

            public void InvalidateCache()
            {
            }
        }

        private sealed class IdleEditorRuntimeStatePort : IEditorRuntimeStatePort
        {
            public bool IsCompiling => false;
            public bool IsUpdating => false;
            public bool IsPlaying => false;
            public bool IsPaused => false;
        }
    }
}
