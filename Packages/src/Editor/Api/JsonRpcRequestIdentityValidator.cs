using System;

namespace io.github.hatayama.UnityCliLoop
{
    internal static class JsonRpcRequestIdentityValidator
    {
        public static void Validate(
            JsonRpcRequestUloopMetadata metadata,
            string actualProjectRoot)
        {
            if (metadata == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(metadata.ExpectedProjectRoot))
            {
                throw new ParameterValidationException("Invalid x-uloop metadata: expectedProjectRoot is required.");
            }

            if (string.IsNullOrWhiteSpace(actualProjectRoot))
            {
                throw new ParameterValidationException("Fast project validation is unavailable. Restart Unity CLI Loop and retry.");
            }

            if (!string.Equals(metadata.ExpectedProjectRoot, actualProjectRoot, StringComparison.Ordinal))
            {
                throw new ParameterValidationException("Connected Unity instance belongs to a different project.");
            }
        }
    }
}
