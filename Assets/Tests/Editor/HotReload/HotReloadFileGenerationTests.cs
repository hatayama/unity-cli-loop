using System;
using System.Collections.Generic;
using System.Reflection;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Pure coverage for one file's hot-reload generation: what a re-apply keeps, what it drops,
    /// and how a patch moves from pending to live and back.
    /// </summary>
    public class HotReloadFileGenerationTests
    {
        private const string FixtureProjectRelativePath =
            "Assets/Tests/Editor/HotReload/FileGenerationFixture.cs";
        private const string AddedMethodKey = "FileGenerationFixture.AddedMember()";
        private const string OtherAddedMethodKey = "FileGenerationFixture.OtherAddedMember()";
        private const string HostType = "Ns.Host";
        private const string NestedCecilType = "Ns.Outer/Inner";
        private const string NestedReflectionType = "Ns.Outer+Inner";

        // Any non-empty byte array satisfies a shim generation no test loads bytes from.
        private static readonly byte[] PlaceholderAssemblyBytes = { 0x4D, 0x5A };

        /// <summary>
        /// What: a fresh generation reports neither a shim nor an added-member generation, so a
        /// preflight skip cannot be mistaken for an applied-then-empty generation.
        /// </summary>
        [Test]
        public void NewGeneration_ReportsNeitherGenerationStarted()
        {
            HotReloadFileGeneration generation = CreateGeneration();

            Assert.That(generation.HasShimGeneration, Is.False);
            Assert.That(generation.HasAddedMemberGeneration, Is.False);
            Assert.That(generation.Path, Is.EqualTo(FixtureProjectRelativePath));
        }

        /// <summary>
        /// What: the generation normalizes its identity path to forward slashes, which is what the
        /// added-field rows report.
        /// </summary>
        [Test]
        public void NormalizedPath_ConvertsBackslashesToForwardSlashes()
        {
            HotReloadFileGeneration generation =
                new HotReloadFileGeneration("Assets\\Tests\\Editor\\HotReload\\Windows.cs");

            Assert.That(
                generation.NormalizedPath,
                Is.EqualTo("Assets/Tests/Editor/HotReload/Windows.cs"));
        }

        /// <summary>
        /// What: starting a shim generation opens it with no registered methods, and starting an
        /// added-member generation opens it with no added members.
        /// </summary>
        [Test]
        public void BeginGenerations_StartEmpty()
        {
            HotReloadFileGeneration generation = CreateGeneration();
            BeginShimGeneration(generation);
            generation.BeginAddedMemberGeneration();

            Assert.That(generation.HasShimGeneration, Is.True);
            Assert.That(generation.HasAddedMemberGeneration, Is.True);
            Assert.That(generation.FindShim(GetShimTarget()), Is.Null);
            Assert.That(generation.ListActiveAddedMethodKeys(), Is.Empty);
        }

        /// <summary>
        /// What: registering the same added-method key twice overwrites the shim rather than
        /// stacking a second row, and the description carries the key, path and last shim.
        /// </summary>
        [Test]
        public void RegisterAddedMethod_OverwritesSameKey_AndIsDescribed()
        {
            HotReloadFileGeneration generation = CreateGeneration();
            generation.BeginAddedMemberGeneration();
            generation.RegisterAddedMethod(AddedMethodKey, GetShimTarget(), FixtureProjectRelativePath);
            generation.RegisterAddedMethod(AddedMethodKey, GetAddedTarget(), FixtureProjectRelativePath);

            List<HotReloadAddedMemberInfo> members = new List<HotReloadAddedMemberInfo>();
            generation.DescribeAddedMembers(members);

            Assert.That(generation.AddedMemberCount, Is.EqualTo(1));
            Assert.That(members.Count, Is.EqualTo(1));
            Assert.That(members[0].MethodKey, Is.EqualTo(AddedMethodKey));
            Assert.That(members[0].FilePath, Is.EqualTo(FixtureProjectRelativePath));
            Assert.That(members[0].ShimMethod, Is.EqualTo(GetAddedTarget()));
        }

        /// <summary>
        /// What: IsActiveMember and ListActiveAddedMethodKeys report only the keys this generation
        /// registered.
        /// </summary>
        [Test]
        public void AddedMemberQueries_ReportOnlyRegisteredKeys()
        {
            HotReloadFileGeneration generation = CreateGeneration();
            generation.BeginAddedMemberGeneration();
            generation.RegisterAddedMethod(AddedMethodKey, GetAddedTarget(), FixtureProjectRelativePath);

            Assert.That(generation.IsActiveMember(AddedMethodKey), Is.True);
            Assert.That(generation.IsActiveMember(OtherAddedMethodKey), Is.False);
            Assert.That(generation.ListActiveAddedMethodKeys(), Is.EqualTo(new[] { AddedMethodKey }));
        }

        /// <summary>
        /// What: a second added-member generation drops the previous members and fields, so a
        /// re-apply cannot keep members the edited source no longer declares.
        /// </summary>
        [Test]
        public void BeginAddedMemberGeneration_DropsPreviousMembersAndFields()
        {
            HotReloadFileGeneration generation = CreateGeneration();
            generation.BeginAddedMemberGeneration();
            generation.RegisterAddedMethod(AddedMethodKey, GetAddedTarget(), FixtureProjectRelativePath);
            generation.ReplaceAddedFields(new[] { HostType + ".alpha" });

            generation.BeginAddedMemberGeneration();

            Assert.That(generation.AddedMemberCount, Is.EqualTo(0));
            Assert.That(CollectFields(generation, HostType), Is.Empty);
        }

        /// <summary>
        /// What: a second shim generation empties the method map while the live patches it holds
        /// survive, because the re-apply that follows retires them itself.
        /// </summary>
        [Test]
        public void BeginShimGeneration_ClearsShimsButKeepsLivePatches()
        {
            HotReloadFileGeneration generation = CreateGeneration();
            BeginShimGeneration(generation);
            RegisterShim(generation);
            generation.BeginPatch(GetShimTarget(), GetAddedTarget());
            generation.CommitPatch(GetShimTarget());

            BeginShimGeneration(generation);

            Assert.That(generation.FindShim(GetShimTarget()), Is.Null);
            Assert.That(generation.IsPatchActive(GetShimTarget()), Is.True);
        }

        /// <summary>
        /// What: removing one shim method takes that method out while the file keeps its
        /// generation, so a sibling registration in the same run still lands here.
        /// </summary>
        [Test]
        public void RemoveShimMethod_RemovesTheMethodButKeepsTheGeneration()
        {
            HotReloadFileGeneration generation = CreateGeneration();
            BeginShimGeneration(generation);
            RegisterShim(generation);

            generation.RemoveShimMethod(GetShimTarget());

            Assert.That(generation.FindShim(GetShimTarget()), Is.Null);
            Assert.That(generation.HasShimGeneration, Is.True);
        }

        /// <summary>
        /// What: a patch is pending until it is committed, and only a committed patch is active or
        /// visible to the shim lookup a report reads.
        /// </summary>
        [Test]
        public void BeginPatch_IsPendingUntilCommitted()
        {
            HotReloadFileGeneration generation = CreateGeneration();
            BeginShimGeneration(generation);
            RegisterShim(generation);

            generation.BeginPatch(GetShimTarget(), GetAddedTarget());

            Assert.That(generation.IsPatchPending(GetShimTarget()), Is.True);
            Assert.That(generation.IsPatchActive(GetShimTarget()), Is.False);
            Assert.That(generation.HasPatch(GetShimTarget()), Is.True);
            Assert.That(generation.ActivePatchCount, Is.EqualTo(0));
            Assert.That(generation.FindPatchShim(GetShimTarget()), Is.EqualTo(GetAddedTarget()));

            generation.CommitPatch(GetShimTarget());

            Assert.That(generation.IsPatchPending(GetShimTarget()), Is.False);
            Assert.That(generation.IsPatchActive(GetShimTarget()), Is.True);
            Assert.That(generation.ActivePatchCount, Is.EqualTo(1));
            Assert.That(generation.ListActiveMethods(), Is.EqualTo(new MethodBase[] { GetShimTarget() }));
        }

        /// <summary>
        /// What: abandoning a pending patch removes it, which is how a failed Harmony call leaves
        /// no record of a patch that never went live.
        /// </summary>
        [Test]
        public void AbandonPatch_RemovesThePendingPatch()
        {
            HotReloadFileGeneration generation = CreateGeneration();
            BeginShimGeneration(generation);
            RegisterShim(generation);
            generation.BeginPatch(GetShimTarget(), GetAddedTarget());

            generation.AbandonPatch(GetShimTarget());

            Assert.That(generation.HasPatch(GetShimTarget()), Is.False);
        }

        /// <summary>
        /// What: abandoning a committed patch leaves it live, so a late failure path cannot drop
        /// the record of a patch Harmony already accepted.
        /// </summary>
        [Test]
        public void AbandonPatch_OnCommittedPatch_LeavesItActive()
        {
            HotReloadFileGeneration generation = CreateGeneration();
            BeginShimGeneration(generation);
            RegisterShim(generation);
            generation.BeginPatch(GetShimTarget(), GetAddedTarget());
            generation.CommitPatch(GetShimTarget());

            generation.AbandonPatch(GetShimTarget());

            Assert.That(generation.IsPatchActive(GetShimTarget()), Is.True);
        }

        /// <summary>
        /// What: deactivating a live patch hands the entry back so a failed unpatch can restore it
        /// whole, even after the shim registration is gone.
        /// </summary>
        [Test]
        public void DeactivateThenReactivatePatch_RestoresTheLivePatch()
        {
            HotReloadFileGeneration generation = CreateGeneration();
            BeginShimGeneration(generation);
            RegisterShim(generation);
            generation.BeginPatch(GetShimTarget(), GetAddedTarget());
            generation.CommitPatch(GetShimTarget());

            HotReloadActivePatchEntry removed = generation.DeactivatePatch(GetShimTarget());
            Assert.That(removed, Is.Not.Null);
            Assert.That(generation.IsPatchActive(GetShimTarget()), Is.False);

            generation.RemoveShimMethod(GetShimTarget());
            generation.ReactivatePatch(removed);

            Assert.That(generation.IsPatchActive(GetShimTarget()), Is.True);
            Assert.That(generation.ActivePatchCount, Is.EqualTo(1));
        }

        /// <summary>
        /// What: deactivating a method that holds no live patch returns null instead of inventing
        /// an entry a caller would restore.
        /// </summary>
        [Test]
        public void DeactivatePatch_WithoutLivePatch_ReturnsNull()
        {
            HotReloadFileGeneration generation = CreateGeneration();

            Assert.That(generation.DeactivatePatch(GetShimTarget()), Is.Null);
        }

        /// <summary>
        /// What: replacing the added fields overwrites the whole set, so a name the new list omits
        /// is dropped.
        /// </summary>
        [Test]
        public void ReplaceAddedFields_ReplacesTheWholeSet()
        {
            HotReloadFileGeneration generation = CreateGeneration();
            generation.BeginAddedMemberGeneration();
            generation.ReplaceAddedFields(new[] { HostType + ".oldField", HostType + ".keptField" });
            generation.ReplaceAddedFields(new[] { HostType + ".keptField", HostType + ".newField" });

            Assert.That(
                CollectFields(generation, HostType),
                Is.EquivalentTo(new[] { "keptField", "newField" }));
        }

        /// <summary>
        /// What: an empty replacement leaves the generation with no added fields at all.
        /// </summary>
        [Test]
        public void ReplaceAddedFields_EmptyList_DropsEveryField()
        {
            HotReloadFileGeneration generation = CreateGeneration();
            generation.BeginAddedMemberGeneration();
            generation.ReplaceAddedFields(new[] { HostType + ".alpha" });

            generation.ReplaceAddedFields(Array.Empty<string>());

            Assert.That(CollectFields(generation, HostType), Is.Empty);
            Assert.That(DescribeFields(generation), Is.Empty);
        }

        /// <summary>
        /// What: added fields on a nested type are stored under the reflection key, so a Cecil
        /// query spelled with '/' finds them once the domain normalizes it.
        /// </summary>
        [Test]
        public void AddedFields_NestedType_AreStoredUnderTheReflectionKey()
        {
            HotReloadFileGeneration generation = CreateGeneration();
            generation.BeginAddedMemberGeneration();
            generation.ReplaceAddedFields(new[] { NestedCecilType + ".count" });

            Assert.That(
                CollectFields(generation, NestedReflectionType),
                Is.EqualTo(new[] { "count" }));

            List<HotReloadAddedFieldDescription> described = DescribeFields(generation);
            Assert.That(described.Count, Is.EqualTo(1));
            Assert.That(described[0].TypeName, Is.EqualTo(NestedReflectionType));
            Assert.That(described[0].FieldName, Is.EqualTo("count"));
            Assert.That(described[0].ProjectRelativePath, Is.EqualTo(FixtureProjectRelativePath));
        }

        /// <summary>
        /// What: a recorded superseded signature is retrievable by the old method key, an unknown
        /// key is not, and removing one drops only that key.
        /// </summary>
        [Test]
        public void SupersededSignatures_RecordThenRemove_AffectOnlyThatKey()
        {
            HotReloadFileGeneration generation = CreateGeneration();
            generation.RecordSupersededSignature("Old.One()", "New.One()");
            generation.RecordSupersededSignature("Old.Two()", "New.Two()");

            Assert.That(
                generation.TryGetSupersededReplacement("Old.One()", out string firstReplacement),
                Is.True);
            Assert.That(firstReplacement, Is.EqualTo("New.One()"));
            Assert.That(
                generation.TryGetSupersededReplacement("Missing.Key()", out string missingReplacement),
                Is.False);
            Assert.That(missingReplacement, Is.Null);

            generation.RemoveSupersededSignature("Old.One()");

            Assert.That(generation.TryGetSupersededReplacement("Old.One()", out string _), Is.False);
            Assert.That(
                generation.TryGetSupersededReplacement("Old.Two()", out string keptReplacement),
                Is.True);
            Assert.That(keptReplacement, Is.EqualTo("New.Two()"));
        }

        /// <summary>
        /// What: the shim lookup a report reads carries the generation's compiled bytes and shows
        /// only the methods whose patch is live, so a registered-but-unpatched method hides it.
        /// </summary>
        [Test]
        public void BuildShimLookup_ShowsOnlyMethodsWithALivePatch()
        {
            HotReloadFileGeneration generation = CreateGeneration();
            BeginShimGeneration(generation);
            RegisterShim(generation);

            Assert.That(
                generation.BuildShimLookup(),
                Is.Null,
                "A registered shim whose patch is only pending must not be visible yet.");

            generation.BeginPatch(GetShimTarget(), GetAddedTarget());
            generation.CommitPatch(GetShimTarget());

            HotReloadShimFileLookup activeLookup = generation.BuildShimLookup();
            Assert.That(activeLookup, Is.Not.Null);
            Assert.That(activeLookup.AssemblyBytes, Is.EqualTo(PlaceholderAssemblyBytes));
            Assert.That(activeLookup.Methods.Count, Is.EqualTo(1));
            Assert.That(activeLookup.Methods[0].OriginalMethod, Is.EqualTo(GetShimTarget()));
            Assert.That(activeLookup.Methods[0].ShimMethod, Is.EqualTo(GetAddedTarget()));
            Assert.That(activeLookup.Methods[0].IsDelegation, Is.False);
            Assert.That(activeLookup.Methods[0].SourceStartLine, Is.EqualTo(1));
            Assert.That(activeLookup.Methods[0].SourceEndLine, Is.EqualTo(2));
        }

        private static HotReloadFileGeneration CreateGeneration()
        {
            return new HotReloadFileGeneration(FixtureProjectRelativePath);
        }

        private static void BeginShimGeneration(HotReloadFileGeneration generation)
        {
            generation.BeginShimGeneration(
                PlaceholderAssemblyBytes,
                null,
                typeof(HotReloadFileGenerationTests).Assembly);
        }

        private static void RegisterShim(HotReloadFileGeneration generation)
        {
            generation.RegisterShimMethod(
                GetShimTarget(),
                new HotReloadShimMethodEntry(GetAddedTarget(), false, 1, 2));
        }

        private static IReadOnlyList<string> CollectFields(
            HotReloadFileGeneration generation,
            string reflectionTypeName)
        {
            HashSet<string> fields = new HashSet<string>(StringComparer.Ordinal);
            generation.CollectAddedFieldsForType(reflectionTypeName, fields);
            List<string> sorted = new List<string>(fields);
            sorted.Sort(StringComparer.Ordinal);
            return sorted;
        }

        private static List<HotReloadAddedFieldDescription> DescribeFields(
            HotReloadFileGeneration generation)
        {
            List<HotReloadAddedFieldDescription> descriptions =
                new List<HotReloadAddedFieldDescription>();
            generation.DescribeAddedFields(descriptions);
            return descriptions;
        }

        private static MethodInfo GetShimTarget()
        {
            return typeof(HotReloadFileGenerationTests).GetMethod(
                nameof(ShimTarget),
                BindingFlags.Static | BindingFlags.NonPublic);
        }

        private static MethodInfo GetAddedTarget()
        {
            return typeof(HotReloadFileGenerationTests).GetMethod(
                nameof(AddedTarget),
                BindingFlags.Static | BindingFlags.NonPublic);
        }

        private static int ShimTarget()
        {
            return 1;
        }

        private static int AddedTarget()
        {
            return 2;
        }
    }
}
