using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Sends and stops project IPC heartbeat frames for an accepted client request.
    /// </summary>
    internal sealed class UnityCliLoopBridgeHeartbeatService
    {
        internal async Task SendHeartbeatsAsync(
            Func<string> createHeartbeatJson,
            Func<string, Task> writeFrameAsync,
            TimeSpan interval,
            CancellationToken ct)
        {
            while (true)
            {
                try
                {
                    await Task.Delay(interval, ct);
                    await writeFrameAsync(createHeartbeatJson());
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (IOException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
            }
        }

        internal async Task StopHeartbeatsAsync(
            Task heartbeatTask,
            CancellationTokenSource heartbeatCancellationSource)
        {
            if (heartbeatTask == null)
            {
                return;
            }

            heartbeatCancellationSource?.Cancel();
            await heartbeatTask;
        }
    }
}
