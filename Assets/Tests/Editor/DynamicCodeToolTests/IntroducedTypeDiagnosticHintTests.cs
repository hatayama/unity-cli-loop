using System.Collections.Generic;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Covers the missing-type diagnostic explanation for hot-reload introduced types.
    /// </summary>
    [TestFixture]
    public sealed class IntroducedTypeDiagnosticHintTests
    {
        private const string SingleMatchHint =
            "'Widget' is a hot-reload introduced type (Example.Widget). execute-dynamic-code compiles against the compiled assemblies only, so an introduced type is not visible here until it is compiled. Use reflection through the loaded assembly (AppDomain.CurrentDomain.GetAssemblies) while it is active, or run 'uloop compile' to make it a compiled type.";

        private const string SingleMatchReflectionSuggestion =
            "Locate the type with AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes()).First(t => t.FullName == \"Example.Widget\") and drive it through reflection";

        private const string CompileSuggestion =
            "Run 'uloop compile' when the type is final, then reference it directly";

        /// <summary>
        /// Verifies a CS0246 whose type name is an active introduced type gets the introduced-type
        /// hint and both suggestions.
        /// </summary>
        [Test]
        public void TryBuild_WhenTypeNotFoundNamesAnActiveIntroducedType_ExplainsTheIntroducedType()
        {
            bool built = IntroducedTypeDiagnosticHint.TryBuild(
                "CS0246",
                "The type or namespace name 'Widget' could not be found",
                new List<string> { "Example.Widget" },
                out string hint,
                out List<string> suggestions);

            Assert.That(built, Is.True);
            Assert.That(hint, Is.EqualTo(SingleMatchHint));
            Assert.That(
                suggestions,
                Is.EqualTo(new[] { SingleMatchReflectionSuggestion, CompileSuggestion }));
        }

        /// <summary>
        /// Verifies a CS0234 whose type name and namespace match an active introduced type gets the
        /// same explanation as CS0246.
        /// </summary>
        [Test]
        public void TryBuild_WhenNamespaceMemberMissingMatchesAnIntroducedType_ExplainsTheIntroducedType()
        {
            bool built = IntroducedTypeDiagnosticHint.TryBuild(
                "CS0234",
                "The type or namespace name 'Widget' does not exist in the namespace 'Example' (are you missing an assembly reference?)",
                new List<string> { "Example.Widget" },
                out string hint,
                out List<string> suggestions);

            Assert.That(built, Is.True);
            Assert.That(hint, Is.EqualTo(SingleMatchHint));
            Assert.That(
                suggestions,
                Is.EqualTo(new[] { SingleMatchReflectionSuggestion, CompileSuggestion }));
        }

        /// <summary>
        /// Verifies a nested introduced type matches on its simple name while its namespace prefix
        /// still gates the CS0234 match.
        /// </summary>
        [Test]
        public void TryBuild_WhenIntroducedTypeIsNested_MatchesOnTheSimpleName()
        {
            bool built = IntroducedTypeDiagnosticHint.TryBuild(
                "CS0234",
                "The type or namespace name 'Widget' does not exist in the namespace 'Example'",
                new List<string> { "Example.Outer+Widget" },
                out string hint,
                out List<string> suggestions);

            Assert.That(built, Is.True);
            Assert.That(
                hint,
                Is.EqualTo(
                    "'Widget' is a hot-reload introduced type (Example.Outer+Widget). execute-dynamic-code compiles against the compiled assemblies only, so an introduced type is not visible here until it is compiled. Use reflection through the loaded assembly (AppDomain.CurrentDomain.GetAssemblies) while it is active, or run 'uloop compile' to make it a compiled type."));
            Assert.That(
                suggestions,
                Is.EqualTo(new[]
                {
                    "Locate the type with AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes()).First(t => t.FullName == \"Example.Outer+Widget\") and drive it through reflection",
                    CompileSuggestion
                }));
        }

        /// <summary>
        /// Verifies no explanation is produced when this domain holds no active introduced type.
        /// </summary>
        [Test]
        public void TryBuild_WhenNoIntroducedTypeIsActive_ProducesNothing()
        {
            bool built = IntroducedTypeDiagnosticHint.TryBuild(
                "CS0246",
                "The type or namespace name 'Widget' could not be found",
                new List<string>(),
                out string hint,
                out List<string> suggestions);

            Assert.That(built, Is.False);
            Assert.That(hint, Is.Empty);
            Assert.That(suggestions, Is.Null);
        }

        /// <summary>
        /// Verifies an identifier diagnostic is left to the existing hints even when an introduced
        /// type shares the name.
        /// </summary>
        [Test]
        public void TryBuild_WhenErrorCodeIsIdentifierNotFound_ProducesNothing()
        {
            bool built = IntroducedTypeDiagnosticHint.TryBuild(
                "CS0103",
                "The name 'Widget' does not exist in the current context",
                new List<string> { "Example.Widget" },
                out string hint,
                out List<string> suggestions);

            Assert.That(built, Is.False);
            Assert.That(hint, Is.Empty);
            Assert.That(suggestions, Is.Null);
        }

        /// <summary>
        /// Verifies a missing type that no introduced type is named after produces nothing.
        /// </summary>
        [Test]
        public void TryBuild_WhenNoIntroducedTypeSharesTheName_ProducesNothing()
        {
            bool built = IntroducedTypeDiagnosticHint.TryBuild(
                "CS0246",
                "The type or namespace name 'Widget' could not be found",
                new List<string> { "Example.Gadget" },
                out string hint,
                out List<string> suggestions);

            Assert.That(built, Is.False);
            Assert.That(hint, Is.Empty);
            Assert.That(suggestions, Is.Null);
        }

        /// <summary>
        /// Verifies a CS0234 does not match an introduced type of the same simple name that lives in
        /// another namespace.
        /// </summary>
        [Test]
        public void TryBuild_WhenNamespaceMemberMissingAndNamespacesDiffer_ProducesNothing()
        {
            bool built = IntroducedTypeDiagnosticHint.TryBuild(
                "CS0234",
                "The type or namespace name 'Widget' does not exist in the namespace 'Example'",
                new List<string> { "Other.Widget" },
                out string hint,
                out List<string> suggestions);

            Assert.That(built, Is.False);
            Assert.That(hint, Is.Empty);
            Assert.That(suggestions, Is.Null);
        }

        /// <summary>
        /// Verifies a CS0246 matching several introduced types lists all of them and suggests
        /// reflection for each.
        /// </summary>
        [Test]
        public void TryBuild_WhenSeveralIntroducedTypesShareTheName_ListsEveryCandidate()
        {
            bool built = IntroducedTypeDiagnosticHint.TryBuild(
                "CS0246",
                "The type or namespace name 'Widget' could not be found",
                new List<string> { "Example.Widget", "Other.Widget" },
                out string hint,
                out List<string> suggestions);

            Assert.That(built, Is.True);
            Assert.That(
                hint,
                Is.EqualTo(
                    "'Widget' matches these hot-reload introduced types: Example.Widget, Other.Widget. execute-dynamic-code compiles against the compiled assemblies only, so none of them is visible here until it is compiled. Pick the one you mean and use reflection through the loaded assembly (AppDomain.CurrentDomain.GetAssemblies), or run 'uloop compile' to make it a compiled type."));
            Assert.That(
                suggestions,
                Is.EqualTo(new[]
                {
                    SingleMatchReflectionSuggestion,
                    "Locate the type with AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes()).First(t => t.FullName == \"Other.Widget\") and drive it through reflection",
                    CompileSuggestion
                }));
        }
    }
}
