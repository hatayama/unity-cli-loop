using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Converts compiler/runtime messages into the user-facing guidance expected by execute-dynamic-code.
    /// </summary>
    internal sealed class DynamicCodeFriendlyErrorConverter
    {
        public DynamicCodeFriendlyError Convert(ExecutionResult result)
        {
            string originalMessage = BuildOriginalMessage(result);
            if (Contains(originalMessage, "Top-level statements must precede"))
            {
                return DynamicCodeFriendlyError.WithGuidance(
                    "There is an issue with the code structure",
                    "In the Dynamic Tool, class and namespace declarations are not required. Write Unity API processing directly.",
                    "GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);\nreturn \"Cube creation completed\";",
                    new List<string>
                    {
                        "Remove class definition and write only the code inside the method",
                        "Remove namespace declaration",
                        "Keep using statements and extract only the main processing"
                    });
            }

            if (Contains(originalMessage, "not all code paths return"))
            {
                return DynamicCodeFriendlyError.WithGuidance(
                    "A return value is required at the end of the code",
                    "In the Dynamic Tool, you must return the execution result. Please add a return statement at the end of the processing.",
                    "GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);\nreturn \"Cube creation completed\";",
                    new List<string>
                    {
                        "Add 'return \"Execution completed\";' at the end of the code",
                        "Add return statements to all paths of conditional branching",
                        "Return the execution result as a string"
                    });
            }

            if (Contains(originalMessage, "'Object' is an ambiguous reference"))
            {
                return DynamicCodeFriendlyError.WithGuidance(
                    "Object class reference is ambiguous",
                    "UnityEngine.Object and System.Object are mixed. Please specify explicitly.",
                    "UnityEngine.Object.DestroyImmediate(obj);",
                    new List<string>
                    {
                        "Explicitly specify UnityEngine.Object",
                        "Use full name description"
                    });
            }

            return DynamicCodeFriendlyError.Message(string.IsNullOrEmpty(result?.ErrorMessage)
                ? originalMessage
                : result.ErrorMessage);
        }

        private static string BuildOriginalMessage(ExecutionResult result)
        {
            if (result == null)
            {
                return string.Empty;
            }

            List<string> messageParts = new();
            AddCompilationErrorMessages(result, messageParts);
            AddIfNotEmpty(messageParts, result.ErrorMessage);
            if (result.Logs != null)
            {
                for (int index = 0; index < result.Logs.Count; index++)
                {
                    AddIfNotEmpty(messageParts, result.Logs[index]);
                }
            }

            return string.Join(" ", messageParts);
        }

        private static void AddCompilationErrorMessages(
            ExecutionResult result,
            List<string> messageParts)
        {
            if (result.CompilationErrors == null)
            {
                return;
            }

            for (int index = 0; index < result.CompilationErrors.Count; index++)
            {
                CompilationError error = result.CompilationErrors[index];
                if (error == null)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(error.ErrorCode))
                {
                    AddIfNotEmpty(messageParts, error.Message);
                    continue;
                }

                AddIfNotEmpty(messageParts, $"{error.ErrorCode}: {error.Message}");
            }
        }

        private static void AddIfNotEmpty(List<string> messageParts, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            messageParts.Add(value);
        }

        private static bool Contains(string source, string value)
        {
            return source?.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    /// <summary>
    /// Carries the friendly execute-dynamic-code failure details returned to CLI users.
    /// </summary>
    internal sealed class DynamicCodeFriendlyError
    {
        private DynamicCodeFriendlyError(
            string friendlyMessage,
            string explanation,
            string example,
            List<string> suggestedSolutions)
        {
            FriendlyMessage = friendlyMessage ?? string.Empty;
            Explanation = explanation ?? string.Empty;
            Example = example ?? string.Empty;
            SuggestedSolutions = suggestedSolutions ?? new List<string>();
        }

        public string FriendlyMessage { get; }

        public string Explanation { get; }

        public string Example { get; }

        public List<string> SuggestedSolutions { get; }

        public static DynamicCodeFriendlyError Message(string message)
        {
            return new DynamicCodeFriendlyError(message, string.Empty, string.Empty, new List<string>());
        }

        public static DynamicCodeFriendlyError WithGuidance(
            string friendlyMessage,
            string explanation,
            string example,
            List<string> suggestedSolutions)
        {
            return new DynamicCodeFriendlyError(friendlyMessage, explanation, example, suggestedSolutions);
        }
    }
}
