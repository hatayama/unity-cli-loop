using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Validates JSON RPC Request Identity data before the owning workflow continues.
    /// </summary>
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

            ProjectRootIdentityValidationResult validation = ProjectRootIdentityValidator.Validate(
                metadata.ExpectedProjectRoot,
                actualProjectRoot);
            if (!validation.IsValid)
            {
                throw new UnityCliLoopToolParameterValidationException(validation.ErrorMessage);
            }
        }
    }
}
