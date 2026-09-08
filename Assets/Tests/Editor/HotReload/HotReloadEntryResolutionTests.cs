using System.Collections.Generic;
using System.Reflection;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for <see cref="HotReloadEntryResolution.ResolveEntries"/> preflight:
    /// which entries resolve, and what a file reports when one of them does not. The test
    /// assembly stands in for the compiled shim assembly, so no worker is started.
    /// </summary>
    public class HotReloadEntryResolutionTests
    {
        private const string TestAssemblyName = "UnityCLILoop.Tests.Editor.HotReload";
        private const string ShimTypeName = "HotReloadHandwrittenShims";
        private const string FixtureTypeMetadataName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadCoreFixture";
        private const string FilePath = "Assets/Tests/Editor/HotReload/HotReloadCoreFixtures.cs";

        private static Assembly ShimAssembly => typeof(HotReloadHandwrittenShims).Assembly;

        /// <summary>
        /// What: every entry of a file resolving leaves the result all-resolved, with one resolved
        /// entry per input row and no failure outcomes.
        /// </summary>
        [Test]
        public void ResolveEntries_WhenEveryEntryResolves_ReportsAllResolved()
        {
            TransformWorkerEntryDto[] entries =
            {
                BuildExistingMethodEntry(
                    nameof(HotReloadCoreFixture.StaticPing),
                    new string[0],
                    "StaticPing__shim0"),
                BuildExistingMethodEntry(
                    nameof(HotReloadCoreFixture.ReplaceableCompute),
                    new[] { "System.Int32" },
                    "ReplaceableCompute__shim0")
            };

            HotReloadEntryResolution.Result result = HotReloadEntryResolution.ResolveEntries(
                TestAssemblyName,
                FilePath,
                ShimAssembly,
                entries,
                new Dictionary<string, string>());

            Assert.That(result.AllResolved, Is.True);
            Assert.That(result.ResolvedEntries, Has.Count.EqualTo(2));
            Assert.That(result.FailureOutcomes, Is.Empty);
        }

        /// <summary>
        /// What: an entry naming a shim method the shim assembly does not declare fails the whole
        /// file — the result is not all-resolved, the failing row is reported Failed, and every
        /// other row of the file is reported Skipped with the atomic-file reason.
        /// </summary>
        [Test]
        public void ResolveEntries_WhenShimMethodIsMissing_FailsTheFileAtomically()
        {
            TransformWorkerEntryDto[] entries =
            {
                BuildExistingMethodEntry(
                    nameof(HotReloadCoreFixture.StaticPing),
                    new string[0],
                    "StaticPing__shim0"),
                BuildExistingMethodEntry(
                    nameof(HotReloadCoreFixture.ReplaceableCompute),
                    new[] { "System.Int32" },
                    "ReplaceableCompute__shimAbsent")
            };

            HotReloadEntryResolution.Result result = HotReloadEntryResolution.ResolveEntries(
                TestAssemblyName,
                FilePath,
                ShimAssembly,
                entries,
                new Dictionary<string, string>());

            Assert.That(result.AllResolved, Is.False);
            Assert.That(result.ResolvedEntries, Is.Empty);
            Assert.That(result.FailureOutcomes, Has.Count.EqualTo(2));
            Assert.That(
                result.FailureOutcomes[0].Kind,
                Is.EqualTo(HotReloadMethodOutcomeKind.Skipped));
            Assert.That(
                result.FailureOutcomes[0].Reason,
                Is.EqualTo(HotReloadConstants.AtomicFileSkipReason));
            Assert.That(
                result.FailureOutcomes[1].Kind,
                Is.EqualTo(HotReloadMethodOutcomeKind.Failed));
            Assert.That(
                result.FailureOutcomes[1].Reason,
                Does.Contain("ReplaceableCompute__shimAbsent"));
        }

        /// <summary>
        /// What: an added-method entry resolves through the shim lookup alone — it needs no
        /// compiled original method, and the resolved entry is marked as an added method.
        /// </summary>
        [Test]
        public void ResolveEntries_WhenEntryIsAnAddedMethod_ResolvesWithoutAnOriginalMethod()
        {
            TransformWorkerEntryDto[] entries =
            {
                new TransformWorkerEntryDto
                {
                    sourceProjectRelativePath = FilePath,
                    typeMetadataName = FixtureTypeMetadataName,
                    methodName = "AddedByThisReload",
                    parameterTypeFullNames = new string[0],
                    shimTypeName = ShimTypeName,
                    shimMethodName = "StaticPing__shim0",
                    patchKind = HotReloadConstants.PatchKindAddedMethod
                }
            };

            HotReloadEntryResolution.Result result = HotReloadEntryResolution.ResolveEntries(
                TestAssemblyName,
                FilePath,
                ShimAssembly,
                entries,
                new Dictionary<string, string>());

            Assert.That(result.AllResolved, Is.True);
            Assert.That(result.ResolvedEntries, Has.Count.EqualTo(1));
            Assert.That(result.ResolvedEntries[0].IsAddedMethod, Is.True);
            Assert.That(result.ResolvedEntries[0].OriginalMethod, Is.Null);
            Assert.That(result.ResolvedEntries[0].ShimMethod, Is.Not.Null);
        }

        private static TransformWorkerEntryDto BuildExistingMethodEntry(
            string methodName,
            string[] parameterTypeFullNames,
            string shimMethodName)
        {
            return new TransformWorkerEntryDto
            {
                sourceProjectRelativePath = FilePath,
                typeMetadataName = FixtureTypeMetadataName,
                methodName = methodName,
                parameterTypeFullNames = parameterTypeFullNames,
                shimTypeName = ShimTypeName,
                shimMethodName = shimMethodName
            };
        }
    }
}
