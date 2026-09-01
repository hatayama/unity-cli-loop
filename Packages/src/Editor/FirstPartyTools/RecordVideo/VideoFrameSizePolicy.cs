using System;
using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Scales Game View dimensions and rounds them down so H.264 can encode them.
    /// </summary>
    internal static class VideoFrameSizePolicy
    {
        internal static int RoundDownToEven(int size)
        {
            return size & ~1;
        }

        internal static (int Width, int Height) Resolve(
            int sourceWidth,
            int sourceHeight,
            float scale)
        {
            Debug.Assert(scale > 0f, "scale must be positive.");
            return (
                RoundDownToEven(FloorScaled(sourceWidth, scale)),
                RoundDownToEven(FloorScaled(sourceHeight, scale)));
        }

        internal static bool MatchesEncoderSize(
            int sourceWidth,
            int sourceHeight,
            float scale,
            int encoderWidth,
            int encoderHeight)
        {
            Debug.Assert(scale > 0f, "scale must be positive.");
            int scaledWidth = FloorScaled(sourceWidth, scale);
            int scaledHeight = FloorScaled(sourceHeight, scale);
            return Math.Abs(scaledWidth - encoderWidth) <= 1
                && Math.Abs(scaledHeight - encoderHeight) <= 1;
        }

        private static int FloorScaled(int sourceSize, float scale)
        {
            return (int)Math.Floor(sourceSize * (double)scale);
        }
    }
}
