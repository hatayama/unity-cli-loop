using System;
using System.Collections.Generic;
using System.Reflection;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers keeping the scene in step with the bindings: which instances get a proxy, when a
    /// proxy is taken back off, and when nothing happens at all.
    /// </summary>
    public class HotReloadUnityMessageProxyAttacherTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();
        private HotReloadStubPlayModeQuery _playMode;
        private HotReloadUnityMessageProxyAttacher _attacher;

        [SetUp]
        public void SetUp()
        {
            _playMode = new HotReloadStubPlayModeQuery { IsPlaying = true };
            _attacher = new HotReloadUnityMessageProxyAttacher(
                _playMode,
                new HotReloadUnityMessageProxyTypeBuilder());
        }

        [TearDown]
        public void TearDown()
        {
            _attacher.Clear();
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
        /// What: a tick gives an instance that was already in the scene a proxy that knows its
        /// target and is kept out of the hierarchy and out of saved scenes.
        /// </summary>
        [Test]
        public void Tick_AfterBind_AttachesAProxyToTheExistingInstance()
        {
            HotReloadUnityMessageProxyFixture target = CreateFixture();
            BindFixture();

            _attacher.Tick();

            HotReloadUnityMessageProxy[] proxies = ProxiesOn(target.gameObject);
            Assert.That(proxies.Length, Is.EqualTo(1));
            Assert.That(proxies[0].Target, Is.SameAs(target));
            Assert.That(proxies[0].hideFlags, Is.EqualTo(HideFlags.HideAndDontSave));
        }

        /// <summary>
        /// What: a second tick leaves the instance with the one proxy it already has, instead of
        /// stacking a new one on every editor update.
        /// </summary>
        [Test]
        public void Tick_RunTwice_DoesNotAttachASecondProxy()
        {
            HotReloadUnityMessageProxyFixture target = CreateFixture();
            BindFixture();
            _attacher.Tick();

            _attacher.Tick();

            Assert.That(ProxiesOn(target.gameObject).Length, Is.EqualTo(1));
        }

        /// <summary>
        /// What: an instance created after a tick is picked up by the next one, which is how
        /// objects spawned during Play Mode get their added messages.
        /// </summary>
        [Test]
        public void Tick_WithAnInstanceCreatedAfterTheFirstTick_AttachesToItNext()
        {
            BindFixture();
            _attacher.Tick();
            HotReloadUnityMessageProxyFixture late = CreateFixture();

            _attacher.Tick();

            Assert.That(ProxiesOn(late.gameObject).Length, Is.EqualTo(1));
        }

        /// <summary>
        /// What: every instance of a bound type gets its own proxy, not just the first one found.
        /// </summary>
        [Test]
        public void Tick_WithTwoInstancesOfTheBoundType_AttachesToBoth()
        {
            HotReloadUnityMessageProxyFixture first = CreateFixture();
            HotReloadUnityMessageProxyFixture second = CreateFixture();
            BindFixture();

            _attacher.Tick();

            Assert.That(ProxiesOn(first.gameObject).Length, Is.EqualTo(1));
            Assert.That(ProxiesOn(second.gameObject).Length, Is.EqualTo(1));
        }

        /// <summary>
        /// What: binding the same type again replaces the attached proxies, so a reload that
        /// changed the added messages leaves only the proxy built for the new ones.
        /// </summary>
        [Test]
        public void Tick_AfterRebindingTheSameType_ReplacesTheProxyWithTheNewOne()
        {
            HotReloadUnityMessageProxyFixture target = CreateFixture();
            BindFixture("Update");
            _attacher.Tick();
            Type firstProxyType = ProxiesOn(target.gameObject)[0].GetType();
            BindFixture("Update", "OnTriggerEnter");

            _attacher.Tick();

            HotReloadUnityMessageProxy[] proxies = ProxiesOn(target.gameObject);
            Assert.That(proxies.Length, Is.EqualTo(1));
            Assert.That(proxies[0].GetType(), Is.Not.SameAs(firstProxyType));
        }

        /// <summary>
        /// What: a proxy whose target component was removed on its own is destroyed at the next
        /// tick, rather than staying on the GameObject forwarding to nothing.
        /// </summary>
        [Test]
        public void Tick_AfterTheTargetComponentWasDestroyed_RemovesTheOrphanedProxy()
        {
            HotReloadUnityMessageProxyFixture target = CreateFixture();
            GameObject owner = target.gameObject;
            BindFixture();
            _attacher.Tick();
            UnityEngine.Object.DestroyImmediate(target);

            _attacher.Tick();

            Assert.That(ProxiesOn(owner).Length, Is.EqualTo(0));
        }

        /// <summary>
        /// What: nothing is attached outside Play Mode, where Unity never delivers the messages a
        /// proxy exists to receive.
        /// </summary>
        [Test]
        public void Tick_WhileNotPlaying_AttachesNothing()
        {
            HotReloadUnityMessageProxyFixture target = CreateFixture();
            BindFixture();
            _playMode.IsPlaying = false;

            _attacher.Tick();

            Assert.That(ProxiesOn(target.gameObject).Length, Is.EqualTo(0));
        }

        /// <summary>
        /// What: a suspended attacher keeps its bindings but attaches nothing, and picks the work
        /// back up once it is resumed.
        /// </summary>
        [Test]
        public void Tick_WhileSuspended_AttachesNothingUntilResumed()
        {
            HotReloadUnityMessageProxyFixture target = CreateFixture();
            BindFixture();
            _attacher.Suspend();

            _attacher.Tick();
            Assert.That(ProxiesOn(target.gameObject).Length, Is.EqualTo(0));

            _attacher.Resume();
            _attacher.Tick();
            Assert.That(ProxiesOn(target.gameObject).Length, Is.EqualTo(1));
        }

        /// <summary>
        /// What: clearing takes every proxy off the scene and forgets every binding, which is what
        /// reverting a hot reload has to leave behind.
        /// </summary>
        [Test]
        public void Clear_AfterProxiesWereAttached_RemovesThemAndForgetsTheBindings()
        {
            HotReloadUnityMessageProxyFixture target = CreateFixture();
            BindFixture();
            _attacher.Tick();

            _attacher.Clear();

            Assert.That(ProxiesOn(target.gameObject).Length, Is.EqualTo(0));
            Assert.That(_attacher.BoundTargetTypes, Is.Empty);
        }

        /// <summary>
        /// What: unbinding one type leaves the proxy of another target type on the same GameObject
        /// alone, so reverting one type does not silence the messages added to a second one.
        /// </summary>
        [Test]
        public void Unbind_WithAnotherTargetTypeOnTheSameObject_KeepsTheOtherProxy()
        {
            GameObject owner = CreateGameObject("ProxyAttacherTests_Shared");
            owner.AddComponent<HotReloadUnityMessageProxyFixture>();
            owner.AddComponent<HotReloadUnityMessageInternalFixture>();
            BindFixture();
            BindInternalFixture();
            _attacher.Tick();
            Assert.That(ProxiesOn(owner).Length, Is.EqualTo(2));

            _attacher.Unbind(typeof(HotReloadUnityMessageProxyFixture));

            HotReloadUnityMessageProxy[] proxies = ProxiesOn(owner);
            Assert.That(proxies.Length, Is.EqualTo(1));
            Assert.That(proxies[0].Target, Is.InstanceOf<HotReloadUnityMessageInternalFixture>());
        }

        /// <summary>
        /// What: rebinding a bound type to new shims for the same messages keeps the attached proxy
        /// in place, so no second AddComponent reruns an added Start, and the proxy now forwards to
        /// the new shim.
        /// </summary>
        [Test]
        public void TryRebind_WithTheSameMessages_KeepsTheAttachedProxyAndForwardsToTheNewShim()
        {
            HotReloadUnityMessageProxyFixture target = CreateFixture();
            BindFixture();
            _attacher.Tick();
            HotReloadUnityMessageProxy first = ProxiesOn(target.gameObject)[0];

            bool rebound = _attacher.TryRebind(
                typeof(HotReloadUnityMessageProxyFixture),
                CreateBinding(
                    typeof(HotReloadUnityMessageDuplicateFixtureShims),
                    typeof(HotReloadUnityMessageProxyFixture),
                    new[] { "Update" }));
            _attacher.Tick();

            Assert.That(rebound, Is.True);
            HotReloadUnityMessageProxy[] proxies = ProxiesOn(target.gameObject);
            Assert.That(proxies.Length, Is.EqualTo(1));
            Assert.That(proxies[0], Is.SameAs(first));
            InvokeMessage(proxies[0], "Update");
            Assert.That(target.UpdateCount, Is.EqualTo(100), "The new shim adds 100; the first one adds 1.");
        }

        /// <summary>
        /// What: a binding that declares other messages is refused, leaving the proxy type and the
        /// binding it forwards through as they were.
        /// </summary>
        [Test]
        public void TryRebind_WithOtherMessages_RefusesAndKeepsTheProxyType()
        {
            BindFixture("Update");
            Type before = _attacher.FindProxyType(typeof(HotReloadUnityMessageProxyFixture));

            bool rebound = _attacher.TryRebind(
                typeof(HotReloadUnityMessageProxyFixture),
                CreateBinding(
                    typeof(HotReloadUnityMessageProxyFixtureShims),
                    typeof(HotReloadUnityMessageProxyFixture),
                    new[] { "Update", "Start" }));

            Assert.That(rebound, Is.False);
            Assert.That(_attacher.FindProxyType(typeof(HotReloadUnityMessageProxyFixture)), Is.SameAs(before));
        }

        /// <summary>
        /// What: a slot whose gate differs is refused even when the name and parameters match, so a
        /// proxy type is only reused when every slot carries the same meaning.
        /// </summary>
        [Test]
        public void TryRebind_WithADifferentGate_Refuses()
        {
            BindFixture("Update");
            HotReloadUnityMessageBinding ungated = new HotReloadUnityMessageBinding(
                typeof(HotReloadUnityMessageProxyFixture),
                new[] { "Update" },
                new[] { false },
                new Action<MonoBehaviour, object[]>[] { (target, args) => { } },
                new[] { Type.EmptyTypes });

            bool rebound = _attacher.TryRebind(typeof(HotReloadUnityMessageProxyFixture), ungated);

            Assert.That(rebound, Is.False);
        }

        /// <summary>
        /// What: a type that was never bound is refused, so the caller falls back to building a
        /// proxy type for it.
        /// </summary>
        [Test]
        public void TryRebind_ForATypeNotBound_Refuses()
        {
            bool rebound = _attacher.TryRebind(
                typeof(HotReloadUnityMessageProxyFixture),
                CreateBinding(
                    typeof(HotReloadUnityMessageProxyFixtureShims),
                    typeof(HotReloadUnityMessageProxyFixture),
                    new[] { "Update" }));

            Assert.That(rebound, Is.False);
            Assert.That(_attacher.FindProxyType(typeof(HotReloadUnityMessageProxyFixture)), Is.Null);
        }

        private void BindFixture(params string[] messageNames)
        {
            string[] names = messageNames.Length == 0 ? new[] { "Update" } : messageNames;
            _attacher.Bind(
                typeof(HotReloadUnityMessageProxyFixture),
                CreateBinding(typeof(HotReloadUnityMessageProxyFixtureShims), typeof(HotReloadUnityMessageProxyFixture), names));
        }

        private void BindInternalFixture()
        {
            _attacher.Bind(
                typeof(HotReloadUnityMessageInternalFixture),
                CreateBinding(
                    typeof(HotReloadUnityMessageInternalFixtureShims),
                    typeof(HotReloadUnityMessageInternalFixture),
                    new[] { "Update" }));
        }

        private static HotReloadUnityMessageBinding CreateBinding(Type shimHost, Type targetType, string[] messageNames)
        {
            List<MethodInfo> shims = new List<MethodInfo>();
            foreach (string messageName in messageNames)
            {
                MethodInfo shim = shimHost.GetMethod(messageName, BindingFlags.Public | BindingFlags.Static);
                Assert.That(shim, Is.Not.Null, "The fixture shim method must exist.");
                shims.Add(shim);
            }

            return HotReloadUnityMessageForwarderFactory.CreateBinding(targetType, shims);
        }

        private static void InvokeMessage(HotReloadUnityMessageProxy proxy, string messageName)
        {
            MethodInfo message = proxy.GetType().GetMethod(
                messageName,
                BindingFlags.Public | BindingFlags.Instance);
            Assert.That(message, Is.Not.Null, "The generated proxy must declare " + messageName + ".");
            message.Invoke(proxy, null);
        }

        private static HotReloadUnityMessageProxy[] ProxiesOn(GameObject owner)
        {
            return owner.GetComponents<HotReloadUnityMessageProxy>();
        }

        private HotReloadUnityMessageProxyFixture CreateFixture()
        {
            GameObject owner = CreateGameObject("ProxyAttacherTests_Target");
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
