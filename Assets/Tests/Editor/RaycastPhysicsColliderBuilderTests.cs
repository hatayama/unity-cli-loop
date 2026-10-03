using System.Collections.Generic;

using NUnit.Framework;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the sample coverage of the clustered raycast grid and the collider metadata reported for a hit,
    /// including which components are listed. GameObjects are created per test and destroyed in TearDown.
    /// </summary>
    public sealed class RaycastPhysicsColliderBuilderTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
            }
        }

        /// <summary>
        /// Verifies the coverage uses half of each grid step and spans the image shifted by the input offset.
        /// </summary>
        [Test]
        public void CreateClusterSampleCoverage_SpansTheImageBelowTheInputOffset()
        {
            int columnsPlusOne = RaycastPhysicsColliderBuilder.CLUSTERED_GRID_COLUMNS + 1;
            int rowsPlusOne = RaycastPhysicsColliderBuilder.CLUSTERED_GRID_ROWS + 1;
            Vector2 imageSize = new Vector2(columnsPlusOne * 20f, rowsPlusOne * 10f);

            RaycastSampleCoverage coverage = RaycastPhysicsColliderBuilder.CreateClusterSampleCoverage(imageSize, 30);

            Assert.That(coverage.HalfStepX, Is.EqualTo(10f).Within(0.0001f));
            Assert.That(coverage.HalfStepY, Is.EqualTo(5f).Within(0.0001f));
            Assert.That(coverage.MinX, Is.EqualTo(0f));
            Assert.That(coverage.MinY, Is.EqualTo(30f));
            Assert.That(coverage.MaxX, Is.EqualTo(imageSize.x));
            Assert.That(coverage.MaxY, Is.EqualTo(30f + imageSize.y));
        }

        /// <summary>
        /// Verifies the metadata names the hit object, its full path, and its layer name.
        /// </summary>
        [Test]
        public void CreateColliderMetadata_DescribesTheHitObject()
        {
            _root = new GameObject("RaycastBuilderRoot");
            GameObject crate = new GameObject("Crate");
            crate.transform.SetParent(_root.transform);
            crate.layer = LayerMask.NameToLayer("Water");
            BoxCollider collider = crate.AddComponent<BoxCollider>();

            RaycastColliderMetadata metadata = RaycastPhysicsColliderBuilder.CreateColliderMetadata(collider);

            Assert.That(metadata.Name, Is.EqualTo("Crate"));
            Assert.That(metadata.Path, Is.EqualTo("RaycastBuilderRoot/Crate"));
            Assert.That(metadata.Layer, Is.EqualTo("Water"));
            Assert.That(metadata.Components, Is.EqualTo(new List<string> { "BoxCollider" }));
        }

        /// <summary>
        /// Verifies colliders and scripts are listed once per type, while other built-in components are left out.
        /// </summary>
        [Test]
        public void GetRelevantComponentTypeNames_ListsCollidersAndScriptsOncePerType()
        {
            _root = new GameObject("RaycastBuilderRoot");
            _root.AddComponent<BoxCollider>();
            _root.AddComponent<Rigidbody>();
            _root.AddComponent<BoxCollider>();
            _root.AddComponent<SphereCollider>();
            _root.AddComponent<RaycastPhysicsColliderBuilderTestMarker>();

            List<string> names = RaycastPhysicsColliderBuilder.GetRelevantComponentTypeNames(_root);

            Assert.That(
                names,
                Is.EqualTo(new List<string> { "BoxCollider", "SphereCollider", nameof(RaycastPhysicsColliderBuilderTestMarker) }));
        }
    }

    internal sealed class RaycastPhysicsColliderBuilderTestMarker : MonoBehaviour
    {
    }
}
