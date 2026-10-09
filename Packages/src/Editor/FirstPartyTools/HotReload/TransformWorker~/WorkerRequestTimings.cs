using System;
using System.Collections.Generic;
using System.Diagnostics;

// One stage inside the worker and the milliseconds it took.
internal sealed class WorkerTimingStep
{
    public string Step { get; set; }

    // Why int and not long: the Editor-side DTO sync test maps int only, and no stage of one
    // request comes near int's range.
    public int Ms { get; set; }
}

// Records how long each stage of one request took, as the time between consecutive laps.
// Why laps and not scopes: the pipeline stages are statements of one long method, and wrapping
// each in a scope would break the locals the later stages read.
internal sealed class WorkerRequestTimings
{
    private readonly List<WorkerTimingStep> _steps = new List<WorkerTimingStep>();
    private readonly Stopwatch _lap = Stopwatch.StartNew();

    // Adds the time since the previous lap to the step; a step lapped again is summed in place.
    public void Lap(string step)
    {
        if (string.IsNullOrEmpty(step))
        {
            throw new ArgumentException("step must not be null or empty.", nameof(step));
        }

        int elapsed = (int)_lap.ElapsedMilliseconds;
        _lap.Restart();
        WorkerTimingStep existing = _steps.Find(candidate => candidate.Step == step);
        if (existing == null)
        {
            _steps.Add(new WorkerTimingStep { Step = step, Ms = elapsed });
            return;
        }

        existing.Ms += elapsed;
    }

    public WorkerTimingStep[] ToArray()
    {
        return _steps.ToArray();
    }
}
