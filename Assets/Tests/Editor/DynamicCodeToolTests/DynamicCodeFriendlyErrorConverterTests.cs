using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Verifies which compiler and runtime messages get dedicated guidance and how the plain message is
    /// assembled for everything else.
    /// </summary>
    public sealed class DynamicCodeFriendlyErrorConverterTests
    {
        private DynamicCodeFriendlyErrorConverter _converter;

        [SetUp]
        public void SetUp()
        {
            _converter = new DynamicCodeFriendlyErrorConverter();
        }

        /// <summary>
        /// Verifies an ambiguous Object reference gets the guidance to name UnityEngine.Object explicitly.
        /// </summary>
        [Test]
        public void Convert_WithAnAmbiguousObjectReference_ExplainsHowToQualifyIt()
        {
            ExecutionResult result = new ExecutionResult
            {
                CompilationErrors = new List<CompilationError>
                {
                    new CompilationError { ErrorCode = "CS0104", Message = "'Object' is an ambiguous reference between 'UnityEngine.Object' and 'object'" }
                }
            };

            DynamicCodeFriendlyError error = _converter.Convert(result);

            Assert.That(error.FriendlyMessage, Is.EqualTo("Object class reference is ambiguous"));
            Assert.That(error.Example, Is.EqualTo("UnityEngine.Object.DestroyImmediate(obj);"));
            Assert.That(error.SuggestedSolutions, Is.EqualTo(new List<string> { "Explicitly specify UnityEngine.Object", "Use full name description" }));
        }

        /// <summary>
        /// Verifies a missing result yields an empty plain message.
        /// </summary>
        [Test]
        public void Convert_WithoutAResult_ReturnsAnEmptyMessage()
        {
            DynamicCodeFriendlyError error = _converter.Convert(null);

            Assert.That(error.FriendlyMessage, Is.Empty);
            Assert.That(error.SuggestedSolutions, Is.Empty);
        }

        /// <summary>
        /// Verifies that without an error message the plain message joins the compiler messages, coded or not,
        /// and the logs, skipping missing entries.
        /// </summary>
        [Test]
        public void Convert_WithoutAnErrorMessage_JoinsCompilerMessagesAndLogs()
        {
            ExecutionResult result = new ExecutionResult
            {
                CompilationErrors = new List<CompilationError>
                {
                    null,
                    new CompilationError { Message = "uncoded problem" },
                    new CompilationError { ErrorCode = "CS1002", Message = "; expected" }
                },
                Logs = new List<string> { "first log", string.Empty }
            };

            DynamicCodeFriendlyError error = _converter.Convert(result);

            Assert.That(error.FriendlyMessage, Is.EqualTo("uncoded problem CS1002: ; expected first log"));
        }

        /// <summary>
        /// Verifies a result without compiler errors or logs falls back to its error message.
        /// </summary>
        [Test]
        public void Convert_WithOnlyAnErrorMessage_ReturnsIt()
        {
            ExecutionResult result = new ExecutionResult
            {
                ErrorMessage = "runtime failure",
                CompilationErrors = null,
                Logs = null
            };

            DynamicCodeFriendlyError error = _converter.Convert(result);

            Assert.That(error.FriendlyMessage, Is.EqualTo("runtime failure"));
        }
    }
}
