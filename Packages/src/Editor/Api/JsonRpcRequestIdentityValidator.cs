using System;

namespace io.github.hatayama.uLoopMCP
{
    internal static class JsonRpcRequestIdentityValidator
    {
        public static void Validate(
            JsonRpcRequestUloopMetadata metadata,
            string actualProjectRoot,
            string actualServerSessionId)
        {
            if (metadata == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(metadata.ExpectedProjectRoot))
            {
                throw new ParameterValidationException("Invalid x-uloop metadata: expectedProjectRoot is required.");
            }

            if (string.IsNullOrWhiteSpace(metadata.ExpectedServerSessionId))
            {
                throw new ParameterValidationException("Invalid x-uloop metadata: expectedServerSessionId is required.");
            }

            if (string.IsNullOrWhiteSpace(actualProjectRoot))
            {
                throw new ParameterValidationException("Fast project validation is unavailable. Restart Unity CLI Loop and retry.");
            }

            if (string.IsNullOrWhiteSpace(actualServerSessionId))
            {
                throw new ParameterValidationException("Unity CLI Loop server session changed. Retry the command.");
            }

            if (!string.Equals(metadata.ExpectedProjectRoot, actualProjectRoot, StringComparison.Ordinal))
            {
                throw new ParameterValidationException("Connected Unity instance belongs to a different project.");
            }

            if (!string.Equals(metadata.ExpectedServerSessionId, actualServerSessionId, StringComparison.Ordinal))
            {
                throw new ParameterValidationException("Unity CLI Loop server session changed. Retry the command.");
            }
        }
    }
}
