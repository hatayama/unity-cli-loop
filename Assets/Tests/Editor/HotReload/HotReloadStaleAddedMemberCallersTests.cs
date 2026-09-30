using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Pure coverage for the run-level warning about calls that earlier reloads left running into
    /// added members no generation registers any more: which calls it names, and on which runs.
    /// </summary>
    public class HotReloadStaleAddedMemberCallersTests
    {
        private const string CallerPath = "Assets/Caller.cs";
        private const string HostPath = "Assets/Host.cs";
        private const string CallerLabel = "Ns.Caller.Call()";
        private const string RetiredLabel = "Ns.Host.Bar(System.Int32)";

        /// <summary>
        /// What: a call into an added member no generation registers is named as "caller calls
        /// member" in the one warning of a run that touched both files.
        /// </summary>
        [Test]
        public void DescribeOrNull_CallIntoAnUnregisteredMember_NamesThePair()
        {
            string warning = new HotReloadStaleAddedMemberCallers().DescribeOrNull(
                new[] { CreateCall(CallerLabel, RetiredLabel) },
                Array.Empty<HotReloadAddedMemberInfo>(),
                new[] { CallerPath, HostPath });

            Assert.That(
                warning,
                Is.EqualTo(
                    string.Format(
                        HotReloadConstants.StaleAddedMemberCallsWarningFormat,
                        CallerLabel + " calls " + RetiredLabel)));
        }

        /// <summary>
        /// What: a call into a member some generation still registers under that label is not
        /// reported, whichever file registers it now.
        /// </summary>
        [Test]
        public void DescribeOrNull_CallIntoARegisteredMember_ReportsNothing()
        {
            string warning = new HotReloadStaleAddedMemberCallers().DescribeOrNull(
                new[] { CreateCall(CallerLabel, RetiredLabel) },
                new[] { new HotReloadAddedMemberInfo(RetiredLabel, "Assets/HostPart.cs", null) },
                new[] { CallerPath, HostPath });

            Assert.That(warning, Is.Null);
        }

        /// <summary>
        /// What: a run that touched only the caller's file, or only the file that declared the
        /// member, still reports the call; a run that touched neither does not.
        /// </summary>
        [TestCase(CallerPath, true)]
        [TestCase(HostPath, true)]
        [TestCase("Assets/Other.cs", false)]
        public void DescribeOrNull_ReportsOnlyWhenTheRunTouchedEitherEnd(string pathInRun, bool expectWarning)
        {
            string warning = new HotReloadStaleAddedMemberCallers().DescribeOrNull(
                new[] { CreateCall(CallerLabel, RetiredLabel) },
                Array.Empty<HotReloadAddedMemberInfo>(),
                new[] { pathInRun });

            Assert.That(warning != null, Is.EqualTo(expectWarning), warning);
        }

        /// <summary>
        /// What: several stale calls are listed once each, in ordinal order, whatever order the
        /// generations reported them in.
        /// </summary>
        [Test]
        public void DescribeOrNull_ListsEachStaleCallOnceInOrdinalOrder()
        {
            string warning = new HotReloadStaleAddedMemberCallers().DescribeOrNull(
                new[]
                {
                    CreateCall("Ns.Caller.Zed()", RetiredLabel),
                    CreateCall(CallerLabel, RetiredLabel),
                    CreateCall(CallerLabel, RetiredLabel)
                },
                Array.Empty<HotReloadAddedMemberInfo>(),
                new[] { CallerPath });

            Assert.That(
                warning,
                Is.EqualTo(
                    string.Format(
                        HotReloadConstants.StaleAddedMemberCallsWarningFormat,
                        CallerLabel + " calls " + RetiredLabel + ", Ns.Caller.Zed() calls " + RetiredLabel)));
        }

        private static HotReloadAddedMemberCall CreateCall(string callerLabel, string calleeLabel)
        {
            return new HotReloadAddedMemberCall(
                callerLabel,
                CallerPath,
                new HotReloadCalledAddedMember(calleeLabel, HostPath));
        }
    }
}
