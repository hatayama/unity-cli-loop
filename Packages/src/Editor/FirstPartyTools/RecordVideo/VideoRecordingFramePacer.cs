using System;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Computes how many frames a recording tick should emit from wall-clock elapsed time.
    /// </summary>
    internal static class VideoRecordingFramePacer
    {
        // One second of catch-up at the highest supported frame rate; bounds the burst after an editor stall.
        internal const int MaxFramesPerTick = 60;

        internal static int FramesDue(double elapsedSeconds, int frameRate, int encodedFrameCount)
        {
            int expected = (int)Math.Floor(elapsedSeconds * frameRate);
            int due = expected - encodedFrameCount;
            if (due < 0)
            {
                return 0;
            }

            return Math.Min(due, MaxFramesPerTick);
        }
    }
}
