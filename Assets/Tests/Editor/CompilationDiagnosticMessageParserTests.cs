using NUnit.Framework;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
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
