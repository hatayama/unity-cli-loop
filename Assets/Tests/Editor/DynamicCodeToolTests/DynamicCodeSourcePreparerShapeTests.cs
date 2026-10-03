using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Verifies how the source preparer handles snippets that mix declarations with statements, snippets
    /// without a return, and raw string literals that are not interpolated.
    /// </summary>
    public sealed class DynamicCodeSourcePreparerShapeTests
    {
        /// <summary>
        /// Verifies a type declaration mixed with top-level statements cannot be prepared.
        /// </summary>
        [Test]
        public void Prepare_WithATypeDeclarationAndStatements_ReturnsNoSource()
        {
            PreparedDynamicCode prepared = Prepare("class Helper { }\nreturn 1;");

            Assert.That(prepared.PreparedSource, Is.Null);
            Assert.That(prepared.IsScriptMode, Is.False);
        }

        /// <summary>
        /// Verifies statements without a return get a trailing return null.
        /// </summary>
        [Test]
        public void Prepare_WithoutAReturn_AppendsReturnNull()
        {
            PreparedDynamicCode prepared = DynamicCodeSourcePreparer.PrepareWithoutLiteralHoisting(
                "int x = 1;",
                DynamicCodeConstants.DEFAULT_NAMESPACE,
                DynamicCodeConstants.DEFAULT_CLASS_NAME);

            Assert.That(prepared.IsScriptMode, Is.True);
            Assert.That(UserSnippetLines(prepared), Is.EqualTo(new[] { "int x = 1;", "return null;" }));
        }

        /// <summary>
        /// Verifies a snippet with only using directives gets a body of just return null.
        /// </summary>
        [Test]
        public void Prepare_WithOnlyUsingDirectives_UsesReturnNullAsTheBody()
        {
            PreparedDynamicCode prepared = Prepare("using System.Text;");

            Assert.That(prepared.IsScriptMode, Is.True);
            Assert.That(UserSnippetLines(prepared), Is.EqualTo(new[] { "return null;" }));
            Assert.That(prepared.PreparedSource, Does.Contain("using System.Text;"));
        }

        /// <summary>
        /// Verifies a raw string literal that is not interpolated leaves literal hoisting on for the rest of the
        /// snippet.
        /// </summary>
        [Test]
        public void Prepare_WithANonInterpolatedRawString_StillHoistsOtherLiterals()
        {
            PreparedDynamicCode prepared = Prepare("string raw = \"\"\"text\"\"\";\nreturn \"hoisted\";");

            Assert.That(prepared.HoistedLiteralBindings, Is.Not.Empty);
        }

        private static string[] UserSnippetLines(PreparedDynamicCode prepared)
        {
            Assert.That(WrappedDynamicCodeUserSnippetExtractor.TryExtract(prepared.PreparedSource, out string snippet), Is.True);
            return WrappedDynamicCodeUserSnippetExtractor.SplitNormalizedLines(snippet);
        }

        private static PreparedDynamicCode Prepare(string source)
        {
            return DynamicCodeSourcePreparer.Prepare(
                source,
                DynamicCodeConstants.DEFAULT_NAMESPACE,
                DynamicCodeConstants.DEFAULT_CLASS_NAME);
        }
    }
}
