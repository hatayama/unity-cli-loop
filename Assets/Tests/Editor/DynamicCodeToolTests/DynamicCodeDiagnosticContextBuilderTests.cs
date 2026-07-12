using System;
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
        }

        [Test]
        public void TryExtract_WhenWrappedSourceContainsLineDirectives_ReturnsIndentedUserSnippet()
        {
            // Verifies wrapped sources expose only the user region between #line markers.
            string wrappedSource = WrapperTemplate.Build(
                Array.Empty<string>(),
                Array.Empty<string>(),
                "TestNs",
                "TestClass",
                "int a=1;\nint e= ;\nreturn a;");

            bool extracted = WrappedDynamicCodeUserSnippetExtractor.TryExtract(wrappedSource, out string userSnippet);

            Assert.That(extracted, Is.True);
            Assert.That(userSnippet, Does.Contain("int e= ;"));
            Assert.That(userSnippet, Does.Not.Contain("using UnityEditor;"));
        }
    }
}
