using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;

using io.github.hatayama.UnityCliLoop.ToolContracts;

using Debug = UnityEngine.Debug;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Owns at most one resident transform worker process (`dotnet worker.dll --serve`) and runs
    /// transform requests through it one at a time, so callers pay the dotnet start-up cost once
    /// per Editor session instead of once per run.
    ///
    /// Concurrency: a single conversation gate serializes bootstrap, process start, and the
    /// request/response exchange; a separate state lock guards the process reference and the
    /// lifecycle generation so <see cref="Shutdown"/> never waits behind a running request.
    ///
    /// Lifecycle: <see cref="Shutdown"/> bumps the generation and terminates the process. A request
    /// that observes a generation change ends as <see cref="TransformWorkerHostResultKind.LifecycleClosed"/>
    /// and never starts a new process. If the Editor itself dies while a request is in flight, the
    /// worker lives until that RunTransform finishes and remains as an orphan if it hangs; the
    /// one-shot worker has the same property (a child outlives a dead parent until it completes),
    /// so this is accepted as equivalent. Between requests the worker exits on its own when its
    /// stdin closes or after the idle timeout.
    /// </summary>
    internal sealed class TransformWorkerHost
    {
        private const int MaxConversationAttempts = 2;
        private const int GracefulQuitWaitMilliseconds = 500;
        private const int KillWaitMilliseconds = 2000;

        public static readonly TransformWorkerHost Shared = new TransformWorkerHost(
            TransformWorkerLaunchTargetResolution.ResolveAsync,
            TransformWorkerProcessChannel.Start,
            HotReloadConstants.WorkerProcessTimeoutMilliseconds);

        private readonly TransformWorkerLaunchTargetResolver _resolveLaunchTarget;
        private readonly TransformWorkerChannelFactory _channelFactory;
        private readonly int _responseTimeoutMilliseconds;
        private readonly SemaphoreSlim _conversationGate = new SemaphoreSlim(1, 1);
        private readonly object _stateLock = new object();

        private ITransformWorkerChannel _channel;
        private string _channelWorkerDirectory;
        private int _generation;
        private int _launchCount;

        public TransformWorkerHost(
            TransformWorkerLaunchTargetResolver resolveLaunchTarget,
            TransformWorkerChannelFactory channelFactory,
            int responseTimeoutMilliseconds)
        {
            if (resolveLaunchTarget == null)
            {
                throw new ArgumentNullException(nameof(resolveLaunchTarget));
            }

            if (channelFactory == null)
            {
                throw new ArgumentNullException(nameof(channelFactory));
            }

            if (responseTimeoutMilliseconds <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(responseTimeoutMilliseconds));
            }

            _resolveLaunchTarget = resolveLaunchTarget;
            _channelFactory = channelFactory;
            _responseTimeoutMilliseconds = responseTimeoutMilliseconds;
        }

        /// <summary>Number of worker processes started so far; tests use it to prove reuse.</summary>
        public int LaunchCount
        {
            get
            {
                lock (_stateLock)
                {
                    return _launchCount;
                }
            }
        }

        /// <summary>Id of the live worker, or null when none is running.</summary>
        public int? CurrentProcessId
        {
            get
            {
                lock (_stateLock)
                {
                    return _channel == null || _channel.HasExited ? (int?)null : _channel.Id;
                }
            }
        }

        /// <summary>
        /// Runs one transform request through the resident worker, starting or restarting it as
        /// needed. Never throws for worker or protocol failures; cancellation propagates as
        /// <see cref="OperationCanceledException"/> after the in-flight worker has been discarded.
        /// </summary>
        public async Task<TransformWorkerHostResult> RunAsync(TransformWorkerInputDto input, CancellationToken ct)
        {
            Debug.Assert(input != null, "input must not be null.");
            Debug.Assert(input.sources != null && input.sources.Length > 0, "sources must not be empty.");

            // Why WaitAsync before the try: a cancel while queued must not release a gate this
            // request never acquired.
            await _conversationGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                int generation = ReadGeneration();
                TransformWorkerLaunchTarget target = await _resolveLaunchTarget(ct).ConfigureAwait(false);
                if (!target.Success)
                {
                    return TransformWorkerHostResult.Failure(TransformWorkerHostResultKind.BootstrapFailed, target.ErrorMessage);
                }

                return await RunConversationsAsync(input, target, generation, ct).ConfigureAwait(false);
            }
            finally
            {
                _conversationGate.Release();
            }
        }

        /// <summary>
        /// Terminates the resident worker and invalidates every in-flight request. Safe to call
        /// from lifecycle callbacks while a request is running; it does not wait for the gate.
        /// </summary>
        public void Shutdown(string trigger)
        {
            Debug.Assert(!string.IsNullOrEmpty(trigger), "trigger must not be empty.");

            ITransformWorkerChannel channel;
            lock (_stateLock)
            {
                _generation++;
                channel = _channel;
                _channel = null;
                _channelWorkerDirectory = null;
            }

            if (channel == null)
            {
                return;
            }

            // Why read the id first: the channel is disposed by TerminateChannel and a disposed
            // process no longer exposes its id.
            int processId = channel.Id;
            TerminateChannel(channel);
            VibeLogger.LogInfo(
                HotReloadConstants.VibeLogWorkerHostShutdown,
                "Resident transform worker stopped.",
                new { trigger, processId });
        }

        private async Task<TransformWorkerHostResult> RunConversationsAsync(
            TransformWorkerInputDto input,
            TransformWorkerLaunchTarget target,
            int generation,
            CancellationToken ct)
        {
            string tempDirectory = Path.Combine(Path.GetTempPath(), "uloop-hot-reload-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            string inputJsonPath = Path.Combine(tempDirectory, "input.json");
            string outputJsonPath = Path.Combine(tempDirectory, "output.json");
            try
            {
                File.WriteAllText(inputJsonPath, JsonConvert.SerializeObject(input), new UTF8Encoding(false));
                string requestLine = TransformWorkerServeProtocol.EncodeRequestLine(inputJsonPath, outputJsonPath);

                string lastBrokenReason = string.Empty;
                for (int attempt = 1; attempt <= MaxConversationAttempts; attempt++)
                {
                    if (IsGenerationClosed(generation))
                    {
                        return LifecycleClosed("before starting attempt " + attempt);
                    }

                    ConversationOutcome outcome = await RunOneConversationAsync(
                        target, requestLine, outputJsonPath, input.sources.Length, generation, ct).ConfigureAwait(false);
                    if (outcome.Result != null)
                    {
                        return outcome.Result;
                    }

                    lastBrokenReason = outcome.BrokenReason;
                    VibeLogger.LogWarning(
                        HotReloadConstants.VibeLogWorkerHostBrokenConversation,
                        "Resident transform worker conversation broke; the process was discarded.",
                        new { attempt, reason = lastBrokenReason });
                }

                if (IsGenerationClosed(generation))
                {
                    return LifecycleClosed("after the last attempt broke");
                }

                return TransformWorkerHostResult.Failure(
                    TransformWorkerHostResultKind.RetryExhausted,
                    "Resident transform worker conversation broke " + MaxConversationAttempts
                    + " times in a row. Last reason: " + lastBrokenReason);
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        // One exchange with one process. Returns a final result, or a broken-conversation reason
        // after the process has been discarded so the caller may try once more.
        private async Task<ConversationOutcome> RunOneConversationAsync(
            TransformWorkerLaunchTarget target,
            string requestLine,
            string outputJsonPath,
            int expectedFileCount,
            int generation,
            CancellationToken ct)
        {
            ITransformWorkerChannel channel = EnsureChannel(target, out string startFailure);
            if (channel == null)
            {
                return ConversationOutcome.Broken(startFailure);
            }

            if (IsGenerationClosed(generation))
            {
                return ConversationOutcome.Final(LifecycleClosed("before sending the request"));
            }

            Stopwatch deadline = Stopwatch.StartNew();
            try
            {
                channel.RequestWriter.WriteLine(requestLine);
                channel.RequestWriter.Flush();

                string header = await ReadWithinDeadlineAsync(channel, deadline, ct).ConfigureAwait(false);
                if (header == null)
                {
                    return await HandleMissingLineAsync(channel, deadline, "response header").ConfigureAwait(false);
                }

                if (!TransformWorkerServeProtocol.TryParseResponseHeader(header, out int exitCode, out int diagnosticByteCount))
                {
                    DiscardChannel(channel);
                    return ConversationOutcome.Broken("unrecognized response header: " + header);
                }

                string diagnosticsLine = await ReadWithinDeadlineAsync(channel, deadline, ct).ConfigureAwait(false);
                if (diagnosticsLine == null)
                {
                    return await HandleMissingLineAsync(channel, deadline, "diagnostics line").ConfigureAwait(false);
                }

                if (!TransformWorkerServeProtocol.TryDecodeDiagnostics(diagnosticsLine, diagnosticByteCount, out string diagnostics))
                {
                    DiscardChannel(channel);
                    return ConversationOutcome.Broken("diagnostics payload did not match its declared length");
                }

                // Why check here and not again later: from this point on nothing touches the
                // process, so a shutdown that lands after this line cannot corrupt the result.
                if (IsGenerationClosed(generation))
                {
                    return ConversationOutcome.Final(LifecycleClosed("after receiving the response"));
                }

                return InterpretResponse(channel, exitCode, diagnostics, outputJsonPath, expectedFileCount);
            }
            catch (IOException ex)
            {
                DiscardChannel(channel);
                return ConversationOutcome.Broken("pipe failure: " + ex.Message);
            }
            catch (ObjectDisposedException ex)
            {
                DiscardChannel(channel);
                return ConversationOutcome.Broken("pipe closed: " + ex.Message);
            }
            catch (OperationCanceledException)
            {
                // Why discard: the worker may still be mid-request; its late response would
                // desynchronize the next conversation.
                DiscardChannel(channel);
                throw;
            }
        }

        private ConversationOutcome InterpretResponse(
            ITransformWorkerChannel channel,
            int exitCode,
            string diagnostics,
            string outputJsonPath,
            int expectedFileCount)
        {
            if (exitCode != 0)
            {
                // The request itself failed; the process is healthy and stays for the next one.
                return ConversationOutcome.Final(TransformWorkerHostResult.Failure(
                    TransformWorkerHostResultKind.WorkerFailed,
                    "Transform worker exited with code " + exitCode
                    + ".\nstdout:\n" + diagnostics
                    + "\nstderr:\n" + channel.ReadStandardErrorTail()));
            }

            TransformWorkerOutputDto output = TransformWorkerOutputReader.TryRead(outputJsonPath, expectedFileCount, out string readError);
            if (output == null)
            {
                // Why broken and not WorkerFailed: exit 0 without a usable output file means the
                // frame and the file system disagree, which only a fresh process can rule out.
                DiscardChannel(channel);
                return ConversationOutcome.Broken(readError);
            }

            if (output.parseErrors.Length > 0)
            {
                return ConversationOutcome.Final(TransformWorkerHostResult.Failure(
                    TransformWorkerHostResultKind.WorkerFailed,
                    string.Join("\n", output.parseErrors)));
            }

            return ConversationOutcome.Final(TransformWorkerHostResult.Completed(output));
        }

        private async Task<ConversationOutcome> HandleMissingLineAsync(
            ITransformWorkerChannel channel,
            Stopwatch deadline,
            string expectedLine)
        {
            if (deadline.ElapsedMilliseconds < _responseTimeoutMilliseconds)
            {
                DiscardChannel(channel);
                return ConversationOutcome.Broken("worker closed its output before the " + expectedLine);
            }

            // Why no retry: a hang is a property of this request as much as of the process, and a
            // second 120 s wait would double the worst case for callers.
            int processId = channel.Id;
            await Task.Run(() => DiscardChannel(channel)).ConfigureAwait(false);
            return ConversationOutcome.Final(TransformWorkerHostResult.Failure(
                TransformWorkerHostResultKind.TimedOut,
                "Transform worker did not answer within " + _responseTimeoutMilliseconds
                + " ms (waiting for the " + expectedLine + "); process " + processId + " was killed."));
        }

        private async Task<string> ReadWithinDeadlineAsync(ITransformWorkerChannel channel, Stopwatch deadline, CancellationToken ct)
        {
            int remaining = _responseTimeoutMilliseconds - (int)Math.Min(deadline.ElapsedMilliseconds, int.MaxValue);
            return await TransformWorkerHostLineReader.ReadLineAsync(channel.ResponseReader, remaining, ct).ConfigureAwait(false);
        }

        // Returns the live channel for the target directory, starting a new process when none is
        // alive or the compiled worker moved. Null with a reason when the process could not start.
        private ITransformWorkerChannel EnsureChannel(TransformWorkerLaunchTarget target, out string startFailure)
        {
            startFailure = null;
            ITransformWorkerChannel stale = null;
            lock (_stateLock)
            {
                if (_channel != null && !_channel.HasExited && _channelWorkerDirectory == target.WorkerDirectory)
                {
                    return _channel;
                }

                stale = _channel;
                _channel = null;
                _channelWorkerDirectory = null;
            }

            bool restarted = stale != null;
            if (stale != null)
            {
                TerminateChannel(stale);
            }

            ITransformWorkerChannel started = _channelFactory(target.WorkerDirectory, target.DotnetHostPath);
            if (started == null)
            {
                startFailure = "worker process could not be started from " + target.WorkerDirectory;
                return null;
            }

            int launchCount;
            lock (_stateLock)
            {
                _channel = started;
                _channelWorkerDirectory = target.WorkerDirectory;
                _launchCount++;
                launchCount = _launchCount;
            }

            VibeLogger.LogInfo(
                restarted ? HotReloadConstants.VibeLogWorkerHostRestarted : HotReloadConstants.VibeLogWorkerHostStarted,
                restarted ? "Resident transform worker restarted." : "Resident transform worker started.",
                new { processId = started.Id, launchCount, workerDirectory = target.WorkerDirectory });
            return started;
        }

        // Removes and terminates the channel only if it is still the current one; a concurrent
        // Shutdown that already took it owns its termination.
        private void DiscardChannel(ITransformWorkerChannel channel)
        {
            lock (_stateLock)
            {
                if (!ReferenceEquals(_channel, channel))
                {
                    return;
                }

                _channel = null;
                _channelWorkerDirectory = null;
            }

            TerminateChannel(channel);
        }

        private static void TerminateChannel(ITransformWorkerChannel channel)
        {
            try
            {
                if (!channel.TryQuitGracefully(GracefulQuitWaitMilliseconds))
                {
                    channel.Kill(KillWaitMilliseconds);
                }
            }
            catch (IOException)
            {
                // The pipe is already gone; the process is exiting or exited.
                channel.Kill(KillWaitMilliseconds);
            }
            catch (InvalidOperationException)
            {
                // The process exited between the liveness check and the write.
            }
            finally
            {
                channel.Dispose();
            }
        }

        private int ReadGeneration()
        {
            lock (_stateLock)
            {
                return _generation;
            }
        }

        private bool IsGenerationClosed(int generation)
        {
            return ReadGeneration() != generation;
        }

        private TransformWorkerHostResult LifecycleClosed(string when)
        {
            VibeLogger.LogInfo(
                HotReloadConstants.VibeLogWorkerHostLifecycleClosed,
                "Resident transform worker request abandoned because the host was shut down.",
                new { when });
            return TransformWorkerHostResult.Failure(
                TransformWorkerHostResultKind.LifecycleClosed,
                "The resident transform worker was shut down " + when + ".");
        }

        private readonly struct ConversationOutcome
        {
            public TransformWorkerHostResult Result { get; }
            public string BrokenReason { get; }

            private ConversationOutcome(TransformWorkerHostResult result, string brokenReason)
            {
                Result = result;
                BrokenReason = brokenReason;
            }

            public static ConversationOutcome Final(TransformWorkerHostResult result)
            {
                return new ConversationOutcome(result, null);
            }

            public static ConversationOutcome Broken(string reason)
            {
                return new ConversationOutcome(null, reason);
            }
        }
    }

    /// <summary>
    /// Reads and validates a worker output file. Null with a reason when the file is missing,
    /// unreadable as JSON, or does not carry one per-file row per source.
    /// </summary>
    internal static class TransformWorkerOutputReader
    {
        public static TransformWorkerOutputDto TryRead(string outputJsonPath, int expectedFileCount, out string error)
        {
            error = null;
            if (!File.Exists(outputJsonPath))
            {
                error = "worker exited 0 but produced no output JSON file";
                return null;
            }

            string outputJson = File.ReadAllText(outputJsonPath, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            TransformWorkerOutputDto output;
            try
            {
                output = JsonConvert.DeserializeObject<TransformWorkerOutputDto>(outputJson);
            }
            catch (JsonException ex)
            {
                error = "worker output JSON could not be parsed: " + ex.Message;
                return null;
            }

            if (output == null)
            {
                error = "worker output JSON deserialized to null";
                return null;
            }

            TransformWorkerClient.CoalesceOutput(output);
            if (output.parseErrors.Length == 0 && output.files.Length != expectedFileCount)
            {
                error = "worker output carried " + output.files.Length + " file rows for " + expectedFileCount + " sources";
                return null;
            }

            return output;
        }
    }
}
