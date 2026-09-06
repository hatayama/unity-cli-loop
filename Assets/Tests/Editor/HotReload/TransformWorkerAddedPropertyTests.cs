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
    /// Worker coverage for added properties that are emitted through added-method shims.
    /// </summary>
    public class TransformWorkerAddedPropertyTests
    {
        private const string TestAssemblyName = "UnityCLILoop.Tests.Editor.HotReload";
        private const string HostProjectRelativePath =
            "Assets/Tests/Editor/HotReload/HotReloadAddedMemberHost.cs";

        private const string HostCloseMarker =
            "        public int ReadPrivateSeed()\n        {\n            return _privateSeed;\n        }\n    }";

        private const string ExistingCallerOriginal =
            "        public int ExistingCaller(int value)\n        {\n            return value;\n        }";

        private const string VirtualOrAbstractReason =
            "Added virtual, override, abstract, or interface properties are skipped; the compiled type has no vtable slot. "
            + "Run 'uloop compile' to add them.";

        private const string InitAccessorReason =
            "Added properties with init accessors are skipped; the shim cannot preserve initialization-only assignment. "
            + "Run 'uloop compile' to add them.";

        private const string CompoundAssignmentReason =
            "Compound assignment, increment, and decrement of an added property are skipped; the accessor shim cannot preserve the operation. "
            + "Run 'uloop compile' to add it.";

        private const string ConsumedWriteReason =
            "The value of an assignment to an added property is consumed; the setter shim returns void. "
            + "Run 'uloop compile' to add it.";

        private const string NameofReferenceReason =
            "References to added properties inside nameof are skipped; the member does not exist in the compiled assembly. "
            + "Run 'uloop compile' to add it.";

        private const string ObjectInitializerReason =
            "Object initializers that assign added properties are skipped; the setter shim cannot rewrite the initializer. "
            + "Run 'uloop compile' to add it.";

        private const string RefOutInReason =
            "Added properties cannot be passed by ref, out, or in. Run 'uloop compile' to add them.";

        private const string SetOnlyReason =
            "Added properties with only a setter are skipped; the shim requires a getter identity. "
            + "Run 'uloop compile' to add them.";

        private const string StructHostReason =
            "Added properties on struct types are skipped; the shim requires a reference-type instance. "
            + "Run 'uloop compile' to add them.";

        private const string CompiledMemberKindChangedReason =
            "Property 'PublicSeed' is declared as a field or an event in the compiled assembly. "
            + "Run 'uloop compile'.";

        private const string InitializerNotEmittableReason =
            "Added property initializer cannot run in the shim lambda. Run 'uloop compile' to add it.";

        private const string HostStoreKeyPrefix =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadAddedMemberHost::";

        /// <summary>
        /// An expression-bodied property added to a compiled host emits an added getter
        /// and rewrites an edited caller to that getter shim.
        /// </summary>
        [Test]
        public async Task Emit_AddedExpressionBodiedGetter_RegistersAddedMethodEntry()
        {
            TransformWorkerClientResult result = await RunEditedHostAsync(
                "AddedExpressionBodiedGetter.cs",
                "public int Doubled => PublicSeed * 2;",
                "        public int ExistingCaller(int value)\n        {\n"
                + "            return Doubled + value;\n        }");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            TransformWorkerEntryDto getter = FindEntry(result, "get_Doubled");
            TransformWorkerEntryDto caller = FindEntry(result, nameof(HotReloadAddedMemberHost.ExistingCaller));
            Assert.That(getter, Is.Not.Null, FormatSkipped(result.Output.skipped));
            Assert.That(getter.patchKind, Is.EqualTo(HotReloadConstants.PatchKindAddedMethod));
            Assert.That(FindSkipReason(result, "get_Doubled"), Is.Null);
            Assert.That(result.Output.shimSource, Does.Contain(getter.shimMethodName));
            Assert.That(caller, Is.Not.Null, FormatSkipped(result.Output.skipped));
            Assert.That(
                SliceShimMethod(result.Output.shimSource, caller.shimMethodName),
                Does.Contain(getter.shimMethodName));
            Assert.That(caller.calledAddedMethodKeys, Does.Contain(BuildHostMethodKey("get_Doubled")));
        }

        /// <summary>
        /// A property with bodied accessors emits both added accessor entries and rewrites
        /// simple assignment to the setter shim.
        /// </summary>
        [Test]
        public async Task Emit_AddedBodiedSetter_RegistersSetterEntryAndRewritesAssignment()
        {
            TransformWorkerClientResult result = await RunEditedHostAsync(
                "AddedBodiedSetter.cs",
                "public int Stored\n        {\n            get { return _privateSeed; }\n"
                + "            set { _privateSeed = value; }\n        }",
                "        public int ExistingCaller(int value)\n        {\n"
                + "            Stored = value;\n            return Stored;\n        }");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            TransformWorkerEntryDto getter = FindEntry(result, "get_Stored");
            TransformWorkerEntryDto setter = FindEntry(result, "set_Stored");
            TransformWorkerEntryDto caller = FindEntry(result, nameof(HotReloadAddedMemberHost.ExistingCaller));
            Assert.That(getter, Is.Not.Null, FormatSkipped(result.Output.skipped));
            Assert.That(setter, Is.Not.Null, FormatSkipped(result.Output.skipped));
            Assert.That(caller, Is.Not.Null, FormatSkipped(result.Output.skipped));
            string callerShim = SliceShimMethod(result.Output.shimSource, caller.shimMethodName);
            Assert.That(callerShim, Does.Contain(getter.shimMethodName));
            Assert.That(callerShim, Does.Contain(setter.shimMethodName));
            Assert.That(
                SliceShimMethod(result.Output.shimSource, getter.shimMethodName),
                Does.Not.Contain("__uloopInstance._privateSeed"));
            Assert.That(
                SliceShimMethod(result.Output.shimSource, setter.shimMethodName),
                Does.Not.Contain("__uloopInstance._privateSeed"));
        }

        /// <summary>
        /// A static bodied property emits a getter shim without an instance parameter.
        /// </summary>
        [Test]
        public async Task Emit_AddedStaticBodiedGetter_UsesNoInstanceParameter()
        {
            TransformWorkerClientResult result = await RunEditedHostAsync(
                "AddedStaticBodiedGetter.cs",
                "public static int StaticDoubled => 4;",
                "        public int ExistingCaller(int value)\n        {\n"
                + "            return StaticDoubled + value;\n        }");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            TransformWorkerEntryDto getter = FindEntry(result, "get_StaticDoubled");
            Assert.That(getter, Is.Not.Null, FormatSkipped(result.Output.skipped));
            Assert.That(
                result.Output.shimSource,
                Does.Contain("public static int " + getter.shimMethodName + "()"));
        }

        /// <summary>
        /// A virtual added property is rejected with the property-specific vtable reason.
        /// </summary>
        [Test]
        public async Task Skip_AddedVirtualProperty_SkipsWithVirtualReason()
        {
            TransformWorkerClientResult result = await RunEditedHostAsync(
                "AddedVirtualProperty.cs",
                "public virtual int AddedVirtual => 1;");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(FindEntry(result, "get_AddedVirtual"), Is.Null);
            Assert.That(FindSkipReason(result, "get_AddedVirtual"), Is.EqualTo(VirtualOrAbstractReason));
        }

        /// <summary>
        /// An added init accessor is rejected with the property-specific initialization reason.
        /// </summary>
        [Test]
        public async Task Skip_AddedInitAccessor_SkipsWithInitReason()
        {
            TransformWorkerClientResult result = await RunEditedHostAsync(
                "AddedInitAccessor.cs",
                "public int AddedInit { get => 1; init { } }");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(FindEntry(result, "get_AddedInit"), Is.Null);
            Assert.That(FindSkipReason(result, "get_AddedInit"), Is.EqualTo(InitAccessorReason));
        }

        /// <summary>
        /// Compound assignment to an added property skips the edited caller before shim compilation.
        /// </summary>
        [Test]
        public async Task Skip_AddedPropertyCompoundAssignment_SkipsBodyWithPreciseReason()
        {
            TransformWorkerClientResult result = await RunEditedHostAsync(
                "AddedPropertyCompoundAssignment.cs",
                "public int Stored { get { return _privateSeed; } set { _privateSeed = value; } }",
                "        public int ExistingCaller(int value)\n        {\n"
                + "            Stored += value;\n            return value;\n        }");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(
                FindSkipReason(result, nameof(HotReloadAddedMemberHost.ExistingCaller)),
                Is.EqualTo(CompoundAssignmentReason));
            Assert.That(FindEntry(result, "get_Stored"), Is.Not.Null);
        }

        /// <summary>
        /// A consumed simple assignment to an added property skips the caller because a setter returns void.
        /// </summary>
        [Test]
        public async Task Skip_AddedPropertyConsumedWrite_SkipsBody()
        {
            TransformWorkerClientResult result = await RunEditedHostAsync(
                "AddedPropertyConsumedWrite.cs",
                "public int Stored { get { return _privateSeed; } set { _privateSeed = value; } }",
                "        public int ExistingCaller(int value)\n        {\n"
                + "            int stored = (Stored = value);\n            return stored;\n        }");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(
                FindSkipReason(result, nameof(HotReloadAddedMemberHost.ExistingCaller)),
                Is.EqualTo(ConsumedWriteReason));
            Assert.That(FindEntry(result, "get_Stored"), Is.Not.Null);
        }

        /// <summary>
        /// Incrementing an added property skips the caller before shim compilation.
        /// </summary>
        [Test]
        public async Task Skip_AddedPropertyIncrement_SkipsBody()
        {
            TransformWorkerClientResult result = await RunEditedHostAsync(
                "AddedPropertyIncrement.cs",
                "public int Stored { get { return _privateSeed; } set { _privateSeed = value; } }",
                "        public int ExistingCaller(int value)\n        {\n"
                + "            Stored++;\n            return value;\n        }");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(
                FindSkipReason(result, nameof(HotReloadAddedMemberHost.ExistingCaller)),
                Is.EqualTo(CompoundAssignmentReason));
            Assert.That(FindEntry(result, "get_Stored"), Is.Not.Null);
        }

        /// <summary>
        /// Nameof of an added property skips the caller instead of leaving an unresolvable member reference.
        /// </summary>
        [Test]
        public async Task Skip_AddedPropertyNameof_SkipsBody()
        {
            TransformWorkerClientResult result = await RunEditedHostAsync(
                "AddedPropertyNameof.cs",
                "public int Stored { get { return _privateSeed; } set { _privateSeed = value; } }",
                "        public int ExistingCaller(int value)\n        {\n"
                + "            return nameof(Stored).Length + value;\n        }");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(FindSkipReason(result, nameof(HotReloadAddedMemberHost.ExistingCaller)), Is.EqualTo(NameofReferenceReason));
        }

        /// <summary>
        /// An object initializer assigning an added property skips the caller before shim compilation.
        /// </summary>
        [Test]
        public async Task Skip_AddedPropertyObjectInitializer_SkipsBody()
        {
            TransformWorkerClientResult result = await RunEditedHostAsync(
                "AddedPropertyObjectInitializer.cs",
                "public int Stored { get { return _privateSeed; } set { _privateSeed = value; } }",
                "        public int ExistingCaller(int value)\n        {\n"
                + "            return new HotReloadAddedMemberHost { Stored = value }.ReadPrivateSeed();\n        }");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(FindSkipReason(result, nameof(HotReloadAddedMemberHost.ExistingCaller)), Is.EqualTo(ObjectInitializerReason));
        }

        /// <summary>
        /// Passing an added property through an in argument skips the caller.
        /// </summary>
        [Test]
        public async Task Skip_AddedPropertyInArgument_SkipsBody()
        {
            TransformWorkerClientResult result = await RunEditedHostAsync(
                "AddedPropertyInArgument.cs",
                "public int Stored { get { return _privateSeed; } set { _privateSeed = value; } }\n\n"
                + "        private static int Consume(in int input) { return input; }",
                "        public int ExistingCaller(int value)\n        {\n"
                + "            return Consume(in Stored) + value;\n        }");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(FindSkipReason(result, nameof(HotReloadAddedMemberHost.ExistingCaller)), Is.EqualTo(RefOutInReason));
        }

        /// <summary>
        /// A set-only property is skipped without attempting to construct a missing getter binding.
        /// </summary>
        [Test]
        public async Task Skip_AddedSetOnlyProperty_SkipsWithSetOnlyReason()
        {
            TransformWorkerClientResult result = await RunEditedHostAsync(
                "AddedSetOnlyProperty.cs",
                "public int Sink { set => _privateSeed = value; }");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(FindEntry(result, "set_Sink"), Is.Null);
            Assert.That(FindSkipReason(result, "set_Sink"), Is.EqualTo(SetOnlyReason));
        }

        /// <summary>
        /// A same-named local remains a local and does not trigger added-property shape guards.
        /// </summary>
        [Test]
        public async Task Keep_SameNamedLocal_EmitsCaller()
        {
            TransformWorkerClientResult result = await RunEditedHostAsync(
                "SameNamedLocal.cs",
                "public int Stored { get { return _privateSeed; } set { _privateSeed = value; } }",
                "        public int ExistingCaller(int value)\n        {\n"
                + "            int Stored = value;\n            Stored++;\n            return Stored;\n        }");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(FindEntry(result, nameof(HotReloadAddedMemberHost.ExistingCaller)), Is.Not.Null);
            Assert.That(FindSkipReason(result, nameof(HotReloadAddedMemberHost.ExistingCaller)), Is.Null);
        }

        /// <summary>
        /// A base receiver in an added accessor is skipped before a static shim is emitted.
        /// </summary>
        [Test]
        public async Task Skip_AddedPropertyBaseReceiver_SkipsAccessor()
        {
            TransformWorkerClientResult result = await RunEditedHostAsync(
                "AddedPropertyBaseReceiver.cs",
                "public int BaseValue => base.GetHashCode();");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(FindEntry(result, "get_BaseValue"), Is.Null);
            Assert.That(
                FindSkipReason(result, "get_BaseValue"),
                Does.Contain("base. members are skipped"));
        }

        /// <summary>
        /// An expression-bodied added setter preserves its original source line in the emitted shim.
        /// </summary>
        [Test]
        public async Task Emit_AddedExpressionBodiedSetter_CarriesLineDirective()
        {
            TransformWorkerClientResult result = await RunEditedHostAsync(
                "AddedExpressionBodiedSetterLine.cs",
                "public int Stored { get => _privateSeed; set => _privateSeed = value; }");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            TransformWorkerEntryDto setter = FindEntry(result, "set_Stored");
            Assert.That(setter, Is.Not.Null, FormatSkipped(result.Output.skipped));
            string setterShim = SliceSetterShimBeforeTrailingLineDefault(
                result.Output.shimSource,
                setter.shimMethodName);
            Assert.That(setterShim, Does.Match("#line [0-9]+ \""));
        }

        /// <summary>
        /// Excluding an added accessor removes its shim and skips callers that depend on it.
        /// </summary>
        [Test]
        public async Task Isolation_ExcludedAddedAccessorKey_DropsAccessorShimAndSkipsCaller()
        {
            TransformWorkerClientResult result = await RunEditedHostAsync(
                "ExcludedAddedPropertyAccessor.cs",
                "public int Doubled => PublicSeed * 2;",
                "        public int ExistingCaller(int value)\n        {\n"
                + "            return Doubled + value;\n        }",
                new[] { BuildHostMethodKey("get_Doubled") });

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(FindEntry(result, "get_Doubled"), Is.Null);
            Assert.That(FindEntry(result, nameof(HotReloadAddedMemberHost.ExistingCaller)), Is.Null);
            Assert.That(
                FindSkipReason(result, nameof(HotReloadAddedMemberHost.ExistingCaller)),
                Does.Contain("Uses an added property that hot reload cannot emit."));
        }

        /// <summary>
        /// An unavailable accessor body removes the complete property and skips dependent callers.
        /// </summary>
        [Test]
        public async Task Isolation_UnavailableAddedPropertyAccessor_DropsBothAccessors()
        {
            TransformWorkerClientResult result = await RunEditedHostAsync(
                "UnavailableAddedPropertyAccessor.cs",
                "public int Blocked\n        {\n            get { return nameof(Stored).Length; }\n"
                + "            set { _privateSeed = value; }\n        }\n\n"
                + "        public int Stored { get { return _privateSeed; } set { _privateSeed = value; } }",
                "        public int ExistingCaller(int value)\n        {\n"
                + "            return Blocked + value;\n        }");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(FindEntry(result, "get_Blocked"), Is.Null);
            Assert.That(FindEntry(result, "set_Blocked"), Is.Null);
            Assert.That(FindSkipReason(result, "get_Blocked"), Is.EqualTo(NameofReferenceReason));
            Assert.That(FindSkipReason(result, "set_Blocked"), Is.EqualTo(NameofReferenceReason));
            Assert.That(
                FindSkipReason(result, nameof(HotReloadAddedMemberHost.ExistingCaller)),
                Does.Contain("Uses an added property that hot reload cannot emit."));
        }

        /// <summary>
        /// An added bodied property with a baseline remains excluded from outside-body drift.
        /// </summary>
        [Test]
        public async Task Drift_AddedBodiedProperty_ProducesNoOutsideBodyWarning()
        {
            TransformWorkerClientResult result = await RunEditedHostAsync(
                "AddedBodiedPropertyDrift.cs",
                "public int DriftSafe => PublicSeed * 2;");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            string[] warnings = result.Output.files[0].declarationDriftWarnings ?? Array.Empty<string>();
            Assert.That(warnings, Has.None.Contain("Edits outside method bodies"));
        }

        /// <summary>
        /// An added auto-property emits store-backed accessor shims and reports its backing
        /// value through the added-field rows so the Editor keeps the added-field lifetime.
        /// </summary>
        [Test]
        public async Task Emit_AddedAutoProperty_EmitsStoreBackedAccessors()
        {
            TransformWorkerClientResult result = await RunEditedHostAsync(
                "AddedAutoProperty.cs",
                "public int Count { get; set; }",
                "        public int ExistingCaller(int value)\n        {\n"
                + "            Count = value;\n            return Count;\n        }");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            TransformWorkerEntryDto getter = FindEntry(result, "get_Count");
            TransformWorkerEntryDto setter = FindEntry(result, "set_Count");
            Assert.That(getter, Is.Not.Null, FormatSkipped(result.Output.skipped));
            Assert.That(setter, Is.Not.Null, FormatSkipped(result.Output.skipped));
            Assert.That(getter.patchKind, Is.EqualTo(HotReloadConstants.PatchKindAddedMethod));
            string getterShim = SliceShimMethod(result.Output.shimSource, getter.shimMethodName);
            Assert.That(getterShim, Does.Contain("GetOrInit<"));
            Assert.That(getterShim, Does.Contain(HostStoreKeyPrefix + "Count"));
            string setterShim = SliceShimMethod(result.Output.shimSource, setter.shimMethodName);
            Assert.That(setterShim, Does.Contain("Set<"));
            Assert.That(setterShim, Does.Contain(HostStoreKeyPrefix + "Count"));
            Assert.That(
                result.Output.files[0].addedFieldNames,
                Has.Some.Contains("HotReloadAddedMemberHost.Count"));
            Assert.That(result.Output.hasAddedFieldRewrites, Is.True);
        }

        /// <summary>
        /// A literal initializer on an added auto-property becomes the store's static lambda so
        /// the first read produces the declared value.
        /// </summary>
        [Test]
        public async Task Emit_AddedAutoPropertyWithInitializer_UsesStaticLambda()
        {
            TransformWorkerClientResult result = await RunEditedHostAsync(
                "AddedAutoPropertyInitializer.cs",
                "public int Base { get; } = 41;",
                "        public int ExistingCaller(int value)\n        {\n"
                + "            return Base + value;\n        }");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            TransformWorkerEntryDto getter = FindEntry(result, "get_Base");
            Assert.That(getter, Is.Not.Null, FormatSkipped(result.Output.skipped));
            Assert.That(
                SliceShimMethod(result.Output.shimSource, getter.shimMethodName),
                Does.Contain("static () => 41"));
        }

        /// <summary>
        /// An object-creation initializer on an added auto-property is skipped for the same
        /// reason as an added field, because the shim lambda cannot run the constructor.
        /// </summary>
        [Test]
        public async Task Skip_AddedAutoPropertyObjectCreationInitializer_SkipsWithInitializerReason()
        {
            TransformWorkerClientResult result = await RunEditedHostAsync(
                "AddedAutoPropertyObjectCreationInitializer.cs",
                "public System.Collections.Generic.List<int> Items { get; }"
                + " = new System.Collections.Generic.List<int>();");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(FindEntry(result, "get_Items"), Is.Null);
            Assert.That(FindSkipReason(result, "get_Items"), Is.EqualTo(InitializerNotEmittableReason));
        }

        /// <summary>
        /// A static added auto-property uses the static store entry points so no instance
        /// receiver is required.
        /// </summary>
        [Test]
        public async Task Emit_AddedStaticAutoProperty_UsesStaticStore()
        {
            TransformWorkerClientResult result = await RunEditedHostAsync(
                "AddedStaticAutoProperty.cs",
                "public static int Total { get; set; }",
                "        public int ExistingCaller(int value)\n        {\n"
                + "            Total = value;\n            return Total;\n        }");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            TransformWorkerEntryDto getter = FindEntry(result, "get_Total");
            TransformWorkerEntryDto setter = FindEntry(result, "set_Total");
            Assert.That(getter, Is.Not.Null, FormatSkipped(result.Output.skipped));
            Assert.That(setter, Is.Not.Null, FormatSkipped(result.Output.skipped));
            string getterShim = SliceShimMethod(result.Output.shimSource, getter.shimMethodName);
            Assert.That(getterShim, Does.Contain("GetOrInitStatic<"));
            Assert.That(getterShim, Does.Not.Contain("GetOrInit<"));
            Assert.That(
                SliceShimMethod(result.Output.shimSource, setter.shimMethodName),
                Does.Contain("SetStatic<"));
        }

        /// <summary>
        /// An added auto-property on a struct host is skipped, because the store cannot keep
        /// identity for a boxed value-type receiver.
        /// </summary>
        [Test]
        public async Task Skip_AddedAutoPropertyOnStruct_SkipsWithStructHostReason()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = onDisk.Replace(
                "        public int Existing;\n",
                "        public int Existing;\n\n        public int Added { get; set; }\n",
                StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk));

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                HotReloadTestSourceWriter.WriteEditedSource("AddedAutoPropertyStructHost.cs", edited),
                HostProjectRelativePath,
                onDisk,
                null);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(FindEntry(result, "get_Added"), Is.Null);
            Assert.That(FindSkipReason(result, "get_Added"), Is.EqualTo(StructHostReason));
        }

        /// <summary>
        /// An added auto-property initializer that calls a member of its own host is skipped,
        /// because the store's static lambda cannot reach the compiled host's members.
        /// Why not an instance member: a property initializer that reads instance state does not
        /// compile, so the host's own static method is the reachable shape of the same rule.
        /// </summary>
        [Test]
        public async Task Skip_AddedAutoPropertyInitializerReferencingHostMember_SkipsWithInitializerReason()
        {
            TransformWorkerClientResult result = await RunEditedHostAsync(
                "AddedAutoPropertyHostMemberInitializer.cs",
                "public int Twice { get; } = PrivateStaticSeven();");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(FindEntry(result, "get_Twice"), Is.Null);
            Assert.That(FindSkipReason(result, "get_Twice"), Is.EqualTo(InitializerNotEmittableReason));
        }

        /// <summary>
        /// An added auto-property with a baseline stays out of the outside-body drift warnings.
        /// </summary>
        [Test]
        public async Task Drift_AddedAutoProperty_ProducesNoOutsideBodyWarning()
        {
            TransformWorkerClientResult result = await RunEditedHostAsync(
                "AddedAutoPropertyDrift.cs",
                "public int DriftSafeAuto { get; set; }");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            string[] warnings = result.Output.files[0].declarationDriftWarnings ?? Array.Empty<string>();
            Assert.That(warnings, Has.None.Contain("Edits outside method bodies"));
        }

        /// <summary>
        /// Turning a compiled field into a property is skipped rather than emitted, because the
        /// compiled field keeps owning the value that unedited code reads and writes.
        /// </summary>
        [Test]
        public async Task Skip_CompiledFieldReplacedByProperty_SkipsWithMemberKindReason()
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = onDisk.Replace(
                "        public int PublicSeed = 3;",
                "        public int PublicSeed { get; set; }",
                StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk));

            TransformWorkerClientResult result = await RunWorkerOnSourceAsync(
                HotReloadTestSourceWriter.WriteEditedSource("CompiledFieldToProperty.cs", edited),
                HostProjectRelativePath,
                onDisk,
                null);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(FindEntry(result, "get_PublicSeed"), Is.Null);
            Assert.That(FindEntry(result, "set_PublicSeed"), Is.Null);
            Assert.That(FindSkipReason(result, "get_PublicSeed"), Is.EqualTo(CompiledMemberKindChangedReason));
            Assert.That(FindSkipReason(result, "set_PublicSeed"), Is.EqualTo(CompiledMemberKindChangedReason));
        }

        /// <summary>
        /// Compound assignment to an added auto-property skips the caller, because the accessor
        /// shims cannot carry an operation that reads and writes in one expression.
        /// </summary>
        [Test]
        public async Task Skip_CallerCompoundAssignsAddedAutoProperty_SkipsCallerWithCompoundReason()
        {
            TransformWorkerClientResult result = await RunEditedHostAsync(
                "AddedAutoPropertyCompoundAssignment.cs",
                "public int Count { get; set; }",
                "        public int ExistingCaller(int value)\n        {\n"
                + "            Count += 1;\n            return value;\n        }");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(FindEntry(result, nameof(HotReloadAddedMemberHost.ExistingCaller)), Is.Null);
            Assert.That(
                FindSkipReason(result, nameof(HotReloadAddedMemberHost.ExistingCaller)),
                Is.EqualTo(CompoundAssignmentReason));
            Assert.That(FindEntry(result, "get_Count"), Is.Not.Null);
            Assert.That(FindEntry(result, "set_Count"), Is.Not.Null);
        }

        /// <summary>
        /// Incrementing an added auto-property skips the caller for the same reason as a
        /// compound assignment; only plain reads and simple assignment have a rewrite shape.
        /// </summary>
        [Test]
        public async Task Skip_CallerIncrementsAddedAutoProperty_SkipsCallerWithCompoundReason()
        {
            TransformWorkerClientResult result = await RunEditedHostAsync(
                "AddedAutoPropertyIncrement.cs",
                "public int Count { get; set; }",
                "        public int ExistingCaller(int value)\n        {\n"
                + "            Count++;\n            return value;\n        }");

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(FindEntry(result, nameof(HotReloadAddedMemberHost.ExistingCaller)), Is.Null);
            Assert.That(
                FindSkipReason(result, nameof(HotReloadAddedMemberHost.ExistingCaller)),
                Is.EqualTo(CompoundAssignmentReason));
            Assert.That(FindEntry(result, "get_Count"), Is.Not.Null);
            Assert.That(FindEntry(result, "set_Count"), Is.Not.Null);
        }

        private static async Task<TransformWorkerClientResult> RunEditedHostAsync(
            string fileName,
            string extraMembers,
            string callerReplacement = null,
            string[] excludedAddedMethodKeys = null)
        {
            string onDisk = File.ReadAllText(ResolveHostPath());
            string edited = WithHostMembers(onDisk, extraMembers);
            if (callerReplacement != null)
            {
                edited = edited.Replace(ExistingCallerOriginal, callerReplacement, StringComparison.Ordinal);
                Assert.That(edited, Is.Not.EqualTo(onDisk));
            }

            return await RunWorkerOnSourceAsync(
                HotReloadTestSourceWriter.WriteEditedSource(fileName, edited),
                HostProjectRelativePath,
                onDisk,
                excludedAddedMethodKeys);
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

        private static string FindSkipReason(TransformWorkerClientResult result, string methodNameFragment)
        {
            foreach (TransformWorkerSkippedDto skipped in result.Output.skipped)
            {
                if (skipped.method != null
                    && skipped.method.IndexOf(methodNameFragment, StringComparison.Ordinal) >= 0)
                {
                    return skipped.reason;
                }
            }

            return null;
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
            int declarationStart = FindShimMethodDeclarationStart(shimSource, shimMethodName);
            int openBrace = shimSource.IndexOf('{', declarationStart);
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

        private static int FindShimMethodDeclarationStart(string shimSource, string shimMethodName)
        {
            int searchStart = 0;
            while (searchStart < shimSource.Length)
            {
                int declarationStart = shimSource.IndexOf("public static", searchStart, StringComparison.Ordinal);
                if (declarationStart < 0)
                {
                    break;
                }

                int openBrace = shimSource.IndexOf('{', declarationStart);
                int nameIndex = shimSource.IndexOf(shimMethodName, declarationStart, StringComparison.Ordinal);
                if (nameIndex >= declarationStart && nameIndex < openBrace)
                {
                    return declarationStart;
                }

                searchStart = declarationStart + 1;
            }

            Assert.Fail("Shim method missing: " + shimMethodName);
            return -1;
        }

        private static string SliceSetterShimBeforeTrailingLineDefault(string shimSource, string shimMethodName)
        {
            string declaration = "public static void " + shimMethodName + "(";
            int declarationStart = shimSource.IndexOf(declaration, StringComparison.Ordinal);
            Assert.That(declarationStart, Is.GreaterThanOrEqualTo(0), "Setter shim missing: " + shimMethodName);
            int defaultDirective = shimSource.IndexOf("#line default", declarationStart, StringComparison.Ordinal);
            Assert.That(defaultDirective, Is.GreaterThan(declarationStart));
            return shimSource.Substring(declarationStart, defaultDirective - declarationStart);
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

        private static string BuildHostMethodKey(string methodName)
        {
            return typeof(HotReloadAddedMemberHost).FullName + "::" + methodName + "()";
        }

        private static async Task<TransformWorkerClientResult> RunWorkerOnSourceAsync(
            string sourcePath,
            string projectRelativePath,
            string snapshotSource,
            string[] excludedAddedMethodKeys)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string targetDllPath = Path.Combine(
                projectRoot,
                "Library",
                "ScriptAssemblies",
                TestAssemblyName + ".dll");
            Assert.That(File.Exists(targetDllPath), Is.True, "Test assembly dll missing: " + targetDllPath);

            UnityEditor.Compilation.Assembly compilationAssembly = FindCompilationAssembly();
            Assert.That(compilationAssembly, Is.Not.Null, "CompilationPipeline assembly not found.");

            TransformWorkerInputDto input = new TransformWorkerInputDto
            {
                sources = new[]
                {
                    new TransformWorkerSourceDto
                    {
                        sourcePath = sourcePath,
                        projectRelativePath = projectRelativePath,
                        snapshotSource = snapshotSource
                    }
                },
                defines = compilationAssembly.defines ?? Array.Empty<string>(),
                referencePaths = BuildAbsoluteReferencePaths(compilationAssembly.allReferences, targetDllPath),
                targetTypesAssemblyPath = targetDllPath,
                assemblySourcePaths = BuildAbsoluteAssemblySourcePaths(compilationAssembly.sourceFiles),
                excludedMethodKeys = Array.Empty<string>(),
                excludedAddedMethodKeys = excludedAddedMethodKeys ?? Array.Empty<string>()
            };

            return await TransformWorkerClient.RunAsync(input, CancellationToken.None);
        }

        private static UnityEditor.Compilation.Assembly FindCompilationAssembly()
        {
            foreach (UnityEditor.Compilation.Assembly assembly in CompilationPipeline.GetAssemblies())
            {
                if (assembly.name == TestAssemblyName)
                {
                    return assembly;
                }
            }

            return null;
        }

        private static string[] BuildAbsoluteReferencePaths(string[] allReferences, string targetDllPath)
        {
            List<string> paths = new List<string>();
            if (allReferences != null)
            {
                foreach (string reference in allReferences)
                {
                    if (!string.IsNullOrEmpty(reference) && File.Exists(reference))
                    {
                        paths.Add(Path.GetFullPath(reference));
                    }
                }
            }

            string fullTargetPath = Path.GetFullPath(targetDllPath);
            if (!ContainsOrdinalIgnoreCase(paths, fullTargetPath))
            {
                paths.Add(fullTargetPath);
            }

            return paths.ToArray();
        }

        private static bool ContainsOrdinalIgnoreCase(List<string> paths, string candidate)
        {
            foreach (string path in paths)
            {
                if (string.Equals(path, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
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
                string absolutePath = Path.Combine(
                    projectRoot,
                    normalizedRelativePath.Replace('/', Path.DirectorySeparatorChar));
                paths[index] = Path.GetFullPath(absolutePath);
            }

            return paths;
        }
    }
}
