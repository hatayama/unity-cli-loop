using System.Collections.Generic;
using System.IO;
using System.Reflection;

using NUnit.Framework;

using UnityEngine;

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

        private HotReloadDomainTestScope _scope;

        [SetUp]
        public void SetUp()
        {
            _scope = new HotReloadDomainTestScope();
        }

        [TearDown]
        public void TearDown()
        {
            _scope.Dispose();
        }

        // The test assembly stands in for the patch target, so its ScriptAssemblies image is the
        // home the preflight resolves existing methods against.
        private static HotReloadTypeHome TestAssemblyHome
        {
            get
            {
                string dllPath = Path.Combine(
                    ResolveProjectRoot(), "Library/ScriptAssemblies", TestAssemblyName + ".dll");
                return HotReloadTypeHome.ScriptAssemblies(TestAssemblyName, dllPath);
            }
        }

        // The rows these tests build name no home assembly, so every one of them resolves to the
        // file's own home and the resolver never has to look an artifact up in the domain.
        private static HotReloadEntryHomeResolver FileHomeResolver =>
            new HotReloadEntryHomeResolver(
                new HotReloadDomainTestAccess().Domain,
                ResolveProjectRoot());

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
                TestAssemblyHome,
                FileHomeResolver,
                FilePath,
                ShimAssembly,
                entries,
                new Dictionary<string, string>(),
                new HotReloadAddedCalleeIndex(entries));

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
                TestAssemblyHome,
                FileHomeResolver,
                FilePath,
                ShimAssembly,
                entries,
                new Dictionary<string, string>(),
                new HotReloadAddedCalleeIndex(entries));

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
        /// What: an added-method entry naming a shim with no usable invocation counter beside it
        /// (none, or one that is not a long) fails the whole file like a missing shim does, and
        /// the Failed row names the counter the Editor looked for.
        /// </summary>
        [TestCase(nameof(HotReloadHandwrittenShims.AddedWithoutCounter__shim0))]
        [TestCase(nameof(HotReloadHandwrittenShims.AddedWithIntCounter__shim0))]
        public void ResolveEntries_WhenAddedMethodHasNoUsableCounter_FailsTheFileAtomically(string shimMethodName)
        {
            TransformWorkerEntryDto[] entries =
            {
                BuildExistingMethodEntry(
                    nameof(HotReloadCoreFixture.StaticPing),
                    new string[0],
                    "StaticPing__shim0"),
                BuildAddedMethodEntry(shimMethodName)
            };

            HotReloadEntryResolution.Result result = HotReloadEntryResolution.ResolveEntries(
                TestAssemblyHome,
                FileHomeResolver,
                FilePath,
                ShimAssembly,
                entries,
                new Dictionary<string, string>(),
                new HotReloadAddedCalleeIndex(entries));

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
                Does.Contain(shimMethodName + "__uloopCalls"));
        }

        /// <summary>
        /// What: an added-method entry resolves through the shim lookup alone — it needs no
        /// compiled original method, the resolved entry is marked as an added method, and it
        /// carries the invocation counter declared beside its shim.
        /// </summary>
        [Test]
        public void ResolveEntries_WhenEntryIsAnAddedMethod_ResolvesWithoutAnOriginalMethod()
        {
            TransformWorkerEntryDto[] entries =
            {
                BuildAddedMethodEntry(nameof(HotReloadHandwrittenShims.StaticPing__shim0))
            };

            HotReloadEntryResolution.Result result = HotReloadEntryResolution.ResolveEntries(
                TestAssemblyHome,
                FileHomeResolver,
                FilePath,
                ShimAssembly,
                entries,
                new Dictionary<string, string>(),
                new HotReloadAddedCalleeIndex(entries));

            Assert.That(result.AllResolved, Is.True);
            Assert.That(result.ResolvedEntries, Has.Count.EqualTo(1));
            Assert.That(result.ResolvedEntries[0].IsAddedMethod, Is.True);
            Assert.That(result.ResolvedEntries[0].OriginalMethod, Is.Null);
            Assert.That(result.ResolvedEntries[0].ShimMethod, Is.Not.Null);
            Assert.That(
                result.ResolvedEntries[0].InvocationCounter,
                Is.EqualTo(typeof(HotReloadHandwrittenShims).GetField(
                    nameof(HotReloadHandwrittenShims.StaticPing__shim0__uloopCalls))));
        }

        /// <summary>
        /// What: each resolved entry carries the added members its body calls, as the ledger
        /// labels them, whether the entry patches an existing method or is an added method itself;
        /// an entry that calls none carries an empty list.
        /// </summary>
        [Test]
        public void ResolveEntries_RecordsTheAddedMembersEachEntryCalls()
        {
            TransformWorkerEntryDto added = BuildAddedMethodEntry(nameof(HotReloadHandwrittenShims.StaticPing__shim0));
            TransformWorkerEntryDto caller = BuildExistingMethodEntry(
                nameof(HotReloadCoreFixture.StaticPing),
                new string[0],
                "StaticPing__shim0");
            caller.calledAddedMethodKeys = new[] { FixtureTypeMetadataName + "::AddedByThisReload()" };
            TransformWorkerEntryDto[] entries = { added, caller };

            HotReloadEntryResolution.Result result = HotReloadEntryResolution.ResolveEntries(
                TestAssemblyHome,
                FileHomeResolver,
                FilePath,
                ShimAssembly,
                entries,
                new Dictionary<string, string>(),
                new HotReloadAddedCalleeIndex(entries));

            Assert.That(result.AllResolved, Is.True);
            Assert.That(result.ResolvedEntries[0].CalledAddedMembers, Is.Empty);
            Assert.That(result.ResolvedEntries[1].CalledAddedMembers.Count, Is.EqualTo(1));
            Assert.That(
                result.ResolvedEntries[1].CalledAddedMembers[0].Label,
                Is.EqualTo(FixtureTypeMetadataName + ".AddedByThisReload()"));
            Assert.That(result.ResolvedEntries[1].CalledAddedMembers[0].DeclaringFilePath, Is.EqualTo(FilePath));
        }

        /// <summary>
        /// What: an entry calling an added member the group declares no entry for fails the whole
        /// file like a missing shim does, and the Failed row names the call's key.
        /// </summary>
        [Test]
        public void ResolveEntries_WhenACallNamesNoAddedEntry_FailsTheFileAtomically()
        {
            string missingKey = FixtureTypeMetadataName + "::RetiredByThisReload()";
            TransformWorkerEntryDto caller = BuildExistingMethodEntry(
                nameof(HotReloadCoreFixture.ReplaceableCompute),
                new[] { "System.Int32" },
                "ReplaceableCompute__shim0");
            caller.calledAddedMethodKeys = new[] { missingKey };
            TransformWorkerEntryDto[] entries =
            {
                BuildExistingMethodEntry(
                    nameof(HotReloadCoreFixture.StaticPing),
                    new string[0],
                    "StaticPing__shim0"),
                caller
            };

            HotReloadEntryResolution.Result result = HotReloadEntryResolution.ResolveEntries(
                TestAssemblyHome,
                FileHomeResolver,
                FilePath,
                ShimAssembly,
                entries,
                new Dictionary<string, string>(),
                new HotReloadAddedCalleeIndex(entries));

            Assert.That(result.AllResolved, Is.False);
            Assert.That(result.ResolvedEntries, Is.Empty);
            Assert.That(result.FailureOutcomes, Has.Count.EqualTo(2));
            Assert.That(
                result.FailureOutcomes[0].Reason,
                Is.EqualTo(HotReloadConstants.AtomicFileSkipReason));
            Assert.That(
                result.FailureOutcomes[1].Kind,
                Is.EqualTo(HotReloadMethodOutcomeKind.Failed));
            Assert.That(result.FailureOutcomes[1].Reason, Does.Contain(missingKey));
        }

        private static string ResolveProjectRoot()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
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

        private static TransformWorkerEntryDto BuildAddedMethodEntry(string shimMethodName)
        {
            return new TransformWorkerEntryDto
            {
                sourceProjectRelativePath = FilePath,
                typeMetadataName = FixtureTypeMetadataName,
                methodName = "AddedByThisReload",
                parameterTypeFullNames = new string[0],
                shimTypeName = ShimTypeName,
                shimMethodName = shimMethodName,
                patchKind = HotReloadConstants.PatchKindAddedMethod
            };
        }
    }
}
