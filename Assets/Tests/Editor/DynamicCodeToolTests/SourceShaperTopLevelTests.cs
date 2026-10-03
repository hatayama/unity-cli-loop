using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Verifies how the source shaper classifies top-level segments: comments, static using directives,
    /// global-qualified statements, attributed or static local functions, and identifiers that only start
    /// with a keyword.
    /// </summary>
    public sealed class SourceShaperTopLevelTests
    {
        /// <summary>
        /// Verifies a top-level block comment is skipped and does not end up in the statement body.
        /// </summary>
        [Test]
        public void Analyze_WithALeadingBlockComment_KeepsItOutOfTheBody()
        {
            SourceShapeResult result = SourceShaper.Analyze("/* header note */\nint x = 1;");

            Assert.That(result.HasTopLevelStatements, Is.True);
            Assert.That(result.TopLevelBodyBuilder.ToString(), Does.Contain("int x = 1;"));
            Assert.That(result.TopLevelBodyBuilder.ToString(), Does.Not.Contain("header note"));
        }

        /// <summary>
        /// Verifies a using static directive is collected as a directive and defines no alias.
        /// </summary>
        [Test]
        public void Analyze_WithAUsingStaticDirective_CollectsItWithoutAnAlias()
        {
            SourceShapeResult result = SourceShaper.Analyze("using static System.Math;\nreturn Abs(-1);");

            Assert.That(result.UsingDirectives, Is.EqualTo(new[] { "using static System.Math;" }));
            Assert.That(result.AliasedNames, Is.Empty);
            Assert.That(result.TopLevelBodyBuilder.ToString(), Does.Not.Contain("using static"));
        }

        /// <summary>
        /// Verifies a statement that starts with the global:: qualifier is a statement, not a global using.
        /// </summary>
        [Test]
        public void Analyze_WithAGlobalQualifiedStatement_TreatsItAsAStatement()
        {
            SourceShapeResult result = SourceShaper.Analyze("global::System.Console.WriteLine(1);");

            Assert.That(result.UsingDirectives, Is.Empty);
            Assert.That(result.HasTopLevelStatements, Is.True);
        }

        /// <summary>
        /// Verifies an attributed local function is a statement, not a type declaration.
        /// </summary>
        [Test]
        public void Analyze_WithAnAttributedLocalFunction_TreatsItAsAStatement()
        {
            SourceShapeResult result = SourceShaper.Analyze("[System.Obsolete] void Helper() { }\nHelper();");

            Assert.That(result.HasTypeDeclaration, Is.False);
            Assert.That(result.HasTopLevelStatements, Is.True);
        }

        /// <summary>
        /// Verifies a static local function is a statement even though static is also a type modifier.
        /// </summary>
        [Test]
        public void Analyze_WithAStaticLocalFunction_TreatsItAsAStatement()
        {
            SourceShapeResult result = SourceShaper.Analyze("static int Twice(int x) => x * 2;\nreturn Twice(2);");

            Assert.That(result.HasTypeDeclaration, Is.False);
            Assert.That(result.HasTopLevelStatements, Is.True);
        }

        /// <summary>
        /// Verifies an identifier that merely starts with a type keyword is not a type declaration.
        /// </summary>
        [Test]
        public void Analyze_WithAnIdentifierStartingWithAKeyword_TreatsItAsAStatement()
        {
            SourceShapeResult result = SourceShaper.Analyze("int classCount = 1;\nclassCount++;");

            Assert.That(result.HasTypeDeclaration, Is.False);
            Assert.That(result.HasTopLevelStatements, Is.True);
        }
    }
}
