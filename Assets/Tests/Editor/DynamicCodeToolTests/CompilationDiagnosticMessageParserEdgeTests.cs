using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Verifies how names are read from compiler messages: generic arguments are dropped, and messages
    /// without the expected quoted phrases or with blank ones yield no name.
    /// </summary>
    public sealed class CompilationDiagnosticMessageParserEdgeTests
    {
        /// <summary>
        /// Verifies a quoted generic type name loses its type arguments and surrounding spaces.
        /// </summary>
        [Test]
        public void ExtractTypeNameFromMessage_WithAGenericName_DropsTheTypeArguments()
        {
            string result = CompilationDiagnosticMessageParser.ExtractTypeNameFromMessage(
                "The type or namespace name ' List<int> ' could not be found");

            Assert.That(result, Is.EqualTo("List"));
        }

        /// <summary>
        /// Verifies a blank quoted phrase is not taken as a type name.
        /// </summary>
        [Test]
        public void ExtractTypeNameFromMessage_WithABlankQuotedPhrase_ReturnsNull()
        {
            Assert.That(CompilationDiagnosticMessageParser.ExtractTypeNameFromMessage("name '  ' is invalid"), Is.Null);
        }

        /// <summary>
        /// Verifies a null message has no namespace name.
        /// </summary>
        [Test]
        public void ExtractNamespaceNameFromMessage_WithNull_ReturnsNull()
        {
            Assert.That(CompilationDiagnosticMessageParser.ExtractNamespaceNameFromMessage(null), Is.Null);
        }

        /// <summary>
        /// Verifies a blank second quoted phrase is not taken as a namespace.
        /// </summary>
        [Test]
        public void ExtractNamespaceNameFromMessage_WithABlankSecondPhrase_ReturnsNull()
        {
            Assert.That(
                CompilationDiagnosticMessageParser.ExtractNamespaceNameFromMessage("'Missing' does not exist in ' '"),
                Is.Null);
        }

        /// <summary>
        /// Verifies a generic member name loses its type arguments.
        /// </summary>
        [Test]
        public void ExtractMemberNameFromMessage_WithAGenericMember_DropsTheTypeArguments()
        {
            string result = CompilationDiagnosticMessageParser.ExtractMemberNameFromMessage(
                "'GameObject' does not contain a definition for 'GetThing<Camera>'");

            Assert.That(result, Is.EqualTo("GetThing"));
        }

        /// <summary>
        /// Verifies a null message has no member name.
        /// </summary>
        [Test]
        public void ExtractMemberNameFromMessage_WithNull_ReturnsNull()
        {
            Assert.That(CompilationDiagnosticMessageParser.ExtractMemberNameFromMessage(null), Is.Null);
        }

        /// <summary>
        /// Verifies a message with only one quoted phrase has no member name.
        /// </summary>
        [Test]
        public void ExtractMemberNameFromMessage_WithOneQuotedPhrase_ReturnsNull()
        {
            Assert.That(
                CompilationDiagnosticMessageParser.ExtractMemberNameFromMessage("'GameObject' does not contain it"),
                Is.Null);
        }
    }
}
