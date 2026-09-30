using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Pure coverage for how an added member's InvocationCount is read from the counter field its
    /// shim increments.
    /// </summary>
    public class HotReloadAddedMemberInfoTests
    {
        private const string MethodKey = "Fixture.Added()";
        private const string FilePath = "Assets/Fixture.cs";

        // Stands in for the counter a generated shim declares, which the shim increments on each
        // call; SetUp zeroes it so no test sees another's calls.
        public static long Calls;

        [SetUp]
        public void SetUp()
        {
            Calls = 0;
        }

        /// <summary>
        /// What: the count is the counter's value when it is read, not when the member was
        /// registered, so calls made after the registration are included.
        /// </summary>
        [Test]
        public void ReadInvocationCount_ReturnsTheCountersValueWhenRead()
        {
            HotReloadAddedMemberInfo member = new HotReloadAddedMemberInfo(
                MethodKey,
                FilePath,
                null,
                invocationCounter: typeof(HotReloadAddedMemberInfoTests).GetField(nameof(Calls)));
            Calls = 3;

            Assert.That(member.ReadInvocationCount(), Is.EqualTo(3));
        }

        /// <summary>
        /// What: a member built without a counter refuses to report a count, rather than reporting
        /// 0, which would read as never invoked.
        /// </summary>
        [Test]
        public void ReadInvocationCount_WithoutACounter_Throws()
        {
            HotReloadAddedMemberInfo member = new HotReloadAddedMemberInfo(MethodKey, FilePath, null);

            Assert.Throws<InvalidOperationException>(() => member.ReadInvocationCount());
        }
    }
}
