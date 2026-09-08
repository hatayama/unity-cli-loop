using System.Reflection;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Coverage for the one read that answers how much this domain currently holds.
    /// </summary>
    public class HotReloadActiveChangeCountsTests
    {
        private const string AddedMemberFilePath =
            "Assets/Tests/Editor/HotReload/ActiveChangeCountsFile.cs";

        [TearDown]
        public void TearDown()
        {
            HotReloadPatcher.RevertAll();
        }

        /// <summary>
        /// What: an added member with no patched method still counts, both as a patch-and-added
        /// member and in the total a Domain Reload would discard.
        /// </summary>
        [Test]
        public void Capture_WithAddedMemberOnly_CountsItInBothTotals()
        {
            MethodInfo shim = RequireShimMethod();
            HotReloadAddedMemberRegistry.BeginFileGeneration(AddedMemberFilePath);
            HotReloadAddedMemberRegistry.Register(
                AddedMemberFilePath,
                "Host.AddedPing()",
                shim,
                AddedMemberFilePath);

            HotReloadActiveChangeSnapshot snapshot = HotReloadActiveChangeCounts.Capture();

            Assert.That(snapshot.PatchAndAddedMemberCount, Is.EqualTo(1));
            Assert.That(
                snapshot.RuntimeChangeTotal,
                Is.EqualTo(1 + snapshot.IntroducedTypeCount));
        }

        /// <summary>
        /// What: the snapshot reports the same three numbers the separate accessors do, so
        /// removing an accessor or changing one of the sums is caught here.
        /// </summary>
        [Test]
        public void Capture_ReadsTheSameNumbersAsTheAccessors()
        {
            HotReloadActiveChangeSnapshot snapshot = HotReloadActiveChangeCounts.Capture();

            Assert.That(
                snapshot.PatchAndAddedMemberCount,
                Is.EqualTo(HotReloadPatcher.ActiveChangeCount));
            Assert.That(
                snapshot.IntroducedTypeCount,
                Is.EqualTo(HotReloadActiveChangeCounts.IntroducedTypeCount));
            Assert.That(
                snapshot.RuntimeChangeTotal,
                Is.EqualTo(HotReloadActiveChangeCounts.RuntimeChangeTotal));
        }

        private static MethodInfo RequireShimMethod()
        {
            MethodInfo method = typeof(HotReloadActiveChangeCountsTests).GetMethod(
                nameof(ShimTarget),
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return method;
        }

        private static void ShimTarget()
        {
        }
    }
}
