using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Async line reader for the shared Roslyn worker stdout protocol.
    /// Kept free of UnityEngine so pure unit tests can cover timeout and abandonment.
    /// </summary>
    internal static class SharedRoslynCompilerWorkerLineReader
    {
        public const int DefaultResponseTimeoutMilliseconds = 30000;

        /// <summary>
        /// Reads one line with an upper bound. On timeout or cancel, the in-flight ReadLine task is
        /// observed but not awaited; callers must close the stream/process so ReadLine can finish.
        /// </summary>
        public static async Task<string> ReadLineAsync(
            TextReader reader,
            CancellationToken ct,
            int timeoutMilliseconds = DefaultResponseTimeoutMilliseconds)
        {
            Debug.Assert(reader != null, "reader must not be null");
            Debug.Assert(timeoutMilliseconds > 0, "timeoutMilliseconds must be positive");
            ct.ThrowIfCancellationRequested();

            // Why Task.Run: StreamReader.ReadLine has no cancel token; isolation lets timeout return
            // while the blocking read continues until the caller closes the pipe/process.
            Task<string> readTask = Task.Run(() => reader.ReadLine(), CancellationToken.None);
            using CancellationTokenSource timeoutCancellationTokenSource =
                CancellationTokenSource.CreateLinkedTokenSource(ct);
            Task delayTask = Task.Delay(timeoutMilliseconds, timeoutCancellationTokenSource.Token);
            // Why observe delay faults: WhenAny leaves a canceled/faulted delay unobserved otherwise.
            _ = delayTask.ContinueWith(
                static observedTask => _ = observedTask.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            Task completedTask = await Task.WhenAny(readTask, delayTask).ConfigureAwait(false);
            if (ReferenceEquals(completedTask, readTask))
            {
                timeoutCancellationTokenSource.Cancel();
                return await readTask.ConfigureAwait(false);
            }

            ObserveAbandonedRead(readTask);
            ct.ThrowIfCancellationRequested();
            return null;
        }

        public static async Task<List<string>> ReadDiagnosticLinesAsync(
            TextReader reader,
            string endMarker,
            CancellationToken ct,
            int timeoutMilliseconds = DefaultResponseTimeoutMilliseconds)
        {
            Debug.Assert(reader != null, "reader must not be null");
            Debug.Assert(!string.IsNullOrEmpty(endMarker), "endMarker must not be empty");

            List<string> outputLines = new();
            while (true)
            {
                string outputLine = await ReadLineAsync(reader, ct, timeoutMilliseconds)
                    .ConfigureAwait(false);
                if (outputLine == null)
                {
                    return null;
                }

                if (outputLine == endMarker)
                {
                    return outputLines;
                }

                outputLines.Add(outputLine);
            }
        }

        private static void ObserveAbandonedRead(Task<string> readTask)
        {
            _ = readTask.ContinueWith(
                static observedTask => _ = observedTask.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }
}
