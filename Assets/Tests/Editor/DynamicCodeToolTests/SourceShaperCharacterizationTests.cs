using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Characterization tests that lock current SourceShaper Analyze/WrapIfNeeded outputs before god-class splits.
    /// </summary>
    [TestFixture]
    public class SourceShaperCharacterizationTests
    {
        /// <summary>
        /// Pins Analyze classification for a plain top-level return script.
        /// </summary>
        [Test]
        public void Analyze_WhenTopLevelReturn_ShouldMarkTopLevelStatementsOnly()
        {
            SourceShapeResult result = SourceShaper.Analyze("return 1;");

            Assert.That(result.HasTopLevelStatements, Is.True);
            Assert.That(result.HasTypeDeclaration, Is.False);
            Assert.That(result.HasNamespaceDeclaration, Is.False);
            Assert.That(result.UsingDirectives, Is.Empty);
            Assert.That(result.TopLevelBodyBuilder.ToString().TrimEnd(), Is.EqualTo("return 1;"));
        }

        /// <summary>
        /// Pins using-directive extraction and remaining body content.
        /// </summary>
        [Test]
        public void Analyze_WhenUsingDirectiveThenReturn_ShouldExtractUsingAndBody()
        {
            SourceShapeResult result = SourceShaper.Analyze(
                "using System.Text;\nreturn Encoding.UTF8;");

            Assert.That(result.HasTopLevelStatements, Is.True);
            Assert.That(result.UsingDirectives, Has.Count.EqualTo(1));
            Assert.That(result.UsingDirectives[0], Is.EqualTo("using System.Text;"));
            // why: Analyze keeps the newline that followed the using directive in TopLevelBodyBuilder
            Assert.That(result.TopLevelBodyBuilder.ToString().TrimEnd(), Is.EqualTo("\nreturn Encoding.UTF8;"));
        }

        /// <summary>
        /// Pins type-only sources as raw declarations without top-level statements.
        /// </summary>
        [Test]
        public void Analyze_WhenTypeDeclarationOnly_ShouldMarkTypeDeclaration()
        {
            SourceShapeResult result = SourceShaper.Analyze("public class Example { }");

            Assert.That(result.HasTypeDeclaration, Is.True);
            Assert.That(result.HasTopLevelStatements, Is.False);
            Assert.That(result.HasNamespaceDeclaration, Is.False);
        }

        /// <summary>
        /// Pins namespace block sources as namespace declarations; nested types stay unmarked.
        /// </summary>
        [Test]
        public void Analyze_WhenNamespaceDeclarationOnly_ShouldMarkNamespaceDeclaration()
        {
            SourceShapeResult result = SourceShaper.Analyze("namespace Demo { public class Example { } }");

            Assert.That(result.HasNamespaceDeclaration, Is.True);
            // why: nested types inside a skipped namespace block are not separately flagged
            Assert.That(result.HasTypeDeclaration, Is.False);
            Assert.That(result.HasTopLevelStatements, Is.False);
        }

        /// <summary>
        /// Pins WrapIfNeeded passthrough for raw type declarations.
        /// </summary>
        [Test]
        public void WrapIfNeeded_WhenTypeDeclarationOnly_ShouldReturnOriginalSource()
        {
            string source = "public class Example { }";

            string wrapped = SourceShaper.WrapIfNeeded(
                source,
                DynamicCodeConstants.DEFAULT_NAMESPACE,
                DynamicCodeConstants.DEFAULT_CLASS_NAME);

            Assert.That(wrapped, Is.EqualTo(source));
        }

        /// <summary>
        /// Pins WrapIfNeeded rejecting mixed type + top-level statement sources.
        /// </summary>
        [Test]
        public void WrapIfNeeded_WhenMixedTypeAndTopLevel_ShouldReturnNull()
        {
            string wrapped = SourceShaper.WrapIfNeeded(
                "class Example { }\nreturn 1;",
                DynamicCodeConstants.DEFAULT_NAMESPACE,
                DynamicCodeConstants.DEFAULT_CLASS_NAME);

            Assert.That(wrapped, Is.Null);
        }

        /// <summary>
        /// Pins WrapIfNeeded wrapping a script that already returns a value.
        /// </summary>
        [Test]
        public void WrapIfNeeded_WhenTopLevelReturn_ShouldWrapWithUserBodyIntact()
        {
            string wrapped = SourceShaper.WrapIfNeeded(
                "return 1;",
                DynamicCodeConstants.DEFAULT_NAMESPACE,
                DynamicCodeConstants.DEFAULT_CLASS_NAME);

            Assert.That(wrapped, Is.Not.Null);
            Assert.That(wrapped, Does.Contain("namespace " + DynamicCodeConstants.DEFAULT_NAMESPACE));
            Assert.That(wrapped, Does.Contain("public class " + DynamicCodeConstants.DEFAULT_CLASS_NAME));
            Assert.That(wrapped, Does.Contain(WrapperTemplate.UserCodeStartMarker));
            Assert.That(wrapped, Does.Contain("            return 1;"));
            Assert.That(wrapped, Does.Contain(WrapperTemplate.UserCodeEndMarker));
            Assert.That(wrapped, Does.Not.Contain("return null;"));
        }

        /// <summary>
        /// Pins WrapIfNeeded injecting a trailing return null for scripts without an explicit return.
        /// </summary>
        [Test]
        public void WrapIfNeeded_WhenNoReturn_ShouldAppendReturnNull()
        {
            string wrapped = SourceShaper.WrapIfNeeded(
                "var x = 1;",
                DynamicCodeConstants.DEFAULT_NAMESPACE,
                DynamicCodeConstants.DEFAULT_CLASS_NAME);

            Assert.That(wrapped, Is.Not.Null);
            Assert.That(wrapped, Does.Contain("            var x = 1;"));
            Assert.That(wrapped, Does.Contain("            return null;"));
        }

        /// <summary>
        /// Pins WrapIfNeeded wrapping an empty script as a null-returning method body.
        /// </summary>
        [Test]
        public void WrapIfNeeded_WhenEmptySource_ShouldWrapReturnNullOnly()
        {
            string wrapped = SourceShaper.WrapIfNeeded(
                "",
                DynamicCodeConstants.DEFAULT_NAMESPACE,
                DynamicCodeConstants.DEFAULT_CLASS_NAME);

            Assert.That(wrapped, Is.Not.Null);
            Assert.That(wrapped, Does.Contain("            return null;"));
        }

        /// <summary>
        /// Pins using-alias detection flowing into WrapIfNeeded alias tracking.
        /// </summary>
        [Test]
        public void WrapIfNeeded_WhenUsingAlias_ShouldPreserveAliasDirectiveInWrapper()
        {
            string wrapped = SourceShaper.WrapIfNeeded(
                "using Object = System.Object;\nreturn new Object();",
                DynamicCodeConstants.DEFAULT_NAMESPACE,
                DynamicCodeConstants.DEFAULT_CLASS_NAME);

            Assert.That(wrapped, Is.Not.Null);
            Assert.That(wrapped, Does.Contain("using Object = System.Object;"));
            Assert.That(wrapped, Does.Contain("            return new Object();"));
        }
    }
}
