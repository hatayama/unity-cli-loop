using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Worker-side request loop driven over in-process pipes: frame shape, stdout capture, failure
    /// isolation, malformed input, quit, end of input, and the idle exit firing only between requests.
    /// </summary>
    public class TransformWorkerServeLoopTests
    {
        private const int LongIdleTimeoutMilliseconds = 30_000;
        private const int FrameWaitMilliseconds = 10_000;

        private BlockingLinePipe _requests;
        private BlockingLinePipe _responses;
        private BlockingLineWriter _protocolOutput;

        [SetUp]
        public void SetUp()
        {
            _requests = new BlockingLinePipe();
            _responses = new BlockingLinePipe();
            _protocolOutput = new BlockingLineWriter(_responses);
        }

        [TearDown]
        public void TearDown()
        {
            _requests.Complete();
        }

        /// <summary>
        /// What: a request produces exactly one two-line frame on the protocol writer, and everything
        /// the transform wrote to Console.Out, including a line equal to the result marker, arrives
        /// inside the diagnostics payload instead of on the protocol stream.
        /// </summary>
        [Test]
        public void Run_TransformWritesMarkerLikeNoise_NoiseStaysInsideFrame()
        {
            Task<int> loop = StartLoop((inputPath, outputPath) =>
            {
                Console.WriteLine("noise before");
                Console.WriteLine(TransformWorkerServeProtocol.ResultPrefix + " 0 0");
                Console.WriteLine("noise after");
                return 0;
            }, LongIdleTimeoutMilliseconds);

            _requests.Push(TransformWorkerServeProtocol.EncodeRequestLine("/in.json", "/out.json"));
            (int exitCode, string diagnostics) = ReadFrame();

            Assert.That(exitCode, Is.EqualTo(0));
            Assert.That(diagnostics, Does.Contain("noise before"));
            Assert.That(diagnostics, Does.Contain(TransformWorkerServeProtocol.ResultPrefix + " 0 0"));
            Assert.That(diagnostics, Does.Contain("noise after"));
            Assert.That(_responses.PendingLineCount, Is.EqualTo(0), "Only the two frame lines may reach the protocol stream.");

            _requests.Push(TransformWorkerServeProtocol.QuitCommand);
            Assert.That(loop.Wait(FrameWaitMilliseconds), Is.True);
            Assert.That(loop.Result, Is.EqualTo(0));
        }

        /// <summary>
        /// What: an exception inside the transform becomes a frame with exit code 1 carrying the
        /// exception text, the loop keeps serving, and Console.Out is restored afterwards.
        /// </summary>
        [Test]
        public void Run_TransformThrows_ReportsExitOneAndKeepsServing()
        {
            TextWriter originalOut = Console.Out;
            int calls = 0;
            Task<int> loop = StartLoop((inputPath, outputPath) =>
            {
                calls++;
                if (calls == 1)
                {
                    throw new InvalidOperationException("transform exploded\n" + TransformWorkerServeProtocol.ResultPrefix + " 9 9");
                }

                return 0;
            }, LongIdleTimeoutMilliseconds);

            _requests.Push(TransformWorkerServeProtocol.EncodeRequestLine("/in.json", "/out.json"));
            (int firstExit, string firstDiagnostics) = ReadFrame();
            _requests.Push(TransformWorkerServeProtocol.EncodeRequestLine("/in.json", "/out.json"));
            (int secondExit, string _) = ReadFrame();

            Assert.That(firstExit, Is.EqualTo(1));
            Assert.That(firstDiagnostics, Does.Contain("transform exploded"));
            Assert.That(firstDiagnostics, Does.Contain("InvalidOperationException"));
            Assert.That(secondExit, Is.EqualTo(0));
            Assert.That(Console.Out, Is.SameAs(originalOut), "Console.Out must be restored between requests.");

            _requests.Complete();
            Assert.That(loop.Wait(FrameWaitMilliseconds), Is.True);
        }

        /// <summary>
        /// What: a line that is not a run request is answered with the malformed-request exit code and
        /// does not invoke the transform or end the loop.
        /// </summary>
        [Test]
        public void Run_MalformedRequest_AnswersExitTwoWithoutInvokingTransform()
        {
            int calls = 0;
            Task<int> loop = StartLoop((inputPath, outputPath) =>
            {
                calls++;
                return 0;
            }, LongIdleTimeoutMilliseconds);

            _requests.Push("run garbage");
            (int exitCode, string diagnostics) = ReadFrame();

            Assert.That(exitCode, Is.EqualTo(TransformWorkerServeProtocol.MalformedRequestExitCode));
            Assert.That(diagnostics, Does.Contain("malformed request line"));
            Assert.That(calls, Is.EqualTo(0));

            _requests.Push(TransformWorkerServeProtocol.QuitCommand);
            Assert.That(loop.Wait(FrameWaitMilliseconds), Is.True);
            Assert.That(loop.Result, Is.EqualTo(0));
        }

        /// <summary>
        /// What: end of input (the host closed stdin) ends the loop with exit code 0 and no frame.
        /// </summary>
        [Test]
        public void Run_EndOfInput_ReturnsZeroWithoutFrame()
        {
            Task<int> loop = StartLoop((inputPath, outputPath) => 0, LongIdleTimeoutMilliseconds);

            _requests.Complete();

            Assert.That(loop.Wait(FrameWaitMilliseconds), Is.True);
            Assert.That(loop.Result, Is.EqualTo(0));
            Assert.That(_responses.PendingLineCount, Is.EqualTo(0));
        }

        /// <summary>
        /// What: the idle timeout fires only while waiting for a request. A transform that runs longer
        /// than the idle timeout still completes and answers; the loop then exits once no further
        /// request arrives within the idle window.
        /// </summary>
        [Test]
        public void Run_IdleTimeout_FiresOnlyBetweenRequests()
        {
            const int idleTimeoutMilliseconds = 300;
            Task<int> loop = StartLoop((inputPath, outputPath) =>
            {
                Thread.Sleep(idleTimeoutMilliseconds * 3);
                return 0;
            }, idleTimeoutMilliseconds);

            _requests.Push(TransformWorkerServeProtocol.EncodeRequestLine("/in.json", "/out.json"));
            (int exitCode, string _) = ReadFrame();

            Assert.That(exitCode, Is.EqualTo(0), "The slow transform must be answered, not cut off by the idle timer.");
            Assert.That(loop.Wait(FrameWaitMilliseconds), Is.True, "The loop must exit on its own once idle.");
            Assert.That(loop.Result, Is.EqualTo(0));
            Assert.That(_requests.IsCompleted, Is.False, "The exit must come from the idle timer, not from end of input.");
        }

        /// <summary>
        /// What: null arguments and a non-positive idle timeout are rejected before the loop starts.
        /// </summary>
        [Test]
        public void Run_InvalidArguments_Throw()
        {
            TransformWorkerServeLoop.TransformHandler handler = (inputPath, outputPath) => 0;

            Assert.Throws<ArgumentNullException>(() => TransformWorkerServeLoop.Run(null, _protocolOutput, handler, 1000));
            Assert.Throws<ArgumentNullException>(() => TransformWorkerServeLoop.Run(_requests, null, handler, 1000));
            Assert.Throws<ArgumentNullException>(() => TransformWorkerServeLoop.Run(_requests, _protocolOutput, null, 1000));
            Assert.Throws<ArgumentOutOfRangeException>(() => TransformWorkerServeLoop.Run(_requests, _protocolOutput, handler, 0));
        }

        private Task<int> StartLoop(TransformWorkerServeLoop.TransformHandler handler, int idleTimeoutMilliseconds)
        {
            return Task.Run(() => TransformWorkerServeLoop.Run(_requests, _protocolOutput, handler, idleTimeoutMilliseconds));
        }

        private (int exitCode, string diagnostics) ReadFrame()
        {
            string header = _responses.ReadLineWithin(FrameWaitMilliseconds);
            Assert.That(header, Is.Not.Null, "No response header arrived.");
            Assert.That(TransformWorkerServeProtocol.TryParseResponseHeader(header, out int exitCode, out int byteCount), Is.True, "Bad header: " + header);
            string payload = _responses.ReadLineWithin(FrameWaitMilliseconds);
            Assert.That(payload, Is.Not.Null, "No diagnostics line arrived.");
            Assert.That(TransformWorkerServeProtocol.TryDecodeDiagnostics(payload, byteCount, out string diagnostics), Is.True);
            return (exitCode, diagnostics);
        }
    }
}
