namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Compatibility wrapper that keeps the old type name while delegating to the structured metadata validator.
    /// The preload security check must inspect real metadata rows so user literals do not trigger false positives.
    /// </summary>
    internal sealed class PreloadMetadataSecurityValidator : IPreloadAssemblySecurityValidator
    {
        private readonly SystemReflectionMetadataPreloadValidator _validator = new SystemReflectionMetadataPreloadValidator();

        public SecurityValidationResult Validate(byte[] assemblyBytes)
        {
            return _validator.Validate(assemblyBytes);
        }
    }
}
