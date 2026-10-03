using NUnit.Framework;
using UnityEditor.Compilation;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Verifies how compiler messages are split into errors and warnings, how error codes are read, and when
    /// the result is flagged as having ambiguity errors.
    /// </summary>
    public sealed class CompilerDiagnosticsTests
    {
        /// <summary>
        /// Verifies warnings and errors are separated, other message types are dropped, and each error keeps its
        /// code and position.
        /// </summary>
        [Test]
        public void FromMessages_SplitsWarningsFromErrorsAndDropsOtherTypes()
        {
            // Why a cast: only Error and Warning are named on every supported editor version.
            CompilerMessageType otherType = (CompilerMessageType)2;

            CompilerDiagnostics diagnostics = CompilerDiagnostics.FromMessages(new[]
            {
                new CompilerMessage { type = CompilerMessageType.Warning, message = "warning CS0168: unused" },
                new CompilerMessage { type = otherType, message = "note" },
                new CompilerMessage { type = CompilerMessageType.Error, message = "error CS1002: ; expected", line = 3, column = 7 }
            });

            Assert.That(diagnostics.Warnings, Is.EqualTo(new[] { "warning CS0168: unused" }));
            Assert.That(diagnostics.Errors.Count, Is.EqualTo(1));
            Assert.That(diagnostics.Errors[0].ErrorCode, Is.EqualTo("CS1002"));
            Assert.That(diagnostics.Errors[0].Line, Is.EqualTo(3));
            Assert.That(diagnostics.Errors[0].Column, Is.EqualTo(7));
            Assert.That(diagnostics.HasAmbiguityErrors, Is.False);
        }

        /// <summary>
        /// Verifies an error message without a CS code is given the UNKNOWN code.
        /// </summary>
        [Test]
        public void FromMessages_WithoutACode_UsesUnknown()
        {
            CompilerDiagnostics diagnostics = CompilerDiagnostics.FromMessages(new[]
            {
                new CompilerMessage { type = CompilerMessageType.Error, message = "compiler crashed" }
            });

            Assert.That(diagnostics.Errors[0].ErrorCode, Is.EqualTo("UNKNOWN"));
        }

        /// <summary>
        /// Verifies CS0104 and CS0234 each mark the result as having ambiguity errors.
        /// </summary>
        [TestCase("error CS0104: 'Object' is an ambiguous reference")]
        [TestCase("error CS0234: The type 'Foo' does not exist in the namespace 'Bar'")]
        public void FromMessages_WithAnAmbiguityCode_FlagsAmbiguityErrors(string message)
        {
            CompilerDiagnostics diagnostics = CompilerDiagnostics.FromMessages(new[]
            {
                new CompilerMessage { type = CompilerMessageType.Error, message = message }
            });

            Assert.That(diagnostics.HasAmbiguityErrors, Is.True);
        }
    }
}
