using NUnit.Framework;

using UnityEditor;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers the Unity resolver's refusals for asset hosts and for identities that name nothing.
    /// Only existing package assets are read; no scene object or asset is created.
    /// </summary>
    public sealed class HotReloadUnityWiredValueResolverEditorReadTests
    {
        // An imported asset of this project that every test run has, read only to learn its GUID.
        private const string ExistingAssetPath =
            "Assets/Tests/Editor/HotReload/UnityCLILoop.Tests.Editor.HotReload.asmdef";

        // A GUID no asset uses, so the asset database maps it to no path.
        private const string UnknownGuid = "f0e1d2c3b4a5968778695a4b3c2d1e0f";

        private const string MissingSceneHostIdentity =
            "scene:UloopNoSuchScene|path:Host[0]|component:UnityEngine.Transform|index:0";

        private HotReloadUnityWiredValueResolver _resolver;

        [SetUp]
        public void SetUp()
        {
            _resolver = new HotReloadUnityWiredValueResolver(new HotReloadSceneObjectIdentityBuilder());
        }

        /// <summary>
        /// Verifies that an asset host is never reported missing, even when unreadable scenes count as missing.
        /// </summary>
        [Test]
        public void IsHostMissing_WhenHostIsAnAsset_IsFalseEvenWhenUnreadableScenesCountAsMissing()
        {
            bool missing = _resolver.IsHostMissing("asset:" + UnknownGuid + "|local:1", true);

            Assert.That(missing, Is.False);
        }

        /// <summary>
        /// Verifies that an asset host is not looked up again and yields no host.
        /// </summary>
        [Test]
        public void TryResolveHost_WhenHostIsAnAsset_ReturnsFalseWithoutHost()
        {
            bool resolved = _resolver.TryResolveHost("asset:" + UnknownGuid + "|local:1", out object host);

            Assert.That(resolved, Is.False);
            Assert.That(host, Is.Null);
        }

        /// <summary>
        /// Verifies that a scene host whose scene is not loaded is not resolved and yields no host.
        /// </summary>
        [Test]
        public void TryResolveHost_WhenHostSceneIsNotLoaded_ReturnsFalseWithoutHost()
        {
            bool resolved = _resolver.TryResolveHost(MissingSceneHostIdentity, out object host);

            Assert.That(resolved, Is.False);
            Assert.That(host, Is.Null);
        }

        /// <summary>
        /// Verifies that an asset identity without a local id fails and the reason names the identity.
        /// </summary>
        [Test]
        public void TryResolve_WhenAssetIdentityHasNoLocalId_FailsNamingTheIdentity()
        {
            const string identity = "asset:" + UnknownGuid;
            HotReloadWiredValueDescriptor descriptor = HotReloadWiredValueDescriptor.Asset(identity, "UnityEngine.Object");

            bool resolved = _resolver.TryResolve(descriptor, out object value, out string failureReason);

            Assert.That(resolved, Is.False);
            Assert.That(value, Is.Null);
            Assert.That(
                failureReason,
                Is.EqualTo("no object is at " + identity + " any more; wire the field again with a live object"));
        }

        /// <summary>
        /// Verifies that an asset identity whose GUID maps to no asset path fails and the reason names the identity.
        /// </summary>
        [Test]
        public void TryResolve_WhenAssetGuidIsUnknown_FailsNamingTheIdentity()
        {
            const string identity = "asset:" + UnknownGuid + "|local:1";
            Assert.That(
                AssetDatabase.GUIDToAssetPath(UnknownGuid),
                Is.Null.Or.Empty,
                "Precondition: the GUID must not belong to any asset.");
            HotReloadWiredValueDescriptor descriptor = HotReloadWiredValueDescriptor.Asset(identity, "UnityEngine.Object");

            bool resolved = _resolver.TryResolve(descriptor, out object value, out string failureReason);

            Assert.That(resolved, Is.False);
            Assert.That(value, Is.Null);
            Assert.That(
                failureReason,
                Is.EqualTo("no object is at " + identity + " any more; wire the field again with a live object"));
        }

        /// <summary>
        /// Verifies that an existing asset whose objects all have other local ids does not resolve.
        /// </summary>
        [Test]
        public void TryResolve_WhenLocalIdMatchesNoObjectInTheAsset_FailsNamingTheIdentity()
        {
            string guid = AssetDatabase.AssetPathToGUID(ExistingAssetPath);
            Assert.That(guid, Is.Not.Null.And.Not.Empty, "Precondition: the asset must be imported.");
            string identity = "asset:" + guid + "|local:-424242";
            HotReloadWiredValueDescriptor descriptor = HotReloadWiredValueDescriptor.Asset(identity, "UnityEngine.Object");

            bool resolved = _resolver.TryResolve(descriptor, out object value, out string failureReason);

            Assert.That(resolved, Is.False);
            Assert.That(value, Is.Null);
            Assert.That(
                failureReason,
                Is.EqualTo("no object is at " + identity + " any more; wire the field again with a live object"));
        }

        /// <summary>
        /// Verifies that a persistent asset host is described by its GUID and local id.
        /// </summary>
        [Test]
        public void DescribeHost_WhenHostIsAPersistentAsset_ReturnsItsAssetIdentity()
        {
            string guid = AssetDatabase.AssetPathToGUID(ExistingAssetPath);
            UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(ExistingAssetPath);
            Assert.That(asset, Is.Not.Null, "Precondition: the asset must be loadable.");

            string identity = _resolver.DescribeHost(asset);

            Assert.That(identity, Does.StartWith("asset:" + guid + "|local:"));
        }
    }
}
