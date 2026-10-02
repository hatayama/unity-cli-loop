using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies GameObject search over nested children, the tag filter, and the failure response for an
    /// invalid regular expression, on uniquely named temporary GameObjects.
    /// </summary>
    public sealed class FindGameObjectsUseCaseSearchTests
    {
        private const string RootName = "FindSearchFixtureRoot";
        private const string ChildName = "FindSearchFixtureChild";
        private const string GrandChildName = "FindSearchFixtureGrandChild";

        private GameObject _root;
        private FindGameObjectsUseCase _useCase;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject(RootName);
            GameObject child = new GameObject(ChildName);
            child.transform.SetParent(_root.transform);
            GameObject grandChild = new GameObject(GrandChildName);
            grandChild.transform.SetParent(child.transform);
            _useCase = new FindGameObjectsUseCase(new GameObjectFinderService(), new ComponentSerializer());
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
        }

        /// <summary>
        /// Verifies the search walks below the scene roots and returns every nested match with its path.
        /// </summary>
        [Test]
        public void ExecuteAsync_WithAContainsPattern_FindsNestedChildren()
        {
            FindGameObjectsResponse response = Execute(new FindGameObjectsSchema
            {
                NamePattern = "FindSearchFixture",
                SearchMode = SearchMode.contains
            });

            Assert.That(
                response.Results.Select(result => result.Name).ToArray(),
                Is.EquivalentTo(new[] { RootName, ChildName, GrandChildName }));
            Assert.That(
                response.Results.Single(result => result.Name == GrandChildName).Path,
                Is.EqualTo($"{RootName}/{ChildName}/{GrandChildName}"));
        }

        /// <summary>
        /// Verifies a tag filter drops objects whose tag differs, and keeps those whose tag matches.
        /// </summary>
        [Test]
        public void ExecuteAsync_WithATagThatDoesNotMatch_ExcludesTheObject()
        {
            FindGameObjectsResponse mismatch = Execute(new FindGameObjectsSchema
            {
                NamePattern = ChildName,
                Tag = "EditorOnly"
            });
            FindGameObjectsResponse match = Execute(new FindGameObjectsSchema
            {
                NamePattern = ChildName,
                Tag = "Untagged"
            });

            Assert.That(mismatch.Results, Is.Empty);
            Assert.That(match.Results.Select(result => result.Name).ToArray(), Is.EqualTo(new[] { ChildName }));
        }

        /// <summary>
        /// Verifies an invalid regular expression is logged and answered with an empty failed search instead of
        /// escaping the tool.
        /// </summary>
        [Test]
        public void ExecuteAsync_WithAnInvalidRegex_ReturnsTheSearchFailure()
        {
            LogAssert.Expect(LogType.Error, new Regex("^GameObject search failed: "));

            FindGameObjectsResponse response = Execute(new FindGameObjectsSchema
            {
                NamePattern = "(",
                SearchMode = SearchMode.regex
            });

            Assert.That(response.Results, Is.Empty);
            Assert.That(response.TotalFound, Is.EqualTo(0));
            Assert.That(response.ErrorMessage, Is.EqualTo("Search execution failed. Please check the logs for details."));
        }

        private FindGameObjectsResponse Execute(FindGameObjectsSchema parameters)
        {
            System.Threading.Tasks.Task<FindGameObjectsResponse> task = _useCase.ExecuteAsync(parameters, CancellationToken.None);
            Assert.That(task.IsCompletedSuccessfully, Is.True);
            return task.Result;
        }
    }
}
