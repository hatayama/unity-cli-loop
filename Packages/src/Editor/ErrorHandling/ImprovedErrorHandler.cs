using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace io.github.hatayama.UnityCliLoop
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
    /// Translate raw errors/exceptions into structured data (no severity weighting)
    /// </summary>
    public interface IErrorTranslator
    {
        TranslationOutput TranslateFromException(Exception exception);
        TranslationOutput TranslateFromMessage(string errorMessage);
    }

    /// <summary>
    /// Format translation into DTO with severity weighting and final output shape
    /// </summary>
    public interface IErrorFormatter
    {
        UserFriendlyErrorDto Format(TranslationOutput translation, string originalError, Exception exception = null);
    }

    /// <summary>
    /// Default translator using pattern dictionary and simple fallbacks
    /// </summary>
    public class DefaultErrorTranslator : IErrorTranslator
    {
        private readonly ErrorTranslationDictionary _dictionary = new();
        private readonly FriendlyMessageGenerator _messageGenerator = new();

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

            if (exception is McpSecurityException securityException)
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
                    FriendlyMessage = $"Tool '{disabledException.ToolName}' is disabled. To enable it, go to {McpUIConstants.TOOL_SETTINGS_MENU_PATH}",
                    Explanation = "This tool has been disabled in project settings.",
                    Solutions = new List<string>
                    {
                        $"Enable '{disabledException.ToolName}' in {McpUIConstants.TOOL_SETTINGS_MENU_PATH}"
                    }
                };
            }

            if (exception is ParameterValidationException)
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

        public TranslationOutput TranslateFromMessage(string errorMessage)
        {
            ErrorPattern pattern = _dictionary.FindPattern(errorMessage);

            string friendly = pattern?.FriendlyMessage ?? _messageGenerator.TranslateError(errorMessage);
            string explanation = pattern?.Explanation ?? string.Empty;
            string example = pattern?.Example ?? string.Empty;
            List<string> solutions = pattern?.Solutions ?? new List<string>();
            List<string> tips = GetLearningTips(errorMessage);

            return new TranslationOutput
            {
                FriendlyMessage = friendly,
                Explanation = explanation,
                Example = example,
                Solutions = solutions,
                LearningTips = tips
            };
        }

        private List<string> GetLearningTips(string errorMessage)
        {
            List<string> tips = new();

            if (errorMessage.Contains("Top-level statements"))
            {
                tips.Add("In Dynamic Tool, you can write code directly");
                tips.Add("Class and namespace declarations are not required");
            }

            if (errorMessage.Contains("not all code paths return"))
            {
                tips.Add("Add a return statement at the end of the code");
                tips.Add("Return the execution result as a string");
            }

            if (errorMessage.Contains("ambiguous reference"))
            {
                tips.Add("Clearly distinguish between UnityEngine.Object and System.Object");
                tips.Add("Using the full name (UnityEngine.Object) is safer");
            }

            return tips;
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
                if (exception is McpSecurityException)
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
                if (exception is ParameterValidationException)
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
    /// Related Classes: FriendlyMessageGenerator, ErrorTranslationDictionary
    /// Now delegates to Translator and Formatter
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

        public UserFriendlyErrorConverter(IErrorTranslator translator, IErrorFormatter formatter)
        {
            _translator = translator ?? new DefaultErrorTranslator();
            _formatter = formatter ?? new DefaultErrorFormatter();
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

        /// <summary>
        /// Convert execution result errors into a more understandable format
        /// </summary>
        public UserFriendlyErrorDto ProcessError(
            ExecutionResult originalResult, 
            string originalCode)
        {
            string errorMessage = originalResult.ErrorMessage ?? string.Empty;
            if (originalResult.Logs?.Any() == true)
            {
                string combinedErrors = string.Join(" ", originalResult.Logs);
                if (!string.IsNullOrEmpty(combinedErrors))
                {
                    errorMessage += " " + combinedErrors;
                }
            }

            TranslationOutput translation = _translator.TranslateFromMessage(errorMessage);
            return _formatter.Format(translation, errorMessage);
        }
    }

    /// <summary>
    /// Error Translation Dictionary - Translating Common Error Patterns into Understandable Messages
    /// </summary>
    public class ErrorTranslationDictionary
    {
        private static readonly Dictionary<string, ErrorPattern> Patterns = new()
        {
            ["Top-level statements must precede"] = new ErrorPattern
            {
                PatternRegex = @"Top-level statements must precede",
                FriendlyMessage = "There is an issue with the code structure",
                Explanation = "In the Dynamic Tool, class and namespace declarations are not required. Write Unity API processing directly.",
                Example = @"AVOID: namespace Test { class MyClass { void Method() { ... } } }

CORRECT: GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
cube.transform.position = Vector3.zero;
return ""Cube creation completed"";",
                Solutions = new List<string>
                {
                    "Remove class definition and write only the code inside the method",
                    "Remove namespace declaration", 
                    "Keep using statements and extract only the main processing"
                }
            },
            
            ["not all code paths return a value"] = new ErrorPattern
            {
                PatternRegex = @"not all code paths return",
                FriendlyMessage = "A return value is required at the end of the code",
                Explanation = "In the Dynamic Tool, you must return the execution result. Please add a return statement at the end of the processing.",
                Example = @"AVOID: GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
Debug.Log(""Completed"");

CORRECT: GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
Debug.Log(""Completed"");
return ""Cube creation completed"";",
                Solutions = new List<string>
                {
                    "Add 'return \"Execution completed\";' at the end of the code",
                    "Add return statements to all paths of conditional branching",
                    "Return the execution result as a string"
                }
            },

            ["Object' is an ambiguous reference"] = new ErrorPattern
            {
                PatternRegex = @"'Object' is an ambiguous reference",
                FriendlyMessage = "Object class reference is ambiguous",
                Explanation = "UnityEngine.Object and System.Object are mixed. Please specify explicitly.",
                Example = @"AVOID: Object.DestroyImmediate(obj);

CORRECT: UnityEngine.Object.DestroyImmediate(obj);",
                Solutions = new List<string>
                {
                    "Explicitly specify UnityEngine.Object",
                    "Use full name description"
                }
            }
        };

        public ErrorPattern FindPattern(string errorMessage)
        {
            foreach (var kvp in Patterns)
            {
                if (Regex.IsMatch(errorMessage, kvp.Value.PatternRegex))
                {
                    return kvp.Value;
                }
            }
            return null;
        }
    }

    /// <summary>
    /// User-Friendly Message Generation Feature
    /// </summary>
    public class FriendlyMessageGenerator
    {
        public string TranslateError(string originalError)
        {
            // Basic error translation logic
            if (originalError.Contains("Top-level statements"))
            {
                return "There is an issue with the code structure. Class and namespace definitions are not required.";
            }
            
            if (originalError.Contains("not all code paths return"))
            {
                return "A return value is required at the end of the code. Please add 'return \"Completed\";'.";
            }
            
            if (originalError.Contains("ambiguous reference"))
            {
                return "Class reference is ambiguous. Please explicitly write UnityEngine.Object.";
            }
            
            if (originalError.Contains("Identifier expected"))
            {
                return "Syntax error. Please verify the code syntax.";
            }
            
            return originalError; // Fallback
        }
    }

    /// <summary>
    /// Error Pattern Definition
    /// </summary>
    public class ErrorPattern
    {
        public string PatternRegex { get; set; } = "";
        public string FriendlyMessage { get; set; } = "";
        public string Explanation { get; set; } = "";
        public string Example { get; set; } = "";
        public List<string> Solutions { get; set; } = new();
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
        High,
        Critical
    }
}