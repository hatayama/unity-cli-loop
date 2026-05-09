using System;
using System.Collections.Generic;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Application
{
    /// <summary>
    /// Translation result from Translator before formatting
    /// </summary>
    public class TranslationOutput
    {
        public string FriendlyMessage { get; set; } = "";
        public string Explanation { get; set; } = "";
        public string Example { get; set; } = "";
        public List<string> Solutions { get; set; } = new();
        public List<string> LearningTips { get; set; } = new();
    }

    /// <summary>
    /// Translates raw exceptions into structured data before severity weighting.
    /// </summary>
    public interface IErrorTranslator
    {
        TranslationOutput TranslateFromException(Exception exception);
    }

    /// <summary>
    /// Format translation into DTO with severity weighting and final output shape
    /// </summary>
    public interface IErrorFormatter
    {
        UserFriendlyErrorDto Format(TranslationOutput translation, string originalError, Exception exception = null);
    }

    /// <summary>
    /// Maps known Unity CLI Loop exception types to stable user-facing messages.
    /// </summary>
    public class DefaultErrorTranslator : IErrorTranslator
    {
        public TranslationOutput TranslateFromException(Exception exception)
        {
            if (exception == null)
            {
                return new TranslationOutput
                {
                    FriendlyMessage = "Internal error",
                    Explanation = string.Empty,
                    Example = string.Empty,
                    Solutions = new List<string>(),
                    LearningTips = new List<string>()
                };
            }

            if (exception is UnityCliLoopSecurityException securityException)
            {
                return new TranslationOutput
                {
                    FriendlyMessage = "Tool blocked by security settings",
                    Explanation = securityException.SecurityReason ?? string.Empty,
                    Solutions = new List<string>
                    {
                        "Open UnityCliLoop Security Settings and enable the required permission"
                    }
                };
            }

            if (exception is TimeoutException)
            {
                return new TranslationOutput
                {
                    FriendlyMessage = "Request timeout",
                    Explanation = exception.Message,
                    Solutions = new List<string>
                    {
                        "Increase timeout or optimize the operation to complete faster"
                    }
                };
            }

            if (exception is ToolDisabledException disabledException)
            {
                return new TranslationOutput
                {
                    FriendlyMessage = $"Tool '{disabledException.ToolName}' is disabled. To enable it, go to {UnityCliLoopUIConstants.TOOL_SETTINGS_MENU_PATH}",
                    Explanation = "This tool has been disabled in project settings.",
                    Solutions = new List<string>
                    {
                        $"Enable '{disabledException.ToolName}' in {UnityCliLoopUIConstants.TOOL_SETTINGS_MENU_PATH}"
                    }
                };
            }

            if (exception is UnityCliLoopToolParameterValidationException)
            {
                return new TranslationOutput
                {
                    FriendlyMessage = exception.Message,
                    Explanation = string.Empty,
                    Solutions = new List<string>
                    {
                        "Fix parameter types/values according to the tool schema"
                    }
                };
            }

            return new TranslationOutput
            {
                FriendlyMessage = "Internal error",
                Explanation = exception.Message
            };
        }
    }

    /// <summary>
    /// Default formatter applying severity weighting and assembling the DTO
    /// </summary>
    public class DefaultErrorFormatter : IErrorFormatter
    {
        public UserFriendlyErrorDto Format(TranslationOutput translation, string originalError, Exception exception = null)
        {
            return new UserFriendlyErrorDto
            {
                OriginalError = originalError ?? string.Empty,
                FriendlyMessage = translation?.FriendlyMessage ?? string.Empty,
                Explanation = translation?.Explanation ?? string.Empty,
                Example = translation?.Example ?? string.Empty,
                SuggestedSolutions = translation?.Solutions ?? new List<string>(),
                LearningTips = translation?.LearningTips ?? new List<string>(),
                Severity = DetermineSeverity(originalError, exception)
            };
        }

        private ErrorSeverity DetermineSeverity(string errorMessage, Exception exception)
        {
            if (exception != null)
            {
                if (exception is UnityCliLoopSecurityException)
                {
                    return ErrorSeverity.High;
                }
                if (exception is TimeoutException)
                {
                    return ErrorSeverity.Medium;
                }
                if (exception is ToolDisabledException)
                {
                    return ErrorSeverity.Medium;
                }
                if (exception is UnityCliLoopToolParameterValidationException)
                {
                    return ErrorSeverity.Low;
                }
                return ErrorSeverity.High;
            }

            string msg = errorMessage ?? string.Empty;
            if (msg.Contains("Top-level statements") || msg.Contains("not all code paths"))
            {
                return ErrorSeverity.High;
            }
            if (msg.Contains("ambiguous reference"))
            {
                return ErrorSeverity.Medium;
            }
            return ErrorSeverity.Low;
        }
    }

    /// <summary>
    /// Converts exceptions into user-friendly API error data.
    /// </summary>
    public class UserFriendlyErrorConverter
    {
        private readonly IErrorTranslator _translator;
        private readonly IErrorFormatter _formatter;

        public UserFriendlyErrorConverter()
        {
            _translator = new DefaultErrorTranslator();
            _formatter = new DefaultErrorFormatter();
        }

        /// <summary>
        /// Convert an exception to an enhanced, user-friendly error response
        /// </summary>
        public UserFriendlyErrorDto ProcessException(Exception exception)
        {
            if (exception == null)
            {
                return new UserFriendlyErrorDto
                {
                    OriginalError = string.Empty,
                    FriendlyMessage = "Internal error",
                    Explanation = string.Empty,
                    Example = string.Empty,
                    SuggestedSolutions = new List<string>(),
                    LearningTips = new List<string>(),
                    Severity = ErrorSeverity.High
                };
            }

            TranslationOutput translation = _translator.TranslateFromException(exception);
            return _formatter.Format(translation, exception.Message, exception);
        }
    }

    /// <summary>
    /// User-friendly error DTO for API/UI surfaces
    /// </summary>
    public class UserFriendlyErrorDto
    {
        public string OriginalError { get; set; } = "";
        public string FriendlyMessage { get; set; } = "";
        public string Explanation { get; set; } = "";
        public string Example { get; set; } = "";
        public List<string> SuggestedSolutions { get; set; } = new();
        public List<string> LearningTips { get; set; } = new();
        public ErrorSeverity Severity { get; set; }
    }

    /// <summary>
    /// Error Severity
    /// </summary>
    public enum ErrorSeverity
    {
        Low,
        Medium,
        High
    }
}
