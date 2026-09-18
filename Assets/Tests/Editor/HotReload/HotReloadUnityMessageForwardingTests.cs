using System;
using System.Collections.Generic;
using System.Reflection;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers reconciling the proxies against the domain: which target types get bound, when a
    /// binding is rebuilt, and how a proxy that cannot be built is reported.
    /// </summary>
    public class HotReloadUnityMessageForwardingTests
    {
        private const string FixturePath = "Assets/Tests/Editor/HotReload/ForwardingFixture.cs";
        private const string SecondPath = "Assets/Tests/Editor/HotReload/ForwardingSecondFixture.cs";

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
                new HotReloadStubPlayModeQuery { IsPlaying = false },
                new HotReloadUnityMessageProxyTypeBuilder());
            _forwarding = new HotReloadUnityMessageForwarding(_access.Domain, _attacher);
        }

        [TearDown]
        public void TearDown()
        {
            _forwarding.Clear();
            _scope.Dispose();
        }

        /// <summary>
        /// What: an added method the engine would dispatch as a Unity message binds its target type.
        /// </summary>
        [Test]
        public void Reconcile_WithAnAddedForwardedMessage_BindsTheTargetType()
        {
            RegisterAdded(FixturePath, "Fixture.Update", ShimOf(typeof(HotReloadUnityMessageProxyFixtureShims), "Update"));

            _forwarding.Reconcile(null);

            Assert.That(_attacher.FindProxyType(typeof(HotReloadUnityMessageProxyFixture)), Is.Not.Null);
        }

        /// <summary>
        /// What: reconciling again over unchanged shims keeps the proxy type already built, so an
        /// editor update does not emit a new type every tick.
        /// </summary>
        [Test]
        public void Reconcile_RunTwiceOverTheSameShims_KeepsTheProxyTypeItBuilt()
        {
            RegisterAdded(FixturePath, "Fixture.Update", ShimOf(typeof(HotReloadUnityMessageProxyFixtureShims), "Update"));
            _forwarding.Reconcile(null);
            Type first = _attacher.FindProxyType(typeof(HotReloadUnityMessageProxyFixture));

            _forwarding.Reconcile(null);

            Assert.That(_attacher.FindProxyType(typeof(HotReloadUnityMessageProxyFixture)), Is.SameAs(first));
        }

        /// <summary>
        /// What: re-applying the same method produces a new shim, and the proxy is rebuilt around
        /// it even though the method key never changed.
        /// </summary>
        [Test]
        public void Reconcile_AfterTheSameMethodKeyGotANewShim_RebuildsTheProxyType()
        {
            RegisterAdded(FixturePath, "Fixture.Update", ShimOf(typeof(HotReloadUnityMessageProxyFixtureShims), "Update"));
            _forwarding.Reconcile(null);
            Type first = _attacher.FindProxyType(typeof(HotReloadUnityMessageProxyFixture));
            RegisterAdded(
                FixturePath,
                "Fixture.Update",
                ShimOf(typeof(HotReloadUnityMessageDuplicateFixtureShims), "Update"));

            _forwarding.Reconcile(null);

            Type second = _attacher.FindProxyType(typeof(HotReloadUnityMessageProxyFixture));
            Assert.That(second, Is.Not.Null);
            Assert.That(second, Is.Not.SameAs(first));
        }

        /// <summary>
        /// What: reverting everything leaves the domain with no added messages, and the next
        /// reconcile unbinds the type rather than leaving proxies bound to shims that are gone.
        /// </summary>
        [Test]
        public void Reconcile_AfterEverythingWasReverted_UnbindsTheTargetType()
        {
            RegisterAdded(FixturePath, "Fixture.Update", ShimOf(typeof(HotReloadUnityMessageProxyFixtureShims), "Update"));
            _forwarding.Reconcile(null);
            HotReloadCompositionRoot.Services.Patcher.RevertAll();

            _forwarding.Reconcile(null);

            Assert.That(_attacher.FindProxyType(typeof(HotReloadUnityMessageProxyFixture)), Is.Null);
            Assert.That(_attacher.BoundTargetTypes, Is.Empty);
        }

        /// <summary>
        /// What: a target type whose proxy cannot be emitted is reported once and left unbound,
        /// while the other types of the same run are bound as usual.
        /// </summary>
        [Test]
        public void Reconcile_WhenAProxyCannotBeBuilt_WarnsAndStillBindsTheOtherTypes()
        {
            RegisterUnbuildableFixture();
            RegisterAdded(
                SecondPath,
                "Internal.Update",
                ShimOf(typeof(HotReloadUnityMessageInternalFixtureShims), "Update"));
            List<string> warnings = new List<string>();

            _forwarding.Reconcile(warnings);

            Assert.That(warnings.Count, Is.EqualTo(1));
            Assert.That(warnings[0], Does.Contain(typeof(HotReloadUnityMessageProxyFixture).FullName));
            Assert.That(_attacher.FindProxyType(typeof(HotReloadUnityMessageProxyFixture)), Is.Null);
            Assert.That(_attacher.FindProxyType(typeof(HotReloadUnityMessageInternalFixture)), Is.Not.Null);
        }

        /// <summary>
        /// What: the same failing shims are not retried, so the warning is reported once instead of
        /// on every reconcile until the user edits the file again.
        /// </summary>
        [Test]
        public void Reconcile_RunAgainOverFailingShims_DoesNotRepeatTheWarning()
        {
            RegisterUnbuildableFixture();
            _forwarding.Reconcile(new List<string>());
            List<string> warnings = new List<string>();

            _forwarding.Reconcile(warnings);

            Assert.That(warnings, Is.Empty);
        }

        // The same message added to one type twice: a proxy could only declare it once, so building
        // the binding refuses, which is the failure this feature has to survive.
        private void RegisterUnbuildableFixture()
        {
            HotReloadFileGeneration generation = _access.GetOrBeginAddedMemberGeneration(FixturePath);
            generation.RegisterAddedMethod(
                "Fixture.Update.A",
                ShimOf(typeof(HotReloadUnityMessageProxyFixtureShims), "Update"),
                FixturePath);
            generation.RegisterAddedMethod(
                "Fixture.Update.B",
                ShimOf(typeof(HotReloadUnityMessageDuplicateFixtureShims), "Update"),
                FixturePath);
        }

        private void RegisterAdded(string projectRelativePath, string methodKey, MethodInfo shim)
        {
            _access.GetOrBeginAddedMemberGeneration(projectRelativePath)
                .RegisterAddedMethod(methodKey, shim, projectRelativePath);
        }

        private static MethodInfo ShimOf(Type host, string methodName)
        {
            MethodInfo shim = host.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
            Assert.That(shim, Is.Not.Null, "The fixture shim method must exist.");
            return shim;
        }
    }
}
