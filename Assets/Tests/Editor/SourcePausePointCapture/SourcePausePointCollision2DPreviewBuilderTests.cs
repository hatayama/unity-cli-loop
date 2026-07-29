using System.Collections.Generic;

using Newtonsoft.Json.Linq;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies Collision2D capture previews expose collider hierarchy paths instead of raw IDs.
    /// </summary>
    [TestFixture]
    public sealed class SourcePausePointCollision2DPreviewBuilderTests
    {
        private GameObject _rootGameObject;
        private GameObject _childGameObject;
        private GameObject _otherGameObject;

        [TearDown]
        public void TearDown()
        {
            if (_childGameObject != null)
            {
                Object.DestroyImmediate(_childGameObject);
                _childGameObject = null;
            }

            if (_rootGameObject != null)
            {
                Object.DestroyImmediate(_rootGameObject);
                _rootGameObject = null;
            }

            if (_otherGameObject != null)
            {
                Object.DestroyImmediate(_otherGameObject);
                _otherGameObject = null;
            }
        }

        [Test]
        public void BuildPreviewToken_WithSceneColliders_IncludesHierarchyPaths()
        {
            // Verifies Collider/OtherCollider carry Name and UnityObjectPath in {scene}:/parent/child form.
            _rootGameObject = new GameObject("Root");
            _childGameObject = new GameObject("Enemy");
            _childGameObject.transform.SetParent(_rootGameObject.transform);
            BoxCollider2D collider = _childGameObject.AddComponent<BoxCollider2D>();

            _otherGameObject = new GameObject("Ball");
            BoxCollider2D otherCollider = _otherGameObject.AddComponent<BoxCollider2D>();

            Vector2 relativeVelocity = new Vector2(1.5f, -2f);
            const int contactCount = 2;

            JToken token = SourcePausePointCollision2DPreviewBuilder.BuildPreviewToken(
                collider, otherCollider, relativeVelocity, contactCount);

            Assert.That(token["Collider"]["Name"].Value<string>(), Is.EqualTo("Enemy"));
            Assert.That(
                token["Collider"]["UnityObjectPath"].Value<string>(),
                Is.EqualTo($"{_childGameObject.scene.name}:/Root/Enemy"));
            Assert.That(token["OtherCollider"]["Name"].Value<string>(), Is.EqualTo("Ball"));
            Assert.That(
                token["OtherCollider"]["UnityObjectPath"].Value<string>(),
                Is.EqualTo($"{_otherGameObject.scene.name}:/Ball"));
            Assert.That(token["RelativeVelocity"].Value<string>(), Is.EqualTo(relativeVelocity.ToString()));
            Assert.That(token["ContactCount"].Value<int>(), Is.EqualTo(contactCount));
        }

        [Test]
        public void BuildPreviewToken_WithNullColliders_RendersNone()
        {
            // Verifies a null (or Unity fake-null) collider previews as "(none)".
            JToken token = SourcePausePointCollision2DPreviewBuilder.BuildPreviewToken(
                null, null, Vector2.zero, 0);

            Assert.That(token["Collider"].Value<string>(), Is.EqualTo("(none)"));
            Assert.That(token["OtherCollider"].Value<string>(), Is.EqualTo("(none)"));
            Assert.That(token["ContactCount"].Value<int>(), Is.EqualTo(0));
        }

        [Test]
        public void TryBuildToken_WithNonCollision2D_ReturnsFalse()
        {
            // Verifies non-Collision2D values leave the Collision2D special-case path.
            List<int> value = new List<int> { 1, 2, 3 };

            bool built = SourcePausePointCollision2DPreviewBuilder.TryBuildToken(value, out JToken token);

            Assert.That(built, Is.False);
            Assert.That(token, Is.Null);
        }
    }
}
