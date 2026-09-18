using System;
using System.Collections.Generic;
using System.Reflection;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers the generated proxy component: how it receives its target, what it forwards once
    /// Unity calls the message it declares, and when it stays silent.
    /// </summary>
    public class HotReloadUnityMessageProxyTypeBuilderTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();
        private HotReloadUnityMessageProxyTypeBuilder _builder;

        [SetUp]
        public void SetUp()
        {
            _builder = new HotReloadUnityMessageProxyTypeBuilder();
        }

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
        /// What: the target is already in place when the component is constructed, so a message
        /// delivered during AddComponent would already have somewhere to go.
        /// </summary>
        [Test]
        public void Build_AttachedInsideAPendingTargetScope_KnowsItsTarget()
        {
            HotReloadUnityMessageProxyFixture target = CreateFixture();
            Type proxyType = BuildProxyType("Update");

            HotReloadUnityMessageProxy proxy = Attach(proxyType, target);

            Assert.That(proxy.Target, Is.SameAs(target));
        }

        /// <summary>
        /// What: the message Unity would call on the proxy reaches the target's added method.
        /// </summary>
        [Test]
        public void ProxyMessage_WithoutArguments_ReachesTheTarget()
        {
            HotReloadUnityMessageProxyFixture target = CreateFixture();
            Type proxyType = BuildProxyType("Update");
            HotReloadUnityMessageProxy proxy = Attach(proxyType, target);

            InvokeMessage(proxyType, proxy, "Update", null);

            Assert.That(target.UpdateCount, Is.EqualTo(1));
        }

        /// <summary>
        /// What: an argument Unity passes to the proxy is handed on to the target unchanged.
        /// </summary>
        [Test]
        public void ProxyMessage_WithReferenceArgument_ReachesTheTarget()
        {
            HotReloadUnityMessageProxyFixture target = CreateFixture();
            Collider collider = CreateGameObject("ProxyTypeBuilderTests_Collider").AddComponent<SphereCollider>();
            Type proxyType = BuildProxyType("OnTriggerEnter");
            HotReloadUnityMessageProxy proxy = Attach(proxyType, target);

            InvokeMessage(proxyType, proxy, "OnTriggerEnter", new object[] { collider });

            Assert.That(target.TriggerCount, Is.EqualTo(1));
            Assert.That(target.LastCollider, Is.SameAs(collider));
        }

        /// <summary>
        /// What: a value-type argument is boxed into the forwarding array and arrives intact.
        /// </summary>
        [Test]
        public void ProxyMessage_WithValueTypeArgument_ReachesTheTarget()
        {
            HotReloadUnityMessageProxyFixture target = CreateFixture();
            Type proxyType = BuildProxyType("OnApplicationPause");
            HotReloadUnityMessageProxy proxy = Attach(proxyType, target);

            InvokeMessage(proxyType, proxy, "OnApplicationPause", new object[] { true });

            Assert.That(target.PauseValue, Is.True);
        }

        /// <summary>
        /// What: a disabled target stops receiving the per-frame messages Unity would also withhold,
        /// while an event message still reaches it.
        /// </summary>
        [Test]
        public void ProxyMessage_TargetDisabled_ForwardsOnlyTheUngatedMessage()
        {
            HotReloadUnityMessageProxyFixture target = CreateFixture();
            Collider collider = CreateGameObject("ProxyTypeBuilderTests_DisabledCollider").AddComponent<SphereCollider>();
            Type proxyType = BuildProxyType("Update", "OnTriggerEnter");
            HotReloadUnityMessageProxy proxy = Attach(proxyType, target);
            target.enabled = false;

            InvokeMessage(proxyType, proxy, "Update", null);
            InvokeMessage(proxyType, proxy, "OnTriggerEnter", new object[] { collider });

            Assert.That(target.UpdateCount, Is.EqualTo(0));
            Assert.That(target.TriggerCount, Is.EqualTo(1));
        }

        /// <summary>
        /// What: a proxy that was constructed without a target forwards nothing and throws nothing.
        /// </summary>
        [Test]
        public void ProxyMessage_ConstructedWithoutATarget_DoesNothing()
        {
            HotReloadUnityMessageProxyFixture target = CreateFixture();
            Type proxyType = BuildProxyType("Update");
            HotReloadUnityMessageProxy proxy =
                (HotReloadUnityMessageProxy)target.gameObject.AddComponent(proxyType);

            InvokeMessage(proxyType, proxy, "Update", null);

            Assert.That(proxy.Target, Is.Null);
            Assert.That(target.UpdateCount, Is.EqualTo(0));
        }

        /// <summary>
        /// What: a retired proxy stops forwarding before it is destroyed.
        /// </summary>
        [Test]
        public void ProxyMessage_AfterUnbind_DoesNothing()
        {
            HotReloadUnityMessageProxyFixture target = CreateFixture();
            Type proxyType = BuildProxyType("Update");
            HotReloadUnityMessageProxy proxy = Attach(proxyType, target);
            proxy.Unbind();

            InvokeMessage(proxyType, proxy, "Update", null);

            Assert.That(target.UpdateCount, Is.EqualTo(0));
        }

        /// <summary>
        /// What: a target destroyed on its own leaves the proxy forwarding nothing, rather than
        /// calling into a destroyed component.
        /// </summary>
        [Test]
        public void ProxyMessage_TargetDestroyed_DoesNothing()
        {
            HotReloadUnityMessageProxyFixture target = CreateFixture();
            Collider collider = CreateGameObject("ProxyTypeBuilderTests_DestroyedCollider").AddComponent<SphereCollider>();
            // A message the enabled gate never stops, so only the destroyed-target check can hold
            // this call back.
            Type proxyType = BuildProxyType("OnTriggerEnter");
            HotReloadUnityMessageProxy proxy = Attach(proxyType, target);
            UnityEngine.Object.DestroyImmediate(target);

            Assert.DoesNotThrow(() => InvokeMessage(proxyType, proxy, "OnTriggerEnter", new object[] { collider }));
            Assert.That(target.TriggerCount, Is.EqualTo(0));
        }

        /// <summary>
        /// What: building the same messages again yields a separate type, because a module refuses
        /// a name it already defined.
        /// </summary>
        [Test]
        public void Build_SameMessagesTwice_ProducesDistinctTypes()
        {
            Type first = BuildProxyType("Update");
            Type second = BuildProxyType("Update");

            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(second.Name, Is.Not.EqualTo(first.Name));
        }

        /// <summary>
        /// What: a proxy built from a shim named the way the worker names it declares the message
        /// under the member's own name, which is the only name Unity dispatches on.
        /// </summary>
        [Test]
        public void Build_FromAShimNamedAsTheWorkerNamesIt_DeclaresTheMessageName()
        {
            HotReloadUnityMessageProxyFixture target = CreateFixture();
            MethodInfo shim = typeof(HotReloadUnityMessageWorkerNamedFixtureShims).GetMethod(
                "Update__shim0",
                BindingFlags.Public | BindingFlags.Static);
            Assert.That(shim, Is.Not.Null, "The fixture shim method must exist.");
            Type proxyType = _builder.Build(
                HotReloadUnityMessageForwarderFactory.CreateBinding(
                    typeof(HotReloadUnityMessageProxyFixture),
                    new List<MethodInfo> { shim }));
            HotReloadUnityMessageProxy proxy = Attach(proxyType, target);

            InvokeMessage(proxyType, proxy, "Update", null);

            Assert.That(target.UpdateCount, Is.EqualTo(1));
        }

        private Type BuildProxyType(params string[] messageNames)
        {
            List<MethodInfo> shims = new List<MethodInfo>();
            foreach (string messageName in messageNames)
            {
                MethodInfo shim = typeof(HotReloadUnityMessageProxyFixtureShims).GetMethod(
                    messageName,
                    BindingFlags.Public | BindingFlags.Static);
                Assert.That(shim, Is.Not.Null, "The fixture shim method must exist.");
                shims.Add(shim);
            }

            HotReloadUnityMessageBinding binding = HotReloadUnityMessageForwarderFactory.CreateBinding(
                typeof(HotReloadUnityMessageProxyFixture),
                shims);
            return _builder.Build(binding);
        }

        private static HotReloadUnityMessageProxy Attach(Type proxyType, MonoBehaviour target)
        {
            using (HotReloadUnityMessageProxy.BeginPendingTarget(target))
            {
                return (HotReloadUnityMessageProxy)target.gameObject.AddComponent(proxyType);
            }
        }

        private static void InvokeMessage(
            Type proxyType,
            HotReloadUnityMessageProxy proxy,
            string messageName,
            object[] args)
        {
            MethodInfo message = proxyType.GetMethod(messageName, BindingFlags.Public | BindingFlags.Instance);
            Assert.That(message, Is.Not.Null, "The generated proxy must declare the message.");
            message.Invoke(proxy, args);
        }

        private HotReloadUnityMessageProxyFixture CreateFixture()
        {
            GameObject owner = CreateGameObject("ProxyTypeBuilderTests_Target");
            return owner.AddComponent<HotReloadUnityMessageProxyFixture>();
        }

        private GameObject CreateGameObject(string name)
        {
            GameObject created = new GameObject(name);
            _created.Add(created);
            return created;
        }
    }
}
