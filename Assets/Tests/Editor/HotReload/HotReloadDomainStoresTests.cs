using System.IO;
using System.Reflection;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for the domain-scoped store reset a full revert runs.
    /// </summary>
    public class HotReloadDomainStoresTests
    {
        private const string FixtureProjectRelativePath =
            "Assets/Tests/Editor/HotReload/DomainStoresFixture.cs";
        private const string FixtureMethodKey = "DomainStoresFixture.AddedMember()";
        private const string SupersededMethodKey = "DomainStoresFixture.Superseded()";

        [TearDown]
        public void TearDown()
        {
            HotReloadPatcher.RevertAll();
        }

        /// <summary>
        /// What: the reset empties every domain-scoped store, each checked on its own line so a
        /// single missed store cannot hide behind the others.
        /// </summary>
        [Test]
        public void ResetForRevertAll_EmptiesEveryDomainScopedStore()
        {
            string staticFieldKey = HotReloadAddedFieldStore.FormatFieldKey("DomainStoresHost", "seed");
            FillEveryStore(staticFieldKey);

            HotReloadDomainStores.ResetForRevertAll();

            Assert.That(HotReloadAddedMemberRegistry.HasGeneration(FixtureProjectRelativePath), Is.False);
            Assert.That(
                HotReloadAddedFieldStore.GetOrInitStatic(staticFieldKey, () => 20),
                Is.EqualTo(20));
            Assert.That(HotReloadAddedFieldRegistry.DescribeAll(), Is.Empty);
            Assert.That(HotReloadInvocationRegistry.GetCount(FixtureMethodKey), Is.EqualTo(0));
            Assert.That(HotReloadAppliedSourceLedger.TryGet(FixtureProjectRelativePath), Is.Null);
            Assert.That(
                HotReloadSupersededSignatureRegistry.TryGetReplacement(
                    SupersededMethodKey,
                    out string _),
                Is.False);
        }

        private static void FillEveryStore(string staticFieldKey)
        {
            Assembly assembly = typeof(HotReloadDomainStoresTests).Assembly;
            HotReloadFileGenerations.BeginFileGeneration(
                FixtureProjectRelativePath,
                File.ReadAllBytes(assembly.Location),
                null,
                assembly);
            HotReloadFileGenerations.RegisterAddedMethod(
                FixtureProjectRelativePath,
                FixtureMethodKey,
                typeof(HotReloadDomainStoresTests).GetMethod(
                    nameof(AddedTarget),
                    BindingFlags.Static | BindingFlags.NonPublic),
                FixtureProjectRelativePath);
            HotReloadAddedFieldStore.SetStatic(staticFieldKey, 2);
            HotReloadAddedFieldRegistry.ReplaceForFile(
                FixtureProjectRelativePath,
                new[] { "DomainStoresHost.count" });
            HotReloadInvocationRegistry.Increment(FixtureMethodKey);
            HotReloadAppliedSourceLedger.Record(FixtureProjectRelativePath, "hash", true);
            HotReloadSupersededSignatureRegistry.Record(SupersededMethodKey, "Superseded(int)");
        }

        private static int AddedTarget()
        {
            return 1;
        }
    }
}
