using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Verifies the hints attached to compile diagnostics for ambiguous identifiers and wrapper-only lines,
    /// and that the user-snippet exception line is not inserted twice.
    /// </summary>
    public sealed class DynamicCodeExecutionResponseHintTests
    {
        private const string WrapperOriginHint =
            "This diagnostic line does not map to the user snippet; it likely refers to generated wrapper code.";

        private DynamicCodeExecutionResponseFactory _factory;

        [SetUp]
        public void SetUp()
        {
            _factory = new DynamicCodeExecutionResponseFactory();
        }

        /// <summary>
        /// Verifies an unknown identifier with several auto-using candidates lists each candidate namespace.
        /// </summary>
        [Test]
        public void ConvertExecutionResultToResponse_WithAnAmbiguousIdentifier_SuggestsEachCandidate()
        {
            ExecutionResult result = CreateFailure(new CompilationError
            {
                Line = 1,
                ErrorCode = "CS0103",
                Message = "The name 'Random' does not exist in the current context"
            });
            result.AmbiguousTypeCandidates = new Dictionary<string, List<string>>
            {
                { "Random", new List<string> { "UnityEngine", "System" } }
            };

            CompilationErrorDto diagnostic = _factory.ConvertExecutionResultToResponse(result).CompilationErrors[0];

            Assert.That(
                diagnostic.Hint,
                Is.EqualTo("Auto-using resolution found multiple candidates for 'Random': UnityEngine, System. Use a fully-qualified name or add the correct using directive."));
            Assert.That(diagnostic.Suggestions, Is.EqualTo(new List<string> { "Use UnityEngine.Random", "Use System.Random" }));
        }

        /// <summary>
        /// Verifies an ambiguous reference error tells the caller to qualify the name.
        /// </summary>
        [Test]
        public void ConvertExecutionResultToResponse_WithAnAmbiguousReference_SuggestsQualifyingIt()
        {
            ExecutionResult result = CreateFailure(new CompilationError
            {
                Line = 1,
                ErrorCode = "CS0104",
                Message = "'Object' is an ambiguous reference between 'UnityEngine.Object' and 'object'"
            });

            CompilationErrorDto diagnostic = _factory.ConvertExecutionResultToResponse(result).CompilationErrors[0];

            Assert.That(diagnostic.Hint, Is.EqualTo("Identifier is ambiguous; qualify explicitly (e.g., UnityEngine.Object)."));
            Assert.That(diagnostic.Suggestions, Is.EqualTo(new List<string> { "Qualify with full namespace (e.g., UnityEngine.Object)" }));
        }

        /// <summary>
        /// Verifies a diagnostic on a line outside the user snippet gets the wrapper-origin note after its hint.
        /// </summary>
        [Test]
        public void ConvertExecutionResultToResponse_WithALineOutsideTheUserSnippet_AppendsTheWrapperOriginHint()
        {
            ExecutionResult result = CreateFailure(new CompilationError
            {
                Line = 5,
                ErrorCode = "CS0104",
                Message = "'Object' is an ambiguous reference between 'UnityEngine.Object' and 'object'"
            });
            result.UpdatedCode = "class Wrapper\n{\n"
                + WrapperTemplate.UserCodeStartMarker + "\nint x = 1;\n"
                + WrapperTemplate.UserCodeEndMarker + "\n}";

            CompilationErrorDto diagnostic = _factory
                .ConvertExecutionResultToResponse(result, "int x = 1;")
                .CompilationErrors[0];

            Assert.That(
                diagnostic.Hint,
                Is.EqualTo("Identifier is ambiguous; qualify explicitly (e.g., UnityEngine.Object). " + WrapperOriginHint));
        }

        /// <summary>
        /// Verifies a diagnostic inside the user snippet gets no wrapper-origin note.
        /// </summary>
        [Test]
        public void ConvertExecutionResultToResponse_WithALineInsideTheUserSnippet_KeepsTheHintAlone()
        {
            ExecutionResult result = CreateFailure(new CompilationError
            {
                Line = 1,
                ErrorCode = "CS0104",
                Message = "'Object' is an ambiguous reference between 'UnityEngine.Object' and 'object'"
            });
            result.UpdatedCode = "class Wrapper\n{\n"
                + WrapperTemplate.UserCodeStartMarker + "\nint x = 1;\n"
                + WrapperTemplate.UserCodeEndMarker + "\n}";

            CompilationErrorDto diagnostic = _factory
                .ConvertExecutionResultToResponse(result, "int x = 1;")
                .CompilationErrors[0];

            Assert.That(diagnostic.Hint, Is.EqualTo("Identifier is ambiguous; qualify explicitly (e.g., UnityEngine.Object)."));
        }

        /// <summary>
        /// Verifies the user-snippet exception line is not inserted again when the logs already start with it.
        /// </summary>
        [Test]
        public void ConvertExecutionResultToResponse_WhenTheLogsAlreadyStartWithTheSnippetLine_DoesNotRepeatIt()
        {
            const string header = "Exception at user snippet line 3: boom";
            ExecutionResult result = new ExecutionResult
            {
                Success = true,
                ErrorMessage = "boom",
                Logs = new List<string> { header, "at Snippet in user-snippet.cs:line 3" }
            };

            ExecuteDynamicCodeResponse response = _factory.ConvertExecutionResultToResponse(result);

            Assert.That(response.Logs, Is.EqualTo(new List<string> { header, "at Snippet in user-snippet.cs:line 3" }));
        }

        private static ExecutionResult CreateFailure(CompilationError error)
        {
            return new ExecutionResult
            {
                Success = false,
                ErrorMessage = "Compilation failed",
                CompilationErrors = new List<CompilationError> { error }
            };
        }
    }
}
