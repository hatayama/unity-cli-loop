using System.Diagnostics;

using UnityEditor;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Maps record-video quality to MediaEncoder bitrate mode.
    /// </summary>
    internal static class VideoQualityPolicy
    {
        internal static VideoBitrateMode ToBitrateMode(RecordVideoQuality quality)
        {
            switch (quality)
            {
                case RecordVideoQuality.low:
                    return VideoBitrateMode.Low;
                case RecordVideoQuality.medium:
                    return VideoBitrateMode.Medium;
                case RecordVideoQuality.high:
                    return VideoBitrateMode.High;
                default:
                    Debug.Assert(false, "RecordVideoQuality must be low, medium, or high.");
                    return VideoBitrateMode.Medium;
            }
        }
    }
}
