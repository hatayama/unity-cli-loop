using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// One compiled-PDB method span used to recover from a file:line resolve failure.
    /// </summary>
    internal sealed class SourcePausePointNearbyCompiledMethod
    {
        public string DisplayName { get; }
        public int StartLine { get; }
        public int EndLine { get; }

        public SourcePausePointNearbyCompiledMethod(string displayName, int startLine, int endLine)
        {
            Debug.Assert(!string.IsNullOrEmpty(displayName), "displayName must not be empty.");
            Debug.Assert(startLine > 0, "startLine must be a positive 1-based line number.");
            Debug.Assert(endLine >= startLine, "endLine must be on or after startLine.");
            DisplayName = displayName;
            StartLine = startLine;
            EndLine = endLine;
        }
    }

    /// <summary>
    /// Outcome of a file:line resolve attempt. Never thrown; failures are carried as data
    /// so callers can report a specific reason instead of an exception.
    /// </summary>
    internal sealed class SourcePausePointResolveResult
    {
        public bool Success { get; }
        public SourcePausePointResolveFailureReason FailureReason { get; }
        public string ErrorMessage { get; }
        public SourcePausePointResolution Resolution { get; }
        public IReadOnlyList<SourcePausePointNearbyCompiledMethod> NearbyCompiledMethods { get; }

        /// <summary>
        /// The line of the statement the failure names, in the same line numbers as ErrorMessage
        /// and NearbyCompiledMethods; 0 when it names none.
        /// </summary>
        public int StatementLine { get; }

        private SourcePausePointResolveResult(
            bool success,
            SourcePausePointResolveFailureReason failureReason,
            string errorMessage,
            SourcePausePointResolution resolution,
            IReadOnlyList<SourcePausePointNearbyCompiledMethod> nearbyCompiledMethods,
            int statementLine)
        {
            Success = success;
            FailureReason = failureReason;
            ErrorMessage = errorMessage;
            Resolution = resolution;
            NearbyCompiledMethods = nearbyCompiledMethods;
            StatementLine = statementLine;
        }

        public static SourcePausePointResolveResult SuccessResult(SourcePausePointResolution resolution)
        {
            Debug.Assert(resolution != null, "resolution must not be null for a success result.");
            return new SourcePausePointResolveResult(
                true,
                SourcePausePointResolveFailureReason.None,
                string.Empty,
                resolution,
                Array.Empty<SourcePausePointNearbyCompiledMethod>(),
                0);
        }

        public static SourcePausePointResolveResult Failure(
            SourcePausePointResolveFailureReason reason,
            string errorMessage,
            IReadOnlyList<SourcePausePointNearbyCompiledMethod> nearbyCompiledMethods = null,
            int statementLine = 0)
        {
            Debug.Assert(reason != SourcePausePointResolveFailureReason.None, "Failure requires a specific reason.");
            Debug.Assert(statementLine >= 0, "statementLine must be a 1-based line number, or 0 when the failure names none.");
            return new SourcePausePointResolveResult(
                false,
                reason,
                errorMessage,
                null,
                nearbyCompiledMethods ?? Array.Empty<SourcePausePointNearbyCompiledMethod>(),
                statementLine);
        }
    }
}
