using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;

using io.github.hatayama.UnityCliLoop.Runtime;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Captures the managed call stack at a pause point hit and reduces it to the caller
    /// frames worth reporting. Must be called synchronously on the hitting thread.
    /// </summary>
    internal static class SourcePausePointCallerFrameCapture
    {
        // fNeedFileInfo:true is effectively free on Mono (measured at parity with false,
        // ~0.12 ms per capture) and yields file:line for script assemblies compiled with
        // Debug code optimization — the same prerequisite pause points already require.
        public static List<UloopPausePointCallerFrame> CaptureCallerFrames(int maxCallerFrames)
        {
            Debug.Assert(maxCallerFrames >= 0, "maxCallerFrames must not be negative");
            Debug.Assert(
                maxCallerFrames <= UloopPausePointRegistry.MaxCallerFramesLimit,
                "maxCallerFrames must not exceed the caller-frame limit");

            if (maxCallerFrames == 0)
            {
                // Why skip the walk: 0 is the high-frequency trace escape hatch, so the cost of
                // examining 24 frames would defeat the option.
                return new List<UloopPausePointCallerFrame>();
            }

            StackTrace stackTrace = new StackTrace(fNeedFileInfo: true);
            int frameCount = Math.Min(
                stackTrace.FrameCount, SourcePausePointConstants.MaxCallerStackFramesToExamine);
            List<SourcePausePointRawStackFrame> rawFrames =
                new List<SourcePausePointRawStackFrame>(frameCount);
            bool pastCaptureInfrastructure = false;
            for (int i = 0; i < frameCount; i++)
            {
                StackFrame frame = stackTrace.GetFrame(i);
                MethodBase method = frame?.GetMethod();
                string typeFullName = method?.DeclaringType?.FullName;
                if (!pastCaptureInfrastructure)
                {
                    // Skip our own capture frames by identity, not by a fixed count, so
                    // inlining or a future refactor of the call chain cannot silently shift
                    // which frame is treated as the marker's own.
                    bool isCaptureInfrastructure =
                        typeFullName == typeof(SourcePausePointCallerFrameCapture).FullName ||
                        typeFullName == typeof(SourcePausePointCapture).FullName;
                    if (isCaptureInfrastructure)
                    {
                        continue;
                    }

                    pastCaptureInfrastructure = true;
                }

                rawFrames.Add(new SourcePausePointRawStackFrame(
                    typeFullName,
                    method?.Name,
                    frame.GetFileName(),
                    frame.GetFileLineNumber()));
            }

            return SourcePausePointCallerFrameSelector.Select(rawFrames, maxCallerFrames);
        }
    }
}
