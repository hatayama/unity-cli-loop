using System;
using System.ComponentModel;
using System.IO;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Ending one worker channel: a failed kill of a live process surfaces whichever way the quit
    /// failed, a kill that lost the race to the exit is ignored, and the channel is always disposed.
    /// </summary>
    [TestFixture]
    public sealed class TransformWorkerChannelTerminationTests
    {
        /// <summary>
        /// Verifies a live process that refuses the kill after declining to quit is reported, and the channel is still disposed.
        /// </summary>
        [Test]
        public void Terminate_WhenQuitDeclinedAndKillOfLiveProcessFails_ThrowsAndDisposes()
        {
            FakeChannel channel = new FakeChannel
            {
                QuitResult = false,
                KillException = new InvalidOperationException("kill refused"),
                Exited = false,
            };

            InvalidOperationException thrown = Assert.Throws<InvalidOperationException>(
                () => new TransformWorkerChannelTermination().Terminate(channel));

            Assert.That(thrown.Message, Is.EqualTo("kill refused"));
            Assert.That(channel.DisposeCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies a live process that refuses the kill after the pipe broke is reported, and the channel is still disposed.
        /// </summary>
        [Test]
        public void Terminate_WhenPipeBrokenAndKillOfLiveProcessFails_ThrowsAndDisposes()
        {
            FakeChannel channel = new FakeChannel
            {
                QuitException = new IOException("pipe gone"),
                KillException = new InvalidOperationException("kill refused"),
                Exited = false,
            };

            Assert.Throws<InvalidOperationException>(() => new TransformWorkerChannelTermination().Terminate(channel));

            Assert.That(channel.DisposeCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies an OS-reported kill failure of a live process is reported, and the channel is still disposed.
        /// </summary>
        [Test]
        public void Terminate_WhenQuitDeclinedAndOsRefusesKillOfLiveProcess_ThrowsAndDisposes()
        {
            FakeChannel channel = new FakeChannel
            {
                QuitResult = false,
                KillException = new Win32Exception(5),
                Exited = false,
            };

            Assert.Throws<Win32Exception>(() => new TransformWorkerChannelTermination().Terminate(channel));

            Assert.That(channel.DisposeCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies a kill that fails because the process already exited is ignored.
        /// </summary>
        [Test]
        public void Terminate_WhenKillFailsBecauseProcessExited_DoesNotThrowAndDisposes()
        {
            FakeChannel channel = new FakeChannel
            {
                QuitResult = false,
                KillException = new InvalidOperationException("already exited"),
                Exited = true,
            };

            Assert.DoesNotThrow(() => new TransformWorkerChannelTermination().Terminate(channel));

            Assert.That(channel.KillCount, Is.EqualTo(1));
            Assert.That(channel.DisposeCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies a process that exited before the quit command could be written is neither killed nor reported.
        /// </summary>
        [Test]
        public void Terminate_WhenProcessExitedBeforeQuitWrite_DoesNotKillAndDisposes()
        {
            FakeChannel channel = new FakeChannel
            {
                QuitException = new InvalidOperationException("exited"),
            };

            Assert.DoesNotThrow(() => new TransformWorkerChannelTermination().Terminate(channel));

            Assert.That(channel.KillCount, Is.EqualTo(0));
            Assert.That(channel.DisposeCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies a process that quits when asked is not killed.
        /// </summary>
        [Test]
        public void Terminate_WhenProcessQuits_DoesNotKillAndDisposes()
        {
            FakeChannel channel = new FakeChannel { QuitResult = true };

            new TransformWorkerChannelTermination().Terminate(channel);

            Assert.That(channel.KillCount, Is.EqualTo(0));
            Assert.That(channel.DisposeCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Channel whose quit, kill and liveness answers are set by the test.
        /// </summary>
        private sealed class FakeChannel : ITransformWorkerChannel
        {
            public bool QuitResult { get; set; }
            public Exception QuitException { get; set; }
            public Exception KillException { get; set; }
            public bool Exited { get; set; }
            public int KillCount { get; private set; }
            public int DisposeCount { get; private set; }

            public int Id => 1;
            public bool HasExited => Exited;
            public TextWriter RequestWriter => TextWriter.Null;
            public TextReader ResponseReader => TextReader.Null;

            public bool TryQuitGracefully(int waitMilliseconds)
            {
                if (QuitException != null)
                {
                    throw QuitException;
                }

                return QuitResult;
            }

            public void Kill(int waitMilliseconds)
            {
                KillCount++;
                if (KillException != null)
                {
                    throw KillException;
                }
            }

            public string ReadStandardErrorTail()
            {
                return string.Empty;
            }

            public void Dispose()
            {
                DisposeCount++;
            }
        }
    }
}
