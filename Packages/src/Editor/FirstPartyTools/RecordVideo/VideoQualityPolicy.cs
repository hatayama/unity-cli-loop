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
                case RecordVideoQuality.Low:
                    return VideoBitrateMode.Low;
                case RecordVideoQuality.Medium:
                    return VideoBitrateMode.Medium;
                case RecordVideoQuality.High:
                    return VideoBitrateMode.High;
                default:
                    Debug.Assert(false, "RecordVideoQuality must be Low, Medium, or High.");
                    return VideoBitrateMode.Medium;
            }
        }
    }
}
