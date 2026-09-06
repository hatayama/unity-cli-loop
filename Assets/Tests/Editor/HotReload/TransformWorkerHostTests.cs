using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Host state machine over a scripted in-process channel: process reuse, broken-conversation
    /// retry and its bound, final worker failures, timeout, cancellation while queued and while in
    /// flight, shutdown during a request, worker-directory change, and bootstrap failure.
    /// </summary>
    public class TransformWorkerHostTests
    {
        private const int DefaultTimeoutMilliseconds = 30_000;
        private const int ShortTimeoutMilliseconds = 300;
        private const int WaitMilliseconds = 15_000;

        private ScriptedChannelFactory _factory;
        private string _workerDirectory;
        private TransformWorkerHost _host;

        [SetUp]
        public void SetUp()
        {
            _factory = new ScriptedChannelFactory();
            _workerDirectory = "/worker/a";
            _host = CreateHost(DefaultTimeoutMilliseconds);
        }

        [TearDown]
        public void TearDown()
        {
            _host.Shutdown("test teardown");
            _factory.DisposeAll();
        }

        /// <summary>
        /// What: two consecutive requests are answered by the same process; only one launch happens.
        /// </summary>
        [Test]
        public async Task RunAsync_TwoRequests_ReuseOneProcess()
        {
            _factory.Enqueue(ScriptStep.Succeed, ScriptStep.Succeed);

            TransformWorkerHostResult first = await _host.RunAsync(CreateInput(2), CancellationToken.None);
            TransformWorkerHostResult second = await _host.RunAsync(CreateInput(1), CancellationToken.None);

            Assert.That(first.Kind, Is.EqualTo(TransformWorkerHostResultKind.Completed), first.ErrorMessage);
            Assert.That(first.Output.files.Length, Is.EqualTo(2));
            Assert.That(second.Kind, Is.EqualTo(TransformWorkerHostResultKind.Completed), second.ErrorMessage);
            Assert.That(second.Output.files.Length, Is.EqualTo(1));
            Assert.That(_host.LaunchCount, Is.EqualTo(1));
            Assert.That(_factory.Channels[0].RequestCount, Is.EqualTo(2));
        }

        /// <summary>
        /// What: a process that dies without answering is discarded and the request is retried once on
        /// a fresh process, which succeeds.
        /// </summary>
        [Test]
        public async Task RunAsync_FirstProcessCrashes_RetriesOnceOnFreshProcess()
        {
            _factory.Enqueue(ScriptStep.Crash);
            _factory.Enqueue(ScriptStep.Succeed);

            TransformWorkerHostResult result = await _host.RunAsync(CreateInput(1), CancellationToken.None);

            Assert.That(result.Kind, Is.EqualTo(TransformWorkerHostResultKind.Completed), result.ErrorMessage);
            Assert.That(_host.LaunchCount, Is.EqualTo(2));
            Assert.That(_factory.Channels[0].HasExited, Is.True);
        }

        /// <summary>
        /// What: two consecutive broken conversations end the request as RetryExhausted; no third
        /// process is started and the failure names the last reason.
        /// </summary>
        [Test]
        public async Task RunAsync_TwoBrokenConversations_ReportsRetryExhaustedWithoutThirdLaunch()
        {
            _factory.Enqueue(ScriptStep.Garbage);
            _factory.Enqueue(ScriptStep.Crash);

            TransformWorkerHostResult result = await _host.RunAsync(CreateInput(1), CancellationToken.None);

            Assert.That(result.Kind, Is.EqualTo(TransformWorkerHostResultKind.RetryExhausted));
            Assert.That(result.ErrorMessage, Does.Contain("closed its output"));
            Assert.That(_host.LaunchCount, Is.EqualTo(2));
            Assert.That(_host.CurrentProcessId, Is.Null);
        }

        /// <summary>
        /// What: a non-zero exit code is a final WorkerFailed result carrying the worker's diagnostics
        /// and stderr tail; the process is kept and answers the next request.
        /// </summary>
        [Test]
        public async Task RunAsync_WorkerReportsFailure_IsFinalAndKeepsProcess()
        {
            _factory.Enqueue(ScriptStep.Fail, ScriptStep.Succeed);

            TransformWorkerHostResult failed = await _host.RunAsync(CreateInput(1), CancellationToken.None);
            TransformWorkerHostResult next = await _host.RunAsync(CreateInput(1), CancellationToken.None);

            Assert.That(failed.Kind, Is.EqualTo(TransformWorkerHostResultKind.WorkerFailed));
            Assert.That(failed.ErrorMessage, Does.Contain("exited with code 1"));
            Assert.That(failed.ErrorMessage, Does.Contain("scripted failure"));
            Assert.That(failed.ErrorMessage, Does.Contain("stderr tail for test"));
            Assert.That(next.Kind, Is.EqualTo(TransformWorkerHostResultKind.Completed), next.ErrorMessage);
            Assert.That(_host.LaunchCount, Is.EqualTo(1));
        }

        /// <summary>
        /// What: run-level parse errors in a valid output file are a final WorkerFailed result, not a
        /// broken conversation, and the process is kept.
        /// </summary>
        [Test]
        public async Task RunAsync_OutputCarriesRunLevelParseErrors_IsWorkerFailed()
        {
            _factory.Enqueue(ScriptStep.SucceedWithParseErrors);

            TransformWorkerHostResult result = await _host.RunAsync(CreateInput(1), CancellationToken.None);

            Assert.That(result.Kind, Is.EqualTo(TransformWorkerHostResultKind.WorkerFailed));
            Assert.That(result.ErrorMessage, Does.Contain("run-level problem"));
            Assert.That(_host.LaunchCount, Is.EqualTo(1));
            Assert.That(_host.CurrentProcessId, Is.Not.Null);
        }

        /// <summary>
        /// What: exit code 0 without an output file, or with the wrong number of file rows, is a broken
        /// conversation: the process is replaced and the retry on a fresh process succeeds.
        /// </summary>
        [TestCase(ScriptStep.SucceedWithoutOutput)]
        [TestCase(ScriptStep.SucceedWrongFileCount)]
        public async Task RunAsync_ExitZeroWithUnusableOutput_TreatedAsBrokenConversation(ScriptStep firstStep)
        {
            _factory.Enqueue(firstStep);
            _factory.Enqueue(ScriptStep.Succeed);

            TransformWorkerHostResult result = await _host.RunAsync(CreateInput(1), CancellationToken.None);

            Assert.That(result.Kind, Is.EqualTo(TransformWorkerHostResultKind.Completed), result.ErrorMessage);
            Assert.That(_host.LaunchCount, Is.EqualTo(2));
            Assert.That(_factory.Channels[0].HasExited, Is.True);
        }

        /// <summary>
        /// What: a worker that never answers is killed at the response timeout, the request ends as
        /// TimedOut without a retry, and the next request starts a fresh process.
        /// </summary>
        [Test]
        public async Task RunAsync_WorkerHangs_TimesOutKillsAndRelaunchesNextTime()
        {
            _host.Shutdown("swap host");
            _host = CreateHost(ShortTimeoutMilliseconds);
            _factory.Enqueue(ScriptStep.Hang);
            _factory.Enqueue(ScriptStep.Succeed);

            TransformWorkerHostResult timedOut = await _host.RunAsync(CreateInput(1), CancellationToken.None);
            TransformWorkerHostResult next = await _host.RunAsync(CreateInput(1), CancellationToken.None);

            Assert.That(timedOut.Kind, Is.EqualTo(TransformWorkerHostResultKind.TimedOut));
            Assert.That(timedOut.ErrorMessage, Does.Contain("was killed"));
            Assert.That(_factory.Channels[0].HasExited, Is.True);
            Assert.That(_factory.Channels[0].KillCount, Is.EqualTo(1));
            Assert.That(next.Kind, Is.EqualTo(TransformWorkerHostResultKind.Completed), next.ErrorMessage);
            Assert.That(_host.LaunchCount, Is.EqualTo(2));
        }

        /// <summary>
        /// What: a request canceled while waiting for the conversation gate throws
        /// OperationCanceledException, starts no process, and leaves the in-flight request untouched.
        /// </summary>
        [Test]
        public async Task RunAsync_CanceledWhileQueued_ThrowsWithoutLaunching()
        {
            _factory.Enqueue(ScriptStep.Hang, ScriptStep.Succeed);
            using CancellationTokenSource firstCancellation = new CancellationTokenSource();
            using CancellationTokenSource queuedCancellation = new CancellationTokenSource();

            Task<TransformWorkerHostResult> first = _host.RunAsync(CreateInput(1), firstCancellation.Token);
            await _factory.Channels[0].WaitForRequestAsync(WaitMilliseconds);
            Task<TransformWorkerHostResult> queued = _host.RunAsync(CreateInput(1), queuedCancellation.Token);
            queuedCancellation.Cancel();

            // Why not Assert.ThrowsAsync: it blocks the Unity main thread while the awaited task
            // needs that thread for its continuation, which deadlocks the Editor.
            Assert.That(await CompletesWithCancellationAsync(queued), Is.True, "The queued request must be canceled.");
            Assert.That(_host.LaunchCount, Is.EqualTo(1));
            Assert.That(first.IsCompleted, Is.False, "Canceling a queued request must not disturb the in-flight one.");

            _factory.Channels[0].ReleaseHang(ScriptStep.Succeed);
            TransformWorkerHostResult firstResult = await first;
            Assert.That(firstResult.Kind, Is.EqualTo(TransformWorkerHostResultKind.Completed), firstResult.ErrorMessage);
        }

        /// <summary>
        /// What: canceling a request mid-conversation throws OperationCanceledException and discards
        /// the process; the next request starts a fresh one and completes.
        /// </summary>
        [Test]
        public async Task RunAsync_CanceledMidConversation_DiscardsProcessAndRecovers()
        {
            _factory.Enqueue(ScriptStep.Hang);
            _factory.Enqueue(ScriptStep.Succeed);
            using CancellationTokenSource cancellation = new CancellationTokenSource();

            Task<TransformWorkerHostResult> inFlight = _host.RunAsync(CreateInput(1), cancellation.Token);
            await _factory.Channels[0].WaitForRequestAsync(WaitMilliseconds);
            cancellation.Cancel();

            Assert.That(await CompletesWithCancellationAsync(inFlight), Is.True, "The in-flight request must be canceled.");
            Assert.That(_factory.Channels[0].HasExited, Is.True);
            Assert.That(_host.CurrentProcessId, Is.Null);

            TransformWorkerHostResult next = await _host.RunAsync(CreateInput(1), CancellationToken.None);
            Assert.That(next.Kind, Is.EqualTo(TransformWorkerHostResultKind.Completed), next.ErrorMessage);
            Assert.That(_host.LaunchCount, Is.EqualTo(2));
        }

        /// <summary>
        /// What: Shutdown during a request terminates the process without waiting for the gate; the
        /// request ends as LifecycleClosed and does not start a replacement. A later request launches anew.
        /// </summary>
        [Test]
        public async Task Shutdown_DuringRequest_ClosesRequestWithoutRelaunch()
        {
            _factory.Enqueue(ScriptStep.Hang);
            _factory.Enqueue(ScriptStep.Succeed);

            Task<TransformWorkerHostResult> inFlight = _host.RunAsync(CreateInput(1), CancellationToken.None);
            await _factory.Channels[0].WaitForRequestAsync(WaitMilliseconds);
            _host.Shutdown("assembly reload for test");

            TransformWorkerHostResult closed = await inFlight;
            Assert.That(closed.Kind, Is.EqualTo(TransformWorkerHostResultKind.LifecycleClosed));
            Assert.That(_host.LaunchCount, Is.EqualTo(1));
            Assert.That(_factory.Channels[0].HasExited, Is.True);

            TransformWorkerHostResult next = await _host.RunAsync(CreateInput(1), CancellationToken.None);
            Assert.That(next.Kind, Is.EqualTo(TransformWorkerHostResultKind.Completed), next.ErrorMessage);
            Assert.That(_host.LaunchCount, Is.EqualTo(2));
        }

        /// <summary>
        /// What: Shutdown with no process running is a no-op that still invalidates nothing observable;
        /// the next request launches normally.
        /// </summary>
        [Test]
        public async Task Shutdown_WithoutProcess_IsHarmless()
        {
            _factory.Enqueue(ScriptStep.Succeed);

            _host.Shutdown("idle shutdown");
            TransformWorkerHostResult result = await _host.RunAsync(CreateInput(1), CancellationToken.None);

            Assert.That(result.Kind, Is.EqualTo(TransformWorkerHostResultKind.Completed), result.ErrorMessage);
            Assert.That(_host.LaunchCount, Is.EqualTo(1));
        }

        /// <summary>
        /// What: when the compiled worker moves to a new directory (worker source changed), the old
        /// process is stopped and a new one is started from the new directory.
        /// </summary>
        [Test]
        public async Task RunAsync_WorkerDirectoryChanges_RestartsProcess()
        {
            _factory.Enqueue(ScriptStep.Succeed);
            _factory.Enqueue(ScriptStep.Succeed);

            TransformWorkerHostResult first = await _host.RunAsync(CreateInput(1), CancellationToken.None);
            _workerDirectory = "/worker/b";
            TransformWorkerHostResult second = await _host.RunAsync(CreateInput(1), CancellationToken.None);

            Assert.That(first.Kind, Is.EqualTo(TransformWorkerHostResultKind.Completed), first.ErrorMessage);
            Assert.That(second.Kind, Is.EqualTo(TransformWorkerHostResultKind.Completed), second.ErrorMessage);
            Assert.That(_host.LaunchCount, Is.EqualTo(2));
            Assert.That(_factory.Channels[0].HasExited, Is.True);
            Assert.That(_factory.Channels[0].QuitRequested, Is.True, "The old process must be asked to quit gracefully first.");
            Assert.That(_factory.Channels[1].WorkerDirectory, Is.EqualTo("/worker/b"));
        }

        /// <summary>
        /// What: a process that exited on its own between requests (idle exit) is replaced silently.
        /// </summary>
        [Test]
        public async Task RunAsync_ProcessExitedBetweenRequests_Relaunches()
        {
            _factory.Enqueue(ScriptStep.Succeed);
            _factory.Enqueue(ScriptStep.Succeed);

            TransformWorkerHostResult first = await _host.RunAsync(CreateInput(1), CancellationToken.None);
            _factory.Channels[0].SimulateIdleExit();
            TransformWorkerHostResult second = await _host.RunAsync(CreateInput(1), CancellationToken.None);

            Assert.That(first.Kind, Is.EqualTo(TransformWorkerHostResultKind.Completed), first.ErrorMessage);
            Assert.That(second.Kind, Is.EqualTo(TransformWorkerHostResultKind.Completed), second.ErrorMessage);
            Assert.That(_host.LaunchCount, Is.EqualTo(2));
        }

        /// <summary>
        /// What: a launch-target failure (worker could not be compiled) is BootstrapFailed and starts nothing.
        /// </summary>
        [Test]
        public async Task RunAsync_BootstrapFails_ReturnsBootstrapFailedWithoutLaunch()
        {
            _workerDirectory = null;

            TransformWorkerHostResult result = await _host.RunAsync(CreateInput(1), CancellationToken.None);

            Assert.That(result.Kind, Is.EqualTo(TransformWorkerHostResultKind.BootstrapFailed));
            Assert.That(result.ErrorMessage, Does.Contain("bootstrap failed for test"));
            Assert.That(_host.LaunchCount, Is.EqualTo(0));
        }

        /// <summary>
        /// What: a process that cannot be started counts as a broken conversation; two failures in a row
        /// end as RetryExhausted with no launch recorded.
        /// </summary>
        [Test]
        public async Task RunAsync_ProcessStartFailsTwice_ReportsRetryExhausted()
        {
            _factory.StartFailuresRemaining = 2;

            TransformWorkerHostResult result = await _host.RunAsync(CreateInput(1), CancellationToken.None);

            Assert.That(result.Kind, Is.EqualTo(TransformWorkerHostResultKind.RetryExhausted));
            Assert.That(result.ErrorMessage, Does.Contain("could not be started"));
            Assert.That(_host.LaunchCount, Is.EqualTo(0));
        }

        private static async Task<bool> CompletesWithCancellationAsync(Task<TransformWorkerHostResult> request)
        {
            try
            {
                await request;
                return false;
            }
            catch (OperationCanceledException)
            {
                return true;
            }
        }

        private TransformWorkerHost CreateHost(int responseTimeoutMilliseconds)
        {
            return new TransformWorkerHost(ResolveTargetAsync, _factory.Start, responseTimeoutMilliseconds);
        }

        private Task<TransformWorkerLaunchTarget> ResolveTargetAsync(CancellationToken ct)
        {
            if (_workerDirectory == null)
            {
                return Task.FromResult(TransformWorkerLaunchTarget.Failure("bootstrap failed for test"));
            }

            return Task.FromResult(TransformWorkerLaunchTarget.Resolved(_workerDirectory, "dotnet"));
        }

        private static TransformWorkerInputDto CreateInput(int sourceCount)
        {
            TransformWorkerSourceDto[] sources = new TransformWorkerSourceDto[sourceCount];
            for (int index = 0; index < sourceCount; index++)
            {
                sources[index] = new TransformWorkerSourceDto
                {
                    sourcePath = "/project/Source" + index + ".cs",
                    projectRelativePath = "Assets/Source" + index + ".cs"
                };
            }

            return new TransformWorkerInputDto { sources = sources };
        }
    }

    /// <summary>
    /// What the scripted worker does with the next request it receives.
    /// </summary>
    public enum ScriptStep
    {
        Succeed,
        SucceedWithParseErrors,
        SucceedWithoutOutput,
        SucceedWrongFileCount,
        Fail,
        Garbage,
        Crash,
        Hang
    }

    /// <summary>
    /// Creates <see cref="ScriptedWorkerChannel"/> instances in launch order, each with its own
    /// request script, and can be told to fail the next launches.
    /// </summary>
    internal sealed class ScriptedChannelFactory
    {
        private readonly Queue<Queue<ScriptStep>> _scriptsPerLaunch = new Queue<Queue<ScriptStep>>();

        public List<ScriptedWorkerChannel> Channels { get; } = new List<ScriptedWorkerChannel>();
        public int StartFailuresRemaining { get; set; }

        /// <summary>Queues the script for the next launched process.</summary>
        public void Enqueue(params ScriptStep[] steps)
        {
            _scriptsPerLaunch.Enqueue(new Queue<ScriptStep>(steps));
        }

        public ITransformWorkerChannel Start(string workerDirectory, string dotnetHostPath)
        {
            if (StartFailuresRemaining > 0)
            {
                StartFailuresRemaining--;
                return null;
            }

            Queue<ScriptStep> script = _scriptsPerLaunch.Count > 0 ? _scriptsPerLaunch.Dequeue() : new Queue<ScriptStep>();
            ScriptedWorkerChannel channel = new ScriptedWorkerChannel(Channels.Count + 1, workerDirectory, script);
            Channels.Add(channel);
            return channel;
        }

        public void DisposeAll()
        {
            foreach (ScriptedWorkerChannel channel in Channels)
            {
                channel.Kill(0);
                channel.Dispose();
            }
        }
    }

    /// <summary>
    /// In-process worker that speaks the serve protocol over blocking line pipes and follows a script
    /// per request, so host tests can provoke every conversation outcome deterministically.
    /// </summary>
    internal sealed class ScriptedWorkerChannel : ITransformWorkerChannel
    {
        private readonly BlockingLinePipe _requests = new BlockingLinePipe();
        private readonly BlockingLinePipe _responses = new BlockingLinePipe();
        private readonly BlockingLineWriter _protocolOutput;
        private readonly Queue<ScriptStep> _script;
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _hangRelease = new ManualResetEventSlim(false);
        private readonly TaskCompletionSource<bool> _firstRequestReceived = new TaskCompletionSource<bool>();
        private volatile bool _exited;
        private ScriptStep _stepAfterHang = ScriptStep.Crash;

        public ScriptedWorkerChannel(int id, string workerDirectory, Queue<ScriptStep> script)
        {
            Id = id;
            WorkerDirectory = workerDirectory;
            _script = script;
            _protocolOutput = new BlockingLineWriter(_responses);
            RequestWriter = new BlockingLineWriter(_requests);
            ResponseReader = _responses;
            _thread = new Thread(Serve) { IsBackground = true, Name = "scripted-worker-" + id };
            _thread.Start();
        }

        public int Id { get; }
        public string WorkerDirectory { get; }
        public TextWriter RequestWriter { get; }
        public TextReader ResponseReader { get; }
        public int RequestCount { get; private set; }
        public int KillCount { get; private set; }
        public bool QuitRequested { get; private set; }

        public bool HasExited
        {
            get { return _exited; }
        }

        public Task WaitForRequestAsync(int timeoutMilliseconds)
        {
            return Task.WhenAny(_firstRequestReceived.Task, Task.Delay(timeoutMilliseconds));
        }

        /// <summary>Lets a hanging request continue with <paramref name="step"/>.</summary>
        public void ReleaseHang(ScriptStep step)
        {
            _stepAfterHang = step;
            _hangRelease.Set();
        }

        /// <summary>Simulates the worker's own idle exit between requests.</summary>
        public void SimulateIdleExit()
        {
            Exit();
        }

        public bool TryQuitGracefully(int waitMilliseconds)
        {
            if (_exited)
            {
                return true;
            }

            QuitRequested = true;
            RequestWriter.WriteLine(TransformWorkerServeProtocol.QuitCommand);
            return _thread.Join(waitMilliseconds);
        }

        public void Kill(int waitMilliseconds)
        {
            if (_exited)
            {
                return;
            }

            KillCount++;
            Exit();
            _hangRelease.Set();
            _thread.Join(waitMilliseconds);
        }

        public string ReadStandardErrorTail()
        {
            return "stderr tail for test";
        }

        public void Dispose()
        {
            _hangRelease.Dispose();
        }

        private void Serve()
        {
            while (!_exited)
            {
                string line = _requests.ReadLine();
                if (line == null || line == TransformWorkerServeProtocol.QuitCommand)
                {
                    Exit();
                    return;
                }

                RequestCount++;
                _firstRequestReceived.TrySetResult(true);
                if (!TransformWorkerServeProtocol.TryDecodeRequestLine(line, out string inputPath, out string outputPath))
                {
                    WriteFrame(TransformWorkerServeProtocol.MalformedRequestExitCode, "malformed");
                    continue;
                }

                ScriptStep step = _script.Count > 0 ? _script.Dequeue() : ScriptStep.Succeed;
                if (step == ScriptStep.Hang)
                {
                    _hangRelease.Wait();
                    if (_exited)
                    {
                        return;
                    }

                    step = _stepAfterHang;
                }

                if (!Perform(step, inputPath, outputPath))
                {
                    return;
                }
            }
        }

        // Returns false when the scripted worker "died".
        private bool Perform(ScriptStep step, string inputPath, string outputPath)
        {
            int sourceCount = CountSources(inputPath);
            switch (step)
            {
                case ScriptStep.Succeed:
                    WriteOutput(outputPath, sourceCount, Array.Empty<string>());
                    WriteFrame(0, "ok");
                    return true;
                case ScriptStep.SucceedWithParseErrors:
                    WriteOutput(outputPath, 0, new[] { "run-level problem" });
                    WriteFrame(0, "ok");
                    return true;
                case ScriptStep.SucceedWithoutOutput:
                    WriteFrame(0, "forgot the file");
                    return true;
                case ScriptStep.SucceedWrongFileCount:
                    WriteOutput(outputPath, sourceCount + 1, Array.Empty<string>());
                    WriteFrame(0, "ok");
                    return true;
                case ScriptStep.Fail:
                    WriteFrame(1, "scripted failure");
                    return true;
                case ScriptStep.Garbage:
                    _protocolOutput.WriteLine("this is not a frame");
                    return true;
                case ScriptStep.Crash:
                    Exit();
                    return false;
                default:
                    throw new InvalidOperationException("Unhandled step " + step);
            }
        }

        private static int CountSources(string inputPath)
        {
            if (!File.Exists(inputPath))
            {
                return 0;
            }

            TransformWorkerInputDto input = JsonConvert.DeserializeObject<TransformWorkerInputDto>(File.ReadAllText(inputPath));
            return input?.sources?.Length ?? 0;
        }

        private static void WriteOutput(string outputPath, int fileCount, string[] parseErrors)
        {
            TransformWorkerFileOutputDto[] files = new TransformWorkerFileOutputDto[fileCount];
            for (int index = 0; index < fileCount; index++)
            {
                files[index] = new TransformWorkerFileOutputDto();
            }

            TransformWorkerOutputDto output = new TransformWorkerOutputDto { files = files, parseErrors = parseErrors };
            File.WriteAllText(outputPath, JsonConvert.SerializeObject(output));
        }

        private void WriteFrame(int exitCode, string diagnostics)
        {
            string payload = TransformWorkerServeProtocol.EncodeDiagnostics(diagnostics, out int byteCount);
            _protocolOutput.WriteLine(TransformWorkerServeProtocol.EncodeResponseHeader(exitCode, byteCount));
            _protocolOutput.WriteLine(payload);
        }

        private void Exit()
        {
            _exited = true;
            _responses.Complete();
            _requests.Complete();
        }
    }
}
