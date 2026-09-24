using System.Collections.Generic;

using NUnit.Framework;

using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers how the Unity resolver names wired values and finds them again, and the restore
    /// path from a recorded wiring to the host that replaces the recorded one. A scene reload is
    /// imitated by destroying objects and rebuilding them with the same names in the same places.
    /// </summary>
    public class HotReloadUnityWiredValueResolverTests
    {
        private const string Prefix = "UnityResolverTests_";
        private const string AssetPath = "Assets/" + Prefix + "Temp.anim";
        private const string FieldKey = "Ns.Host::target";

        private readonly List<GameObject> _created = new List<GameObject>();
        private HotReloadUnityWiredValueResolver _resolver;

        [SetUp]
        public void SetUp()
        {
            _resolver = new HotReloadUnityWiredValueResolver(new HotReloadSceneObjectIdentityBuilder());
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
            AssetDatabase.DeleteAsset(AssetPath);
        }

        /// <summary>
        /// What: a component wired as a value is described as a scene object and resolves to the
        /// component rebuilt in its place.
        /// </summary>
        [Test]
        public void DescribeValue_SceneComponent_ResolvesToItsReplacement()
        {
            GameObject target = CreateRoot("Target");
            HotReloadWiredValueDescriptor descriptor = _resolver.DescribeValue(target.AddComponent<BoxCollider>());

            Object.DestroyImmediate(target);
            BoxCollider replacement = CreateRoot("Target").AddComponent<BoxCollider>();

            Assert.That(descriptor.Kind, Is.EqualTo(HotReloadWiredValueKind.SceneObject));
            Assert.That(_resolver.TryResolve(descriptor, out object resolved, out string reason), Is.True, reason);
            Assert.That(resolved, Is.SameAs(replacement));
        }

        /// <summary>
        /// What: a game object whose name contains the component marker is still described and
        /// resolved as a game object, not taken for a component.
        /// </summary>
        [Test]
        public void TryResolve_GameObjectNamedLikeAComponent_ResolvesToTheGameObject()
        {
            const string name = "Target|component:UnityEngine.BoxCollider|index:0";
            GameObject target = CreateRoot(name);
            HotReloadWiredValueDescriptor descriptor = _resolver.DescribeValue(target);

            Object.DestroyImmediate(target);
            GameObject replacement = CreateRoot(name);

            Assert.That(_resolver.TryResolve(descriptor, out object resolved, out string reason), Is.True, reason);
            Assert.That(resolved, Is.SameAs(replacement));
        }

        /// <summary>
        /// What: null and non-Unity values are kept as they are.
        /// </summary>
        [Test]
        public void DescribeValue_NullAndPlainValues_AreKeptAsPlain()
        {
            HotReloadWiredValueDescriptor nullDescriptor = _resolver.DescribeValue(null);
            HotReloadWiredValueDescriptor intDescriptor = _resolver.DescribeValue(12);

            Assert.That(nullDescriptor.Kind, Is.EqualTo(HotReloadWiredValueKind.Plain));
            Assert.That(nullDescriptor.PlainValue, Is.Null);
            Assert.That(intDescriptor.Kind, Is.EqualTo(HotReloadWiredValueKind.Plain));
            Assert.That(intDescriptor.PlainValue, Is.EqualTo(12));
        }

        /// <summary>
        /// What: an asset is described by its GUID and local id and resolves to the same object.
        /// </summary>
        [Test]
        public void DescribeValue_Asset_ResolvesToTheSameAsset()
        {
            AnimationClip clip = new AnimationClip();
            AssetDatabase.CreateAsset(clip, AssetPath);

            HotReloadWiredValueDescriptor descriptor = _resolver.DescribeValue(clip);

            Assert.That(descriptor.Kind, Is.EqualTo(HotReloadWiredValueKind.Asset));
            Assert.That(descriptor.Identity, Does.StartWith("asset:"));
            Assert.That(_resolver.TryResolve(descriptor, out object resolved, out string reason), Is.True, reason);
            Assert.That(resolved, Is.SameAs(clip));
        }

        /// <summary>
        /// What: an object created at run time, held by no scene and no asset, is named as not
        /// restorable instead of being remembered by reference.
        /// </summary>
        [Test]
        public void DescribeValue_RuntimeCreatedObject_IsUnrestorable()
        {
            ScriptableObject runtimeOnly = ScriptableObject.CreateInstance<ScriptableObject>();
            try
            {
                HotReloadWiredValueDescriptor descriptor = _resolver.DescribeValue(runtimeOnly);

                Assert.That(descriptor.Kind, Is.EqualTo(HotReloadWiredValueKind.Unrestorable));
                Assert.That(descriptor.UnrestorableReason, Does.Contain("runtime-created"));
            }
            finally
            {
                Object.DestroyImmediate(runtimeOnly);
            }
        }

        /// <summary>
        /// What: a scene object that is gone and not rebuilt does not resolve, and the reason names
        /// where it was.
        /// </summary>
        [Test]
        public void TryResolve_SceneObjectNotRebuilt_FailsNamingItsPlace()
        {
            GameObject target = CreateRoot("Target");
            HotReloadWiredValueDescriptor descriptor = _resolver.DescribeValue(target);

            Object.DestroyImmediate(target);

            Assert.That(_resolver.TryResolve(descriptor, out object resolved, out string reason), Is.False);
            Assert.That(resolved, Is.Null);
            Assert.That(reason, Does.Contain(descriptor.Identity));
        }

        /// <summary>
        /// What: a value wired into a component comes back on the first read of the field on the
        /// component rebuilt in its place, without the initializer, and counts as restored.
        /// </summary>
        [Test]
        public void GetOrInit_HostAndValueRebuiltInPlace_ReturnsTheRebuiltValue()
        {
            HotReloadWiredValuePersistence persistence = new HotReloadWiredValuePersistence(_resolver);
            HotReloadAddedFieldValues values = new HotReloadAddedFieldValues { Restorer = persistence };
            GameObject hostObject = CreateRoot("Host");
            GameObject targetObject = CreateRoot("Target");
            BoxCollider host = hostObject.AddComponent<BoxCollider>();
            BoxCollider target = targetObject.AddComponent<BoxCollider>();
            values.Set(host, FieldKey, target);
            persistence.Record(host, FieldKey, target);

            Object.DestroyImmediate(hostObject);
            Object.DestroyImmediate(targetObject);
            BoxCollider newHost = CreateRoot("Host").AddComponent<BoxCollider>();
            BoxCollider newTarget = CreateRoot("Target").AddComponent<BoxCollider>();

            BoxCollider read = values.GetOrInit<BoxCollider>(newHost, FieldKey, () => null);

            Assert.That(read, Is.SameAs(newTarget));
            Assert.That(persistence.Report.RestoredCount, Is.EqualTo(1));
            Assert.That(persistence.Report.Failures, Is.Empty);
        }

        /// <summary>
        /// What: a host renamed after its identity was taken is missing, because nothing of its
        /// component type sits at the recorded place in the loaded scene.
        /// </summary>
        [Test]
        public void IsHostMissing_HostRenamed_IsTrue()
        {
            GameObject host = CreateRoot("Host");
            string identity = _resolver.DescribeHost(host.transform);

            host.name = Prefix + "Host2";

            Assert.That(_resolver.IsHostMissing(identity), Is.True);
        }

        /// <summary>
        /// What: a host still at its recorded place is not missing.
        /// </summary>
        [Test]
        public void IsHostMissing_HostStillThere_IsFalse()
        {
            GameObject host = CreateRoot("Host");
            string identity = _resolver.DescribeHost(host.transform);

            Assert.That(_resolver.IsHostMissing(identity), Is.False);
        }

        /// <summary>
        /// What: a host in a scene that is not loaded is out of sight rather than missing.
        /// </summary>
        [Test]
        public void IsHostMissing_SceneNotLoaded_IsFalse()
        {
            const string identity = "scene:NoSuchScene|path:Host[0]|component:UnityEngine.Transform|index:0";

            Assert.That(_resolver.IsHostMissing(identity), Is.False);
        }

        private GameObject CreateRoot(string name)
        {
            GameObject gameObject = new GameObject(Prefix + name);
            _created.Add(gameObject);
            return gameObject;
        }
    }
}
