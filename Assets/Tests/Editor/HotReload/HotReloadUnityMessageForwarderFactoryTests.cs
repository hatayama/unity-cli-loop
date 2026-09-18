using System;
using System.Collections.Generic;
using System.Reflection;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers the call a proxy makes into a generated shim: the receiver, the message arguments it
    /// unpacks, and the binding that holds one such call per message.
    /// </summary>
    public class HotReloadUnityMessageForwarderFactoryTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject gameObject in _created)
            {
                if (gameObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(gameObject);
                }
            }

            _created.Clear();
        }

        /// <summary>
        /// What: a message with no arguments of its own reaches the target, with no array to unpack.
        /// </summary>
        [Test]
        public void CreateForwarder_MessageWithoutArguments_CallsTheShimOnTheTarget()
        {
            HotReloadUnityMessageProxyFixture target = CreateFixture();
            Action<MonoBehaviour, object[]> forwarder = HotReloadUnityMessageForwarderFactory.CreateForwarder(
                ShimOf(typeof(HotReloadUnityMessageProxyFixtureShims), "Update"));

            forwarder(target, null);

            Assert.That(target.UpdateCount, Is.EqualTo(1));
        }

        /// <summary>
        /// What: a reference argument is taken out of the array and handed to the shim unchanged.
        /// </summary>
        [Test]
        public void CreateForwarder_ReferenceArgument_ReachesTheShim()
        {
            HotReloadUnityMessageProxyFixture target = CreateFixture();
            GameObject colliderOwner = CreateGameObject("ForwarderFactoryTests_Collider");
            Collider collider = colliderOwner.AddComponent<SphereCollider>();
            Action<MonoBehaviour, object[]> forwarder = HotReloadUnityMessageForwarderFactory.CreateForwarder(
                ShimOf(typeof(HotReloadUnityMessageProxyFixtureShims), "OnTriggerEnter"));

            forwarder(target, new object[] { collider });

            Assert.That(target.TriggerCount, Is.EqualTo(1));
            Assert.That(target.LastCollider, Is.SameAs(collider));
        }

        /// <summary>
        /// What: a value-type argument survives the trip through the object array.
        /// </summary>
        [Test]
        public void CreateForwarder_ValueTypeArgument_ReachesTheShim()
        {
            HotReloadUnityMessageProxyFixture target = CreateFixture();
            Action<MonoBehaviour, object[]> forwarder = HotReloadUnityMessageForwarderFactory.CreateForwarder(
                ShimOf(typeof(HotReloadUnityMessageProxyFixtureShims), "OnApplicationPause"));

            forwarder(target, new object[] { true });

            Assert.That(target.PauseValue, Is.True);
        }

        /// <summary>
        /// What: a target type and a shim the hot-reload assembly cannot see are still called,
        /// because the forwarder skips the accessibility check.
        /// </summary>
        [Test]
        public void CreateForwarder_TargetTypeInvisibleToTheCaller_StillCallsTheShim()
        {
            GameObject owner = CreateGameObject("ForwarderFactoryTests_InternalTarget");
            HotReloadUnityMessageInternalFixture target = owner.AddComponent<HotReloadUnityMessageInternalFixture>();
            Action<MonoBehaviour, object[]> forwarder = HotReloadUnityMessageForwarderFactory.CreateForwarder(
                ShimOf(typeof(HotReloadUnityMessageInternalFixtureShims), "Update"));

            forwarder(target, null);

            Assert.That(target.UpdateCount, Is.EqualTo(1));
        }

        /// <summary>
        /// What: a binding keeps the shims in the order given, gates only the per-frame messages,
        /// and drops the receiver from the parameters the proxy will declare.
        /// </summary>
        [Test]
        public void CreateBinding_MixedMessages_DescribesEachSlot()
        {
            MethodInfo update = ShimOf(typeof(HotReloadUnityMessageProxyFixtureShims), "Update");
            MethodInfo trigger = ShimOf(typeof(HotReloadUnityMessageProxyFixtureShims), "OnTriggerEnter");

            HotReloadUnityMessageBinding binding = HotReloadUnityMessageForwarderFactory.CreateBinding(
                typeof(HotReloadUnityMessageProxyFixture),
                new List<MethodInfo> { update, trigger });

            Assert.That(binding.TargetType, Is.EqualTo(typeof(HotReloadUnityMessageProxyFixture)));
            Assert.That(binding.Count, Is.EqualTo(2));
            Assert.That(binding.GetName(0), Is.EqualTo("Update"));
            Assert.That(binding.IsGated(0), Is.True);
            Assert.That(binding.GetParameterTypes(0), Is.Empty);
            Assert.That(binding.GetName(1), Is.EqualTo("OnTriggerEnter"));
            Assert.That(binding.IsGated(1), Is.False);
            Assert.That(binding.GetParameterTypes(1), Is.EqualTo(new[] { typeof(Collider) }));
        }

        /// <summary>
        /// What: a binding calls the shim of the slot it was asked for.
        /// </summary>
        [Test]
        public void Invoke_Slot_CallsThatSlotsShim()
        {
            HotReloadUnityMessageProxyFixture target = CreateFixture();
            HotReloadUnityMessageBinding binding = HotReloadUnityMessageForwarderFactory.CreateBinding(
                typeof(HotReloadUnityMessageProxyFixture),
                new List<MethodInfo>
                {
                    ShimOf(typeof(HotReloadUnityMessageProxyFixtureShims), "Update"),
                    ShimOf(typeof(HotReloadUnityMessageProxyFixtureShims), "Start")
                });

            binding.Invoke(1, target, null);

            Assert.That(target.StartRan, Is.True);
            Assert.That(target.UpdateCount, Is.EqualTo(0));
        }

        /// <summary>
        /// What: the same message added to one target type twice is refused, because a proxy type
        /// could declare it only once.
        /// </summary>
        [Test]
        public void CreateBinding_WithTheSameMessageTwice_Refuses()
        {
            List<MethodInfo> shims = new List<MethodInfo>
            {
                ShimOf(typeof(HotReloadUnityMessageProxyFixtureShims), "Update"),
                ShimOf(typeof(HotReloadUnityMessageDuplicateFixtureShims), "Update")
            };

            Assert.Throws<InvalidOperationException>(
                () => HotReloadUnityMessageForwarderFactory.CreateBinding(
                    typeof(HotReloadUnityMessageProxyFixture),
                    shims));
        }

        private HotReloadUnityMessageProxyFixture CreateFixture()
        {
            GameObject owner = CreateGameObject("ForwarderFactoryTests_Target");
            return owner.AddComponent<HotReloadUnityMessageProxyFixture>();
        }

        private GameObject CreateGameObject(string name)
        {
            GameObject created = new GameObject(name);
            _created.Add(created);
            return created;
        }

        private static MethodInfo ShimOf(Type host, string methodName)
        {
            MethodInfo shim = host.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
            Assert.That(shim, Is.Not.Null, "The fixture shim method must exist.");
            return shim;
        }
    }
}
