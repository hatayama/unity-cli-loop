namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// Domain value for platform rule validation.
    /// </summary>
    public class ValidationResult
    {
        /// <summary>
        /// Whether validation was successful
        /// </summary>
        public bool IsValid { get; }

        /// <summary>
        /// Error message when validation fails
        /// </summary>
        public string ErrorMessage { get; }

        /// <summary>
        /// Create ValidationResult
        /// </summary>
        /// <param name="isValid">Validation result</param>
        /// <param name="errorMessage">Error message (null on success)</param>
        public ValidationResult(bool isValid, string errorMessage = null)
        {
            IsValid = isValid;
            ErrorMessage = errorMessage;
        }

        /// <summary>
        /// Create success result
        /// </summary>
        /// <returns>ValidationResult representing success</returns>
        public static ValidationResult Success() => new(true);

        /// <summary>
        /// Create failure result
        /// </summary>
        /// <param name="errorMessage">Error message</param>
        /// <returns>ValidationResult representing failure</returns>
        public static ValidationResult Failure(string errorMessage) => new(false, errorMessage);
    }
}
