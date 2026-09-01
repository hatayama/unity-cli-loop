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
            (int width, int height) resolved = Resolve(sourceWidth, sourceHeight, scale);
            return resolved.width == encoderWidth && resolved.height == encoderHeight;
        }

        private static int FloorScaled(int sourceSize, float scale)
        {
            return (int)Math.Floor(sourceSize * (double)scale);
        }
    }
}
