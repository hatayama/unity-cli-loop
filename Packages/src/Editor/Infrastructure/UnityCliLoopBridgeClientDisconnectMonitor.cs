using System;
using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.UnityCliLoop.Runtime;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Cancels accepted project IPC requests when their client connection disappears.
    /// </summary>
    internal sealed class UnityCliLoopBridgeClientDisconnectMonitor
    {
        private const int ClientDisconnectMonitorPollMilliseconds = 100;

        /// <summary>
        /// Monitors an accepted client connection and cancels the request token source when the client disconnects.
        /// </summary>
        internal async Task MonitorClientDisconnectAsync(
            BridgeClientConnection client,
            CancellationTokenSource requestCancellationTokenSource)
        {
            while (!requestCancellationTokenSource.IsCancellationRequested)
            {
                if (!client.IsConnected)
                {
                    // Why: Option B resumes on mid-request CLI disconnect so an abandoned pause
                    // does not leave frame-dependent tools stuck after the agent dies.
                    UloopPausePointRegistry.ResumeEditorPauseForClientDisconnect();
                    requestCancellationTokenSource.Cancel();
                    return;
                }

                try
                {
                    await Task.Delay(ClientDisconnectMonitorPollMilliseconds, requestCancellationTokenSource.Token);
                }
                catch (OperationCanceledException)
                {
                    // Cancellation is the normal stop signal from StopClientDisconnectMonitorAsync.
                    // Without the token the delay always ran to completion, adding one poll
                    // interval of tail latency to every request teardown and server shutdown.
                    return;
                }
            }
        }

        internal async Task StopClientDisconnectMonitorAsync(
            Task clientDisconnectMonitorTask,
            CancellationTokenSource requestCancellationTokenSource)
        {
            if (clientDisconnectMonitorTask == null)
            {
                return;
            }

            requestCancellationTokenSource.Cancel();
            await clientDisconnectMonitorTask;
        }
    }
}
