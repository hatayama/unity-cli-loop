using System.Text.RegularExpressions;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies the run-tests class filter type.
    /// </summary>
    public sealed class RunTestsClassFilterTests
    {
        private TestFilterCreationService filterService;

        [SetUp]
        public void Setup()
        {
            filterService = new TestFilterCreationService();
        }

        [Test]
        public void TryCreateFilter_WithClassType_ShouldReturnRegexFilterScopedToTheClass()
        {
            // Verifies the class filter type is delivered to Unity as a group-name regex, because
            // Unity's Filter has no dedicated class field.
            (TestExecutionFilter result, string errorMessage) = filterService.TryCreateFilter(TestFilterType.@class, "PlayerTests");

            Assert.That(errorMessage, Is.Null);
            Assert.That(result.FilterType, Is.EqualTo(TestExecutionFilterType.Regex));
            Assert.That(result.FilterValue, Is.EqualTo(TestExecutionFilter.ByTestClass("PlayerTests").FilterValue));
        }

        [Test]
        public void TryCreateFilter_WithClassTypeAndEmptyValue_ShouldReturnErrorMessage()
        {
            // Verifies an empty class name is rejected up front instead of producing a pattern that
            // silently matches nothing.
            (TestExecutionFilter result, string errorMessage) = filterService.TryCreateFilter(TestFilterType.@class, " ");

            Assert.That(result, Is.Null);
            Assert.That(errorMessage, Does.Contain("class"));
        }

        [TestCase("MyGame.Tests.PlayerTests.Jump_AddsVelocity")]
        [TestCase("MyGame.Tests.PlayerTests.Jump_AddsVelocity(1.5,\"a.b\")")]
        [TestCase("PlayerTests.Jump_AddsVelocity")]
        public void ByTestClass_WithBareClassName_MatchesEveryTestOfThatClass(string fullName)
        {
            // Verifies a bare class name matches the class regardless of namespace and of parameterized test names.
            Regex pattern = new Regex(TestExecutionFilter.ByTestClass("PlayerTests").FilterValue);

            Assert.That(pattern.IsMatch(fullName), Is.True);
        }

        [TestCase("MyGame.Tests.PlayerTestsExtra.Jump_AddsVelocity")]
        [TestCase("MyGame.Tests.EnemyPlayerTests.Jump_AddsVelocity")]
        [TestCase("MyGame.PlayerTests.Nested.Jump_AddsVelocity")]
        [TestCase("MyGame.Tests.PlayerTests")]
        public void ByTestClass_WithBareClassName_DoesNotMatchOtherClassesOrNamespaces(string fullName)
        {
            // Verifies the class name is anchored on both sides so prefixes, suffixes, namespaces of the
            // same name, and the fixture node itself do not match.
            Regex pattern = new Regex(TestExecutionFilter.ByTestClass("PlayerTests").FilterValue);

            Assert.That(pattern.IsMatch(fullName), Is.False);
        }

        [Test]
        public void ByTestClass_WithQualifiedClassName_MatchesOnlyThatNamespace()
        {
            // Verifies a namespace-qualified class name restricts the match to that namespace and that
            // regex metacharacters in the name are treated literally.
            Regex pattern = new Regex(TestExecutionFilter.ByTestClass("MyGame.Tests.PlayerTests").FilterValue);

            Assert.That(pattern.IsMatch("MyGame.Tests.PlayerTests.Jump_AddsVelocity"), Is.True);
            Assert.That(pattern.IsMatch("MyGameXTests.PlayerTests.Jump_AddsVelocity"), Is.False);
            Assert.That(pattern.IsMatch("Other.Tests.PlayerTests.Jump_AddsVelocity"), Is.False);
        }

        [Test]
        public void ByTestClass_WithNestedClassName_MatchesNestedFixtureTests()
        {
            // Verifies nested fixtures, which NUnit names with a '+', match by their inner class name.
            Regex pattern = new Regex(TestExecutionFilter.ByTestClass("Inner").FilterValue);

            Assert.That(pattern.IsMatch("MyGame.Tests.Outer+Inner.Jump_AddsVelocity"), Is.True);
        }
    }
}
