using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using UnityCliLoopDebug = UnityEngine.Debug;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// One live resident worker as seen by <see cref="TransformWorkerHost"/>: a request writer, a
    /// response reader, liveness, and termination. Abstracted so host tests can substitute an
    /// in-process serve loop over pipes for the real dotnet process.
    /// </summary>
    internal interface ITransformWorkerChannel : IDisposable
    {
        int Id { get; }
        bool HasExited { get; }
        TextWriter RequestWriter { get; }
        TextReader ResponseReader { get; }

        /// <summary>Asks the worker to exit on its own and waits briefly; false when it did not.</summary>
        bool TryQuitGracefully(int waitMilliseconds);

        /// <summary>Terminates the worker immediately and waits briefly for the exit.</summary>
        void Kill(int waitMilliseconds);

        /// <summary>The most recent standard-error output, bounded, for failure diagnostics.</summary>
        string ReadStandardErrorTail();
    }

    /// <summary>
    /// Creates a channel for a worker directory and dotnet host. The host owns the returned channel.
    /// </summary>
    internal delegate ITransformWorkerChannel TransformWorkerChannelFactory(string workerDirectory, string dotnetHostPath);

    /// <summary>
    /// The real `dotnet worker.dll --serve` process with stdin/stdout redirected for the protocol
    /// and stderr drained continuously into a bounded buffer.
    /// </summary>
    internal sealed class TransformWorkerProcessChannel : ITransformWorkerChannel
    {
        // Why bounded: stderr is only ever shown as the tail of a failure message, and an
        // unbounded buffer on a chatty worker would grow for the life of the Editor session.
        private const int StandardErrorTailCapacity = 64 * 1024;

        private readonly Process _process;
        private readonly StringBuilder _standardErrorTail = new StringBuilder();
        private readonly object _standardErrorLock = new object();

        private TransformWorkerProcessChannel(Process process)
        {
            _process = process;
            RequestWriter = process.StandardInput;
            ResponseReader = process.StandardOutput;
            // Why drain from the start: a worker that writes to stderr while the host is not
            // reading would block on a full pipe and look like a hang.
            _ = Task.Run(DrainStandardErrorAsync);
        }

        public int Id
        {
            get { return _process.Id; }
        }

        public bool HasExited
        {
            get { return _process.HasExited; }
        }

        public TextWriter RequestWriter { get; }

        public TextReader ResponseReader { get; }

        /// <summary>
        /// Starts the worker in resident mode. Returns null when the process could not be started.
        /// </summary>
        public static ITransformWorkerChannel Start(string workerDirectory, string dotnetHostPath)
        {
            UnityCliLoopDebug.Assert(!string.IsNullOrEmpty(workerDirectory), "workerDirectory must not be empty.");
            UnityCliLoopDebug.Assert(!string.IsNullOrEmpty(dotnetHostPath), "dotnetHostPath must not be empty.");

            string workerDllPath = Path.Combine(workerDirectory, HotReloadConstants.WorkerDllFileName);
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = dotnetHostPath,
                Arguments = "\"" + workerDllPath + "\" " + TransformWorkerServeProtocol.ServeArgument,
                WorkingDirectory = workerDirectory,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                // Pairs with the UTF-8 console encodings the worker sets in resident mode.
                StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            };

            Process process;
            try
            {
                process = Process.Start(startInfo);
            }
            catch (Win32Exception ex)
            {
                UnityCliLoopDebug.LogWarning("Transform worker process could not be started: " + ex.Message);
                return null;
            }
            catch (InvalidOperationException ex)
            {
                UnityCliLoopDebug.LogWarning("Transform worker process could not be started: " + ex.Message);
                return null;
            }

            if (process == null)
            {
                return null;
            }

            // Why: the request writer must not buffer a line past the WriteLine, or the worker
            // waits for input while the host waits for a response.
            process.StandardInput.AutoFlush = true;
            return new TransformWorkerProcessChannel(process);
        }

        public bool TryQuitGracefully(int waitMilliseconds)
        {
            if (_process.HasExited)
            {
                return true;
            }

            _process.StandardInput.WriteLine(TransformWorkerServeProtocol.QuitCommand);
            _process.StandardInput.Flush();
            return _process.WaitForExit(waitMilliseconds);
        }

        public void Kill(int waitMilliseconds)
        {
            if (_process.HasExited)
            {
                return;
            }

            _process.Kill();
            _process.WaitForExit(waitMilliseconds);
        }

        public string ReadStandardErrorTail()
        {
            lock (_standardErrorLock)
            {
                return _standardErrorTail.ToString();
            }
        }

        public void Dispose()
        {
            _process.Dispose();
        }

        private async Task DrainStandardErrorAsync()
        {
            try
            {
                while (true)
                {
                    string line = await _process.StandardError.ReadLineAsync().ConfigureAwait(false);
                    if (line == null)
                    {
                        return;
                    }

                    AppendStandardError(line);
                }
            }
            catch (IOException)
            {
                // The pipe closed with the process; nothing further to drain.
            }
            catch (ObjectDisposedException)
            {
                // Disposed after shutdown; nothing further to drain.
            }
        }

        private void AppendStandardError(string line)
        {
            lock (_standardErrorLock)
            {
                _standardErrorTail.AppendLine(line);
                if (_standardErrorTail.Length > StandardErrorTailCapacity)
                {
                    _standardErrorTail.Remove(0, _standardErrorTail.Length - StandardErrorTailCapacity);
                }
            }
        }
    }

    /// <summary>
    /// Deadline-bounded line reads for the response frame. On timeout or cancel the in-flight read
    /// is abandoned; the caller must kill the worker so the pipe closes and the read completes.
    /// </summary>
    internal static class TransformWorkerHostLineReader
    {
        /// <summary>
        /// Returns the line, or null when <paramref name="remainingMilliseconds"/> elapsed first.
        /// Throws <see cref="OperationCanceledException"/> when <paramref name="ct"/> is canceled.
        /// </summary>
        public static async Task<string> ReadLineAsync(TextReader reader, int remainingMilliseconds, CancellationToken ct)
        {
            UnityCliLoopDebug.Assert(reader != null, "reader must not be null.");
            ct.ThrowIfCancellationRequested();
            if (remainingMilliseconds <= 0)
            {
                return null;
            }

            // Why Task.Run: TextReader.ReadLine has no cancellation; isolating it lets the deadline
            // return while the blocking read continues until the pipe is closed.
            Task<string> readTask = Task.Run(() => reader.ReadLine(), CancellationToken.None);
            using CancellationTokenSource delayCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Task delayTask = Task.Delay(remainingMilliseconds, delayCancellation.Token);
            Task completed = await Task.WhenAny(readTask, delayTask).ConfigureAwait(false);
            if (ReferenceEquals(completed, readTask))
            {
                delayCancellation.Cancel();
                try
                {
                    return await readTask.ConfigureAwait(false);
                }
                catch (IOException)
                {
                    return null;
                }
                catch (ObjectDisposedException)
                {
                    return null;
                }
            }

            ObserveAbandonedRead(readTask);
            ct.ThrowIfCancellationRequested();
            return null;
        }

        private static void ObserveAbandonedRead(Task<string> readTask)
        {
            _ = readTask.ContinueWith(
                static observed => _ = observed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }
}
