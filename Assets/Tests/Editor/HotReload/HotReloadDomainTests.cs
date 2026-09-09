using System;
using System.Collections.Generic;
using System.Reflection;

using HarmonyLib;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for the domain that owns every file's hot-reload generation: which
    /// generation a path or a method resolves to, the reads that span files, and the reset a full
    /// revert runs.
    /// </summary>
    public class HotReloadDomainTests
    {
        private const string FileOne = "Assets/Tests/Editor/HotReload/DomainFileOne.cs";
        private const string FileTwo = "Assets/Tests/Editor/HotReload/DomainFileTwo.cs";
        private const string AddedMethodKey = "DomainHost.AddedPing(System.Int32)";
        private const string OtherAddedMethodKey = "DomainHost.AddedPong()";
        private const string SupersededMethodKey = "DomainHost.Superseded()";
        private const string HostType = "Ns.Host";
        private const string NestedCecilType = "Ns.Outer/Inner";
        private const string NestedReflectionType = "Ns.Outer+Inner";

        private HotReloadDomainTestAccess _access;

        [SetUp]
        public void SetUp()
        {
            _access = new HotReloadDomainTestAccess();
            _access.ResetDomain();
        }

        [TearDown]
        public void TearDown()
        {
            HotReloadPatcher.RevertAll();
        }

        /// <summary>
        /// What: starting a file generation opens both the shim and the added-member generation for
        /// that path with nothing recorded in either.
        /// </summary>
        [Test]
        public void BeginGeneration_StartsBothGenerationsEmpty()
        {
            _access.GetOrBeginShimGeneration(FileOne);

            Assert.That(_access.HasShimGeneration(FileOne), Is.True);
            Assert.That(_access.HasAddedMemberGeneration(FileOne), Is.True);
            Assert.That(_access.Domain.ListActiveAddedMethodKeys(FileOne), Is.Empty);
        }

        /// <summary>
        /// What: an added-member-only start drops that file's added members while the shim
        /// generation and its registered methods stay in place.
        /// </summary>
        [Test]
        public void BeginAddedMemberOnlyGeneration_KeepsTheShimGeneration()
        {
            HotReloadFileGeneration generation = _access.GetOrBeginShimGeneration(FileOne);
            generation.RegisterShimMethod(
                GetAddedTarget(),
                new HotReloadShimMethodEntry(GetAddedTarget(), false, 1, 2));
            generation.RegisterAddedMethod(AddedMethodKey, GetAddedTarget(), FileOne);
            Assert.That(
                _access.Domain.ListActiveAddedMethodKeys(FileOne),
                Does.Contain(AddedMethodKey),
                "The added member must be recorded before the start under test drops it.");

            _access.Domain.BeginAddedMemberOnlyGeneration(FileOne);

            Assert.That(_access.HasShimGeneration(FileOne), Is.True);
            Assert.That(
                generation.FindShim(GetAddedTarget()),
                Is.Not.Null,
                "The registered shim method must survive an added-member-only start.");
            Assert.That(_access.Domain.ListActiveAddedMethodKeys(FileOne), Is.Empty);
        }

        /// <summary>
        /// What: restarting one file's added-member generation drops that file's members only, so a
        /// sibling file edited in the same run keeps the members it registered.
        /// </summary>
        [Test]
        public void BeginAddedMemberOnlyGeneration_DropsThatFilesMembersOnly()
        {
            _access.RegisterAddedMember(FileOne, AddedMethodKey, GetAddedTarget(), FileOne);
            _access.RegisterAddedMember(FileTwo, OtherAddedMethodKey, GetAddedTarget(), FileTwo);

            _access.Domain.BeginAddedMemberOnlyGeneration(FileOne);

            Assert.That(_access.Domain.ListActiveAddedMethodKeys(FileOne), Is.Empty);
            Assert.That(
                _access.Domain.ListActiveAddedMethodKeys(FileTwo),
                Is.EqualTo(new[] { OtherAddedMethodKey }));
            Assert.That(
                _access.Domain.ListPathsWithActiveAddedMembers(),
                Is.EqualTo(new[] { FileTwo }));
        }

        /// <summary>
        /// What: an added member is reported for the path it was registered under only, so the same
        /// method key on another file does not answer for it.
        /// </summary>
        [Test]
        public void AddedMemberReads_AnswerForTheRegisteredPathOnly()
        {
            _access.RegisterAddedMember(FileOne, AddedMethodKey, GetAddedTarget(), FileOne);

            Assert.That(_access.Domain.IsActiveMember(FileOne, AddedMethodKey), Is.True);
            Assert.That(_access.Domain.IsActiveMember(FileTwo, AddedMethodKey), Is.False);
            Assert.That(
                _access.Domain.ListActiveAddedMethodKeys(FileOne),
                Is.EqualTo(new[] { AddedMethodKey }));
            Assert.That(_access.Domain.ListActiveAddedMethodKeys(FileTwo), Is.Empty);
        }

        /// <summary>
        /// What: added members from two files are described together, sorted by method key, each
        /// row carrying the file it came from.
        /// </summary>
        [Test]
        public void DescribeAddedMembers_ListsEveryFileSortedByMethodKey()
        {
            _access.RegisterAddedMember(FileTwo, OtherAddedMethodKey, GetAddedTarget(), FileTwo);
            _access.RegisterAddedMember(FileOne, AddedMethodKey, GetAddedTarget(), FileOne);

            IReadOnlyList<HotReloadAddedMemberInfo> described = _access.Domain.DescribeAddedMembers();

            Assert.That(described.Count, Is.EqualTo(2));
            Assert.That(described[0].MethodKey, Is.EqualTo(AddedMethodKey));
            Assert.That(described[0].FilePath, Is.EqualTo(FileOne));
            Assert.That(described[1].MethodKey, Is.EqualTo(OtherAddedMethodKey));
            Assert.That(described[1].FilePath, Is.EqualTo(FileTwo));
            Assert.That(
                _access.Domain.ListPathsWithActiveAddedMembers(),
                Is.EquivalentTo(new[] { FileOne, FileTwo }));
        }

        /// <summary>
        /// What: a type's added fields are unioned across every file and sorted ordinal, so a
        /// pause-point warning names them the same way whichever file added them.
        /// </summary>
        [Test]
        public void GetAddedFieldsForType_AggregatesAcrossFilesSortedOrdinal()
        {
            _access.ReplaceAddedFields(FileOne, new[] { HostType + ".zeta" });
            _access.ReplaceAddedFields(FileTwo, new[] { HostType + ".alpha" });

            Assert.That(
                _access.Domain.GetAddedFieldsForType(HostType),
                Is.EqualTo(new[] { "alpha", "zeta" }));
        }

        /// <summary>
        /// What: a Cecil nested-type query (Outer/Inner) finds fields stored under the reflection
        /// key (Outer+Inner), and so does the reflection spelling.
        /// </summary>
        [Test]
        public void GetAddedFieldsForType_CecilNestedType_HitsTheReflectionStoredKey()
        {
            _access.ReplaceAddedFields(FileOne, new[] { NestedReflectionType + ".count" });

            Assert.That(
                _access.Domain.GetAddedFieldsForType(NestedCecilType),
                Is.EqualTo(new[] { "count" }));
            Assert.That(
                _access.Domain.GetAddedFieldsForType(NestedReflectionType),
                Is.EqualTo(new[] { "count" }));
        }

        /// <summary>
        /// What: replacing one file's added fields with an empty list drops that file's rows and
        /// leaves the other file's fields answering.
        /// </summary>
        [Test]
        public void ReplaceAddedFields_EmptyList_DropsOnlyThatFile()
        {
            _access.ReplaceAddedFields(FileOne, new[] { HostType + ".alpha" });
            _access.ReplaceAddedFields(FileTwo, new[] { HostType + ".beta" });

            _access.ReplaceAddedFields(FileOne, Array.Empty<string>());

            Assert.That(
                _access.Domain.GetAddedFieldsForType(HostType),
                Is.EqualTo(new[] { "beta" }));
        }

        /// <summary>
        /// What: added-field rows are listed in path, then type, then field order, and a file
        /// replaced with an empty list stops contributing rows.
        /// </summary>
        [Test]
        public void DescribeAddedFields_SortsByPathThenTypeThenField()
        {
            _access.ReplaceAddedFields(
                FileTwo,
                new[] { HostType + ".zeta", NestedReflectionType + ".label" });
            _access.ReplaceAddedFields(FileOne, new[] { HostType + ".beta", HostType + ".alpha" });

            IReadOnlyList<HotReloadAddedFieldDescription> first = _access.Domain.DescribeAddedFields();

            Assert.That(first.Count, Is.EqualTo(4));
            AssertRow(first[0], FileOne, HostType, "alpha");
            AssertRow(first[1], FileOne, HostType, "beta");
            AssertRow(first[2], FileTwo, HostType, "zeta");
            AssertRow(first[3], FileTwo, NestedReflectionType, "label");

            _access.ReplaceAddedFields(FileOne, Array.Empty<string>());

            IReadOnlyList<HotReloadAddedFieldDescription> afterEmpty =
                _access.Domain.DescribeAddedFields();
            Assert.That(afterEmpty.Count, Is.EqualTo(2));
            AssertRow(afterEmpty[0], FileTwo, HostType, "zeta");
            AssertRow(afterEmpty[1], FileTwo, NestedReflectionType, "label");
        }

        /// <summary>
        /// What: a superseded signature recorded on one file is retrievable by its old method key
        /// from the domain, and an unrecorded key is not.
        /// </summary>
        [Test]
        public void TryGetSupersededReplacement_FindsTheRecordingGeneration()
        {
            _access.RecordSupersededSignature(FileOne, SupersededMethodKey, "New.Key(System.String)");

            Assert.That(
                _access.Domain.TryGetSupersededReplacement(
                    SupersededMethodKey,
                    out string replacement),
                Is.True);
            Assert.That(replacement, Is.EqualTo("New.Key(System.String)"));
            Assert.That(
                _access.Domain.TryGetSupersededReplacement("Missing.Key()", out string missing),
                Is.False);
            Assert.That(missing, Is.Null);
        }

        /// <summary>
        /// What: an exact path lookup answers only for the recorded spelling, while the requested
        /// path lookup also resolves a path that refers to the same file.
        /// </summary>
        [Test]
        public void FindGeneration_IsExactWhileRequestedPathLookupIsFuzzy()
        {
            _access.GetOrBeginShimGeneration(FileOne);

            const string absoluteSpelling = "/project-root/" + FileOne;
            Assert.That(_access.Domain.FindGeneration(FileOne), Is.Not.Null);
            Assert.That(_access.Domain.FindGeneration(absoluteSpelling), Is.Null);
            Assert.That(
                _access.Domain.FindGenerationForRequestedPath(absoluteSpelling),
                Is.Not.Null,
                "An absolute --file-style path must resolve through the suffix scan.");
        }

        /// <summary>
        /// What: re-applying the same method from a different file path retires the patch its
        /// first path owns, so the method ends up patched once, owned by the newer path, and one
        /// revert takes it out entirely.
        /// </summary>
        [Test]
        public void ApplyFromASecondPath_RetiresThePatchTheFirstPathOwns()
        {
            MethodInfo original = AccessTools.Method(
                typeof(HotReloadCoreFixture),
                nameof(HotReloadCoreFixture.ReplaceableCompute));
            MethodInfo shim = AccessTools.Method(
                typeof(HotReloadHandwrittenShims),
                nameof(HotReloadHandwrittenShims.ReplaceableCompute__shim0));

            Assert.That(
                _access.ApplyPatch(original, shim, HotReloadPatchShape.Transplant, FileOne).Success,
                Is.True);
            Assert.That(
                _access.ApplyPatch(original, shim, HotReloadPatchShape.Transplant, FileTwo).Success,
                Is.True);

            Assert.That(
                _access.Domain.ActivePatchCount,
                Is.EqualTo(1),
                "The method must hold one live patch, not one per path it was applied from.");
            Assert.That(
                _access.Domain.FindGenerationForMethod(original).Path,
                Is.EqualTo(FileTwo));

            Assert.That(
                HotReloadPatcher.Revert(original, out string _),
                Is.EqualTo(HotReloadRevertOutcome.Reverted));

            Assert.That(_access.Domain.ActivePatchCount, Is.EqualTo(0));
            Assert.That(
                HotReloadPausePointCoordination.GetShimLookupForFile?.Invoke(FileOne),
                Is.Null);
            Assert.That(
                HotReloadPausePointCoordination.GetShimLookupForFile?.Invoke(FileTwo),
                Is.Null);
        }

        /// <summary>
        /// What: an added member with no patched method still counts, both as a patch-and-added
        /// member and in the total a Domain Reload would discard.
        /// </summary>
        [Test]
        public void CountActiveChanges_WithAddedMemberOnly_CountsItInBothTotals()
        {
            _access.RegisterAddedMember(FileOne, AddedMethodKey, GetAddedTarget(), FileOne);

            HotReloadActiveChangeSnapshot snapshot = _access.Domain.CountActiveChanges();

            Assert.That(snapshot.PatchAndAddedMemberCount, Is.EqualTo(1));
            Assert.That(
                snapshot.RuntimeChangeTotal,
                Is.EqualTo(1 + snapshot.IntroducedTypeCount));
        }

        /// <summary>
        /// What: the snapshot reports the same numbers the separate accessors do, so removing an
        /// accessor or changing one of the sums is caught here.
        /// </summary>
        [Test]
        public void CountActiveChanges_ReadsTheSameNumbersAsTheAccessors()
        {
            HotReloadActiveChangeSnapshot snapshot = _access.Domain.CountActiveChanges();

            Assert.That(
                snapshot.PatchAndAddedMemberCount,
                Is.EqualTo(_access.Domain.ActiveChangeCount));
            Assert.That(snapshot.IntroducedTypeCount, Is.EqualTo(_access.Domain.IntroducedTypeCount));
            Assert.That(
                snapshot.RuntimeChangeTotal,
                Is.EqualTo(_access.Domain.ActiveChangeCount + _access.Domain.IntroducedTypeCount));
        }

        /// <summary>
        /// What: the applied-source record is readable back per file and cleared per file, which is
        /// how an unchanged-source decision is made for one file without touching its siblings.
        /// </summary>
        [Test]
        public void AppliedSource_IsRecordedAndClearedPerFile()
        {
            _access.Domain.RecordAppliedSource(FileOne, "hash-one", true);
            _access.Domain.RecordAppliedSource(FileTwo, "hash-two", false);

            (string Hash, bool IsFullyApplied)? recorded = _access.Domain.TryGetAppliedSource(FileOne);
            Assert.That(recorded, Is.Not.Null);
            Assert.That(recorded.Value.Hash, Is.EqualTo("hash-one"));
            Assert.That(recorded.Value.IsFullyApplied, Is.True);

            _access.Domain.ClearAppliedSource(FileOne);

            Assert.That(_access.Domain.TryGetAppliedSource(FileOne), Is.Null);
            Assert.That(_access.Domain.TryGetAppliedSource(FileTwo), Is.Not.Null);
        }

        /// <summary>
        /// What: a full revert empties every domain-scoped store, each checked on its own line so a
        /// single missed store cannot hide behind the others.
        /// </summary>
        [Test]
        public void RevertAll_EmptiesEveryDomainScopedStore()
        {
            string staticFieldKey = HotReloadAddedFieldStore.FormatFieldKey("DomainHost", "seed");
            FillEveryStore(staticFieldKey);

            _access.Domain.RevertAll();

            Assert.That(_access.HasAddedMemberGeneration(FileOne), Is.False);
            Assert.That(_access.HasAddedMemberGeneration(FileTwo), Is.False);
            Assert.That(_access.HasShimGeneration(FileOne), Is.False);
            Assert.That(_access.Domain.ListGenerations(), Is.Empty);
            Assert.That(
                HotReloadAddedFieldStore.GetOrInitStatic(staticFieldKey, () => 20),
                Is.EqualTo(20));
            Assert.That(_access.Domain.DescribeAddedFields(), Is.Empty);
            Assert.That(_access.Domain.DescribeAddedMembers(), Is.Empty);
            Assert.That(HotReloadInvocationRegistry.GetCount(AddedMethodKey), Is.EqualTo(0));
            Assert.That(_access.Domain.TryGetAppliedSource(FileOne), Is.Null);
            Assert.That(_access.Domain.TryGetAppliedSource(FileTwo), Is.Null);
            Assert.That(
                _access.Domain.TryGetSupersededReplacement(SupersededMethodKey, out string _),
                Is.False);
        }

        private void FillEveryStore(string staticFieldKey)
        {
            _access.GetOrBeginShimGeneration(FileOne).RegisterShimMethod(
                GetAddedTarget(),
                new HotReloadShimMethodEntry(GetAddedTarget(), false, 1, 2));
            _access.RegisterAddedMember(FileOne, AddedMethodKey, GetAddedTarget(), FileOne);
            _access.RegisterAddedMember(FileTwo, OtherAddedMethodKey, GetAddedTarget(), FileTwo);
            HotReloadAddedFieldStore.SetStatic(staticFieldKey, 2);
            _access.ReplaceAddedFields(FileOne, new[] { "DomainHost.count" });
            _access.ReplaceAddedFields(FileTwo, new[] { "DomainHost.label" });
            HotReloadInvocationRegistry.Increment(AddedMethodKey);
            _access.Domain.RecordAppliedSource(FileOne, "hash", true);
            _access.Domain.RecordAppliedSource(FileTwo, "other-hash", false);
            _access.RecordSupersededSignature(FileOne, SupersededMethodKey, "Superseded(int)");
        }

        private static void AssertRow(
            HotReloadAddedFieldDescription row,
            string expectedPath,
            string expectedType,
            string expectedField)
        {
            Assert.That(row.ProjectRelativePath, Is.EqualTo(expectedPath));
            Assert.That(row.TypeName, Is.EqualTo(expectedType));
            Assert.That(row.FieldName, Is.EqualTo(expectedField));
        }

        private static MethodInfo GetAddedTarget()
        {
            return typeof(HotReloadDomainTests).GetMethod(
                nameof(AddedTarget),
                BindingFlags.Static | BindingFlags.NonPublic);
        }

        private static int AddedTarget()
        {
            return 1;
        }
    }
}
