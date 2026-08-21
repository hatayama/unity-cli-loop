using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEditor.Compilation;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Worker coverage for added/removed field classification, store rewrite, const folding,
    /// initializer visibility skip, and added-field skip/warning reasons.
    /// </summary>
    public class TransformWorkerAddedFieldTests
    {
        private const string TestAssemblyName = "UnityCLILoop.Tests.Editor.HotReload";
        private const string HostProjectRelativePath =
            "Assets/Tests/Editor/HotReload/HotReloadAddedMemberHost.cs";

        private const string HostCloseMarker =
            "        public int ReadPrivateSeed()\n        {\n            return _privateSeed;\n        }\n    }";

        private const string ExistingCallerOriginal =
            "        public int ExistingCaller(int value)\n        {\n            return value;\n        }";

        private const string ExistingFailOriginal =
            "        public int ExistingFail(int value)\n        {\n            return value;\n        }";

        private const string ExistingValueOriginal =
            "        public int ExistingValue()\n        {\n            return 1;\n        }";

        private const string FieldTypeChangedReason =
            "Field 'PublicSeed' has a different type in the compiled assembly. Run 'uloop compile'.";

        private const string FieldModifiersChangedReason =
            "Field 'PublicSeed' changed its static or const modifier in the compiled assembly. Run 'uloop compile'.";

        private const string MemberKindChangedReasonFormat =
            "Field '{0}' is declared as a property or an event in the compiled assembly. Run 'uloop compile'.";

        private const string CompiledPropertyWarningFormat =
            "Compiled property '{0}' was removed or redeclared as a different member kind in the edited source; the compiled member stays until 'uloop compile'.";

        private const string CompiledEventWarningFormat =
            "Compiled event '{0}' was removed or redeclared as a different member kind in the edited source; the compiled member stays until 'uloop compile'.";

        // Keep in sync with OutsideMethodBodyDriftWarningFormat in
        // Packages/src/Editor/FirstPartyTools/HotReload/TransformWorker~/OutsideMethodBodyDriftChecker.cs.
        // That constant lives in the Unity-ignored worker process and is not visible here.
        private const string OutsideMethodBodyDriftWarningFormat =
            "Edits outside method bodies in {0} (fields, initializers, or attributes) are not applied by hot reload; run uloop compile to pick them up.";

        private const string FieldKindChangeProjectRelativePath =
            "Assets/Tests/Editor/HotReload/HotReloadAddedMemberHost.cs";

        private const string FieldKindChangeUntouchedOriginal =
            "        public int UntouchedKind()\n        {\n            return 1;\n        }";

        private const string FieldKindChangeReadOriginal =
            "        public int ReadKind(int value)\n        {\n            return value;\n        }";

        private const string FieldKindChangeWriteOriginal =
            "        public int WriteKind(int value)\n        {\n            return value;\n        }";

        /// <summary>
        /// What: a field present only in the edited source is rewritten to the store, an existing
        /// field stays a real field access, and a snapshot-only field is reported removed.
        /// </summary>
        [Test]
        public async Task Classify_AddedExistingAndRemovedFields_MatchCompiledAssemblyGroundTruth()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = WithHostMembers(onDisk, "public int AddedCount;");
            edited = edited.Replace(
                "        public HotReloadAddedMemberHost Inner;\n\n",
                string.Empty,
                StringComparison.Ordinal);
            edited = edited.Replace(
                ExistingCallerOriginal,
                "        public int ExistingCaller(int value)\n        {\n"
                + "            return PublicSeed + AddedCount + value;\n        }",
                StringComparison.Ordinal);
            string sourcePath = WriteEdited("ClassifyAddedExistingRemovedFields.cs", edited);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                sourcePath,
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);

            TransformWorkerEntryDto caller = FindEntry(result, nameof(HotReloadAddedMemberHost.ExistingCaller));
            Assert.That(caller, Is.Not.Null);
            string slice = SliceShimMethod(result.Output.shimSource, caller.shimMethodName);
            Assert.That(slice, Does.Contain("HotReloadAddedFieldStore"));
            Assert.That(slice, Does.Contain("::AddedCount"));
            Assert.That(slice, Does.Contain("PublicSeed"));
            Assert.That(slice, Does.Not.Contain("::PublicSeed"));
            Assert.That(result.Output.hasAddedFieldRewrites, Is.True);

            bool foundRemoved = false;
            foreach (TransformWorkerRemovedMemberDto removed in result.Output.removedMembers)
            {
                if (removed.kind == HotReloadConstants.RemovedMemberKindField
                    && removed.name == nameof(HotReloadAddedMemberHost.Inner))
                {
                    foundRemoved = true;
                }
            }

            Assert.That(foundRemoved, Is.True, "Inner must be reported as a removed field.");
        }

        /// <summary>
        /// What: a plain added field and a [SerializeField] added field appear in
        /// addedFieldNames as Type.field, sorted ordinal, and a compiled-field type change
        /// does not.
        /// </summary>
        [Test]
        public async Task Classify_AddedFields_ListsStoreAndSerializeNamesInOrdinalOrder()
        {
            string hostTypeName = typeof(HotReloadAddedMemberHost).FullName;
            string[] expectedNames =
            {
                hostTypeName + ".AddedCount",
                hostTypeName + ".AddedSerialized"
            };
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = WithHostMembers(
                onDisk,
                "public int AddedCount;\n        [SerializeField] public int AddedSerialized;");
            edited = edited.Replace(
                ExistingCallerOriginal,
                "        public int ExistingCaller(int value)\n        {\n"
                + "            return AddedCount + AddedSerialized + value;\n        }",
                StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("AddedFieldNamesListed.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output.addedFieldNames, Is.EqualTo(expectedNames));
        }

        /// <summary>
        /// What: an added field that no emitted method body rewrites is omitted from
        /// addedFieldNames.
        /// </summary>
        [Test]
        public async Task Classify_UnusedAddedField_OmitsFromAddedFieldNames()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = WithHostMembers(onDisk, "public int UnusedAdded;");
            edited = edited.Replace(
                ExistingCallerOriginal,
                "        public int ExistingCaller(int value)\n        {\n"
                + "            return value + 1;\n        }",
                StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("UnusedAddedFieldNotListed.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(FindEntry(result, nameof(HotReloadAddedMemberHost.ExistingCaller)), Is.Not.Null);
            Assert.That(result.Output.addedFieldNames, Is.Not.Null);
            Assert.That(result.Output.addedFieldNames, Is.Empty);
        }

        /// <summary>
        /// What: changing a compiled field's type does not list that field in addedFieldNames.
        /// </summary>
        [Test]
        public async Task Classify_CompiledFieldTypeChange_OmitsFieldFromAddedFieldNames()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = onDisk.Replace(
                "        public int PublicSeed = 3;",
                "        public long PublicSeed = 3;",
                StringComparison.Ordinal);
            edited = edited.Replace(
                ExistingCallerOriginal,
                "        public int ExistingCaller(int value)\n        {\n"
                + "            return PublicSeed.GetHashCode() + value;\n        }",
                StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("CompiledFieldTypeChangeNotAdded.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output.addedFieldNames, Is.Not.Null);
            Assert.That(result.Output.addedFieldNames, Is.Empty);
        }

        /// <summary>
        /// What: an added field on a nested compiled type uses '+' in the display name.
        /// </summary>
        [Test]
        public async Task Classify_AddedFieldOnNestedType_UsesPlusInDisplayName()
        {
            string expectedName =
                typeof(HotReloadAddedMemberHost.NestedAddedFieldHost).FullName + ".AddedNested";
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = onDisk.Replace(
                "            public int ExistingNested()\n            {\n                return 1;\n            }",
                "            public int AddedNested;\n\n"
                + "            public int ExistingNested()\n            {\n                return AddedNested;\n            }",
                StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk));

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("AddedNestedFieldDisplayName.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Output.addedFieldNames, Is.EqualTo(new[] { expectedName }));
        }

        /// <summary>
        /// What: uses of an added const fold to a value literal so the shim does not need the
        /// missing const member, and the store flag stays false.
        /// </summary>
        [Test]
        public async Task Rewrite_AddedConst_FoldsToLiteralWithoutStoreFlag()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = WithHostMembers(onDisk, "public const int AddedConst = 4;");
            edited = edited.Replace(
                ExistingCallerOriginal,
                "        public int ExistingCaller(int value)\n        {\n"
                + "            return AddedConst + value;\n        }",
                StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("AddedConstFold.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);

            TransformWorkerEntryDto caller = FindEntry(result, nameof(HotReloadAddedMemberHost.ExistingCaller));
            Assert.That(caller, Is.Not.Null);
            string slice = SliceShimMethod(result.Output.shimSource, caller.shimMethodName);
            Assert.That(slice, Does.Contain("4"));
            Assert.That(slice, Does.Not.Contain("HotReloadAddedFieldStore"));
            Assert.That(slice, Does.Not.Contain("AddedConst"));
            Assert.That(result.Output.hasAddedFieldRewrites, Is.False);
            Assert.That(
                result.Output.addedFieldNames,
                Is.EqualTo(new[] { typeof(HotReloadAddedMemberHost).FullName + ".AddedConst" }));
        }

        /// <summary>
        /// What: nameof(added field) folds to the field name string, including added consts.
        /// </summary>
        [Test]
        public async Task Rewrite_NameofAddedField_FoldsToStringLiteral()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = WithHostMembers(
                onDisk,
                "public int AddedCount;\n        public const int AddedConst = 4;");
            edited = edited.Replace(
                ExistingCallerOriginal,
                "        public int ExistingCaller(int value)\n        {\n"
                + "            return nameof(AddedCount).Length + nameof(AddedConst).Length + value;\n        }",
                StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("NameofAddedField.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);

            TransformWorkerEntryDto caller = FindEntry(result, nameof(HotReloadAddedMemberHost.ExistingCaller));
            Assert.That(caller, Is.Not.Null);
            string slice = SliceShimMethod(result.Output.shimSource, caller.shimMethodName);
            Assert.That(slice, Does.Contain("\"AddedCount\""));
            Assert.That(slice, Does.Contain("\"AddedConst\""));
            Assert.That(slice, Does.Not.Contain("nameof("));
        }

        /// <summary>
        /// What: added-field reads and writes emit GetOrInit/Set, and an initializer becomes a
        /// static lambda on the GetOrInit call.
        /// </summary>
        [Test]
        public async Task Rewrite_AddedFieldReadWriteAndInitializer_UsesStoreAndStaticLambda()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = WithHostMembers(onDisk, "public int AddedCount = 5;");
            edited = edited.Replace(
                ExistingCallerOriginal,
                "        public int ExistingCaller(int value)\n        {\n"
                + "            AddedCount = value;\n            return AddedCount;\n        }",
                StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("AddedFieldReadWrite.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);

            TransformWorkerEntryDto caller = FindEntry(result, nameof(HotReloadAddedMemberHost.ExistingCaller));
            Assert.That(caller, Is.Not.Null);
            string slice = SliceShimMethod(result.Output.shimSource, caller.shimMethodName);
            Assert.That(slice, Does.Contain("GetOrInit<"));
            Assert.That(slice, Does.Contain("Set<"));
            Assert.That(slice, Does.Contain("static () =>"));
            Assert.That(slice, Does.Contain("5"));
            Assert.That(result.Output.hasAddedFieldRewrites, Is.True);
        }

        /// <summary>
        /// What: static added fields use GetOrInitStatic/SetStatic instead of the instance store.
        /// </summary>
        [Test]
        public async Task Rewrite_AddedStaticField_UsesStaticStore()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = WithHostMembers(onDisk, "public static int AddedStatic;");
            edited = edited.Replace(
                ExistingCallerOriginal,
                "        public int ExistingCaller(int value)\n        {\n"
                + "            AddedStatic = value;\n            return AddedStatic;\n        }",
                StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("AddedStaticField.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);

            TransformWorkerEntryDto caller = FindEntry(result, nameof(HotReloadAddedMemberHost.ExistingCaller));
            Assert.That(caller, Is.Not.Null);
            string slice = SliceShimMethod(result.Output.shimSource, caller.shimMethodName);
            Assert.That(slice, Does.Contain("GetOrInitStatic<"));
            Assert.That(slice, Does.Contain("SetStatic<"));
            Assert.That(slice, Does.Not.Contain("GetOrInit<"));
        }

        /// <summary>
        /// What: a method that reads an added field as both this.field and bare field is rewritten
        /// so both store reads use the instance parameter, not this.
        /// </summary>
        [Test]
        public async Task Rewrite_ThisAndBareAddedFieldRead_BothUseInstanceParameter()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = WithHostMembers(onDisk, "public int AddedCount;");
            edited = edited.Replace(
                ExistingCallerOriginal,
                "        public int ExistingCaller(int value)\n        {\n"
                + "            return this.AddedCount + AddedCount + value;\n        }",
                StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("ThisAndBareAddedFieldRead.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);

            TransformWorkerEntryDto caller = FindEntry(result, nameof(HotReloadAddedMemberHost.ExistingCaller));
            Assert.That(caller, Is.Not.Null);
            string slice = SliceShimMethod(result.Output.shimSource, caller.shimMethodName);
            const string expectedReturn =
                "return global::io.github.hatayama.UnityCliLoop.ToolContracts.HotReloadAddedFieldStore.GetOrInit<int>(__uloopInstance, \"io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadAddedMemberHost::AddedCount\", null) + global::io.github.hatayama.UnityCliLoop.ToolContracts.HotReloadAddedFieldStore.GetOrInit<int>(__uloopInstance, \"io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadAddedMemberHost::AddedCount\", null) + value;";
            Assert.That(slice, Does.Contain(expectedReturn));
            Assert.That(slice, Does.Not.Contain("(this"));
        }

        /// <summary>
        /// What: assigning this.field on an added instance field still rewrites to Set with the
        /// instance parameter.
        /// </summary>
        [Test]
        public async Task Rewrite_ThisQualifiedAddedFieldAssignment_UsesInstanceParameter()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = WithHostMembers(onDisk, "public int AddedCount;");
            edited = edited.Replace(
                ExistingCallerOriginal,
                "        public int ExistingCaller(int value)\n        {\n"
                + "            this.AddedCount = value;\n            return value;\n        }",
                StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("ThisQualifiedAddedFieldAssignment.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);

            TransformWorkerEntryDto caller = FindEntry(result, nameof(HotReloadAddedMemberHost.ExistingCaller));
            Assert.That(caller, Is.Not.Null);
            string slice = SliceShimMethod(result.Output.shimSource, caller.shimMethodName);
            Assert.That(
                slice,
                Does.Contain(
                    "global::io.github.hatayama.UnityCliLoop.ToolContracts.HotReloadAddedFieldStore.Set<int>(__uloopInstance, \"io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadAddedMemberHost::AddedCount\", value)"));
            Assert.That(slice, Does.Not.Contain("(this"));
        }

        /// <summary>
        /// What: reading a static added field as TypeName.field still uses the static store,
        /// not an instance receiver.
        /// </summary>
        [Test]
        public async Task Rewrite_TypeQualifiedStaticAddedFieldRead_UsesStaticStore()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = WithHostMembers(onDisk, "public static int AddedStatic;");
            edited = edited.Replace(
                ExistingCallerOriginal,
                "        public int ExistingCaller(int value)\n        {\n"
                + "            return HotReloadAddedMemberHost.AddedStatic + value;\n        }",
                StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("TypeQualifiedStaticAddedFieldRead.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);

            TransformWorkerEntryDto caller = FindEntry(result, nameof(HotReloadAddedMemberHost.ExistingCaller));
            Assert.That(caller, Is.Not.Null);
            string slice = SliceShimMethod(result.Output.shimSource, caller.shimMethodName);
            Assert.That(slice, Does.Contain("GetOrInitStatic<"));
            Assert.That(slice, Does.Not.Contain("GetOrInit<"));
            Assert.That(slice, Does.Not.Contain("(this"));
        }

        /// <summary>
        /// What: compound assignment and increment on an added field expand to Get then Set.
        /// </summary>
        [Test]
        public async Task Rewrite_CompoundAndIncrement_ExpandToGetThenSet()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = WithHostMembers(onDisk, "public int AddedCount;");
            edited = edited.Replace(
                ExistingCallerOriginal,
                "        public int ExistingCaller(int value)\n        {\n"
                + "            AddedCount += value;\n            AddedCount++;\n            ++AddedCount;\n"
                + "            return AddedCount;\n        }",
                StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("AddedFieldCompound.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);

            TransformWorkerEntryDto caller = FindEntry(result, nameof(HotReloadAddedMemberHost.ExistingCaller));
            Assert.That(caller, Is.Not.Null);
            string slice = SliceShimMethod(result.Output.shimSource, caller.shimMethodName);
            Assert.That(slice, Does.Contain("GetOrInit<"));
            Assert.That(slice, Does.Contain("Set<"));
            Assert.That(slice, Does.Contain("+ value"));
            Assert.That(slice, Does.Contain("+ 1"));
        }

        /// <summary>
        /// What: adding a field without other outside-body edits does not fire the drift warning
        /// because handled added field declarations are stripped before comparison.
        /// </summary>
        [Test]
        public async Task Drift_AddedFieldOnly_DoesNotFireOutsideBodyWarning()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = WithHostMembers(onDisk, "public int AddedCount;");
            edited = edited.Replace(
                ExistingCallerOriginal,
                "        public int ExistingCaller(int value)\n        {\n"
                + "            return AddedCount + value;\n        }",
                StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("AddedFieldNoDrift.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            string unexpectedLifetimeWarning = string.Format(
                HotReloadConstants.AddedFieldsLifetimeWarningFormat,
                typeof(HotReloadAddedMemberHost).FullName + ".AddedCount");
            Assert.That(
                result.Output.declarationDriftWarnings,
                Does.Not.Contain(unexpectedLifetimeWarning),
                "Worker must not emit the added-fields lifetime warning; the orchestrator owns it.");
            string expectedOutsideBodyWarning = string.Format(
                OutsideMethodBodyDriftWarningFormat,
                "AddedFieldNoDrift.cs");
            Assert.That(
                result.Output.declarationDriftWarnings,
                Does.Not.Contain(expectedOutsideBodyWarning),
                "Handled added-field declarations must not fire the outside-body warning.\n"
                + string.Join("\n", result.Output.declarationDriftWarnings));

            Assert.That(result.Output.hasAddedFieldRewrites, Is.True);
        }

        /// <summary>
        /// What: an added field initializer that touches a host instance member skips, including
        /// private fields, because the static shim lambda cannot bind those names.
        /// </summary>
        [Test]
        public async Task Skip_PrivateInitializer_UsesLiteralOrExternalStaticReason()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = WithHostMembers(onDisk, "public int AddedFromPrivate = _privateSeed;");
            edited = edited.Replace(
                ExistingCallerOriginal,
                "        public int ExistingCaller(int value)\n        {\n"
                + "            return AddedFromPrivate + value;\n        }",
                StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("AddedFieldPrivateInit.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            AssertHasSkip(result, nameof(HotReloadAddedMemberHost.ExistingCaller), "literal or externally visible static");
            Assert.That(result.Output.hasAddedFieldRewrites, Is.False);
        }

        /// <summary>
        /// What: an added field initializer that reads a public instance field of the host is
        /// skipped; the shim is a different static class so the name would be CS0103.
        /// </summary>
        [Test]
        public async Task Skip_PublicInstanceInitializer_UsesLiteralOrExternalStaticReason()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = WithHostMembers(onDisk, "public int AddedFromPublic = PublicSeed + 1;");
            edited = edited.Replace(
                ExistingCallerOriginal,
                "        public int ExistingCaller(int value)\n        {\n"
                + "            return AddedFromPublic + value;\n        }",
                StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("AddedFieldPublicInit.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            AssertHasSkip(result, nameof(HotReloadAddedMemberHost.ExistingCaller), "literal or externally visible static");
            Assert.That(result.Output.hasAddedFieldRewrites, Is.False);
        }

        /// <summary>
        /// What: an initializer that only uses a literal and an externally visible static API
        /// still rewrites to GetOrInit with a static lambda.
        /// </summary>
        [Test]
        public async Task Rewrite_ExternalStaticInitializer_EmitsStaticLambda()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = WithHostMembers(onDisk, "public int AddedFromStatic = Math.Abs(-4);");
            edited = edited.Replace(
                ExistingCallerOriginal,
                "        public int ExistingCaller(int value)\n        {\n"
                + "            return AddedFromStatic + value;\n        }",
                StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("AddedFieldStaticInit.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);

            TransformWorkerEntryDto caller = FindEntry(result, nameof(HotReloadAddedMemberHost.ExistingCaller));
            Assert.That(caller, Is.Not.Null);
            string slice = SliceShimMethod(result.Output.shimSource, caller.shimMethodName);
            Assert.That(slice, Does.Contain("GetOrInit<"));
            Assert.That(slice, Does.Contain("static () =>"));
            Assert.That(slice, Does.Contain("Abs"));
            Assert.That(result.Output.hasAddedFieldRewrites, Is.True);
        }

        /// <summary>
        /// What: passing an added field by ref, out, or in skips the referencing method.
        /// </summary>
        [Test]
        public async Task Skip_RefOutInAddedField_UsesDedicatedReason()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = WithHostMembers(onDisk, "public int AddedCount;");
            edited = edited.Replace(
                ExistingCallerOriginal,
                "        public int ExistingCaller(int value)\n        {\n"
                + "            int.TryParse(\"1\", out AddedCount);\n            return AddedCount + value;\n        }",
                StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("AddedFieldRef.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            AssertHasSkip(result, nameof(HotReloadAddedMemberHost.ExistingCaller), "ref, out, or in");
        }

        /// <summary>
        /// What: a compound assignment whose value is consumed skips, because Set returns void.
        /// </summary>
        [Test]
        public async Task Skip_ConsumedCompoundAssignment_UsesDedicatedReason()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = WithHostMembers(onDisk, "public int AddedCount;");
            edited = edited.Replace(
                ExistingCallerOriginal,
                "        public int ExistingCaller(int value)\n        {\n"
                + "            int consumed = AddedCount += value;\n            return consumed;\n        }",
                StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("AddedFieldConsumed.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            AssertHasSkip(result, nameof(HotReloadAddedMemberHost.ExistingCaller), "consumed");
        }

        /// <summary>
        /// What: compound assignment through a receiver that may have side effects skips so
        /// Get and Set do not evaluate it twice.
        /// </summary>
        [Test]
        public async Task Skip_DoubleEvalReceiver_UsesDedicatedReason()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = WithHostMembers(onDisk, "public int AddedCount;");
            edited = edited.Replace(
                ExistingCallerOriginal,
                "        public int ExistingCaller(int value)\n        {\n"
                + "            Get().AddedCount += value;\n            return value;\n        }",
                StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("AddedFieldDoubleEval.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            AssertHasSkip(result, nameof(HotReloadAddedMemberHost.ExistingCaller), "side effects");
        }

        /// <summary>
        /// What: a simple assignment through a side-effect receiver still rewrites because Set
        /// evaluates the receiver once.
        /// </summary>
        [Test]
        public async Task Rewrite_SimpleAssignmentSideEffectReceiver_DoesNotSkip()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = WithHostMembers(onDisk, "public int AddedCount;");
            edited = edited.Replace(
                ExistingCallerOriginal,
                "        public int ExistingCaller(int value)\n        {\n"
                + "            Get().AddedCount = value;\n            return value;\n        }",
                StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("AddedFieldSimpleAssignReceiver.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);

            TransformWorkerEntryDto caller = FindEntry(result, nameof(HotReloadAddedMemberHost.ExistingCaller));
            Assert.That(caller, Is.Not.Null);
            string slice = SliceShimMethod(result.Output.shimSource, caller.shimMethodName);
            Assert.That(slice, Does.Contain("Set<"));
            Assert.That(result.Output.hasAddedFieldRewrites, Is.True);
        }

        /// <summary>
        /// What: byte += and ++ emit a cast back to byte so the Set call matches compound
        /// conversion and does not pass an int argument.
        /// </summary>
        [Test]
        public async Task Rewrite_ByteCompoundAndIncrement_CastsToByte()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = WithHostMembers(onDisk, "public byte AddedByte;");
            edited = edited.Replace(
                ExistingCallerOriginal,
                "        public int ExistingCaller(int value)\n        {\n"
                + "            AddedByte += 1;\n            AddedByte++;\n            return AddedByte;\n        }",
                StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("AddedFieldByteCompound.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);

            TransformWorkerEntryDto caller = FindEntry(result, nameof(HotReloadAddedMemberHost.ExistingCaller));
            Assert.That(caller, Is.Not.Null);
            string slice = SliceShimMethod(result.Output.shimSource, caller.shimMethodName);
            Assert.That(slice, Does.Contain("Set<"));
            Assert.That(slice, Does.Contain("GetOrInit<"));
            Assert.That(slice, Does.Contain("(byte)("));
            Assert.That(result.Output.hasAddedFieldRewrites, Is.True);
        }

        /// <summary>
        /// What: writing a member of an added value-type field skips because GetOrInit returns
        /// a copy, so the write would not persist.
        /// </summary>
        [Test]
        public async Task Skip_ValueTypeAddedFieldMemberWrite_UsesDedicatedReason()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = WithHostMembers(onDisk, "public HotReloadAddedFieldStructHost AddedValue;");
            edited = edited.Replace(
                ExistingCallerOriginal,
                "        public int ExistingCaller(int value)\n        {\n"
                + "            AddedValue.Existing = value;\n            return AddedValue.Existing;\n        }",
                StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("AddedFieldN2.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            AssertHasSkip(result, nameof(HotReloadAddedMemberHost.ExistingCaller), "value-type");
        }

        /// <summary>
        /// What: added fields on a compiled struct type skip referencing methods because the
        /// store cannot keep identity without boxing.
        /// </summary>
        [Test]
        public async Task Skip_StructHostAddedField_UsesDedicatedReason()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = onDisk.Replace(
                "        public int Existing;\n",
                "        public int Existing;\n\n        public int Added;\n",
                StringComparison.Ordinal);
            edited = edited.Replace(
                ExistingCallerOriginal,
                "        public int ExistingCaller(int value)\n        {\n"
                + "            HotReloadAddedFieldStructHost local = default;\n"
                + "            return local.Added + value;\n        }",
                StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("AddedFieldStructHost.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            AssertHasSkip(result, nameof(HotReloadAddedMemberHost.ExistingCaller), "struct");
            Assert.That(result.Output.hasAddedFieldRewrites, Is.False);
        }

        /// <summary>
        /// What: an added const on a struct host still folds to a literal because const folding
        /// does not use the instance store.
        /// </summary>
        [Test]
        public async Task Rewrite_AddedConstOnStructHost_FoldsToLiteral()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = onDisk.Replace(
                "        public int Existing;\n",
                "        public int Existing;\n\n        public const int AddedConst = 4;\n",
                StringComparison.Ordinal);
            edited = edited.Replace(
                ExistingCallerOriginal,
                "        public int ExistingCaller(int value)\n        {\n"
                + "            return HotReloadAddedFieldStructHost.AddedConst + value;\n        }",
                StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("AddedConstOnStruct.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);

            TransformWorkerEntryDto caller = FindEntry(result, nameof(HotReloadAddedMemberHost.ExistingCaller));
            Assert.That(caller, Is.Not.Null);
            string slice = SliceShimMethod(result.Output.shimSource, caller.shimMethodName);
            Assert.That(slice, Does.Contain("4"));
            Assert.That(slice, Does.Not.Contain("HotReloadAddedFieldStore"));
            Assert.That(result.Output.hasAddedFieldRewrites, Is.False);
        }

        /// <summary>
        /// What: ++ on an added field whose type has op_Increment but is not a numeric primitive
        /// or enum skips so the rewrite does not emit a broken + 1.
        /// </summary>
        [Test]
        public async Task Skip_NonNumericIncrement_UsesDedicatedReason()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = WithHostMembers(onDisk, "public HotReloadAddedFieldCounter AddedCounter;");
            edited = edited.Replace(
                ExistingCallerOriginal,
                "        public int ExistingCaller(int value)\n        {\n"
                + "            AddedCounter++;\n            return value;\n        }",
                StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("AddedFieldNonNumericIncrement.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            AssertHasSkip(result, nameof(HotReloadAddedMemberHost.ExistingCaller), "numeric");
            Assert.That(result.Output.hasAddedFieldRewrites, Is.False);
        }

        /// <summary>
        /// What: an added field whose type is a private nested type skips because GetOrInit&lt;T&gt;
        /// would be CS0122 in the shim.
        /// </summary>
        [Test]
        public async Task Skip_PrivateFieldType_UsesDedicatedReason()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = WithHostMembers(
                onDisk,
                "private class HiddenBox { }\n        public HiddenBox AddedHidden;");
            edited = edited.Replace(
                ExistingCallerOriginal,
                "        public int ExistingCaller(int value)\n        {\n"
                + "            return AddedHidden == null ? value : value + 1;\n        }",
                StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("AddedFieldPrivateType.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            AssertHasSkip(result, nameof(HotReloadAddedMemberHost.ExistingCaller), "not visible");
            Assert.That(result.Output.hasAddedFieldRewrites, Is.False);
        }

        /// <summary>
        /// What: a non-finite added const cannot be emitted as a C# literal, so referencing
        /// methods skip.
        /// </summary>
        [Test]
        public async Task Skip_NonFiniteAddedConst_UsesUnavailableReason()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = WithHostMembers(onDisk, "public const double AddedNaN = double.NaN;");
            edited = edited.Replace(
                ExistingCallerOriginal,
                "        public int ExistingCaller(int value)\n        {\n"
                + "            return double.IsNaN(AddedNaN) ? value : 0;\n        }",
                StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("AddedConstNaN.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            AssertHasSkip(result, nameof(HotReloadAddedMemberHost.ExistingCaller), "cannot emit");
        }

        /// <summary>
        /// What: editing an existing field initializer still fires outside-body drift even when
        /// an added field in the same file is stripped from the comparison.
        /// </summary>
        [Test]
        public async Task Drift_ExistingInitializerEditWithAddedField_StillWarns()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = WithHostMembers(onDisk, "public int AddedCount;");
            edited = edited.Replace(
                "        public int PublicSeed = 3;",
                "        public int PublicSeed = 99;",
                StringComparison.Ordinal);
            edited = edited.Replace(
                ExistingCallerOriginal,
                "        public int ExistingCaller(int value)\n        {\n"
                + "            return AddedCount + value;\n        }",
                StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("AddedFieldWithInitializerDrift.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);

            bool foundDrift = false;
            foreach (string warning in result.Output.declarationDriftWarnings)
            {
                if (warning != null && warning.Contains("Edits outside method bodies"))
                {
                    foundDrift = true;
                }
            }

            Assert.That(foundDrift, Is.True, "Existing field initializer edits must still warn.");
            AssertHasDeclarationDriftWarning(
                result,
                "Edits outside method bodies in AddedFieldWithInitializerDrift.cs (field initializer: PublicSeed) are not applied by hot reload; run uloop compile to pick them up.");
            Assert.That(result.Output.hasAddedFieldRewrites, Is.True);
        }

        /// <summary>
        /// What: editing an existing field initializer names that field in the outside-body
        /// warning rather than emitting the file-only wording.
        /// </summary>
        [Test]
        public async Task Drift_ExistingInitializerEdit_NamesDeclaration()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = onDisk.Replace(
                "        public int PublicSeed = 3;",
                "        public int PublicSeed = 99;",
                StringComparison.Ordinal);
            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("NamedInitializerDrift.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(
                result.Output.declarationDriftWarnings,
                Is.EqualTo(
                    new[]
                    {
                        "Edits outside method bodies in NamedInitializerDrift.cs (field initializer: PublicSeed) are not applied by hot reload; run uloop compile to pick them up."
                    }));
        }

        /// <summary>
        /// What: swapping two compiled field declarations without changing initializers still
        /// emits the file-only outside-body warning, because initializer order is observable.
        /// </summary>
        [Test]
        public async Task Drift_FieldDeclarationOrderSwap_EmitsFileOnlyWarning()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            const string originalOrder =
                "        public int PublicSeed = 3;\n        public int PairAlpha = 1, PairBeta = 2;";
            const string swappedOrder =
                "        public int PairAlpha = 1, PairBeta = 2;\n        public int PublicSeed = 3;";
            Assert.That(onDisk, Does.Contain(originalOrder));
            string edited = onDisk.Replace(originalOrder, swappedOrder, StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk));
            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("FieldOrderSwapDrift.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(
                result.Output.declarationDriftWarnings,
                Is.EqualTo(
                    new[]
                    {
                        "Edits outside method bodies in FieldOrderSwapDrift.cs (fields, initializers, or attributes) are not applied by hot reload; run uloop compile to pick them up."
                    }));
        }

        /// <summary>
        /// What: changing attributes on a multi-declarator field names every sibling rather
        /// than attributing the edit to a single variable.
        /// </summary>
        [Test]
        public async Task Drift_MultiDeclaratorAttributeEdit_NamesEverySibling()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            Assert.That(onDisk, Does.Contain("        public int PairAlpha = 1, PairBeta = 2;"));
            string edited = onDisk.Replace(
                "        public int PairAlpha = 1, PairBeta = 2;",
                "        [Obsolete]\n        public int PairAlpha = 1, PairBeta = 2;",
                StringComparison.Ordinal);
            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("MultiDeclaratorAttributeDrift.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(
                result.Output.declarationDriftWarnings,
                Is.EqualTo(
                    new[]
                    {
                        "Edits outside method bodies in MultiDeclaratorAttributeDrift.cs (field attributes: PairAlpha, PairBeta) are not applied by hot reload; run uloop compile to pick them up."
                    }));
        }

        /// <summary>
        /// What: changing one initializer on a multi-declarator field names only that
        /// variable, not its sibling.
        /// </summary>
        [Test]
        public async Task Drift_MultiDeclaratorInitializerEdit_NamesOnlyChangedVariable()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = onDisk.Replace(
                "        public int PairAlpha = 1, PairBeta = 2;",
                "        public int PairAlpha = 99, PairBeta = 2;",
                StringComparison.Ordinal);
            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("MultiDeclaratorInitializerDrift.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(
                result.Output.declarationDriftWarnings,
                Is.EqualTo(
                    new[]
                    {
                        "Edits outside method bodies in MultiDeclaratorInitializerDrift.cs (field initializer: PairAlpha) are not applied by hot reload; run uloop compile to pick them up."
                    }));
        }

        /// <summary>
        /// What: splitting a multi-declarator across declarations fails open to the file-only
        /// warning instead of stripping both names and going silent.
        /// </summary>
        [Test]
        public async Task Drift_MultiDeclaratorRegroup_FailsOpenToFileOnlyWarning()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = onDisk.Replace(
                "        public int PairAlpha = 1, PairBeta = 2;",
                "        public int PairAlpha = 1;\n        [Obsolete] public int PairBeta = 2;",
                StringComparison.Ordinal);
            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("MultiDeclaratorRegroupDrift.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(
                result.Output.declarationDriftWarnings,
                Is.EqualTo(
                    new[]
                    {
                        "Edits outside method bodies in MultiDeclaratorRegroupDrift.cs (fields, initializers, or attributes) are not applied by hot reload; run uloop compile to pick them up."
                    }));
        }

        /// <summary>
        /// What: a duplicate field syntax key fails open to the file-only outside-body warning
        /// instead of suppressing it or emitting a named declaration warning.
        /// </summary>
        [Test]
        public async Task Drift_DuplicateFieldSyntaxKey_FailsOpenToFileOnlyWarning()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string snapshotSource = onDisk.Replace(
                "        public int PublicSeed = 3;",
                "        public int PublicSeed = 3;\n        public int DupCollide;\n        public int DupCollide;",
                StringComparison.Ordinal);
            string edited = onDisk.Replace(
                "        public int PublicSeed = 3;",
                "        public int PublicSeed = 99;",
                StringComparison.Ordinal);
            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("DupFieldKeyFailOpen.cs", edited),
                HostProjectRelativePath,
                snapshotSource: snapshotSource);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(
                result.Output.declarationDriftWarnings,
                Is.EqualTo(
                    new[]
                    {
                        "Edits outside method bodies in DupFieldKeyFailOpen.cs (fields, initializers, or attributes) are not applied by hot reload; run uloop compile to pick them up."
                    }));
        }

        /// <summary>
        /// What: a readonly added field can still be read through GetOrInit.
        /// </summary>
        [Test]
        public async Task Rewrite_ReadonlyAddedField_ReadsThroughStore()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = WithHostMembers(onDisk, "public readonly int AddedReadOnly = 5;");
            edited = edited.Replace(
                ExistingCallerOriginal,
                "        public int ExistingCaller(int value)\n        {\n"
                + "            return AddedReadOnly + value;\n        }",
                StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("AddedFieldReadonly.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);

            TransformWorkerEntryDto caller = FindEntry(result, nameof(HotReloadAddedMemberHost.ExistingCaller));
            Assert.That(caller, Is.Not.Null);
            string slice = SliceShimMethod(result.Output.shimSource, caller.shimMethodName);
            Assert.That(slice, Does.Contain("GetOrInit<"));
            Assert.That(slice, Does.Contain("static () =>"));
            Assert.That(result.Output.hasAddedFieldRewrites, Is.True);
        }

        /// <summary>
        /// What: a [SerializeField] added field still rewrites to the store and emits an
        /// Inspector/serialization warning.
        /// </summary>
        [Test]
        public async Task Warning_SerializeField_StillRewrites()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = WithHostMembers(onDisk, "[SerializeField] public int AddedSerialized;");
            edited = edited.Replace(
                ExistingCallerOriginal,
                "        public int ExistingCaller(int value)\n        {\n"
                + "            AddedSerialized = value;\n            return AddedSerialized;\n        }",
                StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("AddedFieldSerialize.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);

            TransformWorkerEntryDto caller = FindEntry(result, nameof(HotReloadAddedMemberHost.ExistingCaller));
            Assert.That(caller, Is.Not.Null);
            string slice = SliceShimMethod(result.Output.shimSource, caller.shimMethodName);
            Assert.That(slice, Does.Contain("HotReloadAddedFieldStore"));
            Assert.That(result.Output.hasAddedFieldRewrites, Is.True);

            bool foundWarning = false;
            foreach (string warning in result.Output.declarationDriftWarnings)
            {
                if (warning != null
                    && warning.Contains("AddedSerialized")
                    && warning.Contains("Inspector"))
                {
                    foundWarning = true;
                }
            }

            Assert.That(foundWarning, Is.True, "SerializeField added fields must warn about Inspector visibility.");
        }

        /// <summary>
        /// What: changing a compiled field to an incompatible type skips the reader and writer
        /// with FieldTypeChanged, while a method that does not touch the field still applies.
        /// </summary>
        [Test]
        public async Task Skip_FieldTypeChangedIncompatible_SkipsReaderAndWriter_LeavesUntouchedMethod()
        {
            await AssertCompiledFieldDeclarationChangeSkipsTouchingMethodsAsync(
                "public string PublicSeed = \"x\";",
                "FieldTypeChangedIncompatible.cs",
                FieldTypeChangedReason);
        }

        /// <summary>
        /// What: changing a compiled int field to long also skips readers and writers, so the
        /// implicit conversion cannot apply against the old compiled storage.
        /// </summary>
        [Test]
        public async Task Skip_FieldTypeChangedIntToLong_SkipsReaderAndWriter_LeavesUntouchedMethod()
        {
            await AssertCompiledFieldDeclarationChangeSkipsTouchingMethodsAsync(
                "public long PublicSeed = 3;",
                "FieldTypeChangedIntToLong.cs",
                FieldTypeChangedReason);
        }

        /// <summary>
        /// What: changing a compiled instance field to static skips readers and writers with
        /// FieldModifiersChanged and does not emit the added-field Inspector warning.
        /// </summary>
        [Test]
        public async Task Skip_FieldModifiersChangedToStatic_SkipsReaderAndWriter_LeavesUntouchedMethod()
        {
            await AssertCompiledFieldDeclarationChangeSkipsTouchingMethodsAsync(
                "[SerializeField] public static int PublicSeed = 3;",
                "FieldModifiersChangedToStatic.cs",
                FieldModifiersChangedReason);
        }

        /// <summary>
        /// What: changing a compiled instance field to const skips readers and writers with
        /// FieldModifiersChanged, covering the implicit static bit, without an Inspector warning.
        /// </summary>
        [Test]
        public async Task Skip_FieldModifiersChangedToConst_SkipsReaderAndWriter_LeavesUntouchedMethod()
        {
            await AssertCompiledFieldDeclarationChangeSkipsTouchingMethodsAsync(
                "[SerializeField] public const int PublicSeed = 3;",
                "FieldModifiersChangedToConst.cs",
                FieldModifiersChangedReason);
        }

        /// <summary>
        /// What: a [SerializeField] compiled field whose type changes is skipped with
        /// FieldTypeChanged and does not emit the added-field Inspector warning.
        /// </summary>
        [Test]
        public async Task Skip_FieldTypeChangedSerializeField_SkipsWithoutInspectorWarning()
        {
            await AssertCompiledFieldDeclarationChangeSkipsTouchingMethodsAsync(
                "[SerializeField] public long PublicSeed = 3;",
                "FieldTypeChangedSerializeField.cs",
                FieldTypeChangedReason);
        }

        /// <summary>
        /// What: rewriting a compiled auto-property Hp to a field skips readers and writers
        /// instead of duplicating storage in the added-field side table.
        /// </summary>
        [Test]
        public async Task Skip_PropertyRewrittenAsField_SkipsReaderAndWriter_LeavesUntouchedMethod()
        {
            await AssertCompiledMemberKindChangeSkipsTouchingMethodsAsync(
                "        public int Hp { get; set; }",
                "[SerializeField] public int Hp;",
                "Hp",
                "PropertyRewrittenAsField.cs");
        }

        /// <summary>
        /// What: rewriting a compiled event ScoreChanged to a field skips readers and writers
        /// instead of duplicating storage in the added-field side table.
        /// </summary>
        [Test]
        public async Task Skip_EventRewrittenAsField_SkipsReaderAndWriter_LeavesUntouchedMethod()
        {
            await AssertCompiledMemberKindChangeSkipsTouchingMethodsAsync(
                "        public event Action ScoreChanged;",
                "[SerializeField] public int ScoreChanged;",
                "ScoreChanged",
                "EventRewrittenAsField.cs");
        }

        /// <summary>
        /// What: replacing compiled property Hp with a field warns even when no method body
        /// is edited (FB9 E-1 kind change).
        /// </summary>
        [Test]
        public async Task Warn_PropertyRewrittenAsField_WithoutTouchingBodies()
        {
            await AssertCompiledPropertyOrEventWarningAsync(
                "        public int Hp { get; set; }",
                "        public int Hp;",
                CompiledPropertyWarningFormat,
                "Hp",
                "WarnPropertyRewrittenAsField.cs");
        }

        /// <summary>
        /// What: replacing compiled event ScoreChanged with a field warns even when no method
        /// body is edited (FB11 E kind change).
        /// </summary>
        [Test]
        public async Task Warn_EventRewrittenAsField_WithoutTouchingBodies()
        {
            await AssertCompiledPropertyOrEventWarningAsync(
                "        public event Action ScoreChanged;",
                "        public int ScoreChanged;",
                CompiledEventWarningFormat,
                "ScoreChanged",
                "WarnEventRewrittenAsField.cs");
        }

        /// <summary>
        /// What: rewriting compiled property Hp to a field emits the kind-change warning and
        /// does not also emit the generic outside-body warning.
        /// </summary>
        [Test]
        public async Task Drift_PropertyKindChangeOnly_DoesNotEmitOutsideBodyWarning()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = onDisk.Replace(
                "        public int Hp { get; set; }",
                "        public int Hp;",
                StringComparison.Ordinal);
            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("PropertyKindChangeOnlyNoOutsideBody.cs", edited),
                FieldKindChangeProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(
                result.Output.declarationDriftWarnings,
                Is.EqualTo(
                    new[]
                    {
                        "Compiled property 'io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadFieldKindChangeFixture.Hp' was removed or redeclared as a different member kind in the edited source; the compiled member stays until 'uloop compile'."
                    }));
        }

        /// <summary>
        /// What: rewriting compiled event ScoreChanged to a field emits the kind-change warning
        /// and does not also emit the generic outside-body warning.
        /// </summary>
        [Test]
        public async Task Drift_EventKindChangeOnly_DoesNotEmitOutsideBodyWarning()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = onDisk.Replace(
                "        public event Action ScoreChanged;",
                "        public int ScoreChanged;",
                StringComparison.Ordinal);
            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("EventKindChangeOnlyNoOutsideBody.cs", edited),
                FieldKindChangeProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(
                result.Output.declarationDriftWarnings,
                Is.EqualTo(
                    new[]
                    {
                        "Compiled event 'io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadFieldKindChangeFixture.ScoreChanged' was removed or redeclared as a different member kind in the edited source; the compiled member stays until 'uloop compile'."
                    }));
        }

        /// <summary>
        /// What: a property-to-field kind change in the same file as an initializer edit keeps
        /// the named outside-body warning for that initializer.
        /// </summary>
        [Test]
        public async Task Drift_PropertyKindChangeWithInitializerEdit_StillEmitsNamedOutsideBodyWarning()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = onDisk.Replace(
                "        public int Hp { get; set; }",
                "        public int Hp;",
                StringComparison.Ordinal);
            edited = edited.Replace(
                "        public int PublicSeed = 3;",
                "        public int PublicSeed = 99;",
                StringComparison.Ordinal);
            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("PropertyKindChangeWithInitializer.cs", edited),
                FieldKindChangeProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(
                result.Output.declarationDriftWarnings,
                Is.EqualTo(
                    new[]
                    {
                        "Compiled property 'io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadFieldKindChangeFixture.Hp' was removed or redeclared as a different member kind in the edited source; the compiled member stays until 'uloop compile'.",
                        "Edits outside method bodies in PropertyKindChangeWithInitializer.cs (field initializer: PublicSeed) are not applied by hot reload; run uloop compile to pick them up."
                    }));
        }

        /// <summary>
        /// What: an event-to-field kind change in the same file as an initializer edit keeps
        /// the named outside-body warning for that initializer.
        /// </summary>
        [Test]
        public async Task Drift_EventKindChangeWithInitializerEdit_StillEmitsNamedOutsideBodyWarning()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = onDisk.Replace(
                "        public event Action ScoreChanged;",
                "        public int ScoreChanged;",
                StringComparison.Ordinal);
            edited = edited.Replace(
                "        public int PublicSeed = 3;",
                "        public int PublicSeed = 99;",
                StringComparison.Ordinal);
            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("EventKindChangeWithInitializer.cs", edited),
                FieldKindChangeProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(
                result.Output.declarationDriftWarnings,
                Is.EqualTo(
                    new[]
                    {
                        "Compiled event 'io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadFieldKindChangeFixture.ScoreChanged' was removed or redeclared as a different member kind in the edited source; the compiled member stays until 'uloop compile'.",
                        "Edits outside method bodies in EventKindChangeWithInitializer.cs (field initializer: PublicSeed) are not applied by hot reload; run uloop compile to pick them up."
                    }));
        }

        /// <summary>
        /// What: deleting compiled property Hp without touching a method body still names it
        /// in the compiled property-or-event warning.
        /// </summary>
        [Test]
        public async Task Warn_CompiledPropertyRemoved_WithoutTouchingBodies()
        {
            await AssertCompiledPropertyOrEventWarningAsync(
                "        public int Hp { get; set; }\n",
                string.Empty,
                CompiledPropertyWarningFormat,
                "Hp",
                "WarnPropertyRemoved.cs");
        }

        /// <summary>
        /// What: deleting compiled event ScoreChanged without touching a method body still
        /// names it in the compiled event warning. Declaration deletion leaves a dangling
        /// reference in ClearScoreChanged; a binding-error skip row from that leftover
        /// call is noise, not the behavior under test.
        /// </summary>
        [Test]
        public async Task Warn_CompiledEventRemoved_WithoutTouchingBodies()
        {
            await AssertCompiledPropertyOrEventWarningAsync(
                "        public event Action ScoreChanged;\n",
                string.Empty,
                CompiledEventWarningFormat,
                "ScoreChanged",
                "WarnEventRemoved.cs");
        }

        /// <summary>
        /// What: adding an event does not emit the compiled property-or-event warning and
        /// does not add an individual Skipped row for that event (SKILL discrepancy kept).
        /// </summary>
        [Test]
        public async Task Classify_AddedEvent_DoesNotWarnOrSkipTheEvent()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = onDisk.Replace(
                "        public event Action ScoreChanged;",
                "        public event Action ScoreChanged;\n        public event Action ExtraScore;",
                StringComparison.Ordinal);
            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("AddedEventNoCompiledKindWarning.cs", edited),
                FieldKindChangeProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            AssertHasNoCompiledKindChangeWarningForMember(result, "ExtraScore");
            AssertHasNoSkipContaining(result, "ExtraScore");
        }

        /// <summary>
        /// What: adding a property does not emit the compiled property warning and does
        /// not add an individual Skipped row for that property (SKILL discrepancy kept).
        /// </summary>
        [Test]
        public async Task Classify_AddedProperty_DoesNotWarnOrSkipTheProperty()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = onDisk.Replace(
                "        public int Hp { get; set; }",
                "        public int Hp { get; set; }\n        public int ExtraHp { get; set; }",
                StringComparison.Ordinal);
            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("AddedPropertyNoCompiledKindWarning.cs", edited),
                FieldKindChangeProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            AssertHasNoCompiledKindChangeWarningForMember(result, "ExtraHp");
            AssertHasNoSkipContaining(result, "ExtraHp");
        }

        /// <summary>
        /// What: a property declared only on the other file of a partial type does not
        /// produce a compiled-property-removed warning when this file is hot-reloaded.
        /// </summary>
        [Test]
        public async Task Warn_PartialOtherFilePropertyOrEvent_DoesNotWarnAsRemoved()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = onDisk.Replace(
                "        public int PartialKept()\n        {\n            return 1;\n        }",
                "        public int PartialKept()\n        {\n            return 11;\n        }",
                StringComparison.Ordinal);
            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("PartialOtherFilePropertyNoFalseWarning.cs", edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            AssertHasNoCompiledKindChangeWarningForMember(result, "PartialOtherProperty");
            AssertHasNoCompiledKindChangeWarningForMember(result, "PartialOtherEvent");
        }

        /// <summary>
        /// What: keeping a compiled explicit-interface property declaration while editing
        /// another method does not emit a compiled-property-removed warning.
        /// </summary>
        [Test]
        public async Task Warn_ExplicitInterfacePropertyKept_DoesNotWarnAsRemoved()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = onDisk.Replace(
                FieldKindChangeUntouchedOriginal,
                "        public int UntouchedKind()\n        {\n            return 2;\n        }",
                StringComparison.Ordinal);
            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited("ExplicitInterfacePropertyKept.cs", edited),
                FieldKindChangeProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            AssertHasNoCompiledKindChangeWarningForMember(result, "ExplicitHp");
        }

        private static async Task AssertCompiledFieldDeclarationChangeSkipsTouchingMethodsAsync(
            string replacementFieldDeclaration,
            string editedFileName,
            string expectedSkipReason)
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = onDisk.Replace(
                "        public int PublicSeed = 3;",
                "        " + replacementFieldDeclaration,
                StringComparison.Ordinal);
            edited = edited.Replace(
                ExistingCallerOriginal,
                "        public int ExistingCaller(int value)\n        {\n"
                + "            return PublicSeed.GetHashCode() + value;\n        }",
                StringComparison.Ordinal);
            edited = edited.Replace(
                ExistingFailOriginal,
                "        public int ExistingFail(int value)\n        {\n"
                + "            PublicSeed = value;\n            return value;\n        }",
                StringComparison.Ordinal);
            edited = edited.Replace(
                ExistingValueOriginal,
                "        public int ExistingValue()\n        {\n            return 2;\n        }",
                StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited(editedFileName, edited),
                HostProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            AssertHasSkip(result, nameof(HotReloadAddedMemberHost.ExistingCaller), expectedSkipReason);
            AssertHasSkip(result, nameof(HotReloadAddedMemberHost.ExistingFail), expectedSkipReason);
            Assert.That(FindEntry(result, nameof(HotReloadAddedMemberHost.ExistingValue)), Is.Not.Null);
            Assert.That(FindEntry(result, nameof(HotReloadAddedMemberHost.ExistingCaller)), Is.Null);
            Assert.That(FindEntry(result, nameof(HotReloadAddedMemberHost.ExistingFail)), Is.Null);
            Assert.That(result.Output.hasAddedFieldRewrites, Is.False);
            AssertHasNoAddedFieldSerializeWarning(result);
        }

        private static async Task AssertCompiledMemberKindChangeSkipsTouchingMethodsAsync(
            string compiledMemberDeclaration,
            string replacementFieldDeclaration,
            string fieldName,
            string editedFileName)
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            Assert.That(onDisk, Does.Contain(compiledMemberDeclaration));
            string edited = onDisk.Replace(
                compiledMemberDeclaration,
                "        " + replacementFieldDeclaration,
                StringComparison.Ordinal);
            edited = edited.Replace(
                FieldKindChangeReadOriginal,
                "        public int ReadKind(int value)\n        {\n"
                + "            return " + fieldName + " + value;\n        }",
                StringComparison.Ordinal);
            edited = edited.Replace(
                FieldKindChangeWriteOriginal,
                "        public int WriteKind(int value)\n        {\n"
                + "            " + fieldName + " = value;\n            return value;\n        }",
                StringComparison.Ordinal);
            edited = edited.Replace(
                FieldKindChangeUntouchedOriginal,
                "        public int UntouchedKind()\n        {\n            return 2;\n        }",
                StringComparison.Ordinal);

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited(editedFileName, edited),
                FieldKindChangeProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            string expectedSkipReason = string.Format(MemberKindChangedReasonFormat, fieldName);
            AssertHasSkip(result, nameof(HotReloadFieldKindChangeFixture.ReadKind), expectedSkipReason);
            AssertHasSkip(result, nameof(HotReloadFieldKindChangeFixture.WriteKind), expectedSkipReason);
            Assert.That(FindEntry(result, nameof(HotReloadFieldKindChangeFixture.UntouchedKind)), Is.Not.Null);
            Assert.That(FindEntry(result, nameof(HotReloadFieldKindChangeFixture.ReadKind)), Is.Null);
            Assert.That(FindEntry(result, nameof(HotReloadFieldKindChangeFixture.WriteKind)), Is.Null);
            Assert.That(result.Output.hasAddedFieldRewrites, Is.False);
            AssertHasNoAddedFieldSerializeWarning(result);
        }

        private static async Task AssertCompiledPropertyOrEventWarningAsync(
            string compiledMemberDeclaration,
            string replacementDeclaration,
            string warningFormat,
            string memberName,
            string editedFileName)
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            Assert.That(onDisk, Does.Contain(compiledMemberDeclaration));
            string edited = onDisk.Replace(
                compiledMemberDeclaration,
                replacementDeclaration,
                StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk));

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                WriteEdited(editedFileName, edited),
                FieldKindChangeProjectRelativePath,
                snapshotSource: onDisk);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            string expectedWarning = string.Format(
                warningFormat,
                typeof(HotReloadFieldKindChangeFixture).FullName + "." + memberName);
            AssertHasDeclarationDriftWarning(result, expectedWarning);
        }

        private static void AssertHasDeclarationDriftWarning(
            TransformWorkerClientResult result,
            string expectedWarning)
        {
            string[] warnings = result.Output.declarationDriftWarnings ?? Array.Empty<string>();
            foreach (string warning in warnings)
            {
                if (warning == expectedWarning)
                {
                    return;
                }
            }

            Assert.Fail(
                "Expected declaration drift warning '" + expectedWarning + "'. Warnings="
                + string.Join("\n", warnings));
        }

        private static void AssertHasNoCompiledKindChangeWarningForMember(
            TransformWorkerClientResult result,
            string memberName)
        {
            string[] warnings = result.Output.declarationDriftWarnings ?? Array.Empty<string>();
            foreach (string warning in warnings)
            {
                if (warning == null)
                {
                    continue;
                }

                bool isKindChange = warning.StartsWith("Compiled property '", StringComparison.Ordinal)
                    || warning.StartsWith("Compiled event '", StringComparison.Ordinal);
                if (isKindChange && warning.Contains(memberName, StringComparison.Ordinal))
                {
                    Assert.Fail(
                        "Member '" + memberName + "' must not emit a compiled kind-change warning. Warnings="
                        + string.Join("\n", warnings));
                }
            }
        }

        private static void AssertHasNoSkipContaining(
            TransformWorkerClientResult result,
            string fragment)
        {
            foreach (TransformWorkerSkippedDto skipped in result.Output.skipped)
            {
                bool methodHit = skipped.method != null
                    && skipped.method.Contains(fragment, StringComparison.Ordinal);
                bool reasonHit = skipped.reason != null
                    && skipped.reason.Contains(fragment, StringComparison.Ordinal);
                if (methodHit || reasonHit)
                {
                    Assert.Fail(
                        "Fragment '" + fragment + "' must not appear in a Skipped row. Skipped="
                        + FormatSkipped(result.Output.skipped));
                }
            }
        }

        private static async Task<TransformWorkerClientResult> RunWorkerOnSourceAsync(
            string sourcePath,
            string projectRelativePath,
            string snapshotSource = null)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string targetDllPath = Path.Combine(
                projectRoot,
                "Library",
                "ScriptAssemblies",
                TestAssemblyName + ".dll");
            Assert.That(File.Exists(targetDllPath), Is.True, "Test assembly dll missing: " + targetDllPath);

            UnityEditor.Compilation.Assembly compilationAssembly = null;
            foreach (UnityEditor.Compilation.Assembly assembly in CompilationPipeline.GetAssemblies())
            {
                if (assembly.name == TestAssemblyName)
                {
                    compilationAssembly = assembly;
                    break;
                }
            }

            Assert.That(compilationAssembly, Is.Not.Null, "CompilationPipeline assembly not found.");

            TransformWorkerInputDto input = new TransformWorkerInputDto
            {
                sourcePath = sourcePath,
                defines = compilationAssembly.defines ?? Array.Empty<string>(),
                referencePaths = BuildAbsoluteReferencePaths(
                    compilationAssembly.allReferences,
                    targetDllPath),
                targetTypesAssemblyPath = targetDllPath,
                snapshotSource = snapshotSource,
                projectRelativePath = projectRelativePath,
                assemblySourcePaths = BuildAbsoluteAssemblySourcePaths(compilationAssembly.sourceFiles),
                excludedMethodKeys = Array.Empty<string>(),
                excludedAddedMethodKeys = Array.Empty<string>()
            };

            return await TransformWorkerClient.RunAsync(input, CancellationToken.None);
        }

        private static string[] BuildAbsoluteReferencePaths(string[] allReferences, string targetDllPath)
        {
            List<string> paths = new List<string>();
            if (allReferences != null)
            {
                foreach (string reference in allReferences)
                {
                    if (string.IsNullOrEmpty(reference) || !File.Exists(reference))
                    {
                        continue;
                    }

                    paths.Add(Path.GetFullPath(reference));
                }
            }

            string fullTarget = Path.GetFullPath(targetDllPath);
            bool hasTarget = false;
            foreach (string path in paths)
            {
                if (string.Equals(path, fullTarget, StringComparison.OrdinalIgnoreCase))
                {
                    hasTarget = true;
                    break;
                }
            }

            if (!hasTarget)
            {
                paths.Add(fullTarget);
            }

            return paths.ToArray();
        }

        private static string[] BuildAbsoluteAssemblySourcePaths(string[] sourceFiles)
        {
            if (sourceFiles == null || sourceFiles.Length == 0)
            {
                return Array.Empty<string>();
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string[] paths = new string[sourceFiles.Length];
            for (int index = 0; index < sourceFiles.Length; index++)
            {
                string normalizedRelativePath = sourceFiles[index].Replace('\\', '/');
                string absoluteSourcePath = Path.Combine(
                    projectRoot,
                    normalizedRelativePath.Replace('/', Path.DirectorySeparatorChar));
                paths[index] = Path.GetFullPath(absoluteSourcePath);
            }

            return paths;
        }

        private static string WithHostMembers(string onDisk, string extraMembers)
        {
            Assert.That(onDisk, Does.Contain(HostCloseMarker));
            return onDisk.Replace(
                HostCloseMarker,
                "        public int ReadPrivateSeed()\n        {\n            return _privateSeed;\n        }\n\n        "
                + extraMembers
                + "\n    }",
                StringComparison.Ordinal);
        }

        private static TransformWorkerEntryDto FindEntry(TransformWorkerClientResult result, string methodName)
        {
            foreach (TransformWorkerEntryDto entry in result.Output.entries)
            {
                if (entry.methodName == methodName)
                {
                    return entry;
                }
            }

            return null;
        }

        private static void AssertHasSkip(
            TransformWorkerClientResult result,
            string methodNameFragment,
            string reasonFragment)
        {
            foreach (TransformWorkerSkippedDto skipped in result.Output.skipped)
            {
                if (skipped.method != null
                    && skipped.method.Contains(methodNameFragment)
                    && skipped.reason != null
                    && skipped.reason.Contains(reasonFragment))
                {
                    return;
                }
            }

            Assert.Fail(
                "Expected skip for '" + methodNameFragment + "' with reason containing '"
                + reasonFragment + "'. Skipped="
                + FormatSkipped(result.Output.skipped));
        }

        private static void AssertHasNoAddedFieldSerializeWarning(TransformWorkerClientResult result)
        {
            foreach (string warning in result.Output.declarationDriftWarnings)
            {
                if (warning != null && warning.Contains("will not appear in the Inspector"))
                {
                    Assert.Fail(
                        "Declaration-changed compiled fields must not emit the added-field "
                        + "Inspector warning. Warnings="
                        + string.Join("\n", result.Output.declarationDriftWarnings));
                }
            }
        }

        private static string FormatSkipped(TransformWorkerSkippedDto[] skipped)
        {
            if (skipped == null || skipped.Length == 0)
            {
                return "(none)";
            }

            List<string> rows = new List<string>();
            foreach (TransformWorkerSkippedDto entry in skipped)
            {
                rows.Add(entry.method + " :: " + entry.reason);
            }

            return string.Join("\n", rows);
        }

        private static string SliceShimMethod(string shimSource, string shimMethodName)
        {
            int nameIndex = shimSource.IndexOf(shimMethodName, StringComparison.Ordinal);
            Assert.That(nameIndex, Is.GreaterThanOrEqualTo(0), "Shim method missing: " + shimMethodName);
            int declarationStart = shimSource.LastIndexOf("public static", nameIndex, StringComparison.Ordinal);
            int openBrace = shimSource.IndexOf('{', nameIndex);
            Assert.That(openBrace, Is.GreaterThan(0));
            int depth = 0;
            for (int index = openBrace; index < shimSource.Length; index++)
            {
                if (shimSource[index] == '{')
                {
                    depth++;
                }
                else if (shimSource[index] == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        return shimSource.Substring(declarationStart, index - declarationStart + 1);
                    }
                }
            }

            Assert.Fail("Unbalanced shim method: " + shimMethodName);
            return string.Empty;
        }

        private static string WriteEdited(string fileName, string contents)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string directory = Path.Combine(projectRoot, HotReloadConstants.TestSourcesRelativeDirectory);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, fileName);
            File.WriteAllText(path, contents);
            return path;
        }

        private static string ResolveHostPath()
        {
            string path = Path.Combine(
                Application.dataPath,
                "Tests",
                "Editor",
                "HotReload",
                "HotReloadAddedMemberHost.cs");
            Assert.That(File.Exists(path), Is.True, "Added-member host source missing: " + path);
            return Path.GetFullPath(path);
        }
    }
}
