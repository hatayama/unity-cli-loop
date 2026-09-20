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
            "'Widget' is a hot-reload introduced type (Example.Widget), and the assembly holding it is referenced by this compilation while it stays active, so the name is what did not resolve. Write it as Example.Widget, or add a using for its namespace. Members that hot reload added to it are separate: those are not visible here at all, and not through reflection either; only code edited in the same reload sees them. If the name still does not resolve, reach the type through reflection (AppDomain.CurrentDomain.GetAssemblies), or run 'uloop compile' to make it a compiled type.";

        private const string SingleMatchNamingSuggestion =
            "Write it as Example.Widget, or add a using for its namespace";

        private const string SingleMatchReflectionSuggestion =
            "Locate the type with AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes()).First(t => t.FullName == \"Example.Widget\") and drive it through reflection";

        private const string CompileSuggestion =
            "Run 'uloop compile' when the type is final, then reference it as an ordinary compiled type";

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
            Assert.That(hint, Does.Contain("not through reflection either"));
            Assert.That(
                suggestions,
                Is.EqualTo(new[] { SingleMatchNamingSuggestion, SingleMatchReflectionSuggestion, CompileSuggestion }));
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
                Is.EqualTo(new[] { SingleMatchNamingSuggestion, SingleMatchReflectionSuggestion, CompileSuggestion }));
        }

        /// <summary>
        /// Verifies a nested introduced type matches on its simple name, its namespace prefix still
        /// gates the CS0234 match, and both spellings of the Cecil metadata name are reported where
        /// they belong: the C# form to write, the reflection form to compare a FullName against.
        /// </summary>
        [Test]
        public void TryBuild_WhenIntroducedTypeIsNested_MatchesOnTheSimpleName()
        {
            bool built = IntroducedTypeDiagnosticHint.TryBuild(
                "CS0234",
                "The type or namespace name 'Widget' does not exist in the namespace 'Example'",
                new List<string> { "Example.Outer/Widget" },
                out string hint,
                out List<string> suggestions);

            Assert.That(built, Is.True);
            Assert.That(
                hint,
                Is.EqualTo(
"'Widget' is a hot-reload introduced type (Example.Outer.Widget), and the assembly holding it is referenced by this compilation while it stays active, so the name is what did not resolve. Write it as Example.Outer.Widget; a using does not bring the simple name of a nested type into scope. Members that hot reload added to it are separate: those are not visible here at all, and not through reflection either; only code edited in the same reload sees them. If the name still does not resolve, reach the type through reflection (AppDomain.CurrentDomain.GetAssemblies), or run 'uloop compile' to make it a compiled type."));
            Assert.That(
                suggestions,
                Is.EqualTo(new[]
                {
                    "Write it as Example.Outer.Widget; a using does not bring the simple name of a nested type into scope",
                    "Locate the type with AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes()).First(t => t.FullName == \"Example.Outer+Widget\") and drive it through reflection",
                    CompileSuggestion
                }));
        }

        /// <summary>
        /// Verifies an introduced type in the global namespace is told to be written by its own
        /// name, with no advice to add a using it could not have.
        /// </summary>
        [Test]
        public void TryBuild_WhenIntroducedTypeHasNoNamespace_LeavesOutTheUsingAdvice()
        {
            bool built = IntroducedTypeDiagnosticHint.TryBuild(
                "CS0246",
                "The type or namespace name 'Widget' could not be found",
                new List<string> { "Widget" },
                out string hint,
                out List<string> suggestions);

            Assert.That(built, Is.True);
            Assert.That(hint, Does.Contain("Write it as Widget, which is already its full name"));
            Assert.That(hint, Does.Not.Contain("add a using"));
            Assert.That(
                suggestions,
                Is.EqualTo(new[]
                {
                    "Write it as Widget, which is already its full name",
                    "Locate the type with AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes()).First(t => t.FullName == \"Widget\") and drive it through reflection",
                    CompileSuggestion
                }));
        }

        /// <summary>
        /// Verifies a CS0234 that names a namespace segment of an active introduced type explains
        /// that type, because a qualified reference fails on the first unresolved segment.
        /// </summary>
        [Test]
        public void TryBuild_WhenNamespaceSegmentOfAnIntroducedTypeIsMissing_ExplainsTheIntroducedType()
        {
            bool built = IntroducedTypeDiagnosticHint.TryBuild(
                "CS0234",
                "The type or namespace name 'Generation' does not exist in the namespace 'Example' (are you missing an assembly reference?)",
                new List<string> { "Example.Generation.Widget" },
                out string hint,
                out List<string> suggestions);

            Assert.That(built, Is.True);
            Assert.That(
                hint,
                Does.StartWith("'Generation' is a hot-reload introduced type (Example.Generation.Widget),"));
            Assert.That(
                suggestions,
                Is.EqualTo(new[]
                {
                    "Write it as Example.Generation.Widget, or add a using for its namespace",
                    "Locate the type with AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes()).First(t => t.FullName == \"Example.Generation.Widget\") and drive it through reflection",
                    CompileSuggestion
                }));
        }

        /// <summary>
        /// Verifies a namespace segment that no introduced type sits under leaves the generic hint
        /// in place instead of naming an unrelated type.
        /// </summary>
        [Test]
        public void TryBuild_WhenNamespaceSegmentMatchesNoIntroducedType_LeavesTheGenericHint()
        {
            bool built = IntroducedTypeDiagnosticHint.TryBuild(
                "CS0234",
                "The type or namespace name 'Generation' does not exist in the namespace 'Example' (are you missing an assembly reference?)",
                new List<string> { "Example.Other.Widget" },
                out string hint,
                out List<string> suggestions);

            Assert.That(built, Is.False);
            Assert.That(hint, Is.Empty);
            Assert.That(suggestions, Is.Null);
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
        /// Verifies an identifier diagnostic whose name is an active introduced type gets the
        /// introduced-type explanation, which is what referring to such a type by its simple name
        /// produces, rather than the generic misspelling hint.
        /// </summary>
        [Test]
        public void TryBuild_WhenIdentifierNotFoundNamesAnActiveIntroducedType_ExplainsTheIntroducedType()
        {
            bool built = IntroducedTypeDiagnosticHint.TryBuild(
                "CS0103",
                "The name 'Widget' does not exist in the current context",
                new List<string> { "Example.Widget" },
                out string hint,
                out List<string> suggestions);

            Assert.That(built, Is.True);
            Assert.That(hint, Is.EqualTo(SingleMatchHint));
            Assert.That(
                suggestions,
                Is.EqualTo(new[] { SingleMatchNamingSuggestion, SingleMatchReflectionSuggestion, CompileSuggestion }));
        }

        /// <summary>
        /// Verifies an identifier diagnostic that no active introduced type is named after is left
        /// to the existing hints, so a plain misspelling still reads as one.
        /// </summary>
        [Test]
        public void TryBuild_WhenIdentifierNotFoundMatchesNoIntroducedType_ProducesNothing()
        {
            bool built = IntroducedTypeDiagnosticHint.TryBuild(
                "CS0103",
                "The name 'Widget' does not exist in the current context",
                new List<string> { "Example.Gadget" },
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
"'Widget' matches these hot-reload introduced types: Example.Widget, Other.Widget. The assembly holding each one is referenced by this compilation while it stays active, so pick the one you mean and write its full name as spelled here. Members that hot reload added to them are separate: those are not visible here at all, and not through reflection either; only code edited in the same reload sees them. If the name still does not resolve, reach the type through reflection (AppDomain.CurrentDomain.GetAssemblies), or run 'uloop compile' to make it a compiled type."));
            Assert.That(
                suggestions,
                Is.EqualTo(new[]
                {
                    SingleMatchNamingSuggestion,
                    SingleMatchReflectionSuggestion,
                    "Write it as Other.Widget, or add a using for its namespace",
                    "Locate the type with AppDomain.CurrentDomain.GetAssemblies().SelectMany(a => a.GetTypes()).First(t => t.FullName == \"Other.Widget\") and drive it through reflection",
                    CompileSuggestion
                }));
        }
    }
}
