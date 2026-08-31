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
    }
}
