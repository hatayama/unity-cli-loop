#nullable enable
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Detects whether every pixel in a buffer shares one Color32 value.
    /// </summary>
    internal static class UniformColorDetector
    {
        /// <summary>
        /// Returns the shared color when all pixels match; otherwise null.
        /// Empty input is treated as non-uniform.
        /// Why full scan (no sampling): sparse non-black pixels must not be missed when deciding
        /// whether a capture retry is warranted.
        /// </summary>
        internal static Color32? DetectUniformColor(Color32[] pixels)
        {
            if (pixels.Length == 0)
            {
                return null;
            }

            Color32 first = pixels[0];
            for (int i = 1; i < pixels.Length; i++)
            {
                Color32 pixel = pixels[i];
                if (pixel.r != first.r || pixel.g != first.g || pixel.b != first.b || pixel.a != first.a)
                {
                    return null;
                }
            }

            return first;
        }
    }
}
