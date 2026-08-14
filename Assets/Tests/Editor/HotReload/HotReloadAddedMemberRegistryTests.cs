using System.Collections.Generic;
using System.Reflection;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Pure ledger coverage for added-method registration, overwrite, per-file clear, and revert.
    /// </summary>
    public class HotReloadAddedMemberRegistryTests
    {
        private const string FileOne = "Assets/Tests/Editor/HotReload/FileOne.cs";
        private const string FileTwo = "Assets/Tests/Editor/HotReload/FileTwo.cs";

        [TearDown]
        public void TearDown()
        {
            HotReloadAddedMemberRegistry.Clear();
        }

        /// <summary>
        /// What: Register records the shim and Describe lists it; a second Register of the same
        /// key overwrites rather than stacking.
        /// </summary>
        [Test]
        public void Register_OverwritesSameKey_AndDescribeListsIt()
        {
            MethodInfo firstShim = RequireExistingCaller();
            MethodInfo secondShim = RequireReadPrivateSeed();
            HotReloadAddedMemberRegistry.BeginFileGeneration(FileOne);
            HotReloadAddedMemberRegistry.Register(FileOne, "Host.AddedPing(System.Int32)", firstShim, FileOne);
            HotReloadAddedMemberRegistry.Register(FileOne, "Host.AddedPing(System.Int32)", secondShim, FileOne);

            Assert.That(HotReloadAddedMemberRegistry.Count, Is.EqualTo(1));
            IReadOnlyList<HotReloadAddedMemberInfo> listed = HotReloadAddedMemberRegistry.Describe();
            Assert.That(listed.Count, Is.EqualTo(1));
            Assert.That(listed[0].MethodKey, Is.EqualTo("Host.AddedPing(System.Int32)"));
            Assert.That(listed[0].ShimMethod, Is.EqualTo(secondShim));
            Assert.That(listed[0].FilePath, Is.EqualTo(FileOne));
        }

        /// <summary>
        /// What: BeginFileGeneration drops that file's members and leaves another file intact.
        /// </summary>
        [Test]
        public void BeginFileGeneration_ClearsOnlyThatFile()
        {
            MethodInfo shim = RequireExistingCaller();
            HotReloadAddedMemberRegistry.BeginFileGeneration(FileOne);
            HotReloadAddedMemberRegistry.Register(FileOne, "Host.AddedOne()", shim, FileOne);
            HotReloadAddedMemberRegistry.BeginFileGeneration(FileTwo);
            HotReloadAddedMemberRegistry.Register(FileTwo, "Host.AddedTwo()", shim, FileTwo);

            HotReloadAddedMemberRegistry.BeginFileGeneration(FileOne);

            Assert.That(HotReloadAddedMemberRegistry.Count, Is.EqualTo(1));
            IReadOnlyList<HotReloadAddedMemberInfo> listed = HotReloadAddedMemberRegistry.Describe();
            Assert.That(listed[0].MethodKey, Is.EqualTo("Host.AddedTwo()"));
        }

        /// <summary>
        /// What: Clear removes every file's added members.
        /// </summary>
        [Test]
        public void Clear_RemovesAllFiles()
        {
            MethodInfo shim = RequireExistingCaller();
            HotReloadAddedMemberRegistry.BeginFileGeneration(FileOne);
            HotReloadAddedMemberRegistry.Register(FileOne, "Host.AddedOne()", shim, FileOne);
            HotReloadAddedMemberRegistry.BeginFileGeneration(FileTwo);
            HotReloadAddedMemberRegistry.Register(FileTwo, "Host.AddedTwo()", shim, FileTwo);

            HotReloadAddedMemberRegistry.Clear();

            Assert.That(HotReloadAddedMemberRegistry.Count, Is.EqualTo(0));
            Assert.That(HotReloadAddedMemberRegistry.Describe(), Is.Empty);
        }

        private static MethodInfo RequireExistingCaller()
        {
            MethodInfo method = typeof(HotReloadAddedMemberHost).GetMethod(
                nameof(HotReloadAddedMemberHost.ExistingCaller),
                BindingFlags.Instance | BindingFlags.Public);
            Assert.That(method, Is.Not.Null);
            return method;
        }

        private static MethodInfo RequireReadPrivateSeed()
        {
            MethodInfo method = typeof(HotReloadAddedMemberHost).GetMethod(
                nameof(HotReloadAddedMemberHost.ReadPrivateSeed),
                BindingFlags.Instance | BindingFlags.Public);
            Assert.That(method, Is.Not.Null);
            return method;
        }
    }
}
