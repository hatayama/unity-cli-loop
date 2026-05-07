using System.Linq;
using NUnit.Framework;
using UnityEditor.Compilation;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Test fixture that verifies External Compiler Message Parser behavior.
    /// </summary>
    [TestFixture]
    public class ExternalCompilerMessageParserTests
    {
        [Test]
        public void Parse_WhenExitCodeIsNonZeroAndOnlyWarningsExist_ShouldAppendInfrastructureError()
        {
            CompilerMessage[] messages = ExternalCompilerMessageParser.Parse(
                "/tmp/test.cs(1,1): warning CS0168: The variable 'value' is declared but never used",
                "compiler infrastructure failure",
                1);

            Assert.That(messages.Count(message => message.type == CompilerMessageType.Warning), Is.EqualTo(1));
            Assert.That(messages.Count(message => message.type == CompilerMessageType.Error), Is.EqualTo(1));
            Assert.That(
                messages.Single(message => message.type == CompilerMessageType.Error).message,
                Does.Contain("exited with code 1"));
        }
    }
}
