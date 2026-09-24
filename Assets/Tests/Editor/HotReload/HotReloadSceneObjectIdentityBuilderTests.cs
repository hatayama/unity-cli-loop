using System.Collections.Generic;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers naming a scene object by its place in the hierarchy and finding the object at that
    /// place again. A scene reload is imitated by destroying an object and building one with the
    /// same name at the same sibling position.
    /// </summary>
    public class HotReloadSceneObjectIdentityBuilderTests
    {
        private const string Prefix = "IdentityBuilderTests_";

        private readonly List<GameObject> _created = new List<GameObject>();
        private HotReloadSceneObjectIdentityBuilder _builder;

        [SetUp]
        public void SetUp()
        {
            _builder = new HotReloadSceneObjectIdentityBuilder();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject gameObject in _created)
            {
                if (gameObject != null)
                {
                    Object.DestroyImmediate(gameObject);
                }
            }

            _created.Clear();
        }

        /// <summary>
        /// What: two components of the same type on one child get different identities, and each
        /// resolves back to itself.
        /// </summary>
        [Test]
        public void DescribeComponent_TwoOfTheSameTypeOnAChild_EachResolvesToItself()
        {
            GameObject child = CreateChild(CreateRoot("Parent"), "Child");
            BoxCollider first = child.AddComponent<BoxCollider>();
            BoxCollider second = child.AddComponent<BoxCollider>();

            string firstIdentity = _builder.DescribeComponent(first);
            string secondIdentity = _builder.DescribeComponent(second);

            Assert.That(firstIdentity, Is.Not.EqualTo(secondIdentity));
            Assert.That(_builder.TryResolveComponent(firstIdentity, out Component firstFound), Is.True);
            Assert.That(_builder.TryResolveComponent(secondIdentity, out Component secondFound), Is.True);
            Assert.That(firstFound, Is.SameAs(first));
            Assert.That(secondFound, Is.SameAs(second));
        }

        /// <summary>
        /// What: after the child is destroyed and rebuilt with the same name at the same place, the
        /// recorded identity finds the rebuilt component.
        /// </summary>
        [Test]
        public void TryResolveComponent_ChildRebuiltInPlace_FindsTheReplacement()
        {
            GameObject parent = CreateRoot("Parent");
            GameObject child = CreateChild(parent, "Child");
            CreateChild(parent, "After");
            string identity = _builder.DescribeComponent(child.AddComponent<BoxCollider>());

            Object.DestroyImmediate(child);
            GameObject rebuilt = CreateChild(parent, "Child");
            rebuilt.transform.SetSiblingIndex(0);
            BoxCollider replacement = rebuilt.AddComponent<BoxCollider>();

            Assert.That(_builder.TryResolveComponent(identity, out Component found), Is.True);
            Assert.That(found, Is.SameAs(replacement));
        }

        /// <summary>
        /// What: a sibling with the recorded name but at another index is not taken for the
        /// recorded object.
        /// </summary>
        [Test]
        public void TryResolveGameObject_SameNameAtAnotherIndex_IsNotMatched()
        {
            GameObject parent = CreateRoot("Parent");
            CreateChild(parent, "Twin");
            GameObject secondTwin = CreateChild(parent, "Twin");
            string identity = _builder.DescribeGameObject(secondTwin);

            Object.DestroyImmediate(secondTwin);

            Assert.That(_builder.TryResolveGameObject(identity, out GameObject found), Is.False);
            Assert.That(found, Is.Null);
        }

        /// <summary>
        /// What: a child whose name spells out a deeper path ("X[0]/Y") is told apart from the
        /// object at that path, and each identity resolves to its own object.
        /// </summary>
        [Test]
        public void Describe_NameThatSpellsAPath_DoesNotCollideWithThatPath()
        {
            GameObject parent = CreateRoot("Parent");
            GameObject x = CreateChild(parent, "X");
            CreateChild(x, "Z");
            GameObject y = CreateChild(x, "Y");
            GameObject spelled = new GameObject(Prefix + "X[0]/" + Prefix + "Y");
            spelled.transform.SetParent(parent.transform, false);

            string yIdentity = _builder.DescribeGameObject(y);
            string spelledIdentity = _builder.DescribeGameObject(spelled);

            Assert.That(spelledIdentity, Is.Not.EqualTo(yIdentity));
            Assert.That(_builder.TryResolveGameObject(yIdentity, out GameObject yFound), Is.True);
            Assert.That(_builder.TryResolveGameObject(spelledIdentity, out GameObject spelledFound), Is.True);
            Assert.That(yFound, Is.SameAs(y));
            Assert.That(spelledFound, Is.SameAs(spelled));
        }

        /// <summary>
        /// What: identities that name nothing loaded, or are not identities at all, do not resolve.
        /// </summary>
        [Test]
        public void TryResolve_UnknownIdentity_ReturnsFalse()
        {
            GameObject root = CreateRoot("Parent");
            string identity = _builder.DescribeGameObject(root);

            Assert.That(_builder.TryResolveGameObject(identity + "/Missing[0]", out _), Is.False);
            Assert.That(_builder.TryResolveGameObject("scene:NoSuchScene|path:" + Prefix + "Parent[0]", out _), Is.False);
            Assert.That(_builder.TryResolveComponent(identity + "|component:UnityEngine.BoxCollider|index:0", out _), Is.False);
            Assert.That(_builder.TryResolveGameObject("not an identity", out _), Is.False);
        }

        private GameObject CreateRoot(string name)
        {
            GameObject gameObject = new GameObject(Prefix + name);
            _created.Add(gameObject);
            return gameObject;
        }

        private static GameObject CreateChild(GameObject parent, string name)
        {
            GameObject child = new GameObject(Prefix + name);
            child.transform.SetParent(parent.transform, false);
            return child;
        }
    }
}
