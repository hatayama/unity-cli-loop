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
        private const string AddedMethodType = "FileGenerationFixture";
        private const string OtherAddedMethodKey = "FileGenerationFixture.OtherAddedMember()";
        private const string HostType = "Ns.Host";
        private const string CompiledAssemblyPath = "<PROJECT_ROOT>/Library/ScriptAssemblies/Fixture.dll";
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
            generation.RegisterAddedMethod(AddedMethodKey, GetShimTarget(), FixtureProjectRelativePath, "AddedMember", AddedMethodType);
            generation.RegisterAddedMethod(AddedMethodKey, GetAddedTarget(), FixtureProjectRelativePath, "AddedMember", AddedMethodType);

            List<HotReloadAddedMemberInfo> members = new List<HotReloadAddedMemberInfo>();
            generation.DescribeAddedMembers(members);

            Assert.That(generation.AddedMemberCount, Is.EqualTo(1));
            Assert.That(members.Count, Is.EqualTo(1));
            Assert.That(members[0].MethodKey, Is.EqualTo(AddedMethodKey));
            Assert.That(members[0].FilePath, Is.EqualTo(FixtureProjectRelativePath));
            Assert.That(members[0].ShimMethod, Is.EqualTo(GetAddedTarget()));
        }

        /// <summary>
        /// What: an added method registered with its source range is found for its first line, its
        /// last line and a line between them, and not for the lines just outside, even in a
        /// generation with no patched method, whose shim lookup is null.
        /// </summary>
        [Test]
        public void FindAddedMethodContainingLine_ReportsTheAddedMethodOnlyInsideItsRange()
        {
            HotReloadFileGeneration generation = CreateGeneration();
            generation.BeginAddedMemberGeneration();
            generation.RegisterAddedMethod(
                AddedMethodKey,
                GetAddedTarget(),
                FixtureProjectRelativePath,
                "AddedMember",
                AddedMethodType,
                sourceStartLine: 20,
                sourceEndLine: 24);

            Assert.That(generation.FindAddedMethodContainingLine(20).Label, Is.EqualTo(AddedMethodKey));
            Assert.That(generation.FindAddedMethodContainingLine(22).Label, Is.EqualTo(AddedMethodKey));
            Assert.That(generation.FindAddedMethodContainingLine(24).Label, Is.EqualTo(AddedMethodKey));
            Assert.That(generation.FindAddedMethodContainingLine(19), Is.Null);
            Assert.That(generation.FindAddedMethodContainingLine(25), Is.Null);
            Assert.That(generation.BuildShimLookup(), Is.Null);
        }

        /// <summary>
        /// What: the added method at a line carries its own name and its declaring type's short
        /// names converted from the metadata name, so a nested type reads Inner with Outer as its
        /// enclosing type and a top-level type drops its namespace.
        /// </summary>
        [Test]
        public void FindAddedMethodContainingLine_ReportsShortTypeNamesFromTheMetadataName()
        {
            HotReloadFileGeneration generation = CreateGeneration();
            generation.BeginAddedMemberGeneration();
            generation.RegisterAddedMethod(
                "Ns.Outer/Inner.Step(System.Int32)",
                GetAddedTarget(),
                FixtureProjectRelativePath,
                "Step",
                NestedCecilType,
                sourceStartLine: 10,
                sourceEndLine: 12);
            generation.RegisterAddedMethod(
                AddedMethodKey,
                GetShimTarget(),
                FixtureProjectRelativePath,
                "AddedMember",
                HostType,
                sourceStartLine: 20,
                sourceEndLine: 24);

            HotReloadAddedMethodAtLine nested = generation.FindAddedMethodContainingLine(11);
            HotReloadAddedMethodAtLine topLevel = generation.FindAddedMethodContainingLine(22);

            Assert.That(nested.Label, Is.EqualTo("Ns.Outer/Inner.Step(System.Int32)"));
            Assert.That(nested.MethodName, Is.EqualTo("Step"));
            Assert.That(nested.DeclaringTypeName, Is.EqualTo("Inner"));
            Assert.That(nested.NestedOuterTypeName, Is.EqualTo("Outer"));
            Assert.That(topLevel.MethodName, Is.EqualTo("AddedMember"));
            Assert.That(topLevel.DeclaringTypeName, Is.EqualTo("Host"));
            Assert.That(topLevel.NestedOuterTypeName, Is.Null);
        }

        /// <summary>
        /// What: a generation with only added methods of a compiled type still names the compiled
        /// assembly its verified snapshot is keyed on, so the compiled line map stays reachable
        /// for a file whose reload added methods but patched none.
        /// </summary>
        [Test]
        public void FindCompiledAssemblyLocation_AddedMethodsOfACompiledType_ReportTheirCompiledAssembly()
        {
            HotReloadFileGeneration generation = CreateGeneration();
            generation.BeginAddedMemberGeneration();
            generation.RegisterAddedMethod(
                AddedMethodKey,
                GetAddedTarget(),
                FixtureProjectRelativePath,
                "AddedMember",
                AddedMethodType,
                compiledAssemblyPath: CompiledAssemblyPath);

            Assert.That(generation.BuildShimLookup(), Is.Null);
            Assert.That(generation.FindCompiledAssemblyLocation(), Is.EqualTo(CompiledAssemblyPath));
            Assert.That(generation.HasActiveHotReloadChanges, Is.True);
        }

        /// <summary>
        /// What: added methods that name no compiled assembly (an introduced type) leave the
        /// generation without one, so such a file is never treated as having a compiled line map,
        /// and a new added-member generation forgets the assembly the previous one named.
        /// </summary>
        [Test]
        public void FindCompiledAssemblyLocation_AddedMethodsWithoutACompiledAssembly_ReportNone()
        {
            HotReloadFileGeneration generation = CreateGeneration();
            generation.BeginAddedMemberGeneration();
            generation.RegisterAddedMethod(AddedMethodKey, GetAddedTarget(), FixtureProjectRelativePath, "AddedMember", AddedMethodType);

            Assert.That(generation.FindCompiledAssemblyLocation(), Is.Null);
            Assert.That(generation.HasActiveHotReloadChanges, Is.True);

            generation.BeginAddedMemberGeneration();
            generation.RegisterAddedMethod(
                OtherAddedMethodKey,
                GetAddedTarget(),
                FixtureProjectRelativePath,
                "AddedMember",
                AddedMethodType,
                compiledAssemblyPath: CompiledAssemblyPath);
            generation.BeginAddedMemberGeneration();

            Assert.That(generation.FindCompiledAssemblyLocation(), Is.Null);
            Assert.That(generation.HasActiveHotReloadChanges, Is.False);
        }

        /// <summary>
        /// What: a generation reports active hot reload changes only while it holds a live patch or
        /// an added method, so a shim registered without a committed patch does not count.
        /// </summary>
        [Test]
        public void HasActiveHotReloadChanges_CountsLivePatchesAndAddedMethodsOnly()
        {
            HotReloadFileGeneration generation = CreateGeneration();
            BeginShimGeneration(generation);
            RegisterShim(generation);

            Assert.That(generation.HasActiveHotReloadChanges, Is.False);

            generation.BeginPatch(GetShimTarget(), GetAddedTarget());
            generation.CommitPatch(GetShimTarget());

            Assert.That(generation.HasActiveHotReloadChanges, Is.True);
        }

        /// <summary>
        /// What: an added method registered without a source range never claims a line, so a
        /// missing range cannot blame an unrelated line on an added method.
        /// </summary>
        [Test]
        public void FindAddedMethodContainingLine_AddedMethodWithoutARange_ReportsNothing()
        {
            HotReloadFileGeneration generation = CreateGeneration();
            generation.BeginAddedMemberGeneration();
            generation.RegisterAddedMethod(AddedMethodKey, GetAddedTarget(), FixtureProjectRelativePath, "AddedMember", AddedMethodType);

            Assert.That(generation.FindAddedMethodContainingLine(0), Is.Null);
            Assert.That(generation.FindAddedMethodContainingLine(1), Is.Null);
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
            generation.RegisterAddedMethod(AddedMethodKey, GetAddedTarget(), FixtureProjectRelativePath, "AddedMember", AddedMethodType);

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
            generation.RegisterAddedMethod(AddedMethodKey, GetAddedTarget(), FixtureProjectRelativePath, "AddedMember", AddedMethodType);
            generation.ReplaceAddedFields(new[] { HostType + ".alpha" }, null, null, null);

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
            generation.ReplaceAddedFields(new[] { HostType + ".oldField", HostType + ".keptField" }, null, null, null);
            generation.ReplaceAddedFields(new[] { HostType + ".keptField", HostType + ".newField" }, null, null, null);

            Assert.That(
                CollectFields(generation, HostType),
                Is.EquivalentTo(new[] { "keptField", "newField" }));
        }

        /// <summary>
        /// What: a field the generation already holds is reported when this run declares it with
        /// a different initializer, and stays unreported when the initializer is the same or was
        /// dropped, or when the field is new to this run.
        /// </summary>
        [Test]
        public void CollectAddedFieldsWithChangedInitializer_ReportsOnlyAChangedInitializer()
        {
            HotReloadFileGeneration generation = CreateGeneration();
            generation.BeginAddedMemberGeneration();
            generation.ReplaceAddedFields(
                new[] { HostType + ".changed", HostType + ".stable", HostType + ".dropped" },
                new[] { "1", "2", "3" },
                null,
                null);

            List<string> changed = new List<string>();
            generation.CollectAddedFieldsWithChangedInitializer(
                new[] { HostType + ".changed", HostType + ".stable", HostType + ".dropped", HostType + ".fresh" },
                new[] { "9", "2", string.Empty, "4" },
                changed);

            Assert.That(changed, Is.EqualTo(new[] { HostType + ".changed" }));
        }

        /// <summary>
        /// What: an initializer row that does not line up with the names is ignored, so a worker
        /// that did not report initializers cannot produce a warning about the wrong field.
        /// </summary>
        [Test]
        public void CollectAddedFieldsWithChangedInitializer_MisalignedInitializers_ReportsNothing()
        {
            HotReloadFileGeneration generation = CreateGeneration();
            generation.BeginAddedMemberGeneration();
            generation.ReplaceAddedFields(new[] { HostType + ".alpha" }, new[] { "1" }, null, null);

            List<string> changed = new List<string>();
            generation.CollectAddedFieldsWithChangedInitializer(
                new[] { HostType + ".alpha" },
                Array.Empty<string>(),
                changed);

            Assert.That(changed, Is.Empty);
        }

        /// <summary>
        /// What: an empty replacement leaves the generation with no added fields at all.
        /// </summary>
        [Test]
        public void ReplaceAddedFields_EmptyList_DropsEveryField()
        {
            HotReloadFileGeneration generation = CreateGeneration();
            generation.BeginAddedMemberGeneration();
            generation.ReplaceAddedFields(new[] { HostType + ".alpha" }, null, null, null);

            generation.ReplaceAddedFields(Array.Empty<string>(), null, null, null);

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
            generation.ReplaceAddedFields(new[] { NestedCecilType + ".count" }, null, null, null);

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
        /// What: an added field's declaration is retrievable by its declaring type and field name
        /// with the store key the worker formed, and a field this generation does not hold is not.
        /// </summary>
        [Test]
        public void ReplaceAddedFields_KeepsDeclarationsLookedUpByTypeAndFieldName()
        {
            HotReloadFileGeneration generation = CreateGeneration();
            generation.BeginAddedMemberGeneration();
            generation.ReplaceAddedFields(
                new[] { HostType + ".wired" },
                null,
                new[] { CreateDeclaration(HostType, "wired", typeof(string), isStatic: false) },
                null);

            Assert.That(
                generation.TryGetAddedFieldDeclaration(
                    HostType,
                    "wired",
                    out HotReloadAddedFieldDeclaration declaration),
                Is.True);
            Assert.That(declaration.StoreFieldKey, Is.EqualTo(HostType + "::wired"));
            Assert.That(declaration.DeclaredTypeAssemblyQualifiedName,
                Is.EqualTo(typeof(string).AssemblyQualifiedName));
            Assert.That(declaration.IsStatic, Is.False);
            Assert.That(
                generation.TryGetAddedFieldDeclaration(HostType, "missing", out HotReloadAddedFieldDeclaration _),
                Is.False);
        }

        /// <summary>
        /// What: replacing the added fields drops the declarations of the fields the new set
        /// omits, so a stale declaration cannot outlive the field it described.
        /// </summary>
        [Test]
        public void ReplaceAddedFields_DropsDeclarationsTheNewSetOmits()
        {
            HotReloadFileGeneration generation = CreateGeneration();
            generation.BeginAddedMemberGeneration();
            generation.ReplaceAddedFields(
                new[] { HostType + ".dropped" },
                null,
                new[] { CreateDeclaration(HostType, "dropped", typeof(int), isStatic: true) },
                null);

            generation.ReplaceAddedFields(
                new[] { HostType + ".kept" },
                null,
                new[] { CreateDeclaration(HostType, "kept", typeof(int), isStatic: true) },
                null);

            Assert.That(
                generation.TryGetAddedFieldDeclaration(HostType, "dropped", out HotReloadAddedFieldDeclaration _),
                Is.False);
            Assert.That(
                generation.TryGetAddedFieldDeclaration(HostType, "kept", out HotReloadAddedFieldDeclaration _),
                Is.True);
        }

        /// <summary>
        /// What: the declaration of a field on a nested type is found whether the caller spells
        /// the type the Cecil way or the reflection way.
        /// </summary>
        [Test]
        public void AddedFieldDeclarations_NestedType_AreFoundByEitherNameForm()
        {
            HotReloadFileGeneration generation = CreateGeneration();
            generation.BeginAddedMemberGeneration();
            generation.ReplaceAddedFields(
                new[] { NestedCecilType + ".count" },
                null,
                new[] { CreateDeclaration(NestedReflectionType, "count", typeof(int), isStatic: false) },
                null);

            Assert.That(
                generation.TryGetAddedFieldDeclaration(
                    NestedReflectionType,
                    "count",
                    out HotReloadAddedFieldDeclaration byReflectionName),
                Is.True);
            Assert.That(byReflectionName.DeclaringTypeName, Is.EqualTo(NestedReflectionType));
            Assert.That(
                generation.TryGetAddedFieldDeclaration(
                    NestedCecilType,
                    "count",
                    out HotReloadAddedFieldDeclaration byCecilName),
                Is.True);
            Assert.That(byCecilName.StoreFieldKey, Is.EqualTo(NestedCecilType + "::count"));
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

        // The store key spells nested types the metadata way, which is what the worker forms and
        // the Editor carries unchanged, so the fixture builds it from the type name it was given.
        private static HotReloadAddedFieldDeclaration CreateDeclaration(
            string declaringTypeName,
            string fieldName,
            Type declaredType,
            bool isStatic)
        {
            return new HotReloadAddedFieldDeclaration(
                declaringTypeName.Replace('+', '/') + "::" + fieldName,
                declaringTypeName,
                fieldName,
                declaredType.AssemblyQualifiedName,
                isStatic);
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
