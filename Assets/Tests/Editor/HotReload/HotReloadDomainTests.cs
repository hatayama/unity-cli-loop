using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;

using HarmonyLib;
using NUnit.Framework;

using UnityEngine;

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

        // A project assembly name no introduced-type artifact can carry.
        private const string ProjectAssemblyName = "DomainTypeHomeFixtureAssembly";
        private const string ArtifactDllPath = "domain-artifact.dll";

        private const string IntroducedOwnerPath = "Assets/DomainIntroduced.cs";

        // A path other than FileOne, the way an override copy differs from the requested file.
        private const string ShimSourcePath = "/override/DomainFileOne.cs";
        private const string ShimSourceHash = "generation-sha256";

        // The copy a recorded reload read, and the hash of what it read.
        private const string RecordedWorkerSourcePath = "/worker-copy/DomainFileOne.cs";
        private const string RecordedSourceHash = "recorded-sha256";

        // Two recorded paths where the second ends with the first, so a suffix lookup matches both.
        private const string PlainRecordedPath = "Assets/Fixture/SuffixOwner.cs";
        private const string NestedRecordedPath = "Packages/sample/Assets/Fixture/SuffixOwner.cs";

        private HotReloadDomainTestAccess _access;

        private HotReloadDomainTestScope _scope;

        [SetUp]
        public void SetUp()
        {
            _scope = new HotReloadDomainTestScope();
            _access = new HotReloadDomainTestAccess();
        }

        [TearDown]
        public void TearDown()
        {
            _scope.Dispose();
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
            generation.RegisterAddedMethod(AddedMethodKey, GetAddedTarget(), FileOne, "AddedPing", "DomainHost");
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
        /// What: a file whose source still hashes the same as the source its shim generation was
        /// compiled from is not reported as changed on disk.
        /// </summary>
        [Test]
        public void HasShimSourceChangedOnDisk_SameHashAsTheGenerationSource_ReturnsFalse()
        {
            _access.BeginShimGenerationFromSource(FileOne, ShimSourcePath, ShimSourceHash);

            bool changed = _access.Domain.HasShimSourceChangedOnDisk(FileOne, _ => ShimSourceHash);

            Assert.That(changed, Is.False);
        }

        /// <summary>
        /// What: a file whose source now hashes differently from the source its shim generation was
        /// compiled from is reported as changed on disk.
        /// </summary>
        [Test]
        public void HasShimSourceChangedOnDisk_DifferentHash_ReturnsTrue()
        {
            _access.BeginShimGenerationFromSource(FileOne, ShimSourcePath, ShimSourceHash);

            bool changed = _access.Domain.HasShimSourceChangedOnDisk(FileOne, _ => "edited-sha256");

            Assert.That(changed, Is.True);
        }

        /// <summary>
        /// What: the check reads the path the generation's source came from, not the requested
        /// path, so a reload from an edited copy compares against that copy.
        /// </summary>
        [Test]
        public void HasShimSourceChangedOnDisk_ReadsTheShimSourcePathNotTheRequestedPath()
        {
            _access.BeginShimGenerationFromSource(FileOne, ShimSourcePath, ShimSourceHash);
            List<string> readPaths = new List<string>();

            _access.Domain.HasShimSourceChangedOnDisk(
                FileOne,
                path =>
                {
                    readPaths.Add(path);
                    return ShimSourceHash;
                });

            Assert.That(readPaths, Is.EqualTo(new[] { ShimSourcePath }));
        }

        /// <summary>
        /// What: a file with no shim generation is never reported as changed, and its source is
        /// not read.
        /// </summary>
        [Test]
        public void HasShimSourceChangedOnDisk_FileWithoutShimGeneration_ReturnsFalse()
        {
            _access.Domain.BeginAddedMemberOnlyGeneration(FileOne);
            bool read = false;

            bool changed = _access.Domain.HasShimSourceChangedOnDisk(
                FileOne,
                _ =>
                {
                    read = true;
                    return "edited-sha256";
                });

            Assert.That(changed, Is.False);
            Assert.That(read, Is.False);
        }

        /// <summary>
        /// What: a generation source that cannot be read is not reported as changed.
        /// </summary>
        [Test]
        public void HasShimSourceChangedOnDisk_UnreadableSource_ReturnsFalse()
        {
            _access.BeginShimGenerationFromSource(FileOne, ShimSourcePath, ShimSourceHash);

            bool changed = _access.Domain.HasShimSourceChangedOnDisk(FileOne, _ => null);

            Assert.That(changed, Is.False);
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
        /// What: an added field's declaration answers whichever file declared it, and a field no
        /// file declares answers nothing.
        /// </summary>
        [Test]
        public void TryGetAddedFieldDeclaration_FindsTheDeclaringFilesRow()
        {
            HotReloadAddedFieldDeclaration wired = new HotReloadAddedFieldDeclaration(
                HostType + "::wired",
                HostType,
                "wired",
                typeof(string).AssemblyQualifiedName,
                isStatic: false);
            _access.ReplaceAddedFields(FileOne, new[] { HostType + ".alpha" });
            _access.ReplaceAddedFields(FileTwo, new[] { HostType + ".wired" }, new[] { wired });

            Assert.That(
                _access.Domain.TryGetAddedFieldDeclaration(
                    HostType,
                    "wired",
                    out HotReloadAddedFieldDeclaration found),
                Is.True);
            Assert.That(found.StoreFieldKey, Is.EqualTo(HostType + "::wired"));
            Assert.That(
                _access.Domain.TryGetAddedFieldDeclaration(
                    HostType,
                    "alpha",
                    out HotReloadAddedFieldDeclaration _),
                Is.False);
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
                HotReloadCompositionRoot.Services.Patcher.Revert(original, out string _),
                Is.EqualTo(HotReloadRevertOutcome.Reverted));

            Assert.That(_access.Domain.ActivePatchCount, Is.EqualTo(0));
            Assert.That(
                HotReloadPausePointCoordination.HotReloadSide?.GetShimLookupForFile(FileOne),
                Is.Null);
            Assert.That(
                HotReloadPausePointCoordination.HotReloadSide?.GetShimLookupForFile(FileTwo),
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
            _access.Domain.AppliedSources.RecordAppliedSource(FileOne, "hash-one", true, "/worker-copy/Recorded.cs", Array.Empty<HotReloadUnappliedRow>());
            _access.Domain.AppliedSources.RecordAppliedSource(FileTwo, "hash-two", false, "/worker-copy/Recorded.cs", Array.Empty<HotReloadUnappliedRow>());

            (string Hash, bool IsFullyApplied)? recorded = _access.Domain.AppliedSources.TryGetAppliedSource(FileOne);
            Assert.That(recorded, Is.Not.Null);
            Assert.That(recorded.Value.Hash, Is.EqualTo("hash-one"));
            Assert.That(recorded.Value.IsFullyApplied, Is.True);

            _access.Domain.AppliedSources.ClearAppliedSource(FileOne);

            Assert.That(_access.Domain.AppliedSources.TryGetAppliedSource(FileOne), Is.Null);
            Assert.That(_access.Domain.AppliedSources.TryGetAppliedSource(FileTwo), Is.Not.Null);
        }

        /// <summary>
        /// What: the membership evidence of a file absent from the compiled source list is readable
        /// back as recorded, so a later reload can re-check the assembly it belongs to without
        /// capturing the evidence again.
        /// </summary>
        [Test]
        public void NewSourceMembershipEvidence_IsReadableBackForTheFileItWasRecordedFor()
        {
            HotReloadNewSourceMembershipEvidence evidence = CreateMembershipEvidence(FileOne);

            _access.Domain.AppliedSources.RecordNewSourceMembershipEvidence(FileOne, evidence);

            Assert.That(
                _access.Domain.AppliedSources.TryGetNewSourceMembershipEvidence(FileOne),
                Is.SameAs(evidence));
            Assert.That(_access.Domain.AppliedSources.TryGetNewSourceMembershipEvidence(FileTwo), Is.Null);
        }

        /// <summary>
        /// What: clearing a file's applied source drops its membership evidence with it and leaves
        /// another file's evidence in place. The evidence only means anything alongside the applied
        /// record, so a file that is no longer applied must not keep answering for its assembly.
        /// </summary>
        [Test]
        public void ClearAppliedSource_DropsTheMembershipEvidenceOfThatFileOnly()
        {
            _access.Domain.AppliedSources.RecordNewSourceMembershipEvidence(FileOne, CreateMembershipEvidence(FileOne));
            _access.Domain.AppliedSources.RecordNewSourceMembershipEvidence(FileTwo, CreateMembershipEvidence(FileTwo));

            _access.Domain.AppliedSources.ClearAppliedSource(FileOne);

            Assert.That(_access.Domain.AppliedSources.TryGetNewSourceMembershipEvidence(FileOne), Is.Null);
            Assert.That(_access.Domain.AppliedSources.TryGetNewSourceMembershipEvidence(FileTwo), Is.Not.Null);
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
            Assert.That(_access.Domain.AppliedSources.TryGetAppliedSource(FileOne), Is.Null);
            Assert.That(_access.Domain.AppliedSources.TryGetAppliedSource(FileTwo), Is.Null);
            Assert.That(_access.Domain.AppliedSources.TryGetNewSourceMembershipEvidence(FileOne), Is.Null);
            Assert.That(_access.Domain.AppliedSources.TryGetNewSourceMembershipEvidence(FileTwo), Is.Null);
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
            _access.Domain.AppliedSources.RecordAppliedSource(FileOne, "hash", true, "/worker-copy/Recorded.cs", Array.Empty<HotReloadUnappliedRow>());
            _access.Domain.AppliedSources.RecordAppliedSource(FileTwo, "other-hash", false, "/worker-copy/Recorded.cs", Array.Empty<HotReloadUnappliedRow>());
            _access.Domain.AppliedSources.RecordNewSourceMembershipEvidence(FileOne, CreateMembershipEvidence(FileOne));
            _access.Domain.AppliedSources.RecordNewSourceMembershipEvidence(FileTwo, CreateMembershipEvidence(FileTwo));
            _access.RecordSupersededSignature(FileOne, SupersededMethodKey, "Superseded(int)");
        }

        // The evidence a file absent from the compiled source list carries: what it was resolved
        // against, so a later reload can tell the same assembly from a different one.
        private static HotReloadNewSourceMembershipEvidence CreateMembershipEvidence(string projectRelativePath)
        {
            return new HotReloadNewSourceMembershipEvidence(
                projectRelativePath,
                ProjectAssemblyName,
                "Library/ScriptAssemblies/" + ProjectAssemblyName + ".dll",
                "domain-evidence-mvid",
                "Assets/Tests/Editor/HotReload/DomainFixture.asmdef",
                Array.Empty<HotReloadNewSourceMembershipBoundary>());
        }

        /// <summary>
        /// What: an assembly name no active artifact carries resolves to the project's compiled
        /// assembly under Library/ScriptAssemblies.
        /// </summary>
        [Test]
        public void ResolveTypeHome_NoActiveArtifactForTheName_ReturnsTheScriptAssembliesHome()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            HotReloadTypeHome home = _access.Domain.ResolveTypeHome(projectRoot, ProjectAssemblyName);

            Assert.That(home.Kind, Is.EqualTo(HotReloadTypeHomeKind.ScriptAssemblies));
            Assert.That(home.AssemblyName, Is.EqualTo(ProjectAssemblyName));
            Assert.That(
                home.DllPath,
                Is.EqualTo(Path.Combine(
                    projectRoot,
                    HotReloadConstants.ScriptAssembliesRelativeDirectory,
                    ProjectAssemblyName + HotReloadConstants.CompiledAssemblyExtension)));
        }

        /// <summary>
        /// What: an assembly name an active artifact carries resolves to that artifact, so the
        /// dll path is the artifact's image rather than a ScriptAssemblies one.
        /// </summary>
        [Test]
        public void ResolveTypeHome_ActiveArtifactCarriesTheName_ReturnsTheRetainedArtifactHome()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            HotReloadIntroducedTypeArtifact artifact = ActivateArtifact();

            HotReloadTypeHome home = _access.Domain.ResolveTypeHome(
                projectRoot,
                artifact.Assembly.GetName().Name);

            Assert.That(home.Kind, Is.EqualTo(HotReloadTypeHomeKind.RetainedArtifact));
            Assert.That(home.DllPath, Is.EqualTo(artifact.DllPath));
        }

        /// <summary>
        /// What: the pause point side is told a project-relative path declares an introduced type,
        /// so it can explain why that file has no compiled source instead of failing blankly.
        /// </summary>
        [Test]
        public void IsIntroducedTypeSourceFile_WithTheOwnerProjectRelativePath_ReturnsTrue()
        {
            ActivateArtifact();

            Assert.That(
                HotReloadPausePointCoordination.HotReloadSide.IsIntroducedTypeSourceFile(IntroducedOwnerPath),
                Is.True);
        }

        /// <summary>
        /// What: the same file named by its absolute path is recognized too, because the pause
        /// point tool asks with whatever path the caller passed on the command line.
        /// </summary>
        [Test]
        public void IsIntroducedTypeSourceFile_WithTheOwnerAbsolutePath_ReturnsTrue()
        {
            ActivateArtifact();
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."))
                .Replace('\\', '/');

            Assert.That(
                HotReloadPausePointCoordination.HotReloadSide.IsIntroducedTypeSourceFile(
                    projectRoot + "/" + IntroducedOwnerPath),
                Is.True);
        }

        /// <summary>
        /// What: a file no active descriptor owns is not reported as an introduced-type source, so
        /// an ordinary compiled file keeps the normal resolve failure guidance.
        /// </summary>
        [Test]
        public void IsIntroducedTypeSourceFile_WithAnotherFile_ReturnsFalse()
        {
            ActivateArtifact();

            Assert.That(
                HotReloadPausePointCoordination.HotReloadSide.IsIntroducedTypeSourceFile(FileOne),
                Is.False);
        }

        private HotReloadIntroducedTypeArtifact ActivateArtifact()
        {
            HotReloadIntroducedTypeArtifact artifact = new HotReloadIntroducedTypeArtifact(
                CreateIntroducedTypeAssembly(),
                ArtifactDllPath,
                "domain-artifact.pdb",
                new List<HotReloadIntroducedTypeDescriptor>
                {
                    new HotReloadIntroducedTypeDescriptor(
                        "OriginalAssembly",
                        "original-mvid",
                        "Example.Introduced",
                        IntroducedOwnerPath,
                        "domain-fingerprint",
                        "public class Introduced { }")
                });
            _access.Domain.IntroducedTypes.RegisterPrepared(artifact);
            _access.Domain.IntroducedTypes.Activate(artifact);
            return artifact;
        }

        /// <summary>
        /// What: an applied-source record needs the path its reload read; an empty one is refused
        /// before anything is recorded.
        /// </summary>
        [Test]
        public void RecordAppliedSource_EmptySourcePath_ThrowsAndRecordsNothing()
        {
            Assert.Throws<ArgumentException>(
                () => _access.Domain.AppliedSources.RecordAppliedSource(
                    FileOne,
                    RecordedSourceHash,
                    false,
                    string.Empty,
                    Array.Empty<HotReloadUnappliedRow>()));

            Assert.That(_access.Domain.AppliedSources.TryGetAppliedSource(FileOne), Is.Null);
        }

        /// <summary>
        /// What: an applied-source record needs its row list, even an empty one; null is refused
        /// before anything is recorded.
        /// </summary>
        [Test]
        public void RecordAppliedSource_NullRows_ThrowsAndRecordsNothing()
        {
            Assert.Throws<ArgumentNullException>(
                () => _access.Domain.AppliedSources.RecordAppliedSource(
                    FileOne,
                    RecordedSourceHash,
                    false,
                    RecordedWorkerSourcePath,
                    null));

            Assert.That(_access.Domain.AppliedSources.TryGetAppliedSource(FileOne), Is.Null);
        }

        /// <summary>
        /// What: a file no reload recorded has no latest reload.
        /// </summary>
        [Test]
        public void GetLatestReloadOfFile_NoRecord_ReturnsNull()
        {
            HotReloadPausePointPort port = CreatePort(_ => RecordedSourceHash);

            Assert.That(port.GetLatestReloadOfFile(FileOne), Is.Null);
        }

        /// <summary>
        /// What: when the file still hashes as the recorded reload read it, the latest reload says
        /// the file is unchanged and hands back the rows that reload left unapplied.
        /// </summary>
        [Test]
        public void GetLatestReloadOfFile_SameContents_ReportsUnchangedWithItsRows()
        {
            RecordReloadOfFileOne(new HotReloadUnappliedRow(SkippableLabel(), HotReloadUnappliedRowKind.Skipped));
            HotReloadPausePointPort port = CreatePort(_ => RecordedSourceHash);

            HotReloadLatestFileReload latest = port.GetLatestReloadOfFile(FileOne);

            Assert.That(latest, Is.Not.Null);
            Assert.That(latest.FileChangedSince, Is.False);
            Assert.That(latest.UnappliedRows.Count, Is.EqualTo(1));
            Assert.That(latest.UnappliedRows[0].Label, Is.EqualTo(SkippableLabel()));
            Assert.That(latest.UnappliedRows[0].Kind, Is.EqualTo(HotReloadUnappliedRowKind.Skipped));
        }

        /// <summary>
        /// What: when the file now hashes differently from what the recorded reload read, the
        /// latest reload says the file changed since.
        /// </summary>
        [Test]
        public void GetLatestReloadOfFile_ChangedContents_ReportsChanged()
        {
            RecordReloadOfFileOne();
            HotReloadPausePointPort port = CreatePort(_ => "edited-sha256");

            HotReloadLatestFileReload latest = port.GetLatestReloadOfFile(FileOne);

            Assert.That(latest, Is.Not.Null);
            Assert.That(latest.FileChangedSince, Is.True);
        }

        /// <summary>
        /// What: a file whose bytes cannot be read counts as unchanged, the way the shim-source
        /// check treats it.
        /// </summary>
        [Test]
        public void GetLatestReloadOfFile_UnreadableFile_ReportsUnchanged()
        {
            RecordReloadOfFileOne();
            HotReloadPausePointPort port = CreatePort(_ => null);

            HotReloadLatestFileReload latest = port.GetLatestReloadOfFile(FileOne);

            Assert.That(latest, Is.Not.Null);
            Assert.That(latest.FileChangedSince, Is.False);
        }

        /// <summary>
        /// What: the check hashes the path the recorded reload read, not the requested path, so a
        /// reload from an edited copy compares against that copy.
        /// </summary>
        [Test]
        public void GetLatestReloadOfFile_ReadsThePathTheReloadReadNotTheRequestedPath()
        {
            RecordReloadOfFileOne();
            List<string> readPaths = new List<string>();
            HotReloadPausePointPort port = CreatePort(path =>
            {
                readPaths.Add(path);
                return RecordedSourceHash;
            });

            port.GetLatestReloadOfFile(FileOne);

            Assert.That(readPaths, Is.EqualTo(new[] { RecordedWorkerSourcePath }));
        }

        /// <summary>
        /// What: an absolute path or one spelled with backslashes finds the record kept under the
        /// project-relative path.
        /// </summary>
        [Test]
        public void GetLatestReloadOfFile_AbsoluteOrBackslashPath_FindsTheSameRecord()
        {
            RecordReloadOfFileOne(new HotReloadUnappliedRow(SkippableLabel(), HotReloadUnappliedRowKind.Skipped));
            HotReloadPausePointPort port = CreatePort(_ => RecordedSourceHash);
            string absolutePath = "/project-root/" + FileOne;
            string backslashPath = FileOne.Replace('/', '\\');

            HotReloadLatestFileReload byAbsolute = port.GetLatestReloadOfFile(absolutePath);
            HotReloadLatestFileReload byBackslash = port.GetLatestReloadOfFile(backslashPath);

            Assert.That(byAbsolute, Is.Not.Null);
            Assert.That(byAbsolute.UnappliedRows.Count, Is.EqualTo(1));
            Assert.That(byBackslash, Is.Not.Null);
            Assert.That(byBackslash.UnappliedRows.Count, Is.EqualTo(1));
        }

        /// <summary>
        /// What: a path that ends with two recorded paths names neither of them for sure, so no
        /// record answers for it rather than the wrong file's.
        /// </summary>
        [Test]
        public void GetLatestReloadOfFile_PathEndingWithTwoRecordedPaths_ReturnsNull()
        {
            RecordReloadOfNestedAndPlainPaths();
            HotReloadPausePointPort port = CreatePort(_ => RecordedSourceHash);

            HotReloadLatestFileReload latest = port.GetLatestReloadOfFile("/project-root/" + NestedRecordedPath);

            Assert.That(latest, Is.Null);
        }

        /// <summary>
        /// What: a path that names one recorded path exactly gets that record, even though it also
        /// ends with another recorded path.
        /// </summary>
        [Test]
        public void GetLatestReloadOfFile_PathNamingOneRecordExactly_ReturnsThatRecordOverASuffixMatch()
        {
            RecordReloadOfNestedAndPlainPaths();
            HotReloadPausePointPort port = CreatePort(_ => RecordedSourceHash);

            HotReloadLatestFileReload latest = port.GetLatestReloadOfFile(NestedRecordedPath);

            Assert.That(latest, Is.Not.Null);
            Assert.That(latest.UnappliedRows.Count, Is.EqualTo(1));
            Assert.That(latest.UnappliedRows[0].Label, Is.EqualTo(SkippableLabel()));
        }

        /// <summary>
        /// What: an empty path has no latest reload.
        /// </summary>
        [Test]
        public void GetLatestReloadOfFile_EmptyPath_ReturnsNull()
        {
            RecordReloadOfFileOne();
            HotReloadPausePointPort port = CreatePort(_ => RecordedSourceHash);

            Assert.That(port.GetLatestReloadOfFile(string.Empty), Is.Null);
        }

        /// <summary>
        /// What: a method the latest reload skipped gets that reload's row back.
        /// </summary>
        [Test]
        public void FindUnappliedRowForMethod_SkippedMethod_ReturnsItsRow()
        {
            RecordReloadOfFileOne(new HotReloadUnappliedRow(SkippableLabel(), HotReloadUnappliedRowKind.Skipped));
            HotReloadPausePointPort port = CreatePort(_ => RecordedSourceHash);

            HotReloadUnappliedRow row = port.FindUnappliedRowForMethod(FileOne, RowLabelHostMethod(nameof(RowLabelHost.Skippable)));

            Assert.That(row, Is.Not.Null);
            Assert.That(row.Kind, Is.EqualTo(HotReloadUnappliedRowKind.Skipped));
        }

        /// <summary>
        /// What: a method of a nested type finds the row the worker labelled from the Cecil
        /// metadata name, because both labels spell the nesting as reflection does.
        /// </summary>
        [Test]
        public void FindUnappliedRowForMethod_MethodOfANestedType_MatchesTheWorkerLabel()
        {
            string workerLabel = HotReloadMethodKeys.FormatMethodLabelParts(
                new HotReloadMetadataTypeName(typeof(RowLabelHost.Nested).FullName.Replace('+', '/')),
                nameof(RowLabelHost.Nested.Ping),
                new[] { "System.String" },
                0);
            RecordReloadOfFileOne(new HotReloadUnappliedRow(workerLabel, HotReloadUnappliedRowKind.Failed));
            HotReloadPausePointPort port = CreatePort(_ => RecordedSourceHash);
            MethodInfo ping = typeof(RowLabelHost.Nested).GetMethod(nameof(RowLabelHost.Nested.Ping));

            HotReloadUnappliedRow row = port.FindUnappliedRowForMethod(FileOne, ping);

            Assert.That(row, Is.Not.Null);
            Assert.That(row.Kind, Is.EqualTo(HotReloadUnappliedRowKind.Failed));
        }

        /// <summary>
        /// What: a method the latest reload applied gets no row, even while another method of the
        /// same file has one.
        /// </summary>
        [Test]
        public void FindUnappliedRowForMethod_AppliedMethod_ReturnsNull()
        {
            RecordReloadOfFileOne(new HotReloadUnappliedRow(SkippableLabel(), HotReloadUnappliedRowKind.Skipped));
            HotReloadPausePointPort port = CreatePort(_ => RecordedSourceHash);
            Assert.That(
                port.FindUnappliedRowForMethod(FileOne, RowLabelHostMethod(nameof(RowLabelHost.Skippable))),
                Is.Not.Null,
                "Precondition: the skipped method must find its row.");

            HotReloadUnappliedRow row = port.FindUnappliedRowForMethod(FileOne, RowLabelHostMethod(nameof(RowLabelHost.Applied)));

            Assert.That(row, Is.Null);
        }

        /// <summary>
        /// What: once the file changed since the latest reload, that reload's rows no longer
        /// describe the file, so no row comes back.
        /// </summary>
        [Test]
        public void FindUnappliedRowForMethod_FileChangedSince_ReturnsNull()
        {
            RecordReloadOfFileOne(new HotReloadUnappliedRow(SkippableLabel(), HotReloadUnappliedRowKind.Skipped));
            MethodInfo skippable = RowLabelHostMethod(nameof(RowLabelHost.Skippable));
            Assert.That(
                CreatePort(_ => RecordedSourceHash).FindUnappliedRowForMethod(FileOne, skippable),
                Is.Not.Null,
                "Precondition: with the recorded bytes the method must find its row.");

            HotReloadUnappliedRow row = CreatePort(_ => "edited-sha256").FindUnappliedRowForMethod(FileOne, skippable);

            Assert.That(row, Is.Null);
        }

        /// <summary>
        /// What: a null method gets no row.
        /// </summary>
        [Test]
        public void FindUnappliedRowForMethod_NullMethod_ReturnsNull()
        {
            RecordReloadOfFileOne(new HotReloadUnappliedRow(SkippableLabel(), HotReloadUnappliedRowKind.Skipped));
            HotReloadPausePointPort port = CreatePort(_ => RecordedSourceHash);

            Assert.That(port.FindUnappliedRowForMethod(FileOne, null), Is.Null);
        }

        /// <summary>
        /// What: a worker row spells a constructed generic parameter type the way Cecil does, so a
        /// method taking one finds no row; the match is exact and does not convert the spelling.
        /// </summary>
        [Test]
        public void FindUnappliedRowForMethod_MethodWithAConstructedGenericParameter_ReturnsNull()
        {
            string workerLabel = HotReloadMethodKeys.FormatMethodLabelParts(
                new HotReloadMetadataTypeName(typeof(RowLabelHost).FullName.Replace('+', '/')),
                nameof(RowLabelHost.TakeList),
                new[] { "System.Collections.Generic.List`1<System.Int32>" },
                0);
            RecordReloadOfFileOne(
                new HotReloadUnappliedRow(workerLabel, HotReloadUnappliedRowKind.Skipped),
                new HotReloadUnappliedRow(SkippableLabel(), HotReloadUnappliedRowKind.Skipped));
            HotReloadPausePointPort port = CreatePort(_ => RecordedSourceHash);
            Assert.That(
                port.FindUnappliedRowForMethod(FileOne, RowLabelHostMethod(nameof(RowLabelHost.Skippable))),
                Is.Not.Null,
                "Precondition: a method without generic parameters must find its row.");

            HotReloadUnappliedRow row = port.FindUnappliedRowForMethod(FileOne, RowLabelHostMethod(nameof(RowLabelHost.TakeList)));

            Assert.That(row, Is.Null);
        }

        // Why a generated name: an artifact assembly is compiled under a name of its own, so a
        // fixture that reused a project assembly's name would not resolve the way production does.
        private static Assembly CreateIntroducedTypeAssembly()
        {
            AssemblyName assemblyName = new AssemblyName("UloopIntroducedTypes_" + Guid.NewGuid().ToString("N"));
            return AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
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

        private HotReloadPausePointPort CreatePort(Func<string, string> readSourceContentHashOrNull)
        {
            return new HotReloadPausePointPort(_access.Domain, readSourceContentHashOrNull);
        }

        // Records a partially applied reload of FileOne that read RecordedWorkerSourcePath.
        private void RecordReloadOfFileOne(params HotReloadUnappliedRow[] rows)
        {
            _access.Domain.AppliedSources.RecordAppliedSource(
                FileOne,
                RecordedSourceHash,
                rows.Length == 0,
                RecordedWorkerSourcePath,
                rows);
        }

        // Records PlainRecordedPath with no rows and NestedRecordedPath, which ends with it, with
        // one Skipped row, so a lookup that returns a record shows which one it found.
        private void RecordReloadOfNestedAndPlainPaths()
        {
            _access.Domain.AppliedSources.RecordAppliedSource(
                PlainRecordedPath,
                RecordedSourceHash,
                true,
                RecordedWorkerSourcePath,
                Array.Empty<HotReloadUnappliedRow>());
            _access.Domain.AppliedSources.RecordAppliedSource(
                NestedRecordedPath,
                RecordedSourceHash,
                false,
                RecordedWorkerSourcePath,
                new[] { new HotReloadUnappliedRow(SkippableLabel(), HotReloadUnappliedRowKind.Skipped) });
        }

        private static string SkippableLabel()
        {
            return HotReloadMethodKeys.FormatMethodLabel(RowLabelHostMethod(nameof(RowLabelHost.Skippable)));
        }

        private static MethodInfo RowLabelHostMethod(string name)
        {
            return typeof(RowLabelHost).GetMethod(name);
        }

        // Methods the port tests label the way hot reload labels a reloaded method.
        private sealed class RowLabelHost
        {
            public void Skippable(int value)
            {
            }

            public void Applied()
            {
            }

            public void TakeList(List<int> values)
            {
            }

            internal sealed class Nested
            {
                public void Ping(string text)
                {
                }
            }
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
