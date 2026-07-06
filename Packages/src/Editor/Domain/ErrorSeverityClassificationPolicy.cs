namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// User-facing severity applied to structured error responses.
    /// </summary>
    public enum ErrorSeverity
    {
        Low,
        Medium,
        High
    }

    /// <summary>
    /// Domain category used to classify errors without depending on application exception types.
    /// </summary>
    public enum ErrorSeverityCategory
    {
        UserMessage,
        BlockingCompilerMessage,
        AmbiguousCompilerMessage,
        SecurityViolation,
        RecoverableExecutionState,
        ParameterValidation,
        UnknownException
    }

    /// <summary>
    /// Maps domain-neutral error categories to user-facing severity.
    /// </summary>
    public static class ErrorSeverityClassificationPolicy
    {
        public static ErrorSeverity DetermineSeverity(ErrorSeverityCategory category)
        {
            switch (category)
            {
                case ErrorSeverityCategory.SecurityViolation:
                case ErrorSeverityCategory.BlockingCompilerMessage:
                case ErrorSeverityCategory.UnknownException:
                    return ErrorSeverity.High;
                case ErrorSeverityCategory.RecoverableExecutionState:
                case ErrorSeverityCategory.AmbiguousCompilerMessage:
                    return ErrorSeverity.Medium;
                case ErrorSeverityCategory.ParameterValidation:
                case ErrorSeverityCategory.UserMessage:
                    return ErrorSeverity.Low;
                default:
                    return ErrorSeverity.High;
            }
        }
    }
}
