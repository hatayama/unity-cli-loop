using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    [TestFixture]
    public sealed class DynamicCodeDiagnosticContextBuilderTests
    {
        [Test]
        public void BuildContext_WhenUserLineFiveHasSyntaxError_ShowsUserSnippetNotWrapperBoilerplate()
        {
            // Verifies #line-mapped user line 5 renders against the user snippet instead of wrapper usings.
            string[] userSnippetLines =
            {
                "int a=1;",
                "int b=2;",
                "int c=3;",
                "int d=4;",
                "int e= ;",
                "return a;"
            };

            string context = DynamicCodeDiagnosticContextBuilder.BuildContext(userSnippetLines, 5, 8);

            Assert.That(context, Does.Contain("L5:int e= ;"));
            Assert.That(context, Does.Not.Contain("using System.Collections.Generic"));
            Assert.That(context, Does.Not.Contain("L7:"));
        }

        [Test]
        public void BuildContext_WhenUserColumnProvided_PlacesCaretUnderErrorCharacter()
        {
            // Verifies the caret line uses the same 1-based column basis as the rendered user line.
            string[] userSnippetLines = { "int e= ;" };
            string context = DynamicCodeDiagnosticContextBuilder.BuildContext(userSnippetLines, 1, 8);
            string[] contextLines = context.Split('\n');

            Assert.That(contextLines[1].IndexOf('^'), Is.EqualTo("L1:".Length + 7));
        }

        [Test]
        public void TryExtract_WhenWrappedSourceContainsLineDirectives_ReturnsIndentedUserSnippet()
        {
            // Verifies wrapped sources expose only the user region between #line markers.
            string wrappedSource = WrapperTemplate.Build(
                System.Array.Empty<string>(),
                System.Array.Empty<string>(),
                "TestNs",
                "TestClass",
                "int a=1;\nint e= ;\nreturn a;");

            bool extracted = WrappedDynamicCodeUserSnippetExtractor.TryExtract(wrappedSource, out string userSnippet);

            Assert.That(extracted, Is.True);
            Assert.That(userSnippet, Does.Contain("int e= ;"));
            Assert.That(userSnippet, Does.Not.Contain("using UnityEditor;"));
        }

        [Test]
        public void Split_WhenOriginalSnippetHasTrailingNewline_DropsTrailingEmptyLine()
        {
            // Verifies trailing newline does not create an extra empty context line.
            string[] lines = DynamicCodeUserSnippetLines.Split("int a=1;\nreturn a;\n");

            Assert.That(lines, Has.Length.EqualTo(2));
            Assert.That(lines[0], Is.EqualTo("int a=1;"));
        }
    }
}
