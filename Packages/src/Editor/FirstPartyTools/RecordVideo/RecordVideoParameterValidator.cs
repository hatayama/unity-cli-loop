using System;
using System.IO;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Validates Start-only record-video parameters without reading the host platform.
    /// </summary>
    internal static class RecordVideoParameterValidator
    {
        private const int MinFrameRate = 1;
        private const int MaxFrameRate = 60;
        private const int MinMaxDurationSeconds = 1;
        private const int MaxMaxDurationSeconds = 600;
        private const string Mp4Extension = ".mp4";
        private const string WebmExtension = ".webm";
        private const string LinuxH264Message =
            "H.264 is not available on Linux; use a .webm output path.";

        internal static ValidationResult Validate(
            int frameRate,
            int maxDurationSeconds,
            string outputPath,
            bool isLinux,
            float resolutionScale)
        {
            if (frameRate < MinFrameRate || frameRate > MaxFrameRate)
            {
                return ValidationResult.Failure(
                    $"FrameRate must be between {MinFrameRate} and {MaxFrameRate}.");
            }

            if (maxDurationSeconds < MinMaxDurationSeconds
                || maxDurationSeconds > MaxMaxDurationSeconds)
            {
                return ValidationResult.Failure(
                    $"MaxDurationSeconds must be between {MinMaxDurationSeconds} and {MaxMaxDurationSeconds}.");
            }

            if (resolutionScale < 0.1f || resolutionScale > 1.0f)
            {
                return ValidationResult.Failure(
                    $"ResolutionScale must be between 0.1 and 1.0, got: {resolutionScale}");
            }

            if (string.IsNullOrEmpty(outputPath))
            {
                return ValidationResult.Success();
            }

            string extension = Path.GetExtension(outputPath);
            bool isMp4 = string.Equals(extension, Mp4Extension, StringComparison.OrdinalIgnoreCase);
            bool isWebm = string.Equals(extension, WebmExtension, StringComparison.OrdinalIgnoreCase);
            if (!isMp4 && !isWebm)
            {
                return ValidationResult.Failure("OutputPath extension must be .mp4 or .webm.");
            }

            if (isLinux && isMp4)
            {
                return ValidationResult.Failure(LinuxH264Message);
            }

            return ValidationResult.Success();
        }
    }
}
