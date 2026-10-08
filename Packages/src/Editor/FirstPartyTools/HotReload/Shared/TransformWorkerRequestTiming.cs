using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Records where one request to the transform worker spent its time, whether it started the
    /// worker process, and how many conversations it began. Only the
    /// hot_reload_worker_request_timing vibe entry reads it.
    /// </summary>
    /// <remarks>
    /// Why not HotReloadRunTiming: that accumulator lives in the hot-reload application assembly,
    /// which this shared kernel cannot reference, and its steps sit outside the phases while these
    /// split the analysis phase itself.
    /// </remarks>
    internal sealed class TransformWorkerRequestTiming
    {
        public const string GateWaitStep = "gate_wait";
        public const string ResolveLaunchTargetStep = "resolve_launch_target";
        public const string WriteInputStep = "write_input";
        public const string ProcessStartStep = "process_start";
        public const string ResponseWaitStep = "response_wait";
        public const string ReadOutputStep = "read_output";

        private readonly List<KeyValuePair<string, long>> _steps = new List<KeyValuePair<string, long>>();

        public IReadOnlyList<KeyValuePair<string, long>> Steps => _steps;

        /// <summary>Whether this request registered a new worker process.</summary>
        public bool WorkerStarted { get; private set; }

        /// <summary>Number of conversations this request began with a worker process.</summary>
        public int Attempts { get; private set; }

        /// <summary>
        /// The stages the worker timed itself, from the output that decided the request; empty when
        /// no output decided it or the worker wrote none.
        /// </summary>
        public IReadOnlyList<TransformWorkerTimingStepDto> WorkerSteps { get; private set; } =
            Array.Empty<TransformWorkerTimingStepDto>();

        public void AddStep(string step, long milliseconds)
        {
            if (string.IsNullOrEmpty(step))
            {
                throw new ArgumentException("step must not be null or empty.", nameof(step));
            }

            if (milliseconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(milliseconds), milliseconds, "Elapsed milliseconds must not be negative.");
            }

            int index = _steps.FindIndex(pair => pair.Key == step);
            if (index < 0)
            {
                _steps.Add(new KeyValuePair<string, long>(step, milliseconds));
                return;
            }

            _steps[index] = new KeyValuePair<string, long>(step, _steps[index].Value + milliseconds);
        }

        /// <summary>
        /// Measures the wall-clock time until the returned scope is disposed and adds it to the step.
        /// </summary>
        public IDisposable Measure(string step)
        {
            return new StepScope(this, step);
        }

        public void MarkWorkerStarted()
        {
            WorkerStarted = true;
        }

        public void MarkAttempt()
        {
            Attempts++;
        }

        public void RecordWorkerSteps(TransformWorkerTimingStepDto[] steps)
        {
            WorkerSteps = steps ?? Array.Empty<TransformWorkerTimingStepDto>();
        }

        private sealed class StepScope : IDisposable
        {
            private readonly TransformWorkerRequestTiming _timing;
            private readonly string _step;
            private readonly Stopwatch _watch = Stopwatch.StartNew();
            private bool _disposed;

            public StepScope(TransformWorkerRequestTiming timing, string step)
            {
                _timing = timing;
                _step = step;
            }

            public void Dispose()
            {
                // Why only once: a second dispose would count the same span twice.
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _watch.Stop();
                _timing.AddStep(_step, _watch.ElapsedMilliseconds);
            }
        }
    }
}
