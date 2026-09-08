using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies Compilation Diagnostic Message Parser behavior.
    /// </summary>
    public class CompilationDiagnosticMessageParserTests
    {
        [Test]
        public void ExtractTypeNameFromMessage_ReturnsNull_WhenMessageIsNull()
        {
            string result = CompilationDiagnosticMessageParser.ExtractTypeNameFromMessage(null);

            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Verifies the namespace of a CS0234 message is read from its second quoted phrase, not
        /// the first one that carries the missing type name.
        /// </summary>
        [Test]
        public void ExtractNamespaceNameFromMessage_ReturnsNamespace_WhenMessageNamesOne()
        {
            string result = CompilationDiagnosticMessageParser.ExtractNamespaceNameFromMessage(
                "The type or namespace name 'Widget' does not exist in the namespace 'Example' (are you missing an assembly reference?)");

            Assert.That(result, Is.EqualTo("Example"));
        }

        /// <summary>
        /// Verifies a message with a single quoted phrase, such as CS0246, reports no namespace.
        /// </summary>
        [Test]
        public void ExtractNamespaceNameFromMessage_ReturnsNull_WhenMessageHasOneQuotedPhrase()
        {
            string result = CompilationDiagnosticMessageParser.ExtractNamespaceNameFromMessage(
                "The type or namespace name 'Widget' could not be found");

            Assert.That(result, Is.Null);
        }
    }
}
