using System;
using System.Collections.Generic;
using System.Reflection;

using NUnit.Framework;

using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers the Play Mode transitions the editor hooks react to: what leaving Play Mode takes
    /// off and holds back, and what entering it again lets through.
    /// </summary>
    public class HotReloadUnityMessageForwardingEditorHooksTests
    {
        private const string FixturePath = "Assets/Tests/Editor/HotReload/EditorHooksFixture.cs";

        private readonly List<GameObject> _created = new List<GameObject>();
        private Func<HotReloadUnityMessageForwarding> _previousProvider;
        private HotReloadDomainTestScope _scope;
        private HotReloadDomainTestAccess _access;
        private HotReloadUnityMessageProxyAttacher _attacher;
        private HotReloadUnityMessageForwarding _forwarding;

        [SetUp]
        public void SetUp()
        {
            _scope = new HotReloadDomainTestScope();
            _access = new HotReloadDomainTestAccess();
            _attacher = new HotReloadUnityMessageProxyAttacher(
                new HotReloadStubPlayModeQuery { IsPlaying = true },
                new HotReloadUnityMessageProxyTypeBuilder());
            _forwarding = new HotReloadUnityMessageForwarding(_access.Domain, _attacher);
            _previousProvider = HotReloadUnityMessageForwardingEditorHooks.GetForwarding;
            HotReloadUnityMessageForwardingEditorHooks.GetForwarding = () => _forwarding;
        }

        [TearDown]
        public void TearDown()
        {
            HotReloadUnityMessageForwardingEditorHooks.GetForwarding = _previousProvider;
            _forwarding.Clear();
            foreach (GameObject gameObject in _created)
            {
                if (gameObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(gameObject);
                }
            }

            _created.Clear();
            _scope.Dispose();
        }

        /// <summary>
        /// What: leaving Play Mode takes the attached proxies off, so the instances that survive
        /// the stop do not come back carrying a component of a type this session emitted.
        /// </summary>
        [Test]
        public void Handle_ExitingPlayMode_TakesTheAttachedProxiesOff()
        {
            HotReloadUnityMessageProxyFixture target = ArrangeAttachedProxy();

            HotReloadUnityMessageForwardingEditorHooks.Handle(PlayModeStateChange.ExitingPlayMode);

            Assert.That(ProxiesOn(target.gameObject), Is.Empty);
            Assert.That(_attacher.BoundTargetTypes, Is.Empty);
        }

        /// <summary>
        /// What: a tick between the request to leave Play Mode and the moment it leaves attaches
        /// nothing, so no proxy is stranded on an instance the stop is about to destroy.
        /// </summary>
        [Test]
        public void Handle_ExitingPlayMode_HoldsBackTheProxyTheNextTickWouldAttach()
        {
            HotReloadUnityMessageProxyFixture target = ArrangeAttachedProxy();
            HotReloadUnityMessageForwardingEditorHooks.Handle(PlayModeStateChange.ExitingPlayMode);

            _forwarding.Tick();

            Assert.That(ProxiesOn(target.gameObject), Is.Empty);
        }

        /// <summary>
        /// What: entering Play Mode again lets the next tick attach, so a session that stopped and
        /// started without a domain reload keeps forwarding the added messages.
        /// </summary>
        [Test]
        public void Handle_EnteredPlayMode_LetsTheNextTickAttachAgain()
        {
            HotReloadUnityMessageProxyFixture target = ArrangeAttachedProxy();
            HotReloadUnityMessageForwardingEditorHooks.Handle(PlayModeStateChange.ExitingPlayMode);

            HotReloadUnityMessageForwardingEditorHooks.Handle(PlayModeStateChange.EnteredPlayMode);
            _forwarding.Tick();

            Assert.That(ProxiesOn(target.gameObject).Length, Is.EqualTo(1));
        }

        /// <summary>
        /// What: a transition that is neither of the two the hooks act on leaves the proxies and
        /// the bindings exactly as they were.
        /// </summary>
        [Test]
        public void Handle_ExitingEditMode_LeavesTheProxiesAlone()
        {
            HotReloadUnityMessageProxyFixture target = ArrangeAttachedProxy();

            HotReloadUnityMessageForwardingEditorHooks.Handle(PlayModeStateChange.ExitingEditMode);

            Assert.That(ProxiesOn(target.gameObject).Length, Is.EqualTo(1));
            Assert.That(
                _attacher.FindProxyType(typeof(HotReloadUnityMessageProxyFixture)), Is.Not.Null);
        }

        // An instance that already carries the proxy of an added Update, which is the state every
        // transition below is measured against.
        private HotReloadUnityMessageProxyFixture ArrangeAttachedProxy()
        {
            GameObject owner = new GameObject("EditorHooksTests_Target");
            _created.Add(owner);
            HotReloadUnityMessageProxyFixture target =
                owner.AddComponent<HotReloadUnityMessageProxyFixture>();
            MethodInfo shim = typeof(HotReloadUnityMessageProxyFixtureShims).GetMethod(
                "Update",
                BindingFlags.Public | BindingFlags.Static);
            Assert.That(shim, Is.Not.Null, "The fixture shim method must exist.");
            _access.GetOrBeginAddedMemberGeneration(FixturePath)
                .RegisterAddedMethod("Fixture.Update", shim, FixturePath, "Update", "Fixture");
            _forwarding.Tick();
            Assert.That(
                ProxiesOn(owner).Length,
                Is.EqualTo(1),
                "Arrange: the tick must have attached the proxy.");
            return target;
        }

        private static HotReloadUnityMessageProxy[] ProxiesOn(GameObject owner)
        {
            return owner.GetComponents<HotReloadUnityMessageProxy>();
        }
    }
}
