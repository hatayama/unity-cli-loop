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
        /// Optional machine-readable code for the failure. Null on success or when the
        /// caller only has a human-readable message.
        /// </summary>
        public string ErrorCode { get; }

        /// <summary>
        /// Create ValidationResult
        /// </summary>
        /// <param name="isValid">Validation result</param>
        /// <param name="errorMessage">Error message (null on success)</param>
        /// <param name="errorCode">Optional machine-readable failure code</param>
        public ValidationResult(bool isValid, string errorMessage = null, string errorCode = null)
        {
            IsValid = isValid;
            ErrorMessage = errorMessage;
            ErrorCode = errorCode;
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

        /// <summary>
        /// Create a failure result that also carries a machine-readable error code.
        /// Why a separate method: overloads are forbidden, and most callers only have a message.
        /// </summary>
        public static ValidationResult FailureWithErrorCode(string errorMessage, string errorCode)
        {
            return new ValidationResult(false, errorMessage, errorCode);
        }
    }
}
