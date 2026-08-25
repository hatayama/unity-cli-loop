using System.Linq;

using io.github.hatayama.UnityCliLoop.Runtime;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Validates pause-point enable arguments before arming a marker.
    /// </summary>
    internal static class PausePointEnableValidation
    {
        // Returns an error message when the Id/File/Line combination fails validation, or null
        // when exactly one of "Id" or "File"+"Line" is provided.
        public static string ValidateEnableMode(EnablePausePointSchema parameters)
        {
            bool hasId = !string.IsNullOrWhiteSpace(parameters.Id);
            bool hasFile = !string.IsNullOrWhiteSpace(parameters.File);
            bool hasLine = parameters.Line > 0;

            if (hasId && (hasFile || hasLine))
            {
                return "Specify either Id or File and Line, not both.";
            }

            if (!hasId && !hasFile && !hasLine)
            {
                return "Id must not be null or empty.";
            }

            if (!hasId && hasFile != hasLine)
            {
                return "File and Line must both be provided together.";
            }

            return null;
        }

        public static string ValidateCaptureSettings(
            EnablePausePointSchema parameters,
            string hitWhen,
            UloopPausePointHitWhenParseResult hitWhenParseResult)
        {
            string[] supportedModes =
            {
                UloopPausePointCaptureMode.SingleShot,
                UloopPausePointCaptureMode.Continuous,
                UloopPausePointCaptureMode.Trace
            };
            if (!supportedModes.Contains(parameters.Mode))
            {
                return $"Mode must be one of: {string.Join(", ", supportedModes)}.";
            }

            if (parameters.MaxHistory <= 0 || parameters.MaxHistory > UloopPausePointRegistry.MaxHistoryLimit)
            {
                return $"MaxHistory must be between 1 and {UloopPausePointRegistry.MaxHistoryLimit}.";
            }

            if (parameters.MaxPreviewElements <= 0 ||
                parameters.MaxPreviewElements > UloopPausePointRegistry.MaxPreviewElementsLimit)
            {
                return $"MaxPreviewElements must be between 1 and {UloopPausePointRegistry.MaxPreviewElementsLimit}.";
            }

            if (parameters.MaxCallerFrames < 0 ||
                parameters.MaxCallerFrames > UloopPausePointRegistry.MaxCallerFramesLimit)
            {
                return $"MaxCallerFrames must be between 0 and {UloopPausePointRegistry.MaxCallerFramesLimit}.";
            }

            if (string.IsNullOrEmpty(hitWhen))
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(parameters.Id) && string.IsNullOrWhiteSpace(parameters.File))
            {
                return "--hit-when requires a --file/--line marker.";
            }

            if (!string.IsNullOrEmpty(hitWhenParseResult.ErrorMessage))
            {
                return hitWhenParseResult.ErrorMessage;
            }

            return null;
        }

        // Returns an error message when id fails validation, or null when it is valid.
        public static string ValidateId(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return "Id must not be null or empty.";
            }

            return null;
        }
    }
}
