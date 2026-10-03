using NUnit.Framework;
using UnityEditor.Compilation;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Verifies what the external compiler parser reports when the output holds no recognizable diagnostics:
    /// nothing on success, and the raw output from whichever stream carried it on failure.
    /// </summary>
    public sealed class ExternalCompilerMessageParserOutputTests
    {
        /// <summary>
        /// Verifies a successful run without diagnostics reports nothing.
        /// </summary>
        [Test]
        public void Parse_WithASuccessfulRunAndNoDiagnostics_ReturnsNoMessages()
        {
            Assert.That(ExternalCompilerMessageParser.Parse("build ok", string.Empty, 0), Is.Empty);
        }

        /// <summary>
        /// Verifies a failure with output only on stderr reports that output.
        /// </summary>
        [Test]
        public void Parse_WithAFailureOnlyOnStderr_ReportsTheStderrText()
        {
            CompilerMessage[] messages = ExternalCompilerMessageParser.Parse("  ", "  host crashed  ", 1);

            Assert.That(messages.Length, Is.EqualTo(1));
            Assert.That(messages[0].type, Is.EqualTo(CompilerMessageType.Error));
            Assert.That(messages[0].message, Is.EqualTo("host crashed"));
        }

        /// <summary>
        /// Verifies a failure with output only on stdout reports that output.
        /// </summary>
        [Test]
        public void Parse_WithAFailureOnlyOnStdout_ReportsTheStdoutText()
        {
            CompilerMessage[] messages = ExternalCompilerMessageParser.Parse("  bad response file  ", null, 1);

            Assert.That(messages.Length, Is.EqualTo(1));
            Assert.That(messages[0].message, Is.EqualTo("bad response file"));
        }
    }
}
