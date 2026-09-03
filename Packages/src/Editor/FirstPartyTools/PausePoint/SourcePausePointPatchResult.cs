using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Outcome of a <see cref="SourcePausePointPatcher"/> patch attempt.
    /// </summary>
    internal sealed class SourcePausePointPatchResult
    {
        public bool Success { get; }
        public SourcePausePointPatchFailureReason FailureReason { get; }
        public string ErrorMessage { get; }
        public string Hint { get; }
        public IReadOnlyList<string> Warnings { get; }
        public string Warning => string.Join(" ", Warnings);
        public Type DeclaringType { get; }
        public bool HasPhysicsCallbackWarning { get; }

        private SourcePausePointPatchResult(
            bool success, SourcePausePointPatchFailureReason failureReason, string errorMessage, string hint,
            IReadOnlyList<string> warnings, Type declaringType, bool hasPhysicsCallbackWarning)
        {
            Success = success;
            FailureReason = failureReason;
            ErrorMessage = errorMessage;
            Hint = hint;
            Warnings = warnings ?? Array.Empty<string>();
            DeclaringType = declaringType;
            HasPhysicsCallbackWarning = hasPhysicsCallbackWarning;
        }

        public static SourcePausePointPatchResult SuccessResult(
            IReadOnlyList<string> warnings = null, Type declaringType = null, bool hasPhysicsCallbackWarning = false)
        {
            return new SourcePausePointPatchResult(
                true, SourcePausePointPatchFailureReason.None, string.Empty, string.Empty,
                warnings, declaringType, hasPhysicsCallbackWarning);
        }

        public static SourcePausePointPatchResult Failure(SourcePausePointPatchFailureReason reason, string errorMessage, string hint)
        {
            return new SourcePausePointPatchResult(
                false, reason, errorMessage, hint, Array.Empty<string>(), declaringType: null, hasPhysicsCallbackWarning: false);
        }
    }
}
