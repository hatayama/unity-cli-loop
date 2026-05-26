using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Executes Unity Editor play mode state changes for the bundled control-play-mode tool.
    /// </summary>
    public class ControlPlayModeUseCase
    {
        public const int DefaultTimeoutSeconds = 180;

        private const int PollIntervalMilliseconds = 50;
        private const int MillisecondsPerSecond = 1000;

        public async Task<ControlPlayModeResponse> ExecuteAsync(ControlPlayModeSchema parameters, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            int timeoutSeconds = ResolveTimeoutSeconds(parameters.TimeoutSeconds);
            int timeoutMilliseconds = timeoutSeconds * MillisecondsPerSecond;
            string completedMessage;
            string requestedMessage;
            Func<bool> isExpectedState;
            bool wasPaused = EditorApplication.isPaused;

            switch (parameters.Action)
            {
                case PlayModeAction.Play:
                    if (wasPaused)
                    {
                        EditorApplication.isPaused = false;
                    }
                    if (!EditorApplication.isPlaying)
                    {
                        EditorApplication.isPlaying = true;
                    }
                    completedMessage = wasPaused ? "Play mode resumed" : "Play mode started";
                    requestedMessage = wasPaused ? "Play mode resume" : "Play mode start";
                    isExpectedState = IsPlayingAndNotPaused;
                    break;

                case PlayModeAction.Stop:
                    if (wasPaused)
                    {
                        EditorApplication.isPaused = false;
                    }
                    if (EditorApplication.isPlaying)
                    {
                        EditorApplication.isPlaying = false;
                    }
                    completedMessage = "Play mode stopped";
                    requestedMessage = "Play mode stop";
                    isExpectedState = IsStoppedAndNotPaused;
                    break;

                case PlayModeAction.Pause:
                    EditorApplication.isPaused = true;
                    completedMessage = "Play mode paused";
                    requestedMessage = "Play mode pause";
                    isExpectedState = IsPaused;
                    break;

                default:
                    return new ControlPlayModeResponse
                    {
                        IsPlaying = EditorApplication.isPlaying,
                        IsPaused = EditorApplication.isPaused,
                        Message = $"Unknown action: {parameters.Action}"
                    };
            }

            bool completed = await WaitForExpectedStateAsync(isExpectedState, timeoutMilliseconds, ct);
            string message = completed
                ? completedMessage
                : $"{requestedMessage} requested but did not complete within {timeoutSeconds}s";

            ControlPlayModeResponse response = new()
            {
                IsPlaying = EditorApplication.isPlaying,
                IsPaused = EditorApplication.isPaused,
                Message = message
            };

            return response;
        }

        private static int ResolveTimeoutSeconds(int timeoutSeconds)
        {
            if (timeoutSeconds > 0)
            {
                return timeoutSeconds;
            }

            return DefaultTimeoutSeconds;
        }

        private static async Task<bool> WaitForExpectedStateAsync(
            Func<bool> isExpectedState,
            int timeoutMilliseconds,
            CancellationToken ct)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            return await ControlPlayModeStateWaiter.WaitUntilAsync(
                isExpectedState,
                WaitForPollAsync,
                () => GetElapsedMilliseconds(stopwatch),
                timeoutMilliseconds,
                ct);
        }

        private static Task WaitForPollAsync(CancellationToken ct)
        {
            return TimerDelay.Wait(PollIntervalMilliseconds, ct);
        }

        private static int GetElapsedMilliseconds(Stopwatch stopwatch)
        {
            long elapsedMilliseconds = stopwatch.ElapsedMilliseconds;
            if (elapsedMilliseconds > int.MaxValue)
            {
                return int.MaxValue;
            }

            return (int)elapsedMilliseconds;
        }

        private static bool IsPlayingAndNotPaused()
        {
            return EditorApplication.isPlaying && !EditorApplication.isPaused;
        }

        private static bool IsStoppedAndNotPaused()
        {
            return !EditorApplication.isPlaying && !EditorApplication.isPaused;
        }

        private static bool IsPaused()
        {
            return EditorApplication.isPaused;
        }
    }

    /// <summary>
    /// Waits for the Editor to report the requested PlayMode state.
    /// </summary>
    internal static class ControlPlayModeStateWaiter
    {
        public static async Task<bool> WaitUntilAsync(
            Func<bool> isExpectedState,
            Func<CancellationToken, Task> waitForPollAsync,
            Func<int> getElapsedMilliseconds,
            int timeoutMilliseconds,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            if (isExpectedState == null)
            {
                throw new ArgumentNullException(nameof(isExpectedState));
            }

            if (waitForPollAsync == null)
            {
                throw new ArgumentNullException(nameof(waitForPollAsync));
            }

            if (getElapsedMilliseconds == null)
            {
                throw new ArgumentNullException(nameof(getElapsedMilliseconds));
            }

            if (timeoutMilliseconds <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds), timeoutMilliseconds, "timeoutMilliseconds must be positive.");
            }

            while (!isExpectedState())
            {
                if (getElapsedMilliseconds() >= timeoutMilliseconds)
                {
                    return false;
                }

                await waitForPollAsync(ct);
                ct.ThrowIfCancellationRequested();
            }

            return true;
        }
    }
}
