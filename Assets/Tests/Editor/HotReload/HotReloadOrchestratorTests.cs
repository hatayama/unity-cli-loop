using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using Newtonsoft.Json.Linq;

using HarmonyLib;

using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// End-to-end EditMode coverage for <see cref="HotReloadOrchestrator"/> using the
    /// files[] / contentPathOverride separation (edited copies under Library/, never Assets/).
    /// </summary>
    public class HotReloadOrchestratorTests
    {
        [TearDown]
        public void TearDown()
        {
            HotReloadPatcher.RevertAll();
            VibeLogger.ClearMemoryLogs();
        }

        // Keep in sync with AddedMethodSkipReasons.UnavailableAddedCall in
        // Packages/src/Editor/FirstPartyTools/HotReload/TransformWorker~/AddedMethodSkipReasons.cs.
        // That type lives in the Unity-ignored worker process and is not visible here.
        private const string UnavailableAddedCallSkipReason =
            "Calls an added method that hot reload cannot emit. Run 'uloop compile'.";

        // Keep in sync with EvaluateHardSkipReason in
        // Packages/src/Editor/FirstPartyTools/HotReload/TransformWorker~/MethodTransformDecider.cs.
        private const string ExpectedGenericMethodSkipReason =
            "Generic methods and methods inside generic types cannot be safely patched with Harmony. Run 'uloop compile'.";

        /// <summary>
        /// What: the added-field store assembly is injected only when the store flag is set,
        /// matching the Harmony optional-reference pattern.
        /// </summary>
        [Test]
        public void AppendOptionalShimAssemblyReferences_StoreFlag_AddsToolContractsAssembly()
        {
            List<string> references = new List<string>();
            HotReloadShimReferenceBuilder.AppendOptionalShimAssemblyReferences(
                references,
                includeHarmonyReference: false,
                includeAddedFieldStoreReference: true);

            Assert.That(references.Count, Is.EqualTo(1));
            Assert.That(
                references[0],
                Is.EqualTo(typeof(HotReloadAddedFieldStore).Assembly.Location));
        }

        /// <summary>
        /// What: both optional flags false leave the reference list unchanged.
        /// </summary>
        [Test]
        public void AppendOptionalShimAssemblyReferences_BothFlagsFalse_AddsNothing()
        {
            List<string> references = new List<string>();
            HotReloadShimReferenceBuilder.AppendOptionalShimAssemblyReferences(
                references,
                includeHarmonyReference: false,
                includeAddedFieldStoreReference: false);

            Assert.That(references, Is.Empty);
        }

        /// <summary>
        /// What: the Harmony flag still injects Harmony without also adding the store assembly.
        /// </summary>
        [Test]
        public void AppendOptionalShimAssemblyReferences_HarmonyFlagOnly_AddsHarmony()
        {
            List<string> references = new List<string>();
            HotReloadShimReferenceBuilder.AppendOptionalShimAssemblyReferences(
                references,
                includeHarmonyReference: true,
                includeAddedFieldStoreReference: false);

            Assert.That(references.Count, Is.EqualTo(1));
            Assert.That(references[0], Is.EqualTo(typeof(Harmony).Assembly.Location));
        }

        /// <summary>
        /// What: a publicized ToolContracts path already in the list is not duplicated when
        /// the store flag would otherwise append the raw Location (CS1703).
        /// </summary>
        [Test]
        public void AppendOptionalShimAssemblyReferences_ExistingPublicizedStore_DoesNotDuplicate()
        {
            string fileName = Path.GetFileName(typeof(HotReloadAddedFieldStore).Assembly.Location);
            string publicizedPath = Path.Combine(
                HotReloadConstants.PublicizedRefsRelativeDirectory,
                fileName);
            List<string> references = new List<string> { publicizedPath };

            HotReloadShimReferenceBuilder.AppendOptionalShimAssemblyReferences(
                references,
                includeHarmonyReference: false,
                includeAddedFieldStoreReference: true);

            Assert.That(references.Count, Is.EqualTo(1));
            Assert.That(references[0], Is.EqualTo(publicizedPath));
        }

        /// <summary>
        /// What: a synthetic worker output with hasAddedFieldRewrites true causes the first-pass
        /// NeedsAddedFieldStoreReference → AppendOptional chain to include the store assembly.
        /// </summary>
        [Test]
        public void NeedsAddedFieldStoreReference_TrueOutput_AppendsStoreAssembly()
        {
            TransformWorkerOutputDto output = new TransformWorkerOutputDto
            {
                hasAddedFieldRewrites = true
            };
            bool includeStore = HotReloadShimReferenceBuilder.NeedsAddedFieldStoreReference(output);
            List<string> references = new List<string>();
            HotReloadShimReferenceBuilder.AppendOptionalShimAssemblyReferences(
                references,
                includeHarmonyReference: false,
                includeAddedFieldStoreReference: includeStore);

            Assert.That(includeStore, Is.True);
            Assert.That(references.Count, Is.EqualTo(1));
            Assert.That(
                references[0],
                Is.EqualTo(typeof(HotReloadAddedFieldStore).Assembly.Location));
        }

        /// <summary>
        /// What: an edited copy that changes ComputeWithPrivate is transplanted, including private
        /// field access, so the live method returns the new value.
        /// </summary>
        [Test]
        public async Task Run_EditedPrivateAccessMethod_PatchesBehavior()
        {
            string fixturePath = ResolveE2EFixturePath();
            string editedPath = WriteEditedSource(
                "ComputeWithPrivate.cs",
                BuildFixtureSource(
                    computeWithPrivateMethod:
                    "public int ComputeWithPrivate(int delta)\n        {\n            return _secret + delta + 100;\n        }"));

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            AssertHasPatched(result, nameof(HotReloadE2EFixture.ComputeWithPrivate));

            HotReloadE2EFixture fixture = new HotReloadE2EFixture();
            Assert.That(fixture.ComputeWithPrivate(5), Is.EqualTo(10 + 5 + 100));
        }

        /// <summary>
        /// What: an edited method whose parameter is named "instance" still hot-reloads; the shim
        /// receiver parameter must not collide with user identifiers (CS0100).
        /// </summary>
        [Test]
        public async Task Run_EditedMethodWithParameterNamedInstance_Patches()
        {
            string fixturePath = ResolveE2EFixturePath();
            string editedPath = WriteEditedSource(
                "InstanceParameterName.cs",
                BuildFixtureSource(
                    computeWithPrivateMethod:
                    "public int ComputeWithPrivate(int instance)\n        {\n            return _secret + instance + 100;\n        }"));

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            AssertHasPatched(result, nameof(HotReloadE2EFixture.ComputeWithPrivate));

            HotReloadE2EFixture fixture = new HotReloadE2EFixture();
            Assert.That(fixture.ComputeWithPrivate(5), Is.EqualTo(10 + 5 + 100));
        }

        /// <summary>
        /// What: expression-bodied edited methods keep a terminating semicolon in generated shims
        /// and still transplant successfully.
        /// </summary>
        [Test]
        public async Task Run_EditedExpressionBodiedMethod_PatchesBehavior()
        {
            string fixturePath = ResolveE2EFixturePath();
            string editedPath = WriteEditedSource(
                "ComputeWithPrivateExpression.cs",
                BuildFixtureSource(
                    computeWithPrivateMethod:
                    "public int ComputeWithPrivate(int delta) => _secret + delta + 100;"));

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            AssertHasPatched(result, nameof(HotReloadE2EFixture.ComputeWithPrivate));

            HotReloadE2EFixture fixture = new HotReloadE2EFixture();
            Assert.That(fixture.ComputeWithPrivate(5), Is.EqualTo(10 + 5 + 100));
        }

        /// <summary>
        /// What: bare sibling-type references and owned members inside object initializers both
        /// compile in generated shims (namespace emission + initializer non-qualification).
        /// </summary>
        [Test]
        public async Task Run_EditedBodyWithSiblingTypeAndObjectInitializer_PatchesBehavior()
        {
            string fixturePath = ResolveE2EFixturePath();
            string editedPath = WriteEditedSource(
                "SiblingAndInitializer.cs",
                BuildFixtureSource(
                    computeWithPrivateMethod:
                    "public int ComputeWithPrivate(int delta)\n        {\n"
                    + "            HotReloadE2ESibling s = new HotReloadE2ESibling { Value = delta };\n"
                    + "            HotReloadE2EFixture other = new HotReloadE2EFixture { Counter = delta };\n"
                    + "            return _secret + s.Value + other.Counter + 100;\n"
                    + "        }"));

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            AssertHasPatched(result, nameof(HotReloadE2EFixture.ComputeWithPrivate));

            HotReloadE2EFixture fixture = new HotReloadE2EFixture();
            Assert.That(fixture.ComputeWithPrivate(5), Is.EqualTo(10 + 5 + 5 + 100));
        }

        /// <summary>
        /// What: 5+ locals force short-form ldloc.s/stloc.s so the LocalBuilder rebind path is
        /// exercised (ldloc.0–3 alone would not cover ReadLocalIndexOperand).
        /// </summary>
        [Test]
        public async Task Run_EditedBodyWithFiveLocals_PatchesBehavior()
        {
            string fixturePath = ResolveE2EFixturePath();
            string editedPath = WriteEditedSource(
                "FiveLocals.cs",
                BuildFixtureSource(
                    computeWithPrivateMethod:
                    "public int ComputeWithPrivate(int delta)\n        {\n"
                    + "            int a = delta + 1;\n"
                    + "            int b = a + delta;\n"
                    + "            int c = b + a;\n"
                    + "            int d = c + b;\n"
                    + "            int e = d + c;\n"
                    + "            return _secret + a + b + c + d + e + (a * b) + (c * d) + (e * a);\n"
                    + "        }"));

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            AssertHasPatched(result, nameof(HotReloadE2EFixture.ComputeWithPrivate));

            HotReloadE2EFixture fixture = new HotReloadE2EFixture();
            Assert.That(
                fixture.ComputeWithPrivate(5),
                Is.EqualTo(10 + 6 + 11 + 17 + 28 + 45 + (6 * 11) + (17 * 28) + (45 * 6)));
        }

        /// <summary>
        /// What: a method with a multidimensional array parameter patches successfully (Cecil
        /// FullName uses [0...,0...] and must match the worker manifest).
        /// </summary>
        [Test]
        public async Task Run_EditedMultidimensionalArrayParameterMethod_PatchesBehavior()
        {
            string fixturePath = ResolveE2EFixturePath();
            string editedPath = WriteEditedSource(
                "SumGrid.cs",
                BuildFixtureSource(
                    computeWithPrivateMethod:
                    "public int ComputeWithPrivate(int delta)\n        {\n            return _secret + delta;\n        }",
                    sumGridMethod:
                    "public int SumGrid(int[,] grid)\n        {\n            return 42;\n        }"));

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            AssertHasPatched(result, nameof(HotReloadE2EFixture.SumGrid));

            HotReloadE2EFixture fixture = new HotReloadE2EFixture();
            Assert.That(fixture.SumGrid(new int[1, 1]), Is.EqualTo(42));
        }

        /// <summary>
        /// What: a struct-returning method with a loop transplants through the full pipeline
        /// (edit → worker → shim compile → transplant) and computes the edited value.
        /// </summary>
        [Test]
        public async Task Run_EditedStructReturnLoopMethod_PatchesBehavior()
        {
            string fixturePath = ResolveE2EFixturePath();
            string editedPath = WriteEditedSource(
                "CenterOfCell.cs",
                BuildFixtureSource(
                    computeWithPrivateMethod:
                    "public int ComputeWithPrivate(int delta)\n        {\n            return _secret + delta;\n        }",
                    centerOfCellMethod:
                    "[MethodImpl(MethodImplOptions.NoInlining)]\n"
                    + "        public Vector3 CenterOfCell(Vector3Int cell)\n"
                    + "        {\n"
                    + "            Vector3 center = Vector3.zero;\n"
                    + "            for (int i = 0; i < 1; i++)\n"
                    + "            {\n"
                    + "                center = new Vector3(cell.x + 0.5f, cell.y + 0.5f, cell.z + 0.5f);\n"
                    + "            }\n"
                    + "\n"
                    + "            return center;\n"
                    + "        }"));

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            AssertHasPatched(result, nameof(HotReloadE2EFixture.CenterOfCell));

            HotReloadE2EFixture fixture = new HotReloadE2EFixture();
            Assert.That(
                fixture.CenterOfCell(new Vector3Int(1, 2, 3)),
                Is.EqualTo(new Vector3(1.5f, 2.5f, 3.5f)));
        }

        /// <summary>
        /// What: a method containing base. is reported as Skipped with an explanatory reason.
        /// </summary>
        [Test]
        public async Task Run_MethodWithBaseCall_IsSkippedWithReason()
        {
            string fixturePath = ResolveE2EFixturePath();
            string editedPath = WriteEditedSource(
                "CallsBaseEdited.cs",
                BuildFixtureSource(
                    computeWithPrivateMethod:
                    "public int ComputeWithPrivate(int delta)\n        {\n            return _secret + delta;\n        }",
                    callsBaseMethod:
                    "public int CallsBase()\n        {\n            return base.BaseSeed() + 2;\n        }"));

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                contentPathOverride: editedPath,
                CancellationToken.None);

            AssertNoFileLevelFailure(result);

            bool found = false;
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Skipped
                    && outcome.Method.Contains(nameof(HotReloadE2EFixture.CallsBase))
                    && outcome.Reason.Contains("base"))
                {
                    found = true;
                }
            }

            Assert.That(found, Is.True, "Expected CallsBase to be Skipped for base. usage.");
        }

        /// <summary>
        /// What: editing a method that references a private static field inside a string
        /// interpolation hole still Patches (alias-qualified names must be parenthesized so csc
        /// does not treat ':' as a format-clause start).
        /// </summary>
        [Test]
        public async Task Run_EditedInterpolationStaticFieldAccess_Patches()
        {
            string fixturePath = ResolveCoreFixturePath();
            string onDisk = File.ReadAllText(fixturePath);
            string editedSource = onDisk.Replace(
                "return $\"total: {formatCallTotal}\";",
                "return $\"total=: {formatCallTotal}\";",
                StringComparison.Ordinal);
            Assert.That(
                editedSource,
                Is.Not.EqualTo(onDisk),
                "Precondition: FormatStaticCount body must differ from on-disk.");

            string editedPath = WriteEditedSource("InterpolationStaticField.cs", editedSource);
            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                contentPathOverride: editedPath,
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            AssertHasPatched(result, nameof(HotReloadInterpolationFixture.FormatStaticCount));
        }

        /// <summary>
        /// What: editing a method that uses a private const as an interpolation alignment width
        /// still Patches (alias-qualified alignment expressions must be parenthesized so csc
        /// does not treat ':' as a format-clause start).
        /// </summary>
        [Test]
        public async Task Run_EditedInterpolationAlignmentConstAccess_Patches()
        {
            string fixturePath = ResolveCoreFixturePath();
            string onDisk = File.ReadAllText(fixturePath);
            string editedSource = onDisk.Replace(
                "return $\"total: {formatCallTotal,PaddingWidth}\";",
                "return $\"total=: {formatCallTotal,PaddingWidth}\";",
                StringComparison.Ordinal);
            Assert.That(
                editedSource,
                Is.Not.EqualTo(onDisk),
                "Precondition: FormatAlignedStaticCount body must differ from on-disk.");

            string editedPath = WriteEditedSource("InterpolationAlignmentConst.cs", editedSource);
            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                contentPathOverride: editedPath,
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            AssertHasPatched(result, nameof(HotReloadInterpolationFixture.FormatAlignedStaticCount));
        }

        /// <summary>
        /// What: running hot reload on the on-disk fixture with no edits yields an empty Methods
        /// list, a positive UnchangedTotal, and the all-unchanged Message wording.
        /// </summary>
        [Test]
        public async Task Run_OnDiskFixtureUnchanged_ReportsEmptyMethodsAndUnchangedTotal()
        {
            string fixturePath = ResolveE2EFixturePath();

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                contentPathOverride: null,
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            Assert.That(result.Methods, Is.Empty, FormatOutcomes(result));
            Assert.That(result.UnchangedTotal, Is.GreaterThan(0));

            HotReloadResponse response = HotReloadTool.BuildApplyResponse(result);
            Assert.That(
                response.Message,
                Does.Contain("methods are unchanged since the last compile; nothing to patch"));
        }

        /// <summary>
        /// What: property getters with bodies are Patched; property setters and indexer getters
        /// with bodies stay Skipped with the accessor reason; auto-property accessors stay unlisted.
        /// </summary>
        [Test]
        public async Task Run_PropertyGettersPatched_SetterAndIndexerAccessorsSkipped()
        {
            string fixturePath = ResolveE2EFixturePath();
            string editedPath = WriteEditedSource(
                "ExplicitBodyGetter.cs",
                BuildFixtureSource(
                    computeWithPrivateMethod:
                    "public int ComputeWithPrivate(int delta)\n"
                    + "        {\n"
                    + "            return _secret + delta;\n"
                    + "        }",
                    explicitAccessorsBlock:
                    "// Explicit-body getter — worker must patch get_ExplicitBodyGetter.\n"
                    + "        public int ExplicitBodyGetter\n"
                    + "        {\n"
                    + "            get { return _secret + 1; }\n"
                    + "        }\n"
                    + "\n"
                    + "        // Explicit-body setter — worker must report set_ExplicitBodySetter as Skipped.\n"
                    + "        public int ExplicitBodySetter\n"
                    + "        {\n"
                    + "            set { _secret = value + 1; }\n"
                    + "        }\n"
                    + "\n"
                    + "        public int Counter;\n"
                    + "\n"
                    + "        private int this[int index] => _secret + index + 1;"));

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                contentPathOverride: editedPath,
                CancellationToken.None);

            AssertNoFileLevelFailure(result);

            const string expectedReason =
                "Property setter, init, or indexer accessors are out of scope for v1; "
                + "run 'uloop compile' to apply accessor edits.";
            bool foundPropertyGetterPatched = false;
            bool foundIndexerGetter = false;
            bool foundPropertySetter = false;
            bool foundAutoPropertyAccessor = false;
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Patched
                    && outcome.Method.Contains("get_ExplicitBodyGetter"))
                {
                    foundPropertyGetterPatched = true;
                }

                bool skippedWithAccessorReason = outcome.Kind == HotReloadMethodOutcomeKind.Skipped
                    && outcome.Reason == expectedReason;
                if (skippedWithAccessorReason && outcome.Method.Contains("get_Item"))
                {
                    foundIndexerGetter = true;
                }

                if (skippedWithAccessorReason && outcome.Method.Contains("set_ExplicitBodySetter"))
                {
                    foundPropertySetter = true;
                }

                if (outcome.Method.Contains("get_HiddenScore") || outcome.Method.Contains("set_HiddenScore"))
                {
                    foundAutoPropertyAccessor = true;
                }
            }

            Assert.That(
                foundPropertyGetterPatched,
                Is.True,
                "Expected get_ExplicitBodyGetter to be Patched; got: " + FormatOutcomes(result));
            Assert.That(
                foundIndexerGetter,
                Is.True,
                "Expected the expression-bodied indexer getter (get_Item) to be Skipped with the accessor out-of-scope reason.");
            Assert.That(
                foundPropertySetter,
                Is.True,
                "Expected the explicit-body setter (set_ExplicitBodySetter) to be Skipped with the accessor out-of-scope reason.");
            Assert.That(
                foundAutoPropertyAccessor,
                Is.False,
                "Auto-property accessors must not be listed; only explicit-body accessors are reported.");
        }

        /// <summary>
        /// What: editing an instance constructor reports it as Skipped with the unsupported-member
        /// reason and leaves Success true.
        /// </summary>
        [Test]
        public async Task Run_EditedInstanceConstructor_IsSkippedAndSuccessStaysTrue()
        {
            string fixturePath = ResolveUnsupportedKindFixturePath();
            string onDisk = File.ReadAllText(fixturePath);
            string editedSource = onDisk.Replace(
                "Marker = 11;",
                "Marker = 111;",
                StringComparison.Ordinal);
            Assert.That(editedSource, Is.Not.EqualTo(onDisk), "Precondition: constructor body must differ.");

            string editedPath = WriteEditedSource("UnsupportedKindCtor.cs", editedSource);
            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                contentPathOverride: editedPath,
                CancellationToken.None);

            AssertNoFileLevelFailure(result);

            const string expectedReason =
                "Constructors, operators, and event accessors are out of scope for v1; "
                + "run 'uloop compile' to apply these edits.";
            AssertHasSkipped(result, ".ctor()", expectedReason);

            bool foundUneditedOverload = false;
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Skipped
                    && outcome.Reason == expectedReason
                    && outcome.Method.Contains(".ctor(System.Int32)"))
                {
                    foundUneditedOverload = true;
                }
            }

            Assert.That(
                foundUneditedOverload,
                Is.False,
                "Unedited constructor overload must not be Skipped; got: " + FormatOutcomes(result));

            HotReloadResponse response = HotReloadTool.BuildApplyResponse(result);
            Assert.That(response.Success, Is.True, "Skipped constructors must not flip Success.");
        }

        /// <summary>
        /// What: editing a static expression-bodied property getter patches runtime reads,
        /// --status lists the Active get_ row, and RevertAll restores the compiled literal.
        /// </summary>
        [Test]
        public async Task Run_EditedPropertyGetter_ApplyStatusRevert_UpdatesRuntimeValue()
        {
            string fixturePath = ResolveShapeFixturePath();
            string onDisk = File.ReadAllText(fixturePath);
            string editedSource = onDisk.Replace(
                "public static float HeightAmplitude => 5f;",
                "public static float HeightAmplitude => 6f;",
                StringComparison.Ordinal);
            Assert.That(editedSource, Is.Not.EqualTo(onDisk), "Precondition: getter body must differ.");

            string editedPath = WriteEditedSource("PropertyGetterHeightAmplitude.cs", editedSource);

            Assert.That(HotReloadPropertyGetterFixture.HeightAmplitude, Is.EqualTo(5f));

            HotReloadOrchestratorResult patched = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);

            AssertNoFileLevelFailure(patched);
            AssertHasPatched(patched, "get_HeightAmplitude");
            Assert.That(patched.ActivePatchTotal, Is.GreaterThanOrEqualTo(1));
            Assert.That(
                patched.Warnings,
                Has.None.Contain("Edits outside method bodies"),
                "Getter-only edits must not warn that outside-body edits were not applied.");
            // Why exact label: Skill docs and --status must share FormatMethodKey shape with apply.
            const string expectedGetterLabel =
                "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload"
                + ".HotReloadPropertyGetterFixture.get_HeightAmplitude()";
            string applyMethodLabel = null;
            foreach (HotReloadMethodOutcome outcome in patched.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Patched
                    && outcome.Method.Contains("get_HeightAmplitude"))
                {
                    applyMethodLabel = outcome.Method;
                    break;
                }
            }

            Assert.That(applyMethodLabel, Is.EqualTo(expectedGetterLabel));
            Assert.That(
                HotReloadPropertyGetterFixture.HeightAmplitude,
                Is.EqualTo(6f),
                "Patched getter must return the edited literal.");

            HotReloadTool tool = new HotReloadTool();
            UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(
                new JObject { ["Status"] = true },
                CancellationToken.None);
            HotReloadResponse status = baseResponse as HotReloadResponse;
            Assert.That(status, Is.Not.Null);
            Assert.That(status.Success, Is.True);

            bool foundActiveGetter = false;
            foreach (HotReloadMethodResult method in status.Methods)
            {
                if (method.Kind == "Active" && method.Method == expectedGetterLabel)
                {
                    foundActiveGetter = true;
                    Assert.That(method.InvocationCount, Is.GreaterThanOrEqualTo(1L));
                    Assert.That(
                        method.FilePath,
                        Is.EqualTo("Assets/Tests/Editor/HotReload/HotReloadShapeFixtures.cs"),
                        "Status Active rows must carry the project-relative source path from apply.");
                }
            }

            Assert.That(
                foundActiveGetter,
                Is.True,
                "Status must list Active get_HeightAmplitude after apply with the same Method label.");

            HotReloadPatcher.RevertAll();
            Assert.That(HotReloadPatcher.DescribeActivePatches(), Is.Empty);
            Assert.That(
                HotReloadPropertyGetterFixture.HeightAmplitude,
                Is.EqualTo(5f),
                "RevertAll must restore the compiled getter body.");
        }

        /// <summary>
        /// What: editing a method wrapped in #if UNITY_EDITOR still patches and returns the
        /// edited value. Directive trivia must not be copied onto the shim because the matching
        /// #endif belongs to the next token.
        /// </summary>
        [Test]
        public async Task Run_EditedIfDefGuardedMethod_PatchesBehavior()
        {
            string fixturePath = ResolveShapeFixturePath();
            string onDisk = File.ReadAllText(fixturePath);
            string editedSource = onDisk.Replace(
                "return 7; // directive-trivia probe",
                "return 42; // directive-trivia probe",
                StringComparison.Ordinal);
            Assert.That(editedSource, Is.Not.EqualTo(onDisk), "Precondition: guarded method body must differ.");

            string editedPath = WriteEditedSource("EditorGuardedReturn.cs", editedSource);

            HotReloadDirectiveTriviaFixture fixture = new HotReloadDirectiveTriviaFixture();
            Assert.That(fixture.EditorGuardedReturn(), Is.EqualTo(7));

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            AssertHasPatched(result, nameof(HotReloadDirectiveTriviaFixture.EditorGuardedReturn));
            Assert.That(fixture.EditorGuardedReturn(), Is.EqualTo(42));
        }

        /// <summary>
        /// What: editing a method that uses a sibling-file global using alias still patches and
        /// returns the edited value. The worker must collect assembly global usings; otherwise
        /// shim compile fails with CS0246.
        /// </summary>
        [Test]
        public async Task Run_EditedMethodUsingSiblingGlobalUsing_PatchesBehavior()
        {
            string fixturePath = ResolveShapeFixturePath();
            string onDisk = File.ReadAllText(fixturePath);
            string editedSource = onDisk.Replace(
                "builder.Append(\"base\");",
                "builder.Append(\"patched\");",
                StringComparison.Ordinal);
            Assert.That(editedSource, Is.Not.EqualTo(onDisk), "Precondition: global-alias body must differ.");

            string editedPath = WriteEditedSource("BuildWithGlobalAlias.cs", editedSource);

            HotReloadGlobalUsingFixture fixture = new HotReloadGlobalUsingFixture();
            Assert.That(fixture.BuildWithGlobalAlias(), Is.EqualTo("base"));

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            AssertHasPatched(result, nameof(HotReloadGlobalUsingFixture.BuildWithGlobalAlias));
            Assert.That(fixture.BuildWithGlobalAlias(), Is.EqualTo("patched"));
        }

        /// <summary>
        /// What: under Debug code optimization, size-only small methods do not emit the
        /// aggregated inline-risk warning (branch a); Patched Reason stays empty.
        /// </summary>
        [Test]
        public async Task Run_MultipleSmallPatchedMethods_OmitsInlineRiskWarningInDebug()
        {
            Assert.That(
                CompilationPipeline.codeOptimization,
                Is.EqualTo(CodeOptimization.Debug),
                "This regression pins Debug behavior; run under Debug code optimization.");

            string fixturePath = ResolveE2EFixturePath();
            string editedPath = WriteEditedSource(
                "MultipleSmallInlineRisk.cs",
                BuildFixtureSource(
                    computeWithPrivateMethod:
                    "public int ComputeWithPrivate(int delta)\n        {\n            return _secret + delta + 100;\n        }",
                    centerOfCellMethod:
                    "[MethodImpl(MethodImplOptions.NoInlining)]\n"
                    + "        public Vector3 CenterOfCell(Vector3Int cell)\n"
                    + "        {\n"
                    + "            return new Vector3(cell.x + 0.5f, cell.y + 0.5f, cell.z + 0.5f);\n"
                    + "        }"));

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            AssertHasPatched(result, nameof(HotReloadE2EFixture.ComputeWithPrivate));
            AssertHasPatched(result, nameof(HotReloadE2EFixture.CenterOfCell));

            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind != HotReloadMethodOutcomeKind.Patched)
                {
                    continue;
                }

                Assert.That(outcome.Reason, Is.Empty, "Patched Reason must not carry per-method inline-risk text.");
            }

            foreach (string warning in result.Warnings)
            {
                Assert.That(
                    warning.Contains("patched methods had pre-patch bodies"),
                    Is.False,
                    "Debug must not emit IL-size inline-risk warnings: " + warning);
            }
        }

        /// <summary>
        /// What: [AggressiveInlining] patched methods still emit exactly one aggregated
        /// inline-risk warning under Debug, listing every at-risk method.
        /// </summary>
        [Test]
        public async Task Run_MultipleAggressiveInliningMethods_AggregatesInlineRiskWarningOnce()
        {
            string fixturePath = ResolveE2EFixturePath();
            string editedPath = WriteEditedSource(
                "MultipleAggressiveInliningInlineRisk.cs",
                BuildFixtureSource(
                    computeWithPrivateMethod:
                    "public int ComputeWithPrivate(int delta)\n        {\n            return _secret + delta;\n        }",
                    inlineRiskAlphaMethod:
                    "[MethodImpl(MethodImplOptions.AggressiveInlining)]\n"
                    + "        public int InlineRiskAlpha()\n"
                    + "        {\n"
                    + "            return 11;\n"
                    + "        }",
                    inlineRiskBetaMethod:
                    "[MethodImpl(MethodImplOptions.AggressiveInlining)]\n"
                    + "        public int InlineRiskBeta()\n"
                    + "        {\n"
                    + "            return 22;\n"
                    + "        }"));

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            AssertHasPatched(result, nameof(HotReloadE2EFixture.InlineRiskAlpha));
            AssertHasPatched(result, nameof(HotReloadE2EFixture.InlineRiskBeta));

            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind != HotReloadMethodOutcomeKind.Patched)
                {
                    continue;
                }

                Assert.That(outcome.Reason, Is.Empty, "Patched Reason must not carry per-method inline-risk text.");
            }

            int aggregatedInlineRiskCount = 0;
            foreach (string warning in result.Warnings)
            {
                if (warning.Contains("patched methods had pre-patch bodies")
                    && warning.Contains(nameof(HotReloadE2EFixture.InlineRiskAlpha))
                    && warning.Contains(nameof(HotReloadE2EFixture.InlineRiskBeta)))
                {
                    aggregatedInlineRiskCount++;
                }
            }

            Assert.That(
                aggregatedInlineRiskCount,
                Is.EqualTo(1),
                "Expected exactly one aggregated inline-risk warning listing the at-risk methods.");
            Assert.That(
                CountWarningsContaining(result.Warnings, "Edits outside method bodies"),
                Is.EqualTo(1),
                "Reconstructed fixture source differs outside method bodies once.");
            Assert.That(
                CountWarningsContaining(result.Warnings, "Removed members stay present"),
                Is.EqualTo(1),
                "Reconstructed fixture source omits compiled members once.");
            int staleSignatureCount = CountWarningsContaining(
                result.Warnings,
                "Compiled call sites of the removed signature");
            Assert.That(
                result.Warnings.Count,
                Is.EqualTo(3 + staleSignatureCount),
                "Exactly one inline-risk, one drift, one removed-members warning, plus stale-signature rows.");
        }

        /// <summary>
        /// What: duplicate file inputs re-patch the same small method yet Debug emits no
        /// IL-size inline-risk warning (branch a); declaration-drift warnings still appear twice.
        /// </summary>
        [Test]
        public async Task Run_DuplicateFileInputs_OmitsInlineRiskWarningInDebug()
        {
            Assert.That(
                CompilationPipeline.codeOptimization,
                Is.EqualTo(CodeOptimization.Debug),
                "This regression pins Debug behavior; run under Debug code optimization.");

            string fixturePath = ResolveE2EFixturePath();
            string editedPath = WriteEditedSource(
                "DuplicateInputInlineRisk.cs",
                BuildFixtureSource(
                    computeWithPrivateMethod:
                    "public int ComputeWithPrivate(int delta)\n        {\n            return _secret + delta + 100;\n        }"));

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath, fixturePath },
                editedPath,
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            AssertHasPatched(result, nameof(HotReloadE2EFixture.ComputeWithPrivate));

            // Methods and PatchedTotal keep reflecting raw patch operations on purpose:
            // the duplicated input re-patches every fixture method once per file entry.
            int computeWithPrivatePatchedOperations = 0;
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Patched
                    && outcome.Method.Contains(nameof(HotReloadE2EFixture.ComputeWithPrivate)))
                {
                    computeWithPrivatePatchedOperations++;
                }
            }

            Assert.That(computeWithPrivatePatchedOperations, Is.EqualTo(2));

            foreach (string warning in result.Warnings)
            {
                Assert.That(
                    warning.Contains("patched methods had pre-patch bodies"),
                    Is.False,
                    "Debug must not emit IL-size inline-risk warnings: " + warning);
            }

            int declarationDriftCount = CountWarningsContaining(
                result.Warnings,
                "Edits outside method bodies");
            Assert.That(
                declarationDriftCount,
                Is.EqualTo(2),
                "Duplicate file inputs each emit a declaration-drift warning.");
        }

        /// <summary>
        /// What: duplicate file inputs re-patch an [AggressiveInlining] method twice yet the
        /// aggregated warning lists that method once.
        /// </summary>
        [Test]
        public async Task Run_DuplicateFileInputs_ListsEachAtRiskMethodOnceInAggregatedWarning()
        {
            string fixturePath = ResolveE2EFixturePath();
            string editedPath = WriteEditedSource(
                "DuplicateInputAggressiveInliningInlineRisk.cs",
                BuildFixtureSource(
                    computeWithPrivateMethod:
                    "public int ComputeWithPrivate(int delta)\n        {\n            return _secret + delta;\n        }",
                    inlineRiskAlphaMethod:
                    "[MethodImpl(MethodImplOptions.AggressiveInlining)]\n"
                    + "        public int InlineRiskAlpha()\n"
                    + "        {\n"
                    + "            return 11;\n"
                    + "        }"));

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath, fixturePath },
                editedPath,
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            AssertHasPatched(result, nameof(HotReloadE2EFixture.InlineRiskAlpha));

            // Methods and PatchedTotal keep reflecting raw patch operations on purpose:
            // the duplicated input re-patches every fixture method once per file entry.
            int inlineRiskAlphaPatchedOperations = 0;
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Patched
                    && outcome.Method.Contains(nameof(HotReloadE2EFixture.InlineRiskAlpha)))
                {
                    inlineRiskAlphaPatchedOperations++;
                }
            }

            Assert.That(inlineRiskAlphaPatchedOperations, Is.EqualTo(2));

            Assert.That(
                CountWarningsContaining(result.Warnings, "Edits outside method bodies"),
                Is.EqualTo(2),
                "Duplicate file inputs each emit a declaration-drift warning.");
            Assert.That(
                CountWarningsContaining(result.Warnings, "Removed members stay present"),
                Is.EqualTo(2),
                "Duplicate file inputs each emit a removed-members warning.");
            int staleSignatureCount = CountWarningsContaining(
                result.Warnings,
                "Compiled call sites of the removed signature");
            Assert.That(
                result.Warnings.Count,
                Is.EqualTo(5 + staleSignatureCount),
                "One aggregated inline-risk warning plus two drift, two removed-members, and stale-signature rows.");

            string aggregatedWarning = null;
            foreach (string warning in result.Warnings)
            {
                if (warning.Contains("patched methods had pre-patch bodies")
                    && warning.Contains(nameof(HotReloadE2EFixture.InlineRiskAlpha)))
                {
                    aggregatedWarning = warning;
                    break;
                }
            }

            Assert.That(aggregatedWarning, Is.Not.Null, "Expected an aggregated inline-risk warning.");
            Assert.That(
                CountOccurrences(aggregatedWarning, nameof(HotReloadE2EFixture.InlineRiskAlpha)),
                Is.EqualTo(1),
                "The aggregated warning must list a re-patched method once, not once per patch operation.");
        }

        /// <summary>
        /// What: a mixed transplant+delegation fixture patches both the sync private-access
        /// method (transplant) and the LINQ private-access method (delegation).
        /// </summary>
        [Test]
        public async Task Run_MixedTransplantAndDelegationFile_PatchesBoth()
        {
            string fixturePath = ResolveE2EFixturePath();
            string editedPath = WriteEditedSource(
                "MixedTransplantDelegation.cs",
                BuildFixtureSource(
                    computeWithPrivateMethod:
                    "public int ComputeWithPrivate(int delta)\n        {\n            return _secret + delta + 100;\n        }",
                    queryPrivateMethod:
                    "public int QueryPrivate()\n        {\n            int[] values = { 1, 2, 3 };\n"
                    + "            return (from value in values where value < _secret select value).Count() + 100;\n        }"));

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            AssertHasPatched(result, nameof(HotReloadE2EFixture.ComputeWithPrivate));
            AssertHasPatched(result, nameof(HotReloadE2EFixture.QueryPrivate));

            HotReloadE2EFixture fixture = new HotReloadE2EFixture();
            Assert.That(fixture.ComputeWithPrivate(5), Is.EqualTo(10 + 5 + 100));
            Assert.That(fixture.QueryPrivate(), Is.EqualTo(3 + 100));
        }

        /// <summary>
        /// What: hot-reloading an async body that writes a private field and calls a private
        /// method applies a delegation patch and changes the await result.
        /// </summary>
        [Test]
        public async Task Run_EditedAsyncPrivateFieldAndMethod_PatchesBehavior()
        {
            string fixturePath = ResolveE2EFixturePath();
            string editedPath = WriteEditedSource(
                "AsyncPrivateFieldAndMethod.cs",
                BuildFixtureSource(
                    computeWithPrivateMethod:
                    "public int ComputeWithPrivate(int delta)\n        {\n            return _secret + delta;\n        }",
                    asyncPrivateFieldAndMethod:
                    "public async Task<int> AsyncPrivateFieldAndMethod(int delta)\n        {\n"
                    + "            await Task.Yield();\n"
                    + "            _secret += delta;\n"
                    + "            BumpSecretBy(1);\n"
                    + "            return _secret;\n"
                    + "        }"));

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            AssertHasPatched(result, nameof(HotReloadE2EFixture.AsyncPrivateFieldAndMethod));

            HotReloadE2EFixture fixture = new HotReloadE2EFixture();
            Assert.That(await fixture.AsyncPrivateFieldAndMethod(5), Is.EqualTo(10 + 5 + 1));
            Assert.That(fixture.SecretForAssert, Is.EqualTo(10 + 5 + 1));
        }

        /// <summary>
        /// What: hot-reloading an iterator body with private field write + private method call
        /// applies a delegation patch and changes the yielded value.
        /// </summary>
        [Test]
        public async Task Run_EditedIteratorPrivateAccess_PatchesBehavior()
        {
            string fixturePath = ResolveE2EFixturePath();
            string editedPath = WriteEditedSource(
                "IteratePrivate.cs",
                BuildFixtureSource(
                    computeWithPrivateMethod:
                    "public int ComputeWithPrivate(int delta)\n        {\n            return _secret + delta;\n        }",
                    iteratePrivateMethod:
                    "public IEnumerator IteratePrivate(int delta)\n        {\n"
                    + "            _secret += delta;\n"
                    + "            BumpSecretBy(1);\n"
                    + "            yield return _secret;\n"
                    + "        }"));

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            AssertHasPatched(result, nameof(HotReloadE2EFixture.IteratePrivate));

            HotReloadE2EFixture fixture = new HotReloadE2EFixture();
            IEnumerator enumerator = fixture.IteratePrivate(5);
            Assert.That(enumerator.MoveNext(), Is.True);
            Assert.That(enumerator.Current, Is.EqualTo(10 + 5 + 1));
            Assert.That(fixture.SecretForAssert, Is.EqualTo(10 + 5 + 1));
        }

        /// <summary>
        /// What: hot-reloading a method whose lambda captures a private field applies a
        /// delegation patch and changes the returned value.
        /// </summary>
        [Test]
        public async Task Run_EditedLambdaPrivateCapture_PatchesBehavior()
        {
            string fixturePath = ResolveE2EFixturePath();
            string editedPath = WriteEditedSource(
                "LambdaPrivate.cs",
                BuildFixtureSource(
                    computeWithPrivateMethod:
                    "public int ComputeWithPrivate(int delta)\n        {\n            return _secret + delta;\n        }",
                    lambdaPrivateMethod:
                    "public int LambdaPrivate(int threshold)\n        {\n"
                    + "            System.Func<int, bool> pred = v => v < (_secret + 100);\n"
                    + "            return pred(threshold) ? 7 : 0;\n"
                    + "        }"));

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            AssertHasPatched(result, nameof(HotReloadE2EFixture.LambdaPrivate));

            HotReloadE2EFixture fixture = new HotReloadE2EFixture();
            Assert.That(fixture.LambdaPrivate(5), Is.EqualTo(7));
        }

        /// <summary>
        /// What: hot-reloading a method that reads/writes a private property applies a
        /// delegation patch and changes the round-trip result.
        /// </summary>
        [Test]
        public async Task Run_EditedPrivatePropertyRoundTrip_PatchesBehavior()
        {
            string fixturePath = ResolveE2EFixturePath();
            string editedPath = WriteEditedSource(
                "PropertyPrivateRoundTrip.cs",
                BuildFixtureSource(
                    computeWithPrivateMethod:
                    "public int ComputeWithPrivate(int delta)\n        {\n            return _secret + delta;\n        }",
                    propertyPrivateRoundTripMethod:
                    "public int PropertyPrivateRoundTrip(int value)\n        {\n"
                    + "            HiddenScore = value + 100;\n"
                    + "            return HiddenScore;\n"
                    + "        }"));

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            AssertHasPatched(result, nameof(HotReloadE2EFixture.PropertyPrivateRoundTrip));

            HotReloadE2EFixture fixture = new HotReloadE2EFixture();
            Assert.That(fixture.PropertyPrivateRoundTrip(5), Is.EqualTo(105));
        }

        /// <summary>
        /// What: an async body that names an internal type as a local stays Skipped with a
        /// type-visibility reason (accessor delegates cannot rescue type mentions).
        /// </summary>
        [Test]
        public async Task Run_AsyncUsesInternalType_IsSkippedWithTypeVisibilityReason()
        {
            string fixturePath = ResolveE2EFixturePath();
            string editedPath = WriteEditedSource(
                "AsyncUsesInternalType.cs",
                BuildFixtureSource(
                    computeWithPrivateMethod:
                    "public int ComputeWithPrivate(int delta)\n        {\n            return _secret + delta;\n        }",
                    asyncUsesInternalTypeMethod:
                    "public async Task<int> AsyncUsesInternalType()\n        {\n"
                    + "            await Task.Yield();\n"
                    + "            HotReloadE2EInternalToken token = new HotReloadE2EInternalToken { N = 99 };\n"
                    + "            return token.N;\n"
                    + "        }"));

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);

            AssertNoFileLevelFailure(result);

            bool found = false;
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Skipped
                    && outcome.Method.Contains(nameof(HotReloadE2EFixture.AsyncUsesInternalType))
                    && outcome.Reason.Contains("not visible"))
                {
                    found = true;
                }
            }

            Assert.That(
                found,
                Is.True,
                "Expected AsyncUsesInternalType to be Skipped for an inaccessible type mention.\n"
                + FormatOutcomes(result));
        }

        /// <summary>
        /// What: BindShimAccessors invokes each type's public static parameterless
        /// __BindAccessors, skips types without one, and records a throwing binder as a failure
        /// keyed by the shim type's short name with the cause message and a compile-and-retry
        /// hint.
        /// </summary>
        [Test]
        public void BindShimAccessors_ThrowingBinder_IsReportedByShimTypeName()
        {
            int callsBefore = HotReloadBindProbeShim.BindCalls;

            Dictionary<string, string> failures = HotReloadEntryApplier.BindShimAccessors(
                typeof(HotReloadBindFailShim).Assembly);

            Assert.That(HotReloadBindProbeShim.BindCalls, Is.EqualTo(callsBefore + 1));
            Assert.That(failures.Count, Is.EqualTo(1));
            Assert.That(failures.ContainsKey(nameof(HotReloadBindFailShim)), Is.True);
            Assert.That(failures[nameof(HotReloadBindFailShim)], Does.Contain("no such member"));
            Assert.That(
                failures[nameof(HotReloadBindFailShim)],
                Does.Contain("Run 'uloop compile' and retry."));
        }

        /// <summary>
        /// What: an edited body that calls a non-existent helper fails shim compile with the
        /// new-member hint (Failed, not a silent skip).
        /// </summary>
        [Test]
        public async Task Run_EditedBodyCallingMissingHelper_FailsShimCompileWithHint()
        {
            string fixturePath = ResolveE2EFixturePath();
            string editedPath = WriteEditedSource(
                "MissingHelper.cs",
                BuildFixtureSource(
                    computeWithPrivateMethod:
                    "public int ComputeWithPrivate(int delta)\n        {\n            return _secret + delta;\n        }",
                    callsMissingHelperMethod:
                    "public int CallsMissingHelper(int value)\n        {\n            return MissingHelperAddedByEdit(value);\n        }"));

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);

            bool foundFailure = false;
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Failed
                    && outcome.Reason.Contains(HotReloadConstants.NewMemberCompileHint))
                {
                    foundFailure = true;
                }
            }

            Assert.That(
                foundFailure,
                Is.True,
                "Expected shim-compile Failed carrying the new-member hint. Outcomes:\n"
                + FormatOutcomes(result));
        }

        /// <summary>
        /// What: a caller that invokes an added generic method fails shim compile, and the
        /// Failed reason's last line is the skipped-member note for that generic add.
        /// </summary>
        [Test]
        public async Task Run_EditedCallerOfAddedGeneric_FailsWithSkippedMemberNote()
        {
            const string genericSkipReason =
                "Added generic methods are skipped; hot reload cannot emit a typed shim for them. "
                + "Run 'uloop compile'.";
            string expectedLastLine = string.Format(
                HotReloadConstants.SkippedMemberCompileFailureNoteFormat,
                "DescribeValue",
                genericSkipReason);
            string fixturePath = ResolveE2EFixturePath();
            // Why also edit ComputeWithPrivate: this test protects the isolation path
            // (BuildFailedMethodOutcomes). The single-entry fallback is covered separately.
            string editedPath = WriteEditedSource(
                "AddedGenericCaller.cs",
                BuildFixtureSource(
                    computeWithPrivateMethod:
                    "public int ComputeWithPrivate(int delta)\n        {\n            return _secret + delta + 100;\n        }",
                    callsMissingHelperMethod:
                    "public int CallsMissingHelper(int value)\n        {\n            return DescribeValue(value);\n        }\n\n"
                    + "        private int DescribeValue<T>(T value)\n        {\n            return 42;\n        }"));

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);

            string failedReason = null;
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Failed
                    && outcome.Method.Contains(nameof(HotReloadE2EFixture.CallsMissingHelper)))
                {
                    failedReason = outcome.Reason;
                    break;
                }
            }

            Assert.That(
                failedReason,
                Is.Not.Null,
                "Expected CallsMissingHelper to fail shim compile.\n" + FormatOutcomes(result));
            string[] reasonLines = failedReason.Replace("\r\n", "\n").Split('\n');
            Assert.That(reasonLines[reasonLines.Length - 1], Is.EqualTo(expectedLastLine));
        }

        /// <summary>
        /// What: editing only the caller of an added generic method (single shim entry) still
        /// ends the Failed reason with the skipped-member note.
        /// </summary>
        [Test]
        public async Task Run_SingleEditedCallerOfAddedGeneric_FailsWithSkippedMemberNote()
        {
            const string genericSkipReason =
                "Added generic methods are skipped; hot reload cannot emit a typed shim for them. "
                + "Run 'uloop compile'.";
            string expectedLastLine = string.Format(
                HotReloadConstants.SkippedMemberCompileFailureNoteFormat,
                "DescribeValue",
                genericSkipReason);
            string fixturePath = ResolveE2EFixturePath();
            string editedPath = WriteEditedSource(
                "AddedGenericCallerSingleEntry.cs",
                BuildFixtureSource(
                    computeWithPrivateMethod:
                    "public int ComputeWithPrivate(int delta)\n        {\n            return _secret + delta;\n        }",
                    callsMissingHelperMethod:
                    "public int CallsMissingHelper(int value)\n        {\n            return DescribeValue(value);\n        }\n\n"
                    + "        private int DescribeValue<T>(T value)\n        {\n            return 42;\n        }"));

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);

            string failedReason = null;
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Failed
                    && outcome.Method.Contains(nameof(HotReloadE2EFixture.CallsMissingHelper)))
                {
                    failedReason = outcome.Reason;
                    break;
                }
            }

            Assert.That(
                failedReason,
                Is.Not.Null,
                "Expected CallsMissingHelper to fail shim compile.\n" + FormatOutcomes(result));
            string[] reasonLines = failedReason.Replace("\r\n", "\n").Split('\n');
            Assert.That(reasonLines[reasonLines.Length - 1], Is.EqualTo(expectedLastLine));
        }

        /// <summary>
        /// What: editing a const value emits a drift warning naming both values while the run
        /// itself stays successful (methods still patch).
        /// </summary>
        [Test]
        public async Task Run_EditedConstValue_WarnsConstDrift()
        {
            string fixturePath = ResolveE2EFixturePath();
            string editedPath = WriteEditedSource(
                "ConstDrift.cs",
                BuildFixtureSource(
                    computeWithPrivateMethod:
                    "public int ComputeWithPrivate(int delta)\n        {\n            return _secret + delta + 100;\n        }",
                    tuningConstDeclaration:
                    "private const int TuningConst = 4;"));

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            AssertHasPatched(result, nameof(HotReloadE2EFixture.ComputeWithPrivate));

            bool foundDrift = false;
            foreach (string warning in result.Warnings)
            {
                if (warning.Contains("TuningConst")
                    && warning.Contains("is 4 in the edited source but 3 in the compiled assembly")
                    && warning.Contains("uloop compile"))
                {
                    foundDrift = true;
                }
            }

            Assert.That(
                foundDrift,
                Is.True,
                "Expected a const drift warning for TuningConst.\n"
                + string.Join("\n", result.Warnings));
        }

        /// <summary>
        /// What: hot-reloading only the referencing file still reports a sibling const-drift
        /// warning when the holder file's on-disk bytes differ from the snapshot.
        /// </summary>
        [Test]
        public async Task Run_ReferencingFileOnly_WarnsSiblingConstDrift()
        {
            using (MutateSiblingTuningValue(7))
            {
                HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                    new[] { ResolveSiblingConstUserPath() },
                    null,
                    CancellationToken.None);

                AssertNoFileLevelFailure(result);
                Assert.That(
                    result.Warnings,
                    Has.Some.EqualTo(ExpectedSiblingTuningDriftWarning),
                    "Expected sibling const drift when only the referencing file is passed.\n"
                    + string.Join("\n", result.Warnings));
            }
        }

        /// <summary>
        /// What: passing both the holder and the referencing file emits the sibling const-drift
        /// warning once after string-equal dedupe.
        /// </summary>
        [Test]
        public async Task Run_HolderAndReferencingFiles_DedupesSiblingConstDriftWarning()
        {
            using (MutateSiblingTuningValue(7))
            {
                HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                    new[] { ResolveSiblingConstUserPath(), ResolveSiblingConstDefinitionsPath() },
                    null,
                    CancellationToken.None);

                AssertNoFileLevelFailure(result);
                int matchCount = 0;
                foreach (string warning in result.Warnings)
                {
                    if (warning == ExpectedSiblingTuningDriftWarning)
                    {
                        matchCount++;
                    }
                }

                Assert.That(
                    matchCount,
                    Is.EqualTo(1),
                    "Expected the sibling const-drift warning once after dedupe.\n"
                    + string.Join("\n", result.Warnings));
            }
        }

        /// <summary>
        /// What: hiding the assembly snapshot directory skips sibling const-drift warnings.
        /// </summary>
        [Test]
        public async Task Run_WhenAssemblySnapshotMissing_DoesNotWarnSiblingConstDrift()
        {
            using (MutateSiblingTuningValue(7))
            using (HideAssemblySnapshotDirectory())
            {
                HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                    new[] { ResolveSiblingConstUserPath() },
                    null,
                    CancellationToken.None);

                AssertNoFileLevelFailure(result);
                foreach (string warning in result.Warnings)
                {
                    Assert.That(
                        warning.Contains("SiblingTuning"),
                        Is.False,
                        "Sibling const drift must stay silent without a snapshot.\n" + warning);
                }
            }
        }

        /// <summary>
        /// What: ProcessFileAsync surfaces the sibling scan-cap warning when 51 assembly
        /// siblings differ from their snapshots by a trailing comment (no const drift).
        /// </summary>
        [Test]
        public async Task Run_WhenFiftyOneSiblingsChanged_WarnsScanLimit()
        {
            using (TouchSmallestSiblingsWithTrailingComment(ResolveSiblingConstUserPath(), 51))
            {
                HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                    new[] { ResolveSiblingConstUserPath() },
                    null,
                    CancellationToken.None);

                AssertNoFileLevelFailure(result);
                Assert.That(
                    result.Warnings,
                    Has.Some.EqualTo(
                        "sibling const-drift scan limited to first 50 changed files (51 total)"),
                    "Expected the orchestrator to copy the sibling scan-cap warning.\n"
                    + string.Join("\n", result.Warnings));
            }
        }

        /// <summary>
        /// What: an unchanged const produces no drift warning.
        /// </summary>
        [Test]
        public async Task Run_UnchangedConst_HasNoDriftWarning()
        {
            string fixturePath = ResolveE2EFixturePath();
            string editedPath = WriteEditedSource(
                "ConstUnchanged.cs",
                BuildFixtureSource(
                    computeWithPrivateMethod:
                    "public int ComputeWithPrivate(int delta)\n        {\n            return _secret + delta;\n        }"));

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);

            AssertNoFileLevelFailure(result);

            foreach (string warning in result.Warnings)
            {
                Assert.That(
                    warning.Contains("TuningConst"),
                    Is.False,
                    "Unexpected drift warning: " + warning);
            }
        }

        /// <summary>
        /// What: editing an enum member value emits a drift warning naming both values; enum
        /// members ride the same const drift detection as class consts.
        /// </summary>
        [Test]
        public async Task Run_EditedEnumMemberValue_WarnsConstDrift()
        {
            string fixturePath = ResolveE2EFixturePath();
            string editedPath = WriteEditedSource(
                "EnumDrift.cs",
                BuildFixtureSource(
                    computeWithPrivateMethod:
                    "public int ComputeWithPrivate(int delta)\n        {\n            return _secret + delta;\n        }",
                    modeEnumDeclaration:
                    "public enum HotReloadE2EMode\n    {\n        Idle = 0,\n        Active = 2\n    }"));

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);

            AssertNoFileLevelFailure(result);

            bool foundDrift = false;
            foreach (string warning in result.Warnings)
            {
                if (warning.Contains("HotReloadE2EMode.Active")
                    && warning.Contains("is 2 in the edited source but 1 in the compiled assembly")
                    && warning.Contains("uloop compile"))
                {
                    foundDrift = true;
                }
            }

            Assert.That(
                foundDrift,
                Is.True,
                "Expected an enum member drift warning for HotReloadE2EMode.Active.\n"
                + string.Join("\n", result.Warnings));
        }

        /// <summary>
        /// What: a const-only edited source (no method bodies, so no shim and no entries) still
        /// surfaces the drift warning through the empty-entries early return.
        /// </summary>
        [Test]
        public async Task Run_ConstOnlyEditedSource_StillWarnsConstDrift()
        {
            string fixturePath = ResolveE2EFixturePath();
            string editedPath = WriteEditedSource(
                "ConstOnlyDrift.cs",
                "namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload\n"
                + "{\n"
                + "    public class HotReloadE2EFixture\n"
                + "    {\n"
                + "        private const int TuningConst = 5;\n"
                + "    }\n"
                + "}\n");

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            Assert.That(result.PatchedTotal, Is.EqualTo(0));

            bool foundDrift = false;
            foreach (string warning in result.Warnings)
            {
                if (warning.Contains("TuningConst")
                    && warning.Contains("is 5 in the edited source but 3 in the compiled assembly")
                    && warning.Contains("uloop compile"))
                {
                    foundDrift = true;
                }
            }

            Assert.That(
                foundDrift,
                Is.True,
                "Expected a const drift warning from the const-only early-return path.\n"
                + string.Join("\n", result.Warnings));
        }

        /// <summary>
        /// What: partial declarations of one type in a single edited file produce exactly one
        /// drift warning per const, not one per declaration.
        /// </summary>
        [Test]
        public async Task Run_PartialTypeConst_WarnsOnce()
        {
            string fixturePath = ResolveE2EFixturePath();
            string editedPath = WriteEditedSource(
                "PartialConstDrift.cs",
                "namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload\n"
                + "{\n"
                + "    public partial class HotReloadE2EFixture\n"
                + "    {\n"
                + "        private const int TuningConst = 6;\n"
                + "    }\n"
                + "\n"
                + "    public partial class HotReloadE2EFixture\n"
                + "    {\n"
                + "    }\n"
                + "}\n");

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);

            AssertNoFileLevelFailure(result);

            int driftCount = 0;
            foreach (string warning in result.Warnings)
            {
                if (warning.Contains("TuningConst"))
                {
                    driftCount++;
                }
            }

            Assert.That(
                driftCount,
                Is.EqualTo(1),
                "Expected exactly one drift warning for TuningConst.\n"
                + string.Join("\n", result.Warnings));
        }

        /// <summary>
        /// What: replacing compiled property Hp with a field surfaces the named warning on
        /// HotReloadOrchestratorResult.Warnings, not only the worker DTO.
        /// </summary>
        [Test]
        public async Task Run_PropertyRewrittenAsField_WarnsOnOrchestratorResult()
        {
            string fixturePath = ResolveAddedMemberHostPath();
            string onDisk = File.ReadAllText(fixturePath);
            string edited = onDisk.Replace(
                "        public int Hp { get; set; }",
                "        public int Hp;",
                StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk));
            string editedPath = WriteEditedSource("OrchestratorPropertyKindChange.cs", edited);

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            string expectedWarning = string.Format(
                "Compiled property '{0}' was removed or redeclared as a different member kind in the edited source; the compiled member stays until 'uloop compile'.",
                typeof(HotReloadFieldKindChangeFixture).FullName + ".Hp");
            Assert.That(
                result.Warnings,
                Does.Contain(expectedWarning),
                string.Join("\n", result.Warnings));
        }

        /// <summary>
        /// What: a file declaring a field-like event hot-reloads its subscriber and handler methods
        /// (no CS0229 from the publicized backing field) — the edited subscriber body (net two
        /// subscriptions via += / -=) must actually apply (HandledCount == 10, not the original
        /// body's 5) and EnableCounting must be Patched — while the raising method (edited to a
        /// double Invoke so it is not treated as unchanged) is Skipped instead of killing the
        /// whole file.
        /// </summary>
        [Test]
        public async Task Run_FieldLikeEventFile_PatchesHandlerAndSkipsRaiser()
        {
            string fixturePath = ResolveEventFixturePath();
            string editedPath = WriteEditedSource(
                "EventFixtureEdit.cs",
                BuildEventFixtureSource(
                    "[MethodImpl(MethodImplOptions.NoInlining)]\n        public void HandleScoreChanged()\n        {\n            HandledCount = HandledCount + 5;\n        }"));

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            AssertHasPatched(result, nameof(HotReloadEventFixture.HandleScoreChanged));
            AssertHasPatched(result, nameof(HotReloadEventFixture.EnableCounting));

            bool raiserSkipped = false;
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Skipped
                    && outcome.Method.Contains(nameof(HotReloadEventFixture.RaiseScore))
                    && outcome.Reason.Contains("event"))
                {
                    raiserSkipped = true;
                }
            }

            Assert.That(raiserSkipped, Is.True,
                "RaiseScore must be Skipped with the event-use reason.\n" + FormatOutcomes(result));

            HotReloadEventFixture fixture = new HotReloadEventFixture();
            fixture.EnableCounting();
            fixture.RaiseScore();
            Assert.That(fixture.HandledCount, Is.EqualTo(10));
        }

        /// <summary>
        /// What: after patching an edited method, a later hot-reload against the on-disk baseline
        /// reverts that patch so runtime behavior and ActivePatchTotal converge to the compiled IL.
        /// </summary>
        [Test]
        public async Task Run_RevertedEditAfterPatch_RestoresOriginalBehaviorAndClearsPatch()
        {
            string fixturePath = ResolveE2EFixturePath();
            string editedPath = WriteEditedSource(
                "RevertConvergence.cs",
                BuildFixtureSource(
                    computeWithPrivateMethod:
                    "public int ComputeWithPrivate(int delta)\n        {\n            return _secret + delta + 100;\n        }"));

            HotReloadOrchestratorResult patched = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);

            AssertNoFileLevelFailure(patched);
            AssertHasPatched(patched, nameof(HotReloadE2EFixture.ComputeWithPrivate));
            HotReloadE2EFixture fixture = new HotReloadE2EFixture();
            Assert.That(fixture.ComputeWithPrivate(5), Is.EqualTo(115));
            Assert.That(patched.ActivePatchTotal, Is.EqualTo(1));

            HotReloadOrchestratorResult reverted = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                contentPathOverride: null,
                CancellationToken.None);

            AssertNoFileLevelFailure(reverted);
            Assert.That(reverted.Methods, Is.Empty, FormatOutcomes(reverted));
            Assert.That(reverted.UnchangedTotal, Is.GreaterThan(0));
            Assert.That(reverted.ActivePatchTotal, Is.EqualTo(0));
            Assert.That(fixture.ComputeWithPrivate(5), Is.EqualTo(15));
        }

        /// <summary>
        /// What: reloading the same edited source a second time reports AlreadyActive and
        /// leaves the existing Harmony patch in place so InvocationCount is preserved.
        /// </summary>
        [Test]
        public async Task Run_IdenticalSourceSecondReload_ReportsAlreadyActiveAndKeepsInvocationCount()
        {
            string fixturePath = ResolveE2EFixturePath();
            string editedPath = WriteEditedSource(
                "IdenticalSourceSecondReload.cs",
                BuildFixtureSource(
                    computeWithPrivateMethod:
                    "public int ComputeWithPrivate(int delta)\n        {\n            return _secret + delta + 100;\n        }"));

            HotReloadOrchestratorResult first = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);

            AssertNoFileLevelFailure(first);
            AssertHasPatched(first, nameof(HotReloadE2EFixture.ComputeWithPrivate));
            HotReloadE2EFixture fixture = new HotReloadE2EFixture();
            Assert.That(fixture.ComputeWithPrivate(5), Is.EqualTo(115));

            MethodInfo computeMethod = typeof(HotReloadE2EFixture).GetMethod(
                nameof(HotReloadE2EFixture.ComputeWithPrivate));
            Assert.That(computeMethod, Is.Not.Null);
            string methodKey = HotReloadPatcher.FormatMethodKey(computeMethod);
            Assert.That(HotReloadInvocationRegistry.GetCount(methodKey), Is.EqualTo(1L));

            HotReloadOrchestratorResult second = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);

            AssertNoFileLevelFailure(second);
            AssertHasAlreadyActive(second, nameof(HotReloadE2EFixture.ComputeWithPrivate));
            Assert.That(second.PatchedTotal, Is.EqualTo(0));
            Assert.That(
                HotReloadInvocationRegistry.GetCount(methodKey),
                Is.EqualTo(1L),
                "The second reload must not replace the patch; InvocationCount stays at the pre-reload value.");
            Assert.That(fixture.ComputeWithPrivate(5), Is.EqualTo(115));
            Assert.That(HotReloadInvocationRegistry.GetCount(methodKey), Is.EqualTo(2L));
            AssertNoUnchangedSourceNonBaselineWarning(second);
        }

        /// <summary>
        /// What: after a patch, reloading the compiled on-disk baseline still peels that patch
        /// (the revert-to-compiled path is not swallowed by the identical-source short-circuit).
        /// </summary>
        [Test]
        public async Task Run_RevertToCompiledBaseline_PeelsPatchAndDoesNotReportAlreadyActive()
        {
            string fixturePath = ResolveE2EFixturePath();
            string editedPath = WriteEditedSource(
                "RevertBaselineNotAlreadyActive.cs",
                BuildFixtureSource(
                    computeWithPrivateMethod:
                    "public int ComputeWithPrivate(int delta)\n        {\n            return _secret + delta + 100;\n        }"));

            HotReloadOrchestratorResult patched = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);

            AssertNoFileLevelFailure(patched);
            AssertHasPatched(patched, nameof(HotReloadE2EFixture.ComputeWithPrivate));
            HotReloadE2EFixture fixture = new HotReloadE2EFixture();
            Assert.That(fixture.ComputeWithPrivate(5), Is.EqualTo(115));

            HotReloadOrchestratorResult reverted = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                contentPathOverride: null,
                CancellationToken.None);

            AssertNoFileLevelFailure(reverted);
            Assert.That(reverted.Methods, Is.Empty, FormatOutcomes(reverted));
            AssertNoAlreadyActive(reverted);
            Assert.That(reverted.UnchangedTotal, Is.GreaterThan(0));
            Assert.That(reverted.ActivePatchTotal, Is.EqualTo(0));
            Assert.That(fixture.ComputeWithPrivate(5), Is.EqualTo(15));
        }

        /// <summary>
        /// What: a run that reports Failed does not short-circuit a later identical reload, so
        /// the same Failed outcome is reported again instead of AlreadyActive.
        /// </summary>
        [Test]
        public async Task Run_FailedMixThenIdenticalReload_DoesNotShortCircuitAndRereportsFailed()
        {
            string fixturePath = ResolveE2EFixturePath();
            string editedPath = WriteEditedSource(
                "FailedMixThenIdenticalReload.cs",
                BuildFixtureSource(
                    computeWithPrivateMethod:
                    "public int ComputeWithPrivate(int delta)\n        {\n            return _secret + delta + 100;\n        }",
                    callsMissingHelperMethod:
                    "public int CallsMissingHelper(int value)\n        {\n            return MissingHelperAddedByEdit(value);\n        }"));

            HotReloadOrchestratorResult first = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);

            AssertHasPatched(first, nameof(HotReloadE2EFixture.ComputeWithPrivate));
            AssertHasFailed(first, nameof(HotReloadE2EFixture.CallsMissingHelper));

            HotReloadOrchestratorResult second = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);

            AssertHasPatched(second, nameof(HotReloadE2EFixture.ComputeWithPrivate));
            AssertHasFailed(second, nameof(HotReloadE2EFixture.CallsMissingHelper));
            AssertNoAlreadyActive(second);
            AssertHasUnchangedSourceNonBaselineWarning(second);
        }

        /// <summary>
        /// What: a later run that Skips a previously patched method while still Patching a
        /// sibling is not recorded, so an identical third reload re-reports the Skip instead of
        /// AlreadyActive.
        /// </summary>
        [Test]
        public async Task Run_SkippedMixThenIdenticalReload_DoesNotShortCircuitAndRereportsSkipped()
        {
            string fixturePath = ResolveE2EFixturePath();
            string firstSource = BuildFixtureSource(
                computeWithPrivateMethod:
                "public int ComputeWithPrivate(int delta)\n        {\n            return _secret + delta + 100;\n        }",
                sumGridMethod:
                "public int SumGrid(int[,] grid)\n        {\n            return 42;\n        }");
            string skippedMixSource = BuildFixtureSource(
                computeWithPrivateMethod:
                "public int ComputeWithPrivate(int delta)\n        {\n            return base.BaseSeed() + delta;\n        }",
                sumGridMethod:
                "public int SumGrid(int[,] grid)\n        {\n            return 42;\n        }");
            string firstPath = WriteEditedSource("SkippedMixThenIdentical1.cs", firstSource);
            string skippedMixPath = WriteEditedSource("SkippedMixThenIdentical2.cs", skippedMixSource);

            HotReloadOrchestratorResult first = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                firstPath,
                CancellationToken.None);
            AssertNoFileLevelFailure(first);
            AssertHasPatched(first, nameof(HotReloadE2EFixture.ComputeWithPrivate));
            AssertHasPatched(first, nameof(HotReloadE2EFixture.SumGrid));

            HotReloadOrchestratorResult second = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                skippedMixPath,
                CancellationToken.None);
            AssertNoFileLevelFailure(second);
            AssertHasSkipped(second, nameof(HotReloadE2EFixture.ComputeWithPrivate), "base");
            AssertHasPatched(second, nameof(HotReloadE2EFixture.SumGrid));
            AssertNoAlreadyActive(second);

            HotReloadOrchestratorResult third = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                skippedMixPath,
                CancellationToken.None);
            AssertNoFileLevelFailure(third);
            AssertHasSkipped(third, nameof(HotReloadE2EFixture.ComputeWithPrivate), "base");
            AssertHasPatched(third, nameof(HotReloadE2EFixture.SumGrid));
            AssertNoAlreadyActive(third);
            AssertHasUnchangedSourceNonBaselineWarning(third);
        }

        /// <summary>
        /// What: an unchanged reload after a Skipped-only (empty-entries) run emits the
        /// non-baseline warning, so all-Skipped files still record a hash.
        /// </summary>
        [Test]
        public async Task Run_AllSkippedThenIdenticalReload_WarnsUnchangedSourceNonBaseline()
        {
            string fixturePath = ResolveE2EFixturePath();
            string editedPath = WriteEditedSource(
                "AllSkippedThenIdenticalReload.cs",
                BuildFixtureSource(
                    computeWithPrivateMethod:
                    "public int ComputeWithPrivate(int delta)\n        {\n            return base.BaseSeed() + delta;\n        }"));

            HotReloadOrchestratorResult first = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);
            AssertNoFileLevelFailure(first);
            AssertHasSkipped(first, nameof(HotReloadE2EFixture.ComputeWithPrivate), "base");
            Assert.That(first.PatchedTotal, Is.EqualTo(0));
            AssertNoUnchangedSourceNonBaselineWarning(first);

            HotReloadOrchestratorResult second = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);
            AssertNoFileLevelFailure(second);
            AssertHasSkipped(second, nameof(HotReloadE2EFixture.ComputeWithPrivate), "base");
            AssertNoAlreadyActive(second);
            AssertHasUnchangedSourceNonBaselineWarning(second);
        }

        /// <summary>
        /// What: changing the source after a non-baseline run does not emit the unchanged-source
        /// warning, because the probe hash no longer matches the recorded non-baseline entry.
        /// </summary>
        [Test]
        public async Task Run_NonBaselineThenChangedSource_DoesNotWarnUnchangedSourceNonBaseline()
        {
            string fixturePath = ResolveE2EFixturePath();
            string skippedPath = WriteEditedSource(
                "NonBaselineThenChanged1.cs",
                BuildFixtureSource(
                    computeWithPrivateMethod:
                    "public int ComputeWithPrivate(int delta)\n        {\n            return base.BaseSeed() + delta;\n        }"));
            string changedPath = WriteEditedSource(
                "NonBaselineThenChanged2.cs",
                BuildFixtureSource(
                    computeWithPrivateMethod:
                    "public int ComputeWithPrivate(int delta)\n        {\n            return _secret + delta + 100;\n        }"));

            HotReloadOrchestratorResult first = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                skippedPath,
                CancellationToken.None);
            AssertNoFileLevelFailure(first);
            AssertHasSkipped(first, nameof(HotReloadE2EFixture.ComputeWithPrivate), "base");

            HotReloadOrchestratorResult second = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                changedPath,
                CancellationToken.None);
            AssertNoFileLevelFailure(second);
            AssertHasPatched(second, nameof(HotReloadE2EFixture.ComputeWithPrivate));
            AssertNoUnchangedSourceNonBaselineWarning(second);
        }

        /// <summary>
        /// What: --revert-all clears the applied-source ledger, so reloading the same edited
        /// source afterwards patches again instead of reporting AlreadyActive.
        /// </summary>
        [Test]
        public async Task Run_RevertAllThenIdenticalSource_DoesNotShortCircuit()
        {
            string fixturePath = ResolveE2EFixturePath();
            string editedPath = WriteEditedSource(
                "RevertAllThenIdenticalSource.cs",
                BuildFixtureSource(
                    computeWithPrivateMethod:
                    "public int ComputeWithPrivate(int delta)\n        {\n            return _secret + delta + 100;\n        }"));

            HotReloadOrchestratorResult first = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);

            AssertNoFileLevelFailure(first);
            AssertHasPatched(first, nameof(HotReloadE2EFixture.ComputeWithPrivate));

            HotReloadPatcher.RevertAll();

            HotReloadOrchestratorResult second = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);

            AssertNoFileLevelFailure(second);
            AssertHasPatched(second, nameof(HotReloadE2EFixture.ComputeWithPrivate));
            AssertNoAlreadyActive(second);
            HotReloadE2EFixture fixture = new HotReloadE2EFixture();
            Assert.That(fixture.ComputeWithPrivate(5), Is.EqualTo(115));
        }

        /// <summary>
        /// What: a const-only source with no verified snapshot does not emit the
        /// "No verified source snapshot" warning (no patch candidates → no noise).
        /// </summary>
        [Test]
        public async Task Run_ConstOnlySourceWithoutSnapshot_DoesNotWarnMissingSnapshot()
        {
            string fixturePath = ResolveE2EFixturePath();
            const string projectRelativePath = "Assets/Tests/Editor/HotReload/HotReloadE2EFixtures.cs";
            string editedPath = WriteEditedSource(
                "ConstOnlyNoSnapshot.cs",
                "namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload\n"
                + "{\n"
                + "    internal static class HotReloadConstOnlyProbe\n"
                + "    {\n"
                + "        public const int Amp = 5;\n"
                + "        public const float Speed = 1.5f;\n"
                + "    }\n"
                + "}\n");

            using (HideVerifiedSnapshot(projectRelativePath))
            {
                HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                    new[] { fixturePath },
                    editedPath,
                    CancellationToken.None);

                AssertNoFileLevelFailure(result);
                foreach (string warning in result.Warnings)
                {
                    Assert.That(
                        warning.Contains("No verified source snapshot"),
                        Is.False,
                        "Const-only source must not warn about missing snapshot.\n"
                        + string.Join("\n", result.Warnings));
                }
            }
        }

        /// <summary>
        /// What: a method-bearing source with no verified snapshot still emits the
        /// "No verified source snapshot" warning (patch candidates exist).
        /// </summary>
        [Test]
        public async Task Run_MethodSourceWithoutSnapshot_WarnsMissingSnapshot()
        {
            string fixturePath = ResolveE2EFixturePath();
            const string projectRelativePath = "Assets/Tests/Editor/HotReload/HotReloadE2EFixtures.cs";
            string editedPath = WriteEditedSource(
                "MethodNoSnapshot.cs",
                BuildFixtureSource(
                    computeWithPrivateMethod:
                    "public int ComputeWithPrivate(int delta)\n        {\n            return _secret + delta + 100;\n        }"));

            using (HideVerifiedSnapshot(projectRelativePath))
            {
                HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                    new[] { fixturePath },
                    editedPath,
                    CancellationToken.None);

                AssertNoFileLevelFailure(result);
                bool found = false;
                foreach (string warning in result.Warnings)
                {
                    if (warning.Contains("No verified source snapshot")
                        && warning.Contains("HotReloadE2EFixtures.cs"))
                    {
                        found = true;
                        break;
                    }
                }

                Assert.That(
                    found,
                    Is.True,
                    "Method-bearing source without snapshot must warn.\n"
                    + string.Join("\n", result.Warnings));
            }
        }

        /// <summary>
        /// What: with a verified source snapshot, hot reload patches only the edited method —
        /// unedited methods appear neither as Patched nor Skipped, and the response carries the
        /// unchanged count.
        /// </summary>
        [Test]
        public async Task Run_WithVerifiedSnapshot_PatchesOnlyEditedMethod()
        {
            string fixturePath = ResolveE2EFixturePath();
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string fixtureProjectRelativePath =
                "Assets/Tests/Editor/HotReload/HotReloadE2EFixtures.cs";
            string targetDllPath = Path.Combine(
                projectRoot,
                HotReloadConstants.ScriptAssembliesRelativeDirectory,
                "UnityCLILoop.Tests.Editor.HotReload"
                + HotReloadConstants.CompiledAssemblyExtension);

            string snapshot = HotReloadSourceBaseline.LoadVerifiedSnapshotSource(
                fixtureProjectRelativePath,
                targetDllPath);
            Assert.That(
                snapshot,
                Is.Not.Null,
                "Verified snapshot must resolve for the E2E fixture; capture regresses otherwise.");

            string editedPath = WriteEditedSource(
                "OnlyEditedMethod.cs",
                BuildFixtureSource(
                    computeWithPrivateMethod:
                    "public int ComputeWithPrivate(int delta)\n        {\n            return _secret + delta + 100;\n        }"));

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            AssertHasPatched(result, nameof(HotReloadE2EFixture.ComputeWithPrivate));

            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                Assert.That(
                    outcome.Method.Contains(nameof(HotReloadE2EFixture.QueryPrivate)),
                    Is.False,
                    "Unedited QueryPrivate must not appear as Patched/Skipped/Failed.\n"
                    + FormatOutcomes(result));
            }

            Assert.That(result.UnchangedTotal, Is.GreaterThan(0));

            HotReloadE2EFixture fixture = new HotReloadE2EFixture();
            Assert.That(fixture.ComputeWithPrivate(5), Is.EqualTo(115));
        }

        /// <summary>
        /// What: when every edited method fails shim compile, isolation is skipped and the
        /// Failed Reason keeps both CS0103 messages in full (no in-uloop truncation).
        /// </summary>
        [Test]
        public async Task Run_AllEditedMethodsFailingShimCompile_ReasonKeepsBothDiagnosticsInFull()
        {
            string fixturePath = ResolveE2EFixturePath();
            string editedSource = BuildFixtureSource(
                computeWithPrivateMethod:
                "public int ComputeWithPrivate(int delta)\n        {\n            return MissingAlphaHelper(delta);\n        }",
                callsMissingHelperMethod:
                "public int CallsMissingHelper(int value)\n        {\n            return MissingBetaHelper(value);\n        }");
            string editedPath = WriteEditedSource("AllEntriesShimFailure.cs", editedSource);

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);

            HotReloadMethodOutcome failed = null;
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Failed)
                {
                    failed = outcome;
                    break;
                }
            }

            Assert.That(failed, Is.Not.Null, FormatOutcomes(result));
            Assert.That(
                failed.Method,
                Is.EqualTo("(shim-compile)"),
                "Multi-entry unattributable failures must keep the file-level label.\n"
                + FormatOutcomes(result));
            Assert.That(failed.Reason, Does.Contain("MissingAlphaHelper"));
            Assert.That(failed.Reason, Does.Contain("MissingBetaHelper"));
            Assert.That(failed.Reason, Does.Contain("CS0103"));
            Assert.That(
                failed.Reason,
                Does.Contain(HotReloadConstants.NewMemberCompileHint),
                "Reason must keep the full joined diagnostics plus new-member hint.\n" + failed.Reason);
            Assert.That(
                failed.Reason.Contains(" ... (truncated)"),
                Is.False,
                "uloop must not truncate shim-compile Reason.\n" + failed.Reason);
        }

        /// <summary>
        /// What: when the only edited method fails shim compile, isolation is skipped
        /// (FailedEntries.Count == entries.Length) and Failed.Method uses that method's
        /// FormatMethodKeyParts label (not "(shim-compile)"), while Reason still carries the
        /// original-file "(line N)" from #line-mapped diagnostics.
        /// </summary>
        [Test]
        public async Task Run_SingleEditedMethodFailingShimCompile_AttributesFailureToThatMethod()
        {
            string fixturePath = ResolveE2EFixturePath();
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string targetDllPath = Path.Combine(
                projectRoot,
                HotReloadConstants.ScriptAssembliesRelativeDirectory,
                "UnityCLILoop.Tests.Editor.HotReload"
                + HotReloadConstants.CompiledAssemblyExtension);
            // Why require a snapshot: without it every method becomes an entry and isolation
            // succeeds — this test pins the single-entry path that only fires when the sole entry fails.
            Assert.That(
                HotReloadSourceBaseline.LoadVerifiedSnapshotSource(
                    "Assets/Tests/Editor/HotReload/HotReloadE2EFixtures.cs",
                    targetDllPath),
                Is.Not.Null,
                "Verified snapshot must resolve so only the edited failing method is an entry.");

            string editedSource = BuildFixtureSource(
                computeWithPrivateMethod:
                "public int ComputeWithPrivate(int delta)\n        {\n            return _secret + delta;\n        }",
                callsMissingHelperMethod:
                "public int CallsMissingHelper(int value)\n        {\n            return MissingHelperAddedByEdit(value);\n        }");
            string editedPath = WriteEditedSource("WholeFileShimFailure.cs", editedSource);

            int expectedOriginalLine = FindLineNumberContaining(editedSource, "MissingHelperAddedByEdit");
            Assert.That(expectedOriginalLine, Is.GreaterThan(0));
            string expectedMethodLabel = HotReloadPatcher.FormatMethodKeyParts(
                typeof(HotReloadE2EFixture).FullName,
                nameof(HotReloadE2EFixture.CallsMissingHelper),
                new[] { typeof(int).FullName },
                genericArity: 0);

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);

            bool foundAttributedFailure = false;
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Failed
                    && outcome.Method == expectedMethodLabel
                    && outcome.Reason.Contains(HotReloadConstants.NewMemberCompileHint)
                    && outcome.Reason.Contains("(line " + expectedOriginalLine + ")"))
                {
                    foundAttributedFailure = true;
                }
            }

            Assert.That(
                foundAttributedFailure,
                Is.True,
                "Single-entry shim compile failure must report Method=" + expectedMethodLabel
                + " with original-file line " + expectedOriginalLine + ".\n" + FormatOutcomes(result));
        }

        /// <summary>
        /// What: a shim compile error in one method no longer kills the file — the failing method
        /// reports Failed with its own compiler error (and the new-member hint) while the other
        /// methods still patch and take effect. Attribution uses original-file line numbers from
        /// #line-mapped diagnostics.
        /// </summary>
        [Test]
        public async Task Run_OneMethodFailingShimCompile_IsolatesFailureAndPatchesRest()
        {
            string fixturePath = ResolveE2EFixturePath();
            string editedSource = BuildFixtureSource(
                computeWithPrivateMethod:
                "public int ComputeWithPrivate(int delta)\n        {\n            return _secret + delta + 100;\n        }",
                callsMissingHelperMethod:
                "public int CallsMissingHelper(int value)\n        {\n            return MissingHelperAddedByEdit(value);\n        }");
            string editedPath = WriteEditedSource("IsolatedShimFailure.cs", editedSource);

            int expectedOriginalLine = FindLineNumberContaining(editedSource, "MissingHelperAddedByEdit");
            Assert.That(expectedOriginalLine, Is.GreaterThan(0));

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            AssertHasPatched(result, nameof(HotReloadE2EFixture.ComputeWithPrivate));

            bool missingHelperFailed = false;
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Failed
                    && outcome.Method.Contains(nameof(HotReloadE2EFixture.CallsMissingHelper))
                    && outcome.Reason.Contains(HotReloadConstants.NewMemberCompileHint)
                    && outcome.Reason.Contains("(line " + expectedOriginalLine + ")"))
                {
                    missingHelperFailed = true;
                }
            }

            Assert.That(missingHelperFailed, Is.True,
                "CallsMissingHelper must fail per-method with its own compiler error and original-file line "
                + expectedOriginalLine + ".\n" + FormatOutcomes(result));

            HotReloadE2EFixture fixture = new HotReloadE2EFixture();
            Assert.That(fixture.ComputeWithPrivate(5), Is.EqualTo(115));
        }

        /// <summary>
        /// What: an added method plus its caller plus an unrelated shim-compile failure still
        /// isolates per-method (the file is not collapsed to a single (shim-compile) Failed).
        /// </summary>
        [Test]
        public async Task Run_AddedMethodCallerAndUnrelatedShimFailure_IsolatesWithoutWipingFile()
        {
            string hostPath = ResolveAddedMemberHostPath();
            string onDisk = File.ReadAllText(hostPath);
            string edited = onDisk.Replace(
                "        public int ExistingValue()\n        {\n            return 1;\n        }",
                "        public int ExistingValue()\n        {\n            return 10;\n        }",
                StringComparison.Ordinal);
            edited = edited.Replace(
                "        public int ExistingCaller(int value)\n        {\n            return value;\n        }",
                "        public int ExistingCaller(int value)\n        {\n            return AddedPing(value);\n        }",
                StringComparison.Ordinal);
            edited = edited.Replace(
                "        public int ExistingFail(int value)\n        {\n            return value;\n        }",
                "        public int ExistingFail(int value)\n        {\n            return MissingHelperAddedByEdit(value);\n        }",
                StringComparison.Ordinal);
            edited = edited.Replace(
                "        public int ReadPrivateSeed()\n        {\n            return _privateSeed;\n        }\n    }",
                "        public int ReadPrivateSeed()\n        {\n            return _privateSeed;\n        }\n\n"
                + "        public int AddedPing(int value)\n        {\n            return value + 1;\n        }\n    }",
                StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk));
            string editedPath = WriteEditedSource("AddedMethodIsolation.cs", edited);

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { hostPath },
                editedPath,
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            AssertHasPatched(result, nameof(HotReloadAddedMemberHost.ExistingValue));
            AssertHasPatched(result, nameof(HotReloadAddedMemberHost.ExistingCaller));
            AssertHasAdded(result, "AddedPing");

            bool failIsolated = false;
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Failed
                    && outcome.Method.Contains(nameof(HotReloadAddedMemberHost.ExistingFail)))
                {
                    failIsolated = true;
                }
            }

            Assert.That(
                failIsolated,
                Is.True,
                "ExistingFail must isolate as a per-method Failed.\n" + FormatOutcomes(result));

            HotReloadAddedMemberHost host = new HotReloadAddedMemberHost();
            Assert.That(host.ExistingValue(), Is.EqualTo(10));
            Assert.That(host.ExistingCaller(3), Is.EqualTo(4));
        }

        /// <summary>
        /// What: an added method that reads a private field still shim-compiles (Harmony is
        /// injected from hasAccessorDelegates) and the added method itself is Added.
        /// </summary>
        [Test]
        public async Task Run_AddedMethodWithPrivateAccess_ShimCompilesAndAdds()
        {
            string hostPath = ResolveAddedMemberHostPath();
            string onDisk = File.ReadAllText(hostPath);
            string edited = onDisk.Replace(
                "        public int ExistingCaller(int value)\n        {\n            return value;\n        }",
                "        public int ExistingCaller(int value)\n        {\n            return AddedReadPrivate();\n        }",
                StringComparison.Ordinal);
            edited = edited.Replace(
                "        public int ReadPrivateSeed()\n        {\n            return _privateSeed;\n        }",
                "        public int ReadPrivateSeed()\n        {\n            return _privateSeed;\n        }\n\n"
                + "        public int AddedReadPrivate()\n        {\n"
                + "            System.Func<int> read = () => _privateSeed;\n"
                + "            return read();\n        }",
                StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk));
            string editedPath = WriteEditedSource("AddedMethodHarmony.cs", edited);

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { hostPath },
                editedPath,
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            AssertHasPatched(result, nameof(HotReloadAddedMemberHost.ExistingCaller));
            AssertHasAdded(result, "AddedReadPrivate");

            HotReloadAddedMemberHost host = new HotReloadAddedMemberHost();
            Assert.That(host.ExistingCaller(0), Is.EqualTo(7));
        }

        /// <summary>
        /// What: an added method that directly reads a compiled private instance field
        /// (no lambda) is Added and the patched caller returns that field's value.
        /// </summary>
        [Test]
        public async Task Run_AddedMethod_DirectPrivateInstanceFieldRead_ReturnsCompiledValue()
        {
            string fixturePath = ResolveAddedPrivateAccessFixturePath();
            string onDisk = File.ReadAllText(fixturePath);
            string edited = onDisk.Replace(
                "        public int ExistingCaller(int value)\n        {\n            return value;\n        }",
                "        public int ExistingCaller(int value)\n        {\n            return AddedReadInstance();\n        }\n\n"
                + "        [MethodImpl(MethodImplOptions.NoInlining)]\n"
                + "        public int AddedReadInstance()\n        {\n            return _instanceSecret;\n        }",
                StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk));

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("AddedDirectPrivateInstance.cs", edited),
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            AssertHasAdded(result, "AddedReadInstance");
            AssertHasPatched(result, nameof(HotReloadAddedPrivateAccessFixture.ExistingCaller));

            HotReloadAddedPrivateAccessFixture host = new HotReloadAddedPrivateAccessFixture();
            Assert.That(host.ExistingCaller(0), Is.EqualTo(7));
        }

        /// <summary>
        /// What: an added method that directly reads a compiled private static field
        /// (FB10 RowColors shape) is Added and the patched caller returns that field's value.
        /// </summary>
        [Test]
        public async Task Run_AddedMethod_DirectPrivateStaticFieldRead_ReturnsCompiledValue()
        {
            string fixturePath = ResolveAddedPrivateAccessFixturePath();
            string onDisk = File.ReadAllText(fixturePath);
            string edited = onDisk.Replace(
                "        public int ExistingCaller(int value)\n        {\n            return value;\n        }",
                "        public int ExistingCaller(int value)\n        {\n            return AddedReadStatic();\n        }\n\n"
                + "        [MethodImpl(MethodImplOptions.NoInlining)]\n"
                + "        public int AddedReadStatic()\n        {\n            return _staticSecret;\n        }",
                StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk));

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("AddedDirectPrivateStatic.cs", edited),
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            AssertHasAdded(result, "AddedReadStatic");
            AssertHasPatched(result, nameof(HotReloadAddedPrivateAccessFixture.ExistingCaller));

            HotReloadAddedPrivateAccessFixture host = new HotReloadAddedPrivateAccessFixture();
            Assert.That(host.ExistingCaller(0), Is.EqualTo(11));
        }

        /// <summary>
        /// What: an added method that reads a compiled private const returns the folded
        /// literal and is Added (const is not rewritten to StaticFieldRefAccess).
        /// </summary>
        [Test]
        public async Task Run_AddedMethod_PrivateConstRead_ReturnsFoldedLiteral()
        {
            string fixturePath = ResolveAddedPrivateAccessFixturePath();
            string onDisk = File.ReadAllText(fixturePath);
            string edited = onDisk.Replace(
                "        public int ExistingCaller(int value)\n        {\n            return value;\n        }",
                "        public int ExistingCaller(int value)\n        {\n            return AddedReadConst();\n        }\n\n"
                + "        [MethodImpl(MethodImplOptions.NoInlining)]\n"
                + "        public int AddedReadConst()\n        {\n            return PrivateConstThree;\n        }",
                StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk));

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("AddedPrivateConstRead.cs", edited),
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            AssertHasAdded(result, "AddedReadConst");
            AssertHasPatched(result, nameof(HotReloadAddedPrivateAccessFixture.ExistingCaller));

            HotReloadAddedPrivateAccessFixture host = new HotReloadAddedPrivateAccessFixture();
            Assert.That(host.ExistingCaller(0), Is.EqualTo(3));
        }

        /// <summary>
        /// What: an added method whose closure body reads a compiled private const still
        /// returns the folded literal (const stays out of accessor rewrite).
        /// </summary>
        [Test]
        public async Task Run_AddedMethod_ClosurePrivateConstRead_ReturnsFoldedLiteral()
        {
            string fixturePath = ResolveAddedPrivateAccessFixturePath();
            string onDisk = File.ReadAllText(fixturePath);
            string edited = onDisk.Replace(
                "        public int ExistingCaller(int value)\n        {\n            return value;\n        }",
                "        public int ExistingCaller(int value)\n        {\n            return AddedReadConstClosure();\n        }\n\n"
                + "        [MethodImpl(MethodImplOptions.NoInlining)]\n"
                + "        public int AddedReadConstClosure()\n        {\n"
                + "            System.Func<int> read = () => PrivateConstThree;\n"
                + "            return read();\n        }",
                StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk));

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("AddedClosurePrivateConstRead.cs", edited),
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            AssertHasAdded(result, "AddedReadConstClosure");
            AssertHasPatched(result, nameof(HotReloadAddedPrivateAccessFixture.ExistingCaller));

            HotReloadAddedPrivateAccessFixture host = new HotReloadAddedPrivateAccessFixture();
            Assert.That(host.ExistingCaller(0), Is.EqualTo(3));
        }

        /// <summary>
        /// What: an added method that mixes a private const with a private instance field
        /// returns the sum (const folds, field uses an accessor) without failing bind.
        /// </summary>
        [Test]
        public async Task Run_AddedMethod_MixedConstAndPrivateInstanceField_ReturnsSum()
        {
            string fixturePath = ResolveAddedPrivateAccessFixturePath();
            string onDisk = File.ReadAllText(fixturePath);
            string edited = onDisk.Replace(
                "        public int ExistingCaller(int value)\n        {\n            return value;\n        }",
                "        public int ExistingCaller(int value)\n        {\n            return AddedMixed();\n        }\n\n"
                + "        [MethodImpl(MethodImplOptions.NoInlining)]\n"
                + "        public int AddedMixed()\n        {\n            return PrivateConstThree + _instanceSecret;\n        }",
                StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk));

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("AddedMixedConstAndField.cs", edited),
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            AssertHasAdded(result, "AddedMixed");
            AssertHasPatched(result, nameof(HotReloadAddedPrivateAccessFixture.ExistingCaller));

            HotReloadAddedPrivateAccessFixture host = new HotReloadAddedPrivateAccessFixture();
            Assert.That(host.ExistingCaller(0), Is.EqualTo(10));
        }

        /// <summary>
        /// What: an added method that writes a compiled private static field (simple assign
        /// then +=) is Added and the compiled reader returns the written value.
        /// </summary>
        [Test]
        public async Task Run_AddedMethod_PrivateStaticFieldWrite_ReadsBackWrittenValue()
        {
            string fixturePath = ResolveAddedPrivateAccessFixturePath();
            string onDisk = File.ReadAllText(fixturePath);
            string edited = onDisk.Replace(
                "        public int ExistingCaller(int value)\n        {\n            return value;\n        }",
                "        public int ExistingCaller(int value)\n        {\n            return AddedWriteStatic();\n        }\n\n"
                + "        [MethodImpl(MethodImplOptions.NoInlining)]\n"
                + "        public int AddedWriteStatic()\n        {\n"
                + "            _staticWritable = 5;\n"
                + "            _staticWritable += 3;\n"
                + "            return _staticWritable;\n        }",
                StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk));

            HotReloadAddedPrivateAccessFixture host = new HotReloadAddedPrivateAccessFixture();
            host.ResetStaticWritable();

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("AddedPrivateStaticWrite.cs", edited),
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            AssertHasAdded(result, "AddedWriteStatic");
            AssertHasPatched(result, nameof(HotReloadAddedPrivateAccessFixture.ExistingCaller));
            Assert.That(host.ExistingCaller(0), Is.EqualTo(8));
            Assert.That(host.ReadStaticWritable(), Is.EqualTo(8));
            host.ResetStaticWritable();
        }

        /// <summary>
        /// What: an existing patched method that reads a private static field through a
        /// closure (pre-existing delegation path) is Patched and returns the compiled value.
        /// </summary>
        [Test]
        public async Task Run_ExistingDelegation_PrivateStaticFieldRead_ReturnsCompiledValue()
        {
            string fixturePath = ResolveAddedPrivateAccessFixturePath();
            string onDisk = File.ReadAllText(fixturePath);
            string edited = onDisk.Replace(
                "        public int ExistingCaller(int value)\n        {\n            return value;\n        }",
                "        public int ExistingCaller(int value)\n        {\n"
                + "            System.Func<int> read = () => _staticSecret;\n"
                + "            return read();\n        }",
                StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk));

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("ExistingDelegationPrivateStaticRead.cs", edited),
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            AssertHasPatched(result, nameof(HotReloadAddedPrivateAccessFixture.ExistingCaller));

            HotReloadAddedPrivateAccessFixture host = new HotReloadAddedPrivateAccessFixture();
            Assert.That(host.ExistingCaller(0), Is.EqualTo(11));
        }

        /// <summary>
        /// What: an added method that calls a compiled private instance method is Added
        /// and the patched caller returns that method's value (MethodDelegate path).
        /// </summary>
        [Test]
        public async Task Run_AddedMethod_DirectPrivateInstanceMethodCall_ReturnsCompiledValue()
        {
            string fixturePath = ResolveAddedPrivateAccessFixturePath();
            string onDisk = File.ReadAllText(fixturePath);
            string edited = onDisk.Replace(
                "        public int ExistingCaller(int value)\n        {\n            return value;\n        }",
                "        public int ExistingCaller(int value)\n        {\n            return AddedCallPrivate();\n        }\n\n"
                + "        [MethodImpl(MethodImplOptions.NoInlining)]\n"
                + "        public int AddedCallPrivate()\n        {\n            return PrivateInstanceSeven();\n        }",
                StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk));

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("AddedDirectPrivateMethod.cs", edited),
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            AssertHasAdded(result, "AddedCallPrivate");
            AssertHasPatched(result, nameof(HotReloadAddedPrivateAccessFixture.ExistingCaller));

            HotReloadAddedPrivateAccessFixture host = new HotReloadAddedPrivateAccessFixture();
            Assert.That(host.ExistingCaller(0), Is.EqualTo(7));
        }

        /// <summary>
        /// What: a return-type change that becomes Added still reads a compiled private
        /// instance field, and the patched same-file caller returns that field's value.
        /// </summary>
        [Test]
        public async Task Run_ReturnTypeChange_AddedMethodReadsPrivateField_ReturnsCompiledValue()
        {
            string fixturePath = ResolveAddedPrivateAccessFixturePath();
            string onDisk = File.ReadAllText(fixturePath);
            string edited = onDisk
                .Replace(
                    "        public int ReadInstanceSecret()\n        {\n            return _instanceSecret;\n        }",
                    "        public long ReadInstanceSecret()\n        {\n            return _instanceSecret;\n        }",
                    StringComparison.Ordinal)
                .Replace(
                    "        public int ExistingCaller(int value)\n        {\n            return value;\n        }",
                    "        public int ExistingCaller(int value)\n        {\n            return (int)ReadInstanceSecret();\n        }",
                    StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk));

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("AddedSignatureChangePrivateField.cs", edited),
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            AssertHasAdded(result, nameof(HotReloadAddedPrivateAccessFixture.ReadInstanceSecret));
            AssertHasPatched(result, nameof(HotReloadAddedPrivateAccessFixture.ExistingCaller));

            HotReloadAddedPrivateAccessFixture host = new HotReloadAddedPrivateAccessFixture();
            Assert.That(host.ExistingCaller(0), Is.EqualTo(7));
        }

        /// <summary>
        /// What: adding a method and calling it from the same file applies the added shim
        /// (Kind Added) and the patched caller returns the new method's result.
        /// </summary>
        [Test]
        public async Task Run_AddedMethod_AppliesThroughShimAndUpdatesRuntime()
        {
            string fixturePath = ResolveAddedMethodApplyFixturePath();
            string onDisk = File.ReadAllText(fixturePath);
            string edited = onDisk.Replace(
                "        public int ExistingCaller(int value)\n        {\n            return value;\n        }",
                "        public int ExistingCaller(int value)\n        {\n            return AddedPing(value);\n        }\n\n"
                + "        public int AddedPing(int value)\n        {\n            return value + 1;\n        }",
                StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk));
            string editedPath = WriteEditedSource("AddedMethodApplyE2E.cs", edited);

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            AssertHasPatched(result, nameof(HotReloadAddedMethodApplyFixture.ExistingCaller));
            AssertHasAdded(result, "AddedPing");
            Assert.That(result.ActivePatchTotal, Is.EqualTo(2));

            bool foundAddedStatus = false;
            foreach (HotReloadAddedMemberInfo added in HotReloadAddedMemberRegistry.Describe())
            {
                if (added.MethodKey.Contains("AddedPing"))
                {
                    foundAddedStatus = true;
                }
            }

            Assert.That(foundAddedStatus, Is.True, "AddedPing must appear in the added-member registry.");

            HotReloadAddedMethodApplyFixture host = new HotReloadAddedMethodApplyFixture();
            Assert.That(host.ExistingCaller(3), Is.EqualTo(4));
        }

        /// <summary>
        /// What: adding a field and reading/writing it from edited bodies stores values per
        /// instance, and a second identical apply reports AlreadyActive while keeping the values.
        /// </summary>
        [Test]
        public async Task Run_AddedField_AppliesThroughStoreAndKeepsValuesOnReapply()
        {
            string fixturePath = ResolveAddedFieldApplyFixturePath();
            string onDisk = File.ReadAllText(fixturePath);
            string edited = WithAddedFieldAccesses(onDisk);
            Assert.That(edited, Is.Not.EqualTo(onDisk));

            HotReloadOrchestratorResult first = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("AddedFieldApplyE2E.cs", edited),
                CancellationToken.None);
            AssertNoFileLevelFailure(first);
            AssertHasPatched(first, nameof(HotReloadAddedFieldApplyFixture.ReadAdded));
            AssertHasPatched(first, nameof(HotReloadAddedFieldApplyFixture.WriteAdded));

            HotReloadAddedFieldApplyFixture firstHost = new HotReloadAddedFieldApplyFixture();
            HotReloadAddedFieldApplyFixture secondHost = new HotReloadAddedFieldApplyFixture();
            firstHost.WriteAdded(10);
            secondHost.WriteAdded(20);
            Assert.That(firstHost.ReadAdded(), Is.EqualTo(10));
            Assert.That(secondHost.ReadAdded(), Is.EqualTo(20));

            HotReloadOrchestratorResult second = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("AddedFieldApplyE2EReapply.cs", edited),
                CancellationToken.None);
            AssertNoFileLevelFailure(second);
            AssertHasAlreadyActive(second, nameof(HotReloadAddedFieldApplyFixture.ReadAdded));
            AssertHasAlreadyActive(second, nameof(HotReloadAddedFieldApplyFixture.WriteAdded));
            Assert.That(firstHost.ReadAdded(), Is.EqualTo(10));
            Assert.That(secondHost.ReadAdded(), Is.EqualTo(20));
        }

        /// <summary>
        /// What: a shim-compile failure after a successful added-field apply leaves the added-field
        /// ledger intact, even though the failed run reports empty AddedFields.
        /// </summary>
        [Test]
        public async Task Run_ShimCompileFailureAfterSuccessfulAddedField_KeepsAddedFieldLedger()
        {
            string fixturePath = ResolveAddedFieldApplyFixturePath();
            string onDisk = File.ReadAllText(fixturePath);
            string applied = WithAddedFieldAccesses(onDisk);
            HotReloadOrchestratorResult first = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("AddedFieldLedgerSuccess.cs", applied),
                CancellationToken.None);
            AssertNoFileLevelFailure(first);
            AssertHasPatched(first, nameof(HotReloadAddedFieldApplyFixture.ReadAdded));

            string typeName = typeof(HotReloadAddedFieldApplyFixture).FullName;
            Assert.That(
                HotReloadAddedFieldRegistry.GetFieldsForType(typeName),
                Is.EqualTo(new[] { "AddedCount" }));

            string failed = WithAddedFieldAccessesCallingMissingHelper(onDisk);
            HotReloadOrchestratorResult second = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("AddedFieldLedgerShimFailure.cs", failed),
                CancellationToken.None);

            bool foundFailure = false;
            foreach (HotReloadMethodOutcome outcome in second.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Failed)
                {
                    foundFailure = true;
                    break;
                }
            }

            Assert.That(foundFailure, Is.True, "Expected a Failed outcome.\n" + FormatOutcomes(second));
            Assert.That(second.AddedFields, Is.Empty);
            Assert.That(
                HotReloadAddedFieldRegistry.GetFieldsForType(typeName),
                Is.EqualTo(new[] { "AddedCount" }));
        }

        /// <summary>
        /// What: RunAsync aggregates AddedFields from two files and sorts them ordinal, so a
        /// later file whose field name sorts first still appears first in the result.
        /// </summary>
        [Test]
        public async Task Run_TwoFilesAddedFields_SortsAggregatedNamesOrdinal()
        {
            string e2ePath = ResolveE2EFixturePath();
            string applyPath = ResolveAddedFieldApplyFixturePath();
            string[] expected =
            {
                typeof(HotReloadAddedFieldApplyFixture).FullName + ".AlphaField",
                typeof(HotReloadE2EFixture).FullName + ".ZetaField"
            };
            string e2eEdited = WithE2EAddedField(
                BuildFixtureSource(
                    computeWithPrivateMethod:
                    "public int ComputeWithPrivate(int delta)\n        {\n            return _secret + ZetaField + delta;\n        }"),
                "ZetaField");
            string applyOnDisk = File.ReadAllText(applyPath);
            string applyEdited = applyOnDisk.Replace(
                "        public int ReadAdded()\n        {\n            return 0;\n        }\n\n"
                + "        [MethodImpl(MethodImplOptions.NoInlining)]\n"
                + "        public void WriteAdded(int value)\n        {\n        }",
                "        public int AlphaField;\n\n"
                + "        [MethodImpl(MethodImplOptions.NoInlining)]\n"
                + "        public int ReadAdded()\n        {\n            return AlphaField;\n        }\n\n"
                + "        [MethodImpl(MethodImplOptions.NoInlining)]\n"
                + "        public void WriteAdded(int value)\n        {\n            AlphaField = value;\n        }",
                StringComparison.Ordinal);

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { e2ePath, applyPath },
                contentPathOverride: null,
                CancellationToken.None,
                new[]
                {
                    WriteEditedSource("AddedFieldSortE2E.cs", e2eEdited),
                    WriteEditedSource("AddedFieldSortApply.cs", applyEdited)
                });

            AssertNoFileLevelFailure(result);
            AssertHasPatched(result, nameof(HotReloadE2EFixture.ComputeWithPrivate));
            AssertHasPatched(result, nameof(HotReloadAddedFieldApplyFixture.ReadAdded));
            Assert.That(result.AddedFields, Is.EqualTo(expected));
            AssertAddedFieldsLifetimeWarningMatchesAddedFields(result);
        }

        /// <summary>
        /// What: a single-entry shim compile failure that also adds a field reports no
        /// AddedFields, because the field was never applied.
        /// </summary>
        [Test]
        public async Task Run_SingleEntryAddedFieldShimFailure_ReportsEmptyAddedFields()
        {
            string fixturePath = ResolveE2EFixturePath();
            string edited = WithE2EAddedField(
                BuildFixtureSource(
                    computeWithPrivateMethod:
                    "public int ComputeWithPrivate(int delta)\n        {\n            return _secret + delta;\n        }",
                    callsMissingHelperMethod:
                    "public int CallsMissingHelper(int value)\n        {\n            return AddedScratch + MissingHelperAddedByEdit(value);\n        }"),
                "AddedScratch");

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("AddedFieldSingleEntryFailure.cs", edited),
                CancellationToken.None);

            bool foundFailure = false;
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Failed
                    && outcome.Method.Contains(nameof(HotReloadE2EFixture.CallsMissingHelper)))
                {
                    foundFailure = true;
                    break;
                }
            }

            Assert.That(foundFailure, Is.True, "Expected CallsMissingHelper to fail.\n" + FormatOutcomes(result));
            Assert.That(result.AddedFields, Is.Not.Null);
            Assert.That(result.AddedFields, Is.Empty);
            AssertAddedFieldsLifetimeWarningMatchesAddedFields(result);
        }

        /// <summary>
        /// What: when only the failing isolated method uses an added field, retry output
        /// replaces first-pass names and AddedFields stays empty.
        /// </summary>
        [Test]
        public async Task Run_IsolatedFailureUsingAddedField_ReportsEmptyAddedFields()
        {
            string fixturePath = ResolveE2EFixturePath();
            string edited = WithE2EAddedField(
                BuildFixtureSource(
                    computeWithPrivateMethod:
                    "public int ComputeWithPrivate(int delta)\n        {\n            return _secret + delta + 100;\n        }",
                    callsMissingHelperMethod:
                    "public int CallsMissingHelper(int value)\n        {\n            return AddedScratch + MissingHelperAddedByEdit(value);\n        }"),
                "AddedScratch");

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("AddedFieldIsolatedFailure.cs", edited),
                CancellationToken.None);

            AssertHasPatched(result, nameof(HotReloadE2EFixture.ComputeWithPrivate));
            bool foundFailure = false;
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Failed
                    && outcome.Method.Contains(nameof(HotReloadE2EFixture.CallsMissingHelper)))
                {
                    foundFailure = true;
                    break;
                }
            }

            Assert.That(foundFailure, Is.True, "Expected CallsMissingHelper to fail.\n" + FormatOutcomes(result));
            Assert.That(result.AddedFields, Is.Not.Null);
            Assert.That(result.AddedFields, Is.Empty);
            AssertAddedFieldsLifetimeWarningMatchesAddedFields(result);
        }

        /// <summary>
        /// What: a declared added field that is never rewritten into a patched body is omitted
        /// from AddedFields, and the lifetime warning lists exactly that same set.
        /// </summary>
        [Test]
        public async Task Run_DeclaredButUnrewrittenAddedField_LifetimeWarningMatchesAddedFields()
        {
            string applyPath = ResolveAddedFieldApplyFixturePath();
            string applyOnDisk = File.ReadAllText(applyPath);
            string applyEdited = applyOnDisk.Replace(
                "        public int ReadAdded()\n        {\n            return 0;\n        }\n\n"
                + "        [MethodImpl(MethodImplOptions.NoInlining)]\n"
                + "        public void WriteAdded(int value)\n        {\n        }",
                "        private int UsedScratch;\n"
                + "        private int UnusedScratch;\n\n"
                + "        [MethodImpl(MethodImplOptions.NoInlining)]\n"
                + "        public int ReadAdded()\n        {\n            return UsedScratch;\n        }\n\n"
                + "        [MethodImpl(MethodImplOptions.NoInlining)]\n"
                + "        public void WriteAdded(int value)\n        {\n            UsedScratch = value;\n        }",
                StringComparison.Ordinal);
            Assert.That(applyEdited, Is.Not.EqualTo(applyOnDisk));

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { applyPath },
                WriteEditedSource("DeclaredButUnrewrittenAddedField.cs", applyEdited),
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            AssertHasPatched(result, nameof(HotReloadAddedFieldApplyFixture.ReadAdded));
            Assert.That(
                result.AddedFields,
                Is.EqualTo(new[] { typeof(HotReloadAddedFieldApplyFixture).FullName + ".UsedScratch" }));
            AssertAddedFieldsLifetimeWarningMatchesAddedFields(result);
        }

        /// <summary>
        /// What: re-applying without the added method clears that file's added-member ledger
        /// so --status cannot keep a method the source no longer declares.
        /// </summary>
        [Test]
        public async Task Run_ReapplyWithoutAddedMethod_ClearsAddedMemberRegistry()
        {
            string fixturePath = ResolveAddedMethodApplyFixturePath();
            string onDisk = File.ReadAllText(fixturePath);
            string withAdded = onDisk.Replace(
                "        public int ExistingCaller(int value)\n        {\n            return value;\n        }",
                "        public int ExistingCaller(int value)\n        {\n            return AddedPing(value);\n        }\n\n"
                + "        public int AddedPing(int value)\n        {\n            return value + 1;\n        }",
                StringComparison.Ordinal);
            HotReloadOrchestratorResult first = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("AddedMethodApplyThenRemove1.cs", withAdded),
                CancellationToken.None);
            AssertHasAdded(first, "AddedPing");

            string valueOnly = onDisk.Replace(
                "            return value;\n        }",
                "            return value + 10;\n        }",
                StringComparison.Ordinal);
            HotReloadOrchestratorResult second = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("AddedMethodApplyThenRemove2.cs", valueOnly),
                CancellationToken.None);
            AssertHasPatched(second, nameof(HotReloadAddedMethodApplyFixture.ExistingCaller));
            foreach (HotReloadMethodOutcome outcome in second.Methods)
            {
                Assert.That(
                    outcome.Kind,
                    Is.Not.EqualTo(HotReloadMethodOutcomeKind.Added),
                    "Re-apply without AddedPing must not keep an Added outcome.\n"
                    + FormatOutcomes(second));
            }

            foreach (HotReloadAddedMemberInfo added in HotReloadAddedMemberRegistry.Describe())
            {
                Assert.That(
                    added.MethodKey,
                    Does.Not.Contain("AddedPing"),
                    "Per-file clear must drop AddedPing on re-apply.");
            }

            AssertNoDeactivatedPatchesWarning(second);
            HotReloadAddedMethodApplyFixture host = new HotReloadAddedMethodApplyFixture();
            Assert.That(host.ExistingCaller(3), Is.EqualTo(13));
        }

        /// <summary>
        /// What: after an added method applies, a later run that only breaks that added body
        /// leaves the registry entry in place when nothing else is applied.
        /// </summary>
        [Test]
        public async Task Run_BrokenAddedMethodAfterSuccess_ObservesRegistryWhenNothingElseApplies()
        {
            string fixturePath = ResolveAddedMethodApplyFixturePath();
            string onDisk = File.ReadAllText(fixturePath);
            HotReloadOrchestratorResult first = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("BrokenAddedAfterSuccess1.cs", WithWorkingAddedPing(onDisk)),
                CancellationToken.None);
            AssertHasAdded(first, "AddedPing");
            Assert.That(CountAddedMembersContaining("AddedPing"), Is.EqualTo(1));

            HotReloadOrchestratorResult second = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("BrokenAddedAfterSuccess2.cs", WithBrokenAddedPing(onDisk)),
                CancellationToken.None);

            int remaining = CountAddedMembersContaining("AddedPing");
            Assert.That(
                remaining,
                Is.EqualTo(1),
                "Observation: AddedPing remaining=" + remaining
                + " ActivePatchTotal=" + second.ActivePatchTotal
                + "\n" + FormatOutcomes(second));
            Assert.That(second.ActivePatchTotal, Is.EqualTo(2));
            AssertNoDeactivatedPatchesWarning(second);
        }

        /// <summary>
        /// What: after an added method applies, a later run that breaks that added body while
        /// still patching an unrelated method drops the added member and warns with its label.
        /// </summary>
        [Test]
        public async Task Run_BrokenAddedMethodAfterSuccess_ObservesRegistryWhenUnrelatedStillPatches()
        {
            string fixturePath = ResolveAddedMethodApplyFixturePath();
            string onDisk = File.ReadAllText(fixturePath);
            HotReloadOrchestratorResult first = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("BrokenAddedUnrelated1.cs", WithWorkingAddedPing(onDisk)),
                CancellationToken.None);
            AssertHasAdded(first, "AddedPing");
            Assert.That(CountAddedMembersContaining("AddedPing"), Is.EqualTo(1));

            string later = WithBrokenAddedPing(onDisk).Replace(
                "        public int Unrelated(int value)\n        {\n            return value;\n        }",
                "        public int Unrelated(int value)\n        {\n            return value + 1;\n        }",
                StringComparison.Ordinal);
            HotReloadOrchestratorResult second = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("BrokenAddedUnrelated2.cs", later),
                CancellationToken.None);

            int remaining = CountAddedMembersContaining("AddedPing");
            Assert.That(
                remaining,
                Is.EqualTo(0),
                "Observation: AddedPing remaining=" + remaining
                + " ActivePatchTotal=" + second.ActivePatchTotal
                + "\n" + FormatOutcomes(second));
            AssertDeactivatedPatchesWarningsEqual(
                second,
                ExpectedDeactivatedAddedMembersWarning(AddedPingMethodLabel()));
        }

        /// <summary>
        /// What: a successful identical re-apply of an added method reports AlreadyActive and
        /// does not emit the deactivated-patches warning.
        /// </summary>
        [Test]
        public async Task Run_ReapplyWorkingAddedMethod_DoesNotWarnDeactivatedPatches()
        {
            string fixturePath = ResolveAddedMethodApplyFixturePath();
            string onDisk = File.ReadAllText(fixturePath);
            string edited = WithWorkingAddedPing(onDisk);
            HotReloadOrchestratorResult first = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("ReapplyWorkingAdded1.cs", edited),
                CancellationToken.None);
            AssertHasAdded(first, "AddedPing");

            HotReloadOrchestratorResult second = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("ReapplyWorkingAdded2.cs", edited),
                CancellationToken.None);
            AssertHasAlreadyActive(
                second,
                "AddedPing",
                HotReloadConstants.AlreadyActiveAddedMemberReason);
            AssertNoDeactivatedPatchesWarning(second);
        }

        /// <summary>
        /// What: an unchanged reload after a fully applied added-method run reports the added
        /// member as AlreadyActive with the added-member Reason and InvocationCount 0, while
        /// the patched caller keeps the ordinary AlreadyActive Reason.
        /// </summary>
        [Test]
        public async Task Run_UnchangedReload_AlreadyActiveAddedMember_UsesAddedReasonAndZeroCount()
        {
            string fixturePath = ResolveAddedMethodApplyFixturePath();
            string onDisk = File.ReadAllText(fixturePath);
            string edited = WithWorkingAddedPing(onDisk);
            HotReloadOrchestratorResult first = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("AlreadyActiveAddedReason1.cs", edited),
                CancellationToken.None);
            AssertHasAdded(first, "AddedPing");
            AssertHasPatched(first, nameof(HotReloadAddedMethodApplyFixture.ExistingCaller));

            HotReloadAddedMethodApplyFixture host = new HotReloadAddedMethodApplyFixture();
            Assert.That(host.ExistingCaller(3), Is.EqualTo(4));

            HotReloadOrchestratorResult second = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("AlreadyActiveAddedReason2.cs", edited),
                CancellationToken.None);
            HotReloadResponse response = HotReloadTool.BuildApplyResponse(second);

            HotReloadMethodResult addedRow = FindResponseMethod(response, "AddedPing");
            Assert.That(addedRow.Kind, Is.EqualTo(nameof(HotReloadMethodOutcomeKind.AlreadyActive)));
            Assert.That(
                addedRow.Reason,
                Is.EqualTo(HotReloadConstants.AlreadyActiveAddedMemberReason));
            Assert.That(addedRow.InvocationCount, Is.EqualTo(0L));

            HotReloadMethodResult patchedRow = FindResponseMethod(
                response,
                nameof(HotReloadAddedMethodApplyFixture.ExistingCaller));
            Assert.That(patchedRow.Kind, Is.EqualTo(nameof(HotReloadMethodOutcomeKind.AlreadyActive)));
            Assert.That(patchedRow.Reason, Is.EqualTo(HotReloadConstants.AlreadyActiveReason));
        }

        /// <summary>
        /// What: adding plain private fields emits one lifetime warning listing every added
        /// field's fully-qualified name in ordinal order, and a body-only run without added
        /// fields does not emit that warning.
        /// </summary>
        [Test]
        public async Task Run_PrivateAddedFields_EmitsLifetimeWarning_BodyOnlyDoesNot()
        {
            string applyPath = ResolveAddedFieldApplyFixturePath();
            string applyOnDisk = File.ReadAllText(applyPath);
            string applyEdited = WithPrivateAddedFields(applyOnDisk);
            HotReloadOrchestratorResult withFields = await HotReloadOrchestrator.RunAsync(
                new[] { applyPath },
                WriteEditedSource("PrivateAddedFieldsLifetime.cs", applyEdited),
                CancellationToken.None);
            AssertNoFileLevelFailure(withFields);
            string[] expectedNames =
            {
                typeof(HotReloadAddedFieldApplyFixture).FullName + ".AlphaScratch",
                typeof(HotReloadAddedFieldApplyFixture).FullName + ".BetaScratch"
            };
            string expectedLifetimeWarning = string.Format(
                HotReloadConstants.AddedFieldsLifetimeWarningFormat,
                string.Join(", ", expectedNames));
            Assert.That(withFields.Warnings, Does.Contain(expectedLifetimeWarning));

            string e2ePath = ResolveE2EFixturePath();
            HotReloadOrchestratorResult bodyOnly = await HotReloadOrchestrator.RunAsync(
                new[] { e2ePath },
                WriteEditedSource(
                    "BodyOnlyNoAddedFieldsLifetime.cs",
                    BuildFixtureSource(
                        computeWithPrivateMethod:
                        "public int ComputeWithPrivate(int delta)\n        {\n            return _secret + delta + 100;\n        }")),
                CancellationToken.None);
            AssertNoFileLevelFailure(bodyOnly);
            string lifetimePrefix = HotReloadConstants.AddedFieldsLifetimeWarningFormat.Substring(
                0,
                HotReloadConstants.AddedFieldsLifetimeWarningFormat.IndexOf("{0}", StringComparison.Ordinal));
            foreach (string warning in bodyOnly.Warnings)
            {
                Assert.That(
                    warning,
                    Does.Not.StartWith(lifetimePrefix),
                    "Body-only edits must not emit the added-fields lifetime warning.\n"
                    + string.Join("\n", bodyOnly.Warnings));
            }
        }

        /// <summary>
        /// What: editing a compiled generic method reports Skipped with the compile-guided
        /// generic reason (exact match).
        /// </summary>
        [Test]
        public async Task Run_GenericMethodEdit_SkipsWithCompileGuidedReason()
        {
            string fixturePath = ResolveGenericMethodSkipFixturePath();
            string onDisk = File.ReadAllText(fixturePath);
            string edited = onDisk.Replace(
                "            return value;",
                "            return value + 1;",
                StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk));

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("GenericMethodSkip.cs", edited),
                CancellationToken.None);
            HotReloadResponse response = HotReloadTool.BuildApplyResponse(result);
            HotReloadMethodResult skipped = FindResponseMethod(response, "Identity");
            Assert.That(skipped.Kind, Is.EqualTo(nameof(HotReloadMethodOutcomeKind.Skipped)));
            Assert.That(skipped.Reason, Is.EqualTo(ExpectedGenericMethodSkipReason));
        }

        /// <summary>
        /// What: two added methods deactivated in one run are listed in ordinal order,
        /// comma-space separated, in the deactivated-patches warning.
        /// </summary>
        [Test]
        public async Task Run_BrokenAddedMethodsAfterSuccess_WarnsOrdinalJoinedLabels()
        {
            string fixturePath = ResolveAddedMethodApplyFixturePath();
            string onDisk = File.ReadAllText(fixturePath);
            HotReloadOrchestratorResult first = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("BrokenAddedMulti1.cs", WithWorkingAddedPingAndPong(onDisk)),
                CancellationToken.None);
            AssertHasAdded(first, "AddedPing");
            AssertHasAdded(first, "AddedPong");

            string later = WithBrokenAddedPingAndPong(onDisk).Replace(
                "        public int Unrelated(int value)\n        {\n            return value;\n        }",
                "        public int Unrelated(int value)\n        {\n            return value + 1;\n        }",
                StringComparison.Ordinal);
            HotReloadOrchestratorResult second = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("BrokenAddedMulti2.cs", later),
                CancellationToken.None);

            AssertDeactivatedPatchesWarningsEqual(
                second,
                ExpectedDeactivatedAddedMembersWarning(
                    AddedPingMethodLabel() + ", " + AddedPongMethodLabel()));
        }

        /// <summary>
        /// What: after an added method applies, a later run that skips it as virtual while
        /// still patching an unrelated method warns with the added method's label.
        /// </summary>
        [Test]
        public async Task Run_VirtualAddedMethodAfterSuccess_WarnsDeactivatedPatches()
        {
            string fixturePath = ResolveAddedMethodApplyFixturePath();
            string onDisk = File.ReadAllText(fixturePath);
            HotReloadOrchestratorResult first = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("VirtualAddedAfterSuccess1.cs", WithWorkingAddedPing(onDisk)),
                CancellationToken.None);
            AssertHasAdded(first, "AddedPing");

            string later = WithVirtualAddedPing(onDisk).Replace(
                "        public int Unrelated(int value)\n        {\n            return value;\n        }",
                "        public int Unrelated(int value)\n        {\n            return value + 1;\n        }",
                StringComparison.Ordinal);
            HotReloadOrchestratorResult second = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("VirtualAddedAfterSuccess2.cs", later),
                CancellationToken.None);

            AssertDeactivatedPatchesWarningsEqual(
                second,
                ExpectedDeactivatedAddedMembersWarning(AddedPingMethodLabel()));
        }

        /// <summary>
        /// What: after an added method applies, a later run that skips every method as virtual
        /// (empty entries) still warns with the added-member deactivation wording.
        /// </summary>
        [Test]
        public async Task Run_VirtualAddedMethodAfterSuccess_EmptyEntries_WarnsDeactivatedAddedMembers()
        {
            string fixturePath = ResolveAddedMethodApplyFixturePath();
            string onDisk = File.ReadAllText(fixturePath);
            HotReloadOrchestratorResult first = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("VirtualAddedEmptyEntries1.cs", WithWorkingAddedPing(onDisk)),
                CancellationToken.None);
            AssertHasAdded(first, "AddedPing");

            HotReloadOrchestratorResult second = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("VirtualAddedEmptyEntries2.cs", WithVirtualAddedPing(onDisk)),
                CancellationToken.None);

            AssertDeactivatedPatchesWarningsEqual(
                second,
                ExpectedDeactivatedAddedMembersWarning(AddedPingMethodLabel()));
        }

        /// <summary>
        /// What: deleting a previously added method and restoring its caller does not emit
        /// the added-member deactivation warning (intentional convergence).
        /// </summary>
        [Test]
        public async Task Run_DeleteAddedMethodAndRestoreCaller_DoesNotWarnDeactivatedAddedMembers()
        {
            string fixturePath = ResolveAddedMethodApplyFixturePath();
            string onDisk = File.ReadAllText(fixturePath);
            HotReloadOrchestratorResult first = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("DeleteAddedRestoreCaller1.cs", WithWorkingAddedPing(onDisk)),
                CancellationToken.None);
            AssertHasAdded(first, "AddedPing");

            HotReloadOrchestratorResult second = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("DeleteAddedRestoreCaller2.cs", onDisk),
                CancellationToken.None);

            AssertNoDeactivatedPatchesWarning(second);
        }

        /// <summary>
        /// What: a return-type change whose compiled callers all live in the edited file applies
        /// as Added plus a Patched caller, and the caller returns the new method's value.
        /// </summary>
        [Test]
        public async Task Run_ReturnTypeChange_SameFileCallers_AppliesAddedAndPatchesCaller()
        {
            string fixturePath = ResolveSignatureChangeSameFileFixturePath();
            string edited = WithSameFileReturnTypeChange(File.ReadAllText(fixturePath));
            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("SignatureChangeSameFile.cs", edited),
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            AssertHasAdded(result, nameof(HotReloadSignatureChangeSameFileFixture.Target));
            AssertHasPatched(result, nameof(HotReloadSignatureChangeSameFileFixture.ExistingCaller));
            Assert.That(result.ActivePatchTotal, Is.EqualTo(2));
            Assert.That(
                CountWarningsContaining(
                    result.Warnings,
                    "applied because its compiled call sites were already hot-reload patched"),
                Is.EqualTo(0),
                "A never-active caller edited in the same run must not emit the re-patched notice.\n"
                + string.Join("\n", result.Warnings));

            HotReloadSignatureChangeSameFileFixture host = new HotReloadSignatureChangeSameFileFixture();
            Assert.That(host.ExistingCaller(3), Is.EqualTo(4));
        }

        /// <summary>
        /// What: after a same-file return-type change applies, a later run that skips the
        /// covering caller still reports the replacement as already-active instead of a fresh
        /// gate skip.
        /// </summary>
        [Test]
        public async Task Run_ReturnTypeChange_LaterCallerSkip_ReportsAlreadyActiveReason()
        {
            string fixturePath = ResolveSignatureChangeAlreadyActiveFixturePath();
            string applied = WithSameFileReturnTypeChange(File.ReadAllText(fixturePath));
            HotReloadOrchestratorResult first = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("SignatureChangeAlreadyActive1.cs", applied),
                CancellationToken.None);
            AssertNoFileLevelFailure(first);
            AssertHasAdded(first, nameof(HotReloadSignatureChangeAlreadyActiveFixture.Target));
            AssertHasPatched(first, nameof(HotReloadSignatureChangeAlreadyActiveFixture.ExistingCaller));
            Assert.That(
                CountAddedMembersContaining(nameof(HotReloadSignatureChangeAlreadyActiveFixture.Target)),
                Is.EqualTo(1));

            string later = applied
                .Replace(
                    "        public int MarkerHp { get; set; }",
                    "        public int MarkerHp;",
                    StringComparison.Ordinal)
                .Replace(
                    "            return (int)Target(value);\n        }",
                    "            MarkerHp = value;\n            return (int)Target(value);\n        }",
                    StringComparison.Ordinal);
            Assert.That(later, Is.Not.EqualTo(applied));

            HotReloadOrchestratorResult second = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("SignatureChangeAlreadyActive2.cs", later),
                CancellationToken.None);
            AssertNoFileLevelFailure(second);
            string expectedLabel = HotReloadPatcher.FormatMethodKeyParts(
                typeof(HotReloadSignatureChangeAlreadyActiveFixture).FullName,
                nameof(HotReloadSignatureChangeAlreadyActiveFixture.Target),
                new[] { "System.Int32" },
                0);
            string expectedReason = string.Format(
                HotReloadConstants.SignatureChangedGateSkipReasonAlreadyActiveFormat,
                expectedLabel);
            Assert.That(FindSkippedReason(second, expectedLabel), Is.EqualTo(expectedReason));
            Assert.That(
                CountAddedMembersContaining(nameof(HotReloadSignatureChangeAlreadyActiveFixture.Target)),
                Is.EqualTo(1),
                "The earlier replacement must stay in the added-member registry.");
            Assert.That(second.ActivePatchTotal, Is.EqualTo(2));
        }

        /// <summary>
        /// What: after a return-type replacement applies, a later run that gates it and
        /// applies an unrelated method warns with the replacement label.
        /// </summary>
        [Test]
        public async Task Run_ReturnTypeChange_GatedReplacementDeactivatedByUnrelatedApply_Warns()
        {
            string fixturePath = ResolveSignatureChangeAlreadyActiveFixturePath();
            string applied = WithSameFileReturnTypeChange(File.ReadAllText(fixturePath));
            HotReloadOrchestratorResult first = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("SignatureChangeGatedDeactivate1.cs", applied),
                CancellationToken.None);
            AssertNoFileLevelFailure(first);
            AssertHasAdded(first, nameof(HotReloadSignatureChangeAlreadyActiveFixture.Target));

            string later = applied
                .Replace(
                    "        public int MarkerHp { get; set; }",
                    "        public int MarkerHp;",
                    StringComparison.Ordinal)
                .Replace(
                    "            return (int)Target(value);\n        }",
                    "            MarkerHp = value;\n            return (int)Target(value);\n        }",
                    StringComparison.Ordinal)
                .Replace(
                    "        public int Unrelated(int value)\n        {\n            return value;\n        }",
                    "        public int Unrelated(int value)\n        {\n            return value + 1;\n        }",
                    StringComparison.Ordinal);
            Assert.That(later, Is.Not.EqualTo(applied));

            HotReloadOrchestratorResult second = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("SignatureChangeGatedDeactivate2.cs", later),
                CancellationToken.None);

            string expectedLabel = HotReloadPatcher.FormatMethodKeyParts(
                typeof(HotReloadSignatureChangeAlreadyActiveFixture).FullName,
                nameof(HotReloadSignatureChangeAlreadyActiveFixture.Target),
                new[] { "System.Int32" },
                0);
            AssertDeactivatedPatchesWarningsEqual(
                second,
                ExpectedDeactivatedAddedMembersWarning(expectedLabel));
        }

        /// <summary>
        /// What: a gated replacement's wire key keeps Cecil '/' and '::', so it cannot match
        /// a registry MethodKey until FormatMethodKeyParts normalizes it.
        /// </summary>
        [Test]
        public void FormatGatedReplacementRegistryKey_NestedType_DiffersFromWireKey()
        {
            TransformWorkerEntryDto entry = new TransformWorkerEntryDto
            {
                typeMetadataName = "Ns.Outer/Inner",
                methodName = "Name",
                parameterTypeFullNames = new[] { "System.Int32" },
                genericArity = 0
            };
            string wireKey = "Ns.Outer/Inner::Name(System.Int32)";
            string registryKey = HotReloadSignatureChangeGate.FormatGatedReplacementRegistryKey(entry);

            Assert.That(wireKey, Is.Not.EqualTo(registryKey));
            Assert.That(registryKey, Is.EqualTo("Ns.Outer+Inner.Name(System.Int32)"));
        }

        /// <summary>
        /// What: generic arity and extra parameter types reach the registry label, including
        /// nested-type '+' normalization.
        /// </summary>
        [Test]
        public void FormatGatedReplacementRegistryKey_NestedGenericMultiArg_MatchesRegistryLabel()
        {
            TransformWorkerEntryDto entry = new TransformWorkerEntryDto
            {
                typeMetadataName = "Ns.Outer/Inner",
                methodName = "Name",
                parameterTypeFullNames = new[] { "System.Int32", "System.String" },
                genericArity = 1
            };

            Assert.That(
                HotReloadSignatureChangeGate.FormatGatedReplacementRegistryKey(entry),
                Is.EqualTo("Ns.Outer+Inner.Name`1(System.Int32,System.String)"));
        }

        /// <summary>
        /// What: a return-type change with a compiled caller in another class is skipped together
        /// with the same-file caller, while an unrelated body edit in the same file still patches.
        /// </summary>
        [Test]
        public async Task Run_ReturnTypeChange_ExternalCaller_SkipsReplacementAndSameFileCaller()
        {
            string fixturePath = ResolveSignatureChangeExternalHostPath();
            string onDisk = File.ReadAllText(fixturePath);
            string edited = onDisk
                .Replace(
                    "        public int Target(int value)\n        {\n            return value;\n        }",
                    "        public long Target(int value)\n        {\n            return value + 1L;\n        }",
                    StringComparison.Ordinal)
                .Replace(
                    "            return Target(value);\n        }",
                    "            return (int)Target(value);\n        }",
                    StringComparison.Ordinal)
                .Replace(
                    "        public int Unrelated(int value)\n        {\n            return value;\n        }",
                    "        public int Unrelated(int value)\n        {\n            return value + 1;\n        }",
                    StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk));

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("SignatureChangeExternal.cs", edited),
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            string expectedExternalLabel =
                "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload"
                + ".HotReloadSignatureChangeExternalHost.Target(System.Int32)";
            string expectedExternalReason = string.Format(
                HotReloadConstants.SignatureChangedGateSkipReasonFormat,
                expectedExternalLabel);
            string actualExternalReason = FindSkippedReason(
                result,
                nameof(HotReloadSignatureChangeExternalHost.Target));
            Assert.That(actualExternalReason, Is.EqualTo(expectedExternalReason));
            Assert.That(
                actualExternalReason,
                Does.Contain("Run 'uloop compile'."),
                "Other-file uncovered callers keep the compile-only CTA.");
            Assert.That(
                actualExternalReason,
                Does.Not.Contain("Editing the bodies of"),
                "Other-file uncovered callers must not use the same-file insert.");
            AssertHasSkipped(
                result,
                nameof(HotReloadSignatureChangeExternalHost.SameFileCaller),
                HotReloadConstants.SignatureChangedGatedCallerSkipReason);
            AssertHasPatched(result, nameof(HotReloadSignatureChangeExternalHost.Unrelated));

            HotReloadSignatureChangeExternalHost host = new HotReloadSignatureChangeExternalHost();
            Assert.That(host.Unrelated(3), Is.EqualTo(4));
            Assert.That(host.Target(3), Is.EqualTo(3));
            Assert.That(host.SameFileCaller(3), Is.EqualTo(3));
        }

        /// <summary>
        /// What: deleting a method that still has a compiled caller outside the file applies other
        /// edits and names that caller in a stale-signature warning.
        /// </summary>
        [Test]
        public async Task Run_DeletedMethod_ExternalCaller_WarnsStaleSignature()
        {
            string fixturePath = ResolveSignatureChangeExternalHostPath();
            string onDisk = File.ReadAllText(fixturePath);
            string edited = onDisk.Replace(
                "        public int Unrelated(int value)\n        {\n            return value;\n        }\n\n"
                + "        [MethodImpl(MethodImplOptions.NoInlining)]\n"
                + "        public int ToDelete(int value)\n        {\n            return value;\n        }",
                "        public int Unrelated(int value)\n        {\n            return value + 1;\n        }",
                StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk));
            Assert.That(edited, Does.Not.Contain("ToDelete"));

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("SignatureChangeDeleted.cs", edited),
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            AssertHasPatched(result, nameof(HotReloadSignatureChangeExternalHost.Unrelated));
            string expectedCallerKey =
                "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload"
                + ".HotReloadSignatureChangeExternalCaller::CallDeleted(System.Int32)";
            string expectedSignatureKey =
                "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload"
                + ".HotReloadSignatureChangeExternalHost::ToDelete(System.Int32)";
            string expectedWarning = string.Format(
                HotReloadConstants.StaleSignatureCallersWarningFormat,
                expectedSignatureKey,
                expectedCallerKey);
            Assert.That(result.Warnings, Does.Contain(expectedWarning), FormatOutcomes(result));

            HotReloadSignatureChangeExternalHost host = new HotReloadSignatureChangeExternalHost();
            Assert.That(host.Unrelated(3), Is.EqualTo(4));
        }

        /// <summary>
        /// What: re-applying the same same-file return-type change reports AlreadyActive and
        /// does not double the added-member registry or ActivePatchTotal.
        /// </summary>
        [Test]
        public async Task Run_ReturnTypeChange_Reapply_DoesNotDoubleRegistry()
        {
            string fixturePath = ResolveSignatureChangeSameFileFixturePath();
            string edited = WithSameFileReturnTypeChange(File.ReadAllText(fixturePath));

            HotReloadOrchestratorResult first = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("SignatureChangeReapply1.cs", edited),
                CancellationToken.None);
            AssertNoFileLevelFailure(first);
            AssertHasAdded(first, nameof(HotReloadSignatureChangeSameFileFixture.Target));
            int firstRegistryCount = CountAddedMembersContaining(
                nameof(HotReloadSignatureChangeSameFileFixture.Target));
            Assert.That(first.ActivePatchTotal, Is.EqualTo(2));
            Assert.That(firstRegistryCount, Is.EqualTo(1));

            HotReloadOrchestratorResult second = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("SignatureChangeReapply2.cs", edited),
                CancellationToken.None);
            AssertNoFileLevelFailure(second);
            AssertHasAlreadyActive(
                second,
                nameof(HotReloadSignatureChangeSameFileFixture.Target),
                HotReloadConstants.AlreadyActiveAddedMemberReason);
            Assert.That(second.ActivePatchTotal, Is.EqualTo(2));
            Assert.That(
                CountAddedMembersContaining(nameof(HotReloadSignatureChangeSameFileFixture.Target)),
                Is.EqualTo(1));

            HotReloadSignatureChangeSameFileFixture host = new HotReloadSignatureChangeSameFileFixture();
            Assert.That(host.ExistingCaller(3), Is.EqualTo(4));
        }

        /// <summary>
        /// What: the coverage recheck reports no loss when the covering caller stays in the
        /// final apply set with the replacement.
        /// </summary>
        [Test]
        public void FindSignatureChangeCoverageLosses_CoveredCallerRemains_ReturnsEmpty()
        {
            TransformWorkerEntryDto replacement = CreateReplacementEntry("Host", "Target");
            TransformWorkerEntryDto caller = CreateOrdinaryEntry("Host", "Caller");
            List<HotReloadCallSiteScanner.CallSiteHit> hits = new List<HotReloadCallSiteScanner.CallSiteHit>
            {
                CreateCallSiteHit("Host::Caller(System.Int32)", "Host::Target(System.Int32)")
            };

            List<string> lost = HotReloadSignatureChangeCoverage.FindSignatureChangeCoverageLosses(
                new[] { replacement, caller },
                hits,
                new[] { "Host::Target(System.Int32)" });

            Assert.That(lost, Is.Empty);
        }

        /// <summary>
        /// What: the coverage recheck reports the replacement when its covering caller key
        /// dropped out of the final apply set.
        /// </summary>
        [Test]
        public void FindSignatureChangeCoverageLosses_CallerKeyDropped_ReturnsReplacementKey()
        {
            TransformWorkerEntryDto replacement = CreateReplacementEntry("Host", "Target");
            List<HotReloadCallSiteScanner.CallSiteHit> hits = new List<HotReloadCallSiteScanner.CallSiteHit>
            {
                CreateCallSiteHit("Host::Caller(System.Int32)", "Host::Target(System.Int32)")
            };

            List<string> lost = HotReloadSignatureChangeCoverage.FindSignatureChangeCoverageLosses(
                new[] { replacement },
                hits,
                new[] { "Host::Target(System.Int32)" });

            Assert.That(lost, Is.EqualTo(new[] { "Host::Target(System.Int32)" }));
        }

        /// <summary>
        /// What: shim-compile-failure isolation rewrites a two-hop UnavailableAddedCall chain
        /// to IsolatedAddedMethodCallerSkipReason.
        /// </summary>
        [Test]
        public void CollectRetryOnlySkippedOutcomes_ShimCompileFailure_RewritesTwoHopIndirectCallers()
        {
            TransformWorkerSkippedDto[] retrySkipped =
            {
                new TransformWorkerSkippedDto
                {
                    method = "Host.Mid()",
                    methodKey = "Host::Mid()",
                    reason = HotReloadConstants.UnavailableAddedCallSkipReason,
                    calledAddedMethodKey = "Host::Broken()"
                },
                new TransformWorkerSkippedDto
                {
                    method = "Host.Outer()",
                    methodKey = "Host::Outer()",
                    reason = HotReloadConstants.UnavailableAddedCallSkipReason,
                    calledAddedMethodKey = "Host::Mid()"
                }
            };

            List<HotReloadMethodOutcome> outcomes = HotReloadShimIsolation.CollectRetryOnlySkippedOutcomes(
                Array.Empty<TransformWorkerSkippedDto>(),
                retrySkipped,
                "test.dll",
                HotReloadConstants.VibeLogIsolationTriggerShimCompileFailure,
                new[] { "Host::Broken()" });

            Assert.That(outcomes.Count, Is.EqualTo(2));
            Assert.That(outcomes[0].Reason, Is.EqualTo(HotReloadConstants.IsolatedAddedMethodCallerSkipReason));
            Assert.That(outcomes[1].Reason, Is.EqualTo(HotReloadConstants.IsolatedAddedMethodCallerSkipReason));
        }

        /// <summary>
        /// What: signature-change-gate isolation leaves UnavailableAddedCall on the same two-hop
        /// chain instead of rewriting it.
        /// </summary>
        [Test]
        public void CollectRetryOnlySkippedOutcomes_SignatureChangeGate_LeavesUnavailableAddedCall()
        {
            TransformWorkerSkippedDto[] retrySkipped =
            {
                new TransformWorkerSkippedDto
                {
                    method = "Host.Mid()",
                    methodKey = "Host::Mid()",
                    reason = HotReloadConstants.UnavailableAddedCallSkipReason,
                    calledAddedMethodKey = "Host::Broken()"
                },
                new TransformWorkerSkippedDto
                {
                    method = "Host.Outer()",
                    methodKey = "Host::Outer()",
                    reason = HotReloadConstants.UnavailableAddedCallSkipReason,
                    calledAddedMethodKey = "Host::Mid()"
                }
            };

            List<HotReloadMethodOutcome> outcomes = HotReloadShimIsolation.CollectRetryOnlySkippedOutcomes(
                Array.Empty<TransformWorkerSkippedDto>(),
                retrySkipped,
                "test.dll",
                HotReloadConstants.VibeLogIsolationTriggerSignatureChangeGate,
                new[] { "Host::Broken()" });

            Assert.That(outcomes.Count, Is.EqualTo(2));
            Assert.That(outcomes[0].Reason, Is.EqualTo(HotReloadConstants.UnavailableAddedCallSkipReason));
            Assert.That(outcomes[1].Reason, Is.EqualTo(HotReloadConstants.UnavailableAddedCallSkipReason));
        }

        /// <summary>
        /// What: a retry skip whose calledAddedMethodKey is outside the excluded set stays
        /// UnavailableAddedCall even on shim-compile-failure isolation.
        /// </summary>
        [Test]
        public void CollectRetryOnlySkippedOutcomes_UnrelatedCalledKey_LeavesUnavailableAddedCall()
        {
            TransformWorkerSkippedDto[] retrySkipped =
            {
                new TransformWorkerSkippedDto
                {
                    method = "Host.Caller()",
                    methodKey = "Host::Caller()",
                    reason = HotReloadConstants.UnavailableAddedCallSkipReason,
                    calledAddedMethodKey = "Host::Unrelated()"
                }
            };

            List<HotReloadMethodOutcome> outcomes = HotReloadShimIsolation.CollectRetryOnlySkippedOutcomes(
                Array.Empty<TransformWorkerSkippedDto>(),
                retrySkipped,
                "test.dll",
                HotReloadConstants.VibeLogIsolationTriggerShimCompileFailure,
                new[] { "Host::Broken()" });

            Assert.That(outcomes.Count, Is.EqualTo(1));
            Assert.That(outcomes[0].Reason, Is.EqualTo(HotReloadConstants.UnavailableAddedCallSkipReason));
        }

        /// <summary>
        /// What: uncovered caller short names use Type.Caller order, last type segment only, and
        /// replace nested '/' with '.'.
        /// </summary>
        [Test]
        public void FormatUncoveredCallerShortNames_TwoCallers_JoinsLastTypeSegmentAndMethodInListOrder()
        {
            string names = HotReloadSignatureChangeCoverage.FormatUncoveredCallerShortNames(
                new[]
                {
                    "Ns.Host::AlphaCaller(System.Int32)",
                    "Ns.Outer/Inner::BetaCaller(System.Int32)"
                });

            Assert.That(names, Is.EqualTo("Host.AlphaCaller, Inner.BetaCaller"));
        }

        /// <summary>
        /// What: an unchanged same-file caller that only needs an implicit int-to-long conversion
        /// is not an apply entry, so the return-type change is skipped with the same-file wording
        /// and is not listed as a removed member.
        /// </summary>
        [Test]
        public async Task Run_ReturnTypeChange_UnchangedSameFileCaller_SkipsReplacement()
        {
            string fixturePath = ResolveSignatureChangeUnchangedCallerFixturePath();
            string onDisk = File.ReadAllText(fixturePath);
            string edited = onDisk.Replace(
                "        public int Target(int value)\n        {\n            return value;\n        }",
                "        public long Target(int value)\n        {\n            return value + 1L;\n        }",
                StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk));

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("SignatureChangeUnchangedCaller.cs", edited),
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            string expectedLabel =
                "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload"
                + ".HotReloadSignatureChangeUnchangedCallerFixture.Target(System.Int32)";
            string expectedReason = string.Format(
                HotReloadConstants.SignatureChangedGateSkipReasonSameFileCallersFormat,
                expectedLabel,
                "HotReloadSignatureChangeUnchangedCallerFixture.StoreTarget");
            string actualReason = FindSkippedReason(
                result,
                nameof(HotReloadSignatureChangeUnchangedCallerFixture.Target));
            Assert.That(actualReason, Is.EqualTo(expectedReason));
            Assert.That(
                actualReason,
                Does.Contain(
                    "Editing the bodies of HotReloadSignatureChangeUnchangedCallerFixture.StoreTarget in this file and reloading again applies them together, or run 'uloop compile'."));
            Assert.That(
                CountWarningsContaining(result.Warnings, "Removed members stay present"),
                Is.EqualTo(0),
                "A gated (not applied) replacement must not appear as a removed member.\n"
                + string.Join("\n", result.Warnings));
            Assert.That(
                CountWarningsContaining(
                    result.Warnings,
                    "applied because its compiled call sites were already hot-reload patched"),
                Is.EqualTo(0),
                "A never-patched caller must keep the gate skip and must not emit the re-patched notice.\n"
                + string.Join("\n", result.Warnings));
        }

        /// <summary>
        /// What: a return-type change with two unchanged same-file callers names both short
        /// caller names in the same-file skip reason, in scanner list order.
        /// </summary>
        [Test]
        public async Task Run_ReturnTypeChange_TwoUnchangedSameFileCallers_NamesBothCallersInSkipReason()
        {
            string fixturePath = ResolveSignatureChangeTwoCallerFixturePath();
            string onDisk = File.ReadAllText(fixturePath);
            string edited = onDisk.Replace(
                "        public int Target(int value)\n        {\n            return value;\n        }",
                "        public long Target(int value)\n        {\n            return value + 1L;\n        }",
                StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk));

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("SignatureChangeTwoUnchangedCallers.cs", edited),
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            string expectedLabel =
                "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload"
                + ".HotReloadSignatureChangeTwoCallerFixture.Target(System.Int32)";
            string expectedCallerNames =
                "HotReloadSignatureChangeTwoCallerFixture.CallerAlpha, "
                + "HotReloadSignatureChangeTwoCallerFixture.CallerBeta";
            string expectedReason = string.Format(
                HotReloadConstants.SignatureChangedGateSkipReasonSameFileCallersFormat,
                expectedLabel,
                expectedCallerNames);
            string actualReason = FindSkippedReason(
                result,
                nameof(HotReloadSignatureChangeTwoCallerFixture.Target));
            Assert.That(actualReason, Is.EqualTo(expectedReason));
        }

        /// <summary>
        /// What: after a caller body patch, a later signature-only change applies and names
        /// that already-patched caller in the re-patched notice.
        /// </summary>
        [Test]
        public async Task Run_ReturnTypeChange_AlreadyPatchedCaller_EmitsRepatchedNotice()
        {
            string fixturePath = ResolveSignatureChangeUnchangedCallerFixturePath();
            string onDisk = File.ReadAllText(fixturePath);
            string callerEdited = onDisk.Replace(
                "        public long StoreTarget(int value)\n        {\n            return Target(value);\n        }",
                "        public long StoreTarget(int value)\n        {\n            return Target(value) + 1L;\n        }",
                StringComparison.Ordinal);
            Assert.That(callerEdited, Is.Not.EqualTo(onDisk));

            HotReloadOrchestratorResult first = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("SignatureChangeAlreadyPatchedCaller1.cs", callerEdited),
                CancellationToken.None);

            AssertNoFileLevelFailure(first);
            AssertHasPatched(first, nameof(HotReloadSignatureChangeUnchangedCallerFixture.StoreTarget));

            string signatureEdited = callerEdited.Replace(
                "        public int Target(int value)\n        {\n            return value;\n        }",
                "        public long Target(int value)\n        {\n            return value + 1L;\n        }",
                StringComparison.Ordinal);
            Assert.That(signatureEdited, Is.Not.EqualTo(callerEdited));

            HotReloadOrchestratorResult second = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("SignatureChangeAlreadyPatchedCaller2.cs", signatureEdited),
                CancellationToken.None);

            AssertNoFileLevelFailure(second);
            AssertHasAdded(second, nameof(HotReloadSignatureChangeUnchangedCallerFixture.Target));
            AssertHasPatched(second, nameof(HotReloadSignatureChangeUnchangedCallerFixture.StoreTarget));

            string expectedOldSignature =
                "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload"
                + ".HotReloadSignatureChangeUnchangedCallerFixture::Target(System.Int32)";
            string expectedCallerLabel = HotReloadPatcher.FormatMethodKeyParts(
                typeof(HotReloadSignatureChangeUnchangedCallerFixture).FullName,
                nameof(HotReloadSignatureChangeUnchangedCallerFixture.StoreTarget),
                new[] { "System.Int32" },
                0);
            string expectedWarning = string.Format(
                HotReloadConstants.SignatureChangeCallersRepatchedNoticeFormat,
                expectedOldSignature,
                expectedCallerLabel);
            int matchingWarningCount = 0;
            foreach (string warning in second.Warnings)
            {
                if (string.Equals(warning, expectedWarning, StringComparison.Ordinal))
                {
                    matchingWarningCount++;
                }
            }

            Assert.That(
                matchingWarningCount,
                Is.EqualTo(1),
                "Expected exactly one re-patched notice.\n" + string.Join("\n", second.Warnings));
        }

        /// <summary>
        /// What: a gated skip of Target plus an already-patched sibling that this run stops
        /// calling Target does not emit an applied re-patched notice for the skipped change.
        /// </summary>
        [Test]
        public async Task Run_ReturnTypeChange_GatedTargetAndSweptNonCaller_OmitsRepatchedNotice()
        {
            string fixturePath = ResolveSignatureChangeTwoCallerFixturePath();
            string onDisk = File.ReadAllText(fixturePath);
            string callerEdited = onDisk.Replace(
                "        public long CallerBeta(int value)\n        {\n            return Target(value);\n        }",
                "        public long CallerBeta(int value)\n        {\n            return Target(value) + 1L;\n        }",
                StringComparison.Ordinal);
            Assert.That(callerEdited, Is.Not.EqualTo(onDisk));

            HotReloadOrchestratorResult first = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("SignatureChangeGatedSwept1.cs", callerEdited),
                CancellationToken.None);

            AssertNoFileLevelFailure(first);
            AssertHasPatched(first, nameof(HotReloadSignatureChangeTwoCallerFixture.CallerBeta));

            string mixed = callerEdited
                .Replace(
                    "        public int Target(int value)\n        {\n            return value;\n        }",
                    "        public long Target(int value)\n        {\n            return value + 1L;\n        }",
                    StringComparison.Ordinal)
                .Replace(
                    "        public long CallerBeta(int value)\n        {\n            return Target(value) + 1L;\n        }",
                    "        public long CallerBeta(int value)\n        {\n            return value;\n        }",
                    StringComparison.Ordinal);
            Assert.That(mixed, Is.Not.EqualTo(callerEdited));

            HotReloadOrchestratorResult second = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("SignatureChangeGatedSwept2.cs", mixed),
                CancellationToken.None);

            AssertNoFileLevelFailure(second);
            AssertHasSkipped(
                second,
                nameof(HotReloadSignatureChangeTwoCallerFixture.Target),
                "The return type of");
            Assert.That(
                CountWarningsContaining(
                    second.Warnings,
                    "applied because its compiled call sites were already hot-reload patched"),
                Is.EqualTo(0),
                "A gated skip must not emit an applied re-patched notice.\n"
                + string.Join("\n", second.Warnings));
        }

        /// <summary>
        /// What: two already-patched callers of the same old signature produce one
        /// re-patched notice that names both callers.
        /// </summary>
        [Test]
        public async Task Run_ReturnTypeChange_TwoAlreadyPatchedCallers_EmitsOneAggregatedNotice()
        {
            string fixturePath = ResolveSignatureChangeTwoCallerFixturePath();
            string onDisk = File.ReadAllText(fixturePath);
            string callersEdited = onDisk
                .Replace(
                    "        public long CallerAlpha(int value)\n        {\n            return Target(value);\n        }",
                    "        public long CallerAlpha(int value)\n        {\n            return Target(value) + 1L;\n        }",
                    StringComparison.Ordinal)
                .Replace(
                    "        public long CallerBeta(int value)\n        {\n            return Target(value);\n        }",
                    "        public long CallerBeta(int value)\n        {\n            return Target(value) + 1L;\n        }",
                    StringComparison.Ordinal);
            Assert.That(callersEdited, Is.Not.EqualTo(onDisk));

            HotReloadOrchestratorResult first = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("SignatureChangeTwoPatchedCallers1.cs", callersEdited),
                CancellationToken.None);

            AssertNoFileLevelFailure(first);
            AssertHasPatched(first, nameof(HotReloadSignatureChangeTwoCallerFixture.CallerAlpha));
            AssertHasPatched(first, nameof(HotReloadSignatureChangeTwoCallerFixture.CallerBeta));

            string signatureEdited = callersEdited.Replace(
                "        public int Target(int value)\n        {\n            return value;\n        }",
                "        public long Target(int value)\n        {\n            return value + 1L;\n        }",
                StringComparison.Ordinal);
            Assert.That(signatureEdited, Is.Not.EqualTo(callersEdited));

            HotReloadOrchestratorResult second = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("SignatureChangeTwoPatchedCallers2.cs", signatureEdited),
                CancellationToken.None);

            AssertNoFileLevelFailure(second);
            AssertHasAdded(second, nameof(HotReloadSignatureChangeTwoCallerFixture.Target));
            AssertHasPatched(second, nameof(HotReloadSignatureChangeTwoCallerFixture.CallerAlpha));
            AssertHasPatched(second, nameof(HotReloadSignatureChangeTwoCallerFixture.CallerBeta));

            string expectedOldSignature =
                "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload"
                + ".HotReloadSignatureChangeTwoCallerFixture::Target(System.Int32)";
            string expectedCallerLabels = string.Join(
                ", ",
                new[]
                {
                    HotReloadPatcher.FormatMethodKeyParts(
                        typeof(HotReloadSignatureChangeTwoCallerFixture).FullName,
                        nameof(HotReloadSignatureChangeTwoCallerFixture.CallerAlpha),
                        new[] { "System.Int32" },
                        0),
                    HotReloadPatcher.FormatMethodKeyParts(
                        typeof(HotReloadSignatureChangeTwoCallerFixture).FullName,
                        nameof(HotReloadSignatureChangeTwoCallerFixture.CallerBeta),
                        new[] { "System.Int32" },
                        0)
                });
            string expectedWarning = string.Format(
                HotReloadConstants.SignatureChangeCallersRepatchedNoticeFormat,
                expectedOldSignature,
                expectedCallerLabels);
            int matchingWarningCount = 0;
            int noticeCount = 0;
            foreach (string warning in second.Warnings)
            {
                if (warning.Contains("applied because its compiled call sites were already hot-reload patched"))
                {
                    noticeCount++;
                }

                if (string.Equals(warning, expectedWarning, StringComparison.Ordinal))
                {
                    matchingWarningCount++;
                }
            }

            Assert.That(
                noticeCount,
                Is.EqualTo(1),
                "Expected exactly one re-patched notice for one old signature.\n"
                + string.Join("\n", second.Warnings));
            Assert.That(
                matchingWarningCount,
                Is.EqualTo(1),
                "Expected the aggregated notice to name both callers.\n"
                + string.Join("\n", second.Warnings));
        }

        /// <summary>
        /// What: an uncovered caller on the same type whose method key is absent from the
        /// edited file (other partial file, ctor, or unhandled member) is not same-file.
        /// </summary>
        [Test]
        public void AreUncoveredCallersInEditedFile_SameTypeOtherPartialMethod_ReturnsFalse()
        {
            TransformWorkerEntryDto replacement = CreateReplacementEntry("Host", "Target");
            bool sameFile = HotReloadSignatureChangeCoverage.AreUncoveredCallersInEditedFile(
                new[] { "Host::OtherFileCaller(System.Int32)" },
                new[] { replacement },
                Array.Empty<TransformWorkerUnchangedMethodDto>());

            Assert.That(sameFile, Is.False);
        }

        /// <summary>
        /// What: a gated same-name replacement does not suppress the removed-members warning
        /// for a deleted Target on another type in the same file.
        /// </summary>
        [Test]
        public async Task Run_ReturnTypeChange_SameNameDeletedOnOtherType_KeepsRemovedMembersWarning()
        {
            string fixturePath = ResolveSignatureChangeSameNameHostsPath();
            string onDisk = File.ReadAllText(fixturePath);
            string edited = onDisk.Replace(
                "        public int Target(int value)\n        {\n            return value;\n        }\n\n"
                + "        [MethodImpl(MethodImplOptions.NoInlining)]\n"
                + "        public long Store",
                "        public long Target(int value)\n        {\n            return value + 1L;\n        }\n\n"
                + "        [MethodImpl(MethodImplOptions.NoInlining)]\n"
                + "        public long Store",
                StringComparison.Ordinal);
            edited = edited.Replace(
                "    public class HotReloadSignatureChangeSameNameDeletedHost\n    {\n"
                + "        [MethodImpl(MethodImplOptions.NoInlining)]\n"
                + "        public int Target(int value)\n        {\n            return value;\n        }\n    }",
                "    public class HotReloadSignatureChangeSameNameDeletedHost\n    {\n    }",
                StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk));
            Assert.That(edited, Does.Contain("HotReloadSignatureChangeSameNameDeletedHost"));
            Assert.That(edited, Does.Not.Contain("return value;\n        }"));

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("SignatureChangeSameNameDeleted.cs", edited),
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            AssertHasSkipped(
                result,
                nameof(HotReloadSignatureChangeSameNameGatedHost.Target),
                "The return type of");
            Assert.That(
                CountWarningsContaining(result.Warnings, "Removed members stay present"),
                Is.EqualTo(1),
                "A same-name deletion on another type must still warn.\n"
                + string.Join("\n", result.Warnings));
            Assert.That(
                result.Warnings,
                Has.Some.Contain("Target"),
                "The deleted same-name method must remain in the removed-members warning.");
        }

        /// <summary>
        /// What: a compiled generic Caller&lt;T&gt;(int) that calls Target is not covered by an
        /// edited non-generic Caller(int), so the return-type change is skipped.
        /// </summary>
        [Test]
        public async Task Run_ReturnTypeChange_GenericArityCaller_SkipsReplacement()
        {
            string fixturePath = ResolveSignatureChangeGenericCallerFixturePath();
            string onDisk = File.ReadAllText(fixturePath);
            string edited = onDisk
                .Replace(
                    "        public int Target(int value)\n        {\n            return value;\n        }",
                    "        public long Target(int value)\n        {\n            return value + 1L;\n        }",
                    StringComparison.Ordinal)
                .Replace(
                    "        public int Caller(int value)\n        {\n            return value;\n        }",
                    "        public int Caller(int value)\n        {\n            return value + 1;\n        }",
                    StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk));

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("SignatureChangeGenericCaller.cs", edited),
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            AssertHasSkipped(
                result,
                nameof(HotReloadSignatureChangeGenericCallerFixture.Target),
                "The return type of");
            string expectedCallerLabel = HotReloadPatcher.FormatMethodKeyParts(
                "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload"
                + ".HotReloadSignatureChangeGenericCallerFixture",
                "Caller",
                new[] { "System.Int32" },
                genericArity: 0);
            AssertHasPatched(result, expectedCallerLabel);
        }

        /// <summary>
        /// What: an unchanged compiled Caller&lt;T&gt;(int) must not revert a live patch on
        /// Caller(int) when a later shim-compile failure skips isolation (patch-preservation path).
        /// </summary>
        [Test]
        public async Task Run_UnchangedGenericCaller_DoesNotRevertPatchedNonGenericSibling()
        {
            string fixturePath = ResolveSignatureChangeGenericCallerFixturePath();
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string targetDllPath = Path.Combine(
                projectRoot,
                HotReloadConstants.ScriptAssembliesRelativeDirectory,
                "UnityCLILoop.Tests.Editor.HotReload"
                + HotReloadConstants.CompiledAssemblyExtension);
            Assert.That(
                HotReloadSourceBaseline.LoadVerifiedSnapshotSource(
                    "Assets/Tests/Editor/HotReload/HotReloadSignatureChangeGenericCallerFixture.cs",
                    targetDllPath),
                Is.Not.Null,
                "Verified snapshot must resolve so Caller<T> is listed as unchanged.");

            string onDisk = File.ReadAllText(fixturePath);
            string patchedSource = onDisk.Replace(
                "        public int Caller(int value)\n        {\n            return value;\n        }",
                "        public int Caller(int value)\n        {\n            return value + 1;\n        }",
                StringComparison.Ordinal);
            Assert.That(patchedSource, Is.Not.EqualTo(onDisk));

            HotReloadOrchestratorResult patched = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("GenericCallerPatchThenPreserve1.cs", patchedSource),
                CancellationToken.None);

            AssertNoFileLevelFailure(patched);
            string expectedCallerLabel = HotReloadPatcher.FormatMethodKeyParts(
                "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload"
                + ".HotReloadSignatureChangeGenericCallerFixture",
                "Caller",
                new[] { "System.Int32" },
                genericArity: 0);
            AssertHasPatched(patched, expectedCallerLabel);

            HotReloadSignatureChangeGenericCallerFixture fixture =
                new HotReloadSignatureChangeGenericCallerFixture();
            Assert.That(fixture.Caller(5), Is.EqualTo(6));

            string uncompilableSource = onDisk.Replace(
                "        public int Caller(int value)\n        {\n            return value;\n        }",
                "        public int Caller(int value)\n        {\n            return MissingHelperAddedByEdit(value);\n        }",
                StringComparison.Ordinal);
            Assert.That(uncompilableSource, Is.Not.EqualTo(onDisk));

            HotReloadOrchestratorResult failed = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("GenericCallerPatchThenPreserve2.cs", uncompilableSource),
                CancellationToken.None);

            bool callerFailed = false;
            foreach (HotReloadMethodOutcome outcome in failed.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Failed
                    && outcome.Method == expectedCallerLabel)
                {
                    callerFailed = true;
                }
            }

            Assert.That(
                callerFailed,
                Is.True,
                "Run2 must fail the non-generic Caller on the isolation-skip path.\n"
                + FormatOutcomes(failed));
            Assert.That(
                fixture.Caller(5),
                Is.EqualTo(6),
                "Unchanged Caller<T> must not peel the live Caller(int) patch.");
        }

        /// <summary>
        /// What: deleting a same-file helper that called Target does not gate Target's return-type
        /// change when no other compiled caller remains.
        /// </summary>
        [Test]
        public async Task Run_ReturnTypeChange_DeletedHelperCaller_AppliesReplacement()
        {
            string fixturePath = ResolveSignatureChangeHelperDeleteFixturePath();
            string onDisk = File.ReadAllText(fixturePath);
            string edited = onDisk
                .Replace(
                    "        public int Target(int value)\n        {\n            return value;\n        }",
                    "        public long Target(int value)\n        {\n            return value + 1L;\n        }",
                    StringComparison.Ordinal)
                .Replace(
                    "\n\n        [MethodImpl(MethodImplOptions.NoInlining)]\n"
                    + "        public int Helper(int value)\n        {\n            return Target(value);\n        }",
                    string.Empty,
                    StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk));
            Assert.That(edited, Does.Not.Contain("public int Helper"));

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("SignatureChangeHelperDelete.cs", edited),
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            AssertHasAdded(result, nameof(HotReloadSignatureChangeHelperDeleteFixture.Target));
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                Assert.That(
                    outcome.Kind,
                    Is.Not.EqualTo(HotReloadMethodOutcomeKind.Skipped),
                    "Helper deletion must not gate Target.\n" + FormatOutcomes(result));
            }
        }

        /// <summary>
        /// What: after the gate passes because the same-file caller is an entry, a shim compile
        /// failure on only that caller drops it from the retry set and the coverage recheck
        /// fails the file instead of applying the replacement.
        /// </summary>
        [Test]
        public async Task Run_ReturnTypeChange_CallerShimCompileFailure_FailsCoverageRecheck()
        {
            string fixturePath = ResolveSignatureChangeSameFileFixturePath();
            string onDisk = File.ReadAllText(fixturePath);
            string edited = onDisk
                .Replace(
                    "        public int Target(int value)\n        {\n            return value;\n        }",
                    "        public long Target(int value)\n        {\n            return value + 1L;\n        }",
                    StringComparison.Ordinal)
                .Replace(
                    "            return Target(value);\n        }",
                    "            return (int)Target(value) + MissingHelperAddedByEdit(value);\n        }",
                    StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk));

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("SignatureChangeCallerCompileFailure.cs", edited),
                CancellationToken.None);

            string expectedReplacementKey =
                "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload"
                + ".HotReloadSignatureChangeSameFileFixture::Target(System.Int32)";
            string expectedReason = string.Format(
                HotReloadConstants.SignatureChangeCoverageLostFailureFormat,
                expectedReplacementKey);
            bool foundGateFailure = false;
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Failed
                    && outcome.Method == "(signature-change-gate)"
                    && outcome.Reason == expectedReason)
                {
                    foundGateFailure = true;
                }

                Assert.That(
                    outcome.Kind,
                    Is.Not.EqualTo(HotReloadMethodOutcomeKind.Added),
                    "Coverage loss must not apply the replacement.\n" + FormatOutcomes(result));
            }

            Assert.That(
                foundGateFailure,
                Is.True,
                "Expected file Failed (signature-change-gate).\n" + FormatOutcomes(result));
        }

        /// <summary>
        /// What: the gate-retry FileFailed path (HotReloadOrchestrator ~267-278) reports a
        /// single (signature-change-gate) Failed when an external caller gates a return-type
        /// change and the isolation retry shim compile fails. This is not the coverage
        /// recheck path (~349-370) covered by
        /// Run_ReturnTypeChange_CallerShimCompileFailure_FailsCoverageRecheck.
        /// </summary>
        [Test]
        public async Task Run_ReturnTypeChange_GateRetryShimCompileFailure_FailsFileWithoutApplying()
        {
            string fixturePath = ResolveSignatureChangeExternalHostPath();
            string onDisk = File.ReadAllText(fixturePath);
            string edited = onDisk
                .Replace(
                    "        public int Target(int value)\n        {\n            return value;\n        }",
                    "        public long Target(int value)\n        {\n            return value + 1L;\n        }",
                    StringComparison.Ordinal)
                .Replace(
                    "        public int Unrelated(int value)\n        {\n            return value;\n        }",
                    "        public int Unrelated(int value)\n        {\n            return MissingHelperAddedByEdit(value);\n        }",
                    StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk));
            VibeLogger.ClearMemoryLogs();

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("SignatureChangeGateRetryFailure.cs", edited),
                CancellationToken.None);

            List<JObject> gateLogs = ReadHotReloadVibeLogs();
            JObject gateFileStart = FindVibeLog(gateLogs, HotReloadConstants.VibeLogFileStart);
            JObject gateIsolationRetry = FindVibeLog(gateLogs, HotReloadConstants.VibeLogIsolationRetry);
            JObject gateShimCompileFailed = FindVibeLog(gateLogs, HotReloadConstants.VibeLogShimCompileFailed);
            AssertSameHotReloadCorrelation(gateFileStart, gateIsolationRetry, gateShimCompileFailed);
            Assert.That(
                (string)gateIsolationRetry["context"]["trigger"],
                Is.EqualTo(HotReloadConstants.VibeLogIsolationTriggerSignatureChangeGate));
            Assert.That(
                (string)gateShimCompileFailed["context"]["stage"],
                Is.EqualTo(HotReloadConstants.VibeLogShimCompileStageRetry));

            Assert.That(result.Methods.Count, Is.EqualTo(1), FormatOutcomes(result));
            HotReloadMethodOutcome outcome = result.Methods[0];
            Assert.That(outcome.Kind, Is.EqualTo(HotReloadMethodOutcomeKind.Failed));
            Assert.That(outcome.Method, Is.EqualTo("(signature-change-gate)"));
            Assert.That(outcome.Reason, Does.Contain("Retry shim compile failed"));
            Assert.That(
                outcome.Reason.Contains("Isolation excluded compiled callers"),
                Is.False,
                "This path must not be the coverage-recheck failure.\n" + outcome.Reason);
            foreach (HotReloadMethodOutcome methodOutcome in result.Methods)
            {
                Assert.That(
                    methodOutcome.Kind,
                    Is.Not.EqualTo(HotReloadMethodOutcomeKind.Added),
                    FormatOutcomes(result));
                Assert.That(
                    methodOutcome.Kind,
                    Is.Not.EqualTo(HotReloadMethodOutcomeKind.Patched),
                    FormatOutcomes(result));
            }
        }

        /// <summary>
        /// What: one file can skip a gated return-type change and still apply a covered
        /// replacement in the same run.
        /// </summary>
        [Test]
        public async Task Run_ReturnTypeChange_MultipleReplacements_GatesOneAndAppliesTheOther()
        {
            string fixturePath = ResolveSignatureChangeMultiReplacementHostPath();
            string onDisk = File.ReadAllText(fixturePath);
            string edited = onDisk
                .Replace(
                    "        public int TargetGated(int value)\n        {\n            return value;\n        }",
                    "        public long TargetGated(int value)\n        {\n            return value + 1L;\n        }",
                    StringComparison.Ordinal)
                .Replace(
                    "        public int TargetCovered(int value)\n        {\n            return value;\n        }",
                    "        public long TargetCovered(int value)\n        {\n            return value + 1L;\n        }",
                    StringComparison.Ordinal)
                .Replace(
                    "            return TargetCovered(value);\n        }",
                    "            return (int)TargetCovered(value);\n        }",
                    StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk));

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("SignatureChangeMultiReplacement.cs", edited),
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            AssertHasSkipped(
                result,
                nameof(HotReloadSignatureChangeMultiReplacementHost.TargetGated),
                "The return type of");
            AssertHasAdded(
                result,
                nameof(HotReloadSignatureChangeMultiReplacementHost.TargetCovered));
            string expectedCallerLabel = HotReloadPatcher.FormatMethodKeyParts(
                "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload"
                + ".HotReloadSignatureChangeMultiReplacementHost",
                "CoveredCaller",
                new[] { "System.Int32" },
                genericArity: 0);
            AssertHasPatched(result, expectedCallerLabel);
        }

        /// <summary>
        /// What: changing ToDelete(int) to ToDelete(int, int) warns about the old compiled
        /// signature's remaining caller and classifies the new signature as Added.
        /// </summary>
        [Test]
        public async Task Run_ParameterChange_ExternalCaller_WarnsStaleSignatureAndAddsNewMethod()
        {
            string fixturePath = ResolveSignatureChangeExternalHostPath();
            string onDisk = File.ReadAllText(fixturePath);
            string edited = onDisk
                .Replace(
                    "        public int ToDelete(int value)\n        {\n            return value;\n        }",
                    "        public int ToDelete(int value, int extra)\n        {\n            return value + extra;\n        }",
                    StringComparison.Ordinal)
                .Replace(
                    "        public int Unrelated(int value)\n        {\n            return value;\n        }",
                    "        public int Unrelated(int value)\n        {\n            return value + 1;\n        }",
                    StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk));
            Assert.That(edited, Does.Contain("ToDelete(int value, int extra)"));

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("SignatureChangeParamChange.cs", edited),
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            string expectedAddedLabel = HotReloadPatcher.FormatMethodKeyParts(
                "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload"
                + ".HotReloadSignatureChangeExternalHost",
                "ToDelete",
                new[] { "System.Int32", "System.Int32" },
                genericArity: 0);
            bool foundAddedNewSignature = false;
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Added
                    && outcome.Method == expectedAddedLabel)
                {
                    foundAddedNewSignature = true;
                }
            }

            Assert.That(
                foundAddedNewSignature,
                Is.True,
                "Parameter change must add ToDelete(int, int), not the old ToDelete(int).\n"
                + FormatOutcomes(result));
            AssertHasPatched(result, nameof(HotReloadSignatureChangeExternalHost.Unrelated));
            string expectedCallerKey =
                "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload"
                + ".HotReloadSignatureChangeExternalCaller::CallDeleted(System.Int32)";
            string expectedSignatureKey =
                "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload"
                + ".HotReloadSignatureChangeExternalHost::ToDelete(System.Int32)";
            string expectedWarning = string.Format(
                HotReloadConstants.StaleSignatureCallersWarningFormat,
                expectedSignatureKey,
                expectedCallerKey);
            Assert.That(result.Warnings, Does.Contain(expectedWarning), FormatOutcomes(result));
        }

        /// <summary>
        /// What: after applying an added method, a later hot-reload against the on-disk baseline
        /// (all-unchanged) clears that file's added-member ledger so --status and Play warnings
        /// do not keep counting it.
        /// </summary>
        [Test]
        public async Task Run_ReapplyOnDiskAfterAddedMethod_ClearsAddedMemberRegistry()
        {
            string fixturePath = ResolveAddedMethodApplyFixturePath();
            string onDisk = File.ReadAllText(fixturePath);
            string withAdded = onDisk.Replace(
                "        public int ExistingCaller(int value)\n        {\n            return value;\n        }",
                "        public int ExistingCaller(int value)\n        {\n            return AddedPing(value);\n        }\n\n"
                + "        public int AddedPing(int value)\n        {\n            return value + 1;\n        }",
                StringComparison.Ordinal);
            HotReloadOrchestratorResult first = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("AddedMethodApplyThenOnDisk1.cs", withAdded),
                CancellationToken.None);
            AssertHasAdded(first, "AddedPing");
            HotReloadAddedMethodApplyFixture host = new HotReloadAddedMethodApplyFixture();
            Assert.That(host.ExistingCaller(3), Is.EqualTo(4));

            HotReloadOrchestratorResult second = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                contentPathOverride: null,
                CancellationToken.None);

            AssertNoFileLevelFailure(second);
            Assert.That(second.Methods, Is.Empty, FormatOutcomes(second));
            Assert.That(second.UnchangedTotal, Is.GreaterThan(0));
            Assert.That(second.ActivePatchTotal, Is.EqualTo(0));
            foreach (HotReloadAddedMemberInfo added in HotReloadAddedMemberRegistry.Describe())
            {
                Assert.That(
                    added.MethodKey,
                    Does.Not.Contain("AddedPing"),
                    "All-unchanged re-apply must drop AddedPing from the added-member ledger.");
            }

            AssertNoDeactivatedPatchesWarning(second);
            Assert.That(host.ExistingCaller(3), Is.EqualTo(3));
        }

        /// <summary>
        /// What: an added method is not registered on the pause-point shim ledger, so enabling
        /// a marker on its source line follows the existing not-found path and does not crash.
        /// </summary>
        [Test]
        public async Task Run_AddedMethod_DoesNotRegisterPausePointShim()
        {
            string fixturePath = ResolveAddedMethodApplyFixturePath();
            string onDisk = File.ReadAllText(fixturePath);
            string edited = onDisk.Replace(
                "        public int ExistingCaller(int value)\n        {\n            return value;\n        }",
                "        public int ExistingCaller(int value)\n        {\n            return AddedPing(value);\n        }\n\n"
                + "        public int AddedPing(int value)\n        {\n            return value + 1;\n        }",
                StringComparison.Ordinal);
            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("AddedMethodPausePoint.cs", edited),
                CancellationToken.None);
            AssertHasAdded(result, "AddedPing");

            string projectRelativePath =
                "Assets/Tests/Editor/HotReload/HotReloadAddedMethodApplyFixture.cs";
            HotReloadShimFileLookup lookup =
                HotReloadPausePointCoordination.GetShimLookupForFile?.Invoke(projectRelativePath);
            Assert.That(
                lookup,
                Is.Not.Null,
                "ExistingCaller patch must still expose a pause-point shim lookup.");
            foreach (HotReloadShimMethodLookup method in lookup.Methods)
            {
                Assert.That(
                    method.OriginalMethod.Name,
                    Is.Not.EqualTo("AddedPing"),
                    "Added methods must not be registered as pause-point shim originals.");
            }

            int addedLine = FindLineNumberContaining(edited, "return value + 1;");
            Assert.That(addedLine, Is.GreaterThan(0));
            UloopPausePointRegistry.ConfigureForTests(new OrchestratorPausePointPauseController(), () => DateTime.UtcNow);
            try
            {
                PausePointResponse enable = new PausePointUseCase().Enable(new EnablePausePointSchema
                {
                    File = projectRelativePath,
                    Line = addedLine,
                    TimeoutSeconds = 30,
                    Mode = UloopPausePointCaptureMode.Continuous
                });
                Assert.That(
                    enable.Success,
                    Is.False,
                    "Enable on an added-method line must take the not-found path, not bind a shim.");
                Assert.That(
                    enable.ErrorCode,
                    Is.EqualTo(SourcePausePointConstants.ErrorCodeResolveFailed));
                Assert.That(
                    enable.ResolvedMethod,
                    Does.Not.Contain("AddedPing"));
            }
            finally
            {
                SourcePausePointPatcher.UnpatchAll();
                UloopPausePointRegistry.ResetForTests();
            }
        }

        /// <summary>
        /// What: a private call in a conditional-access argument list inside a lambda is
        /// accessor-rewritten (Delegation) so the enclosing method Patches (not CS0122 Failed).
        /// </summary>
        [Test]
        public async Task Run_LambdaConditionalAccessArgumentPrivateStaticSeven_AccessorizesAndPatches()
        {
            string hostPath = ResolveAddedMemberHostPath();
            string onDisk = File.ReadAllText(hostPath);
            string edited = onDisk.Replace(
                "        public int ExistingCaller(int value)\n        {\n            return value;\n        }",
                "        public int ExistingCaller(int value)\n        {\n"
                + "            HotReloadAddedMemberHost other = this;\n"
                + "            System.Func<int> read = () => other?.ExistingFail(PrivateStaticSeven()) ?? 0;\n"
                + "            return read();\n        }",
                StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk));
            string editedPath = WriteEditedSource("LambdaConditionalAccessPrivateArg.cs", edited);

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { hostPath },
                editedPath,
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            AssertHasPatched(result, nameof(HotReloadAddedMemberHost.ExistingCaller));
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Failed
                    && outcome.Method.Contains(nameof(HotReloadAddedMemberHost.ExistingCaller)))
                {
                    Assert.Fail("ExistingCaller must not Failed: " + outcome.Reason);
                }
            }

            HotReloadAddedMemberHost host = new HotReloadAddedMemberHost();
            Assert.That(host.ExistingCaller(0), Is.EqualTo(7));
        }

        /// <summary>
        /// What: a non-void private instance call inside a lambda is accessor-rewritten
        /// (Func&lt;Host, int&gt; MethodDelegate) so the enclosing method Patches.
        /// </summary>
        [Test]
        public async Task Run_LambdaInstancePrivateCall_AccessorizesAndPatches()
        {
            string hostPath = ResolveAddedMemberHostPath();
            string onDisk = File.ReadAllText(hostPath);
            string edited = onDisk.Replace(
                "        public int ExistingCaller(int value)\n        {\n            return value;\n        }",
                "        public int ExistingCaller(int value)\n        {\n"
                + "            System.Func<int> read = () => PrivateCall();\n"
                + "            return read();\n        }",
                StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk));
            string editedPath = WriteEditedSource("LambdaInstancePrivateCall.cs", edited);

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { hostPath },
                editedPath,
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            AssertHasPatched(result, nameof(HotReloadAddedMemberHost.ExistingCaller));
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Failed
                    && outcome.Method.Contains(nameof(HotReloadAddedMemberHost.ExistingCaller)))
                {
                    Assert.Fail("ExistingCaller must not Failed: " + outcome.Reason);
                }
            }

            HotReloadAddedMemberHost host = new HotReloadAddedMemberHost();
            Assert.That(host.ExistingCaller(0), Is.EqualTo(7));
        }

        /// <summary>
        /// What: a private instance property getter read inside a lambda is accessor-rewritten
        /// (Func&lt;Host, TProp&gt; MethodDelegate) so the enclosing method Patches.
        /// </summary>
        [Test]
        public async Task Run_LambdaInstancePrivatePropertyGetter_AccessorizesAndPatches()
        {
            string hostPath = ResolveAddedMemberHostPath();
            string onDisk = File.ReadAllText(hostPath);
            string edited = onDisk.Replace(
                "        public int ExistingCaller(int value)\n        {\n            return value;\n        }",
                "        public int ExistingCaller(int value)\n        {\n"
                + "            System.Func<int> read = () => PrivateSeedValue;\n"
                + "            return read();\n        }",
                StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk));
            string editedPath = WriteEditedSource("LambdaInstancePrivatePropertyGetter.cs", edited);

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { hostPath },
                editedPath,
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            AssertHasPatched(result, nameof(HotReloadAddedMemberHost.ExistingCaller));
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Failed
                    && outcome.Method.Contains(nameof(HotReloadAddedMemberHost.ExistingCaller)))
                {
                    Assert.Fail("ExistingCaller must not Failed: " + outcome.Reason);
                }
            }

            HotReloadAddedMemberHost host = new HotReloadAddedMemberHost();
            Assert.That(host.ExistingCaller(0), Is.EqualTo(7));
        }

        /// <summary>
        /// What: deleting a compiled method surfaces the aggregated removed-members warning.
        /// </summary>
        [Test]
        public async Task Run_RemovedMethod_EmitsAggregatedRemovedMembersWarning()
        {
            string hostPath = ResolveAddedMemberHostPath();
            string onDisk = File.ReadAllText(hostPath);
            string edited = onDisk.Replace(
                "        [MethodImpl(MethodImplOptions.NoInlining)]\n"
                + "        public int ExistingFail(int value)\n        {\n            return value;\n        }\n\n",
                string.Empty,
                StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk));
            string editedPath = WriteEditedSource("RemovedMethodWarning.cs", edited);

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { hostPath },
                editedPath,
                CancellationToken.None);

            Assert.That(result.Warnings, Is.Not.Null);
            bool foundWarning = false;
            foreach (string warning in result.Warnings)
            {
                if (warning.Contains(nameof(HotReloadAddedMemberHost.ExistingFail))
                    && warning.Contains("'uloop compile'"))
                {
                    foundWarning = true;
                }
            }

            Assert.That(
                foundWarning,
                Is.True,
                "Removed ExistingFail must appear in the aggregated warning.\n"
                + string.Join("\n", result.Warnings));
        }

        /// <summary>
        /// What: a broken added-method body isolates that added method (and its callers) without
        /// collapsing the file; an unrelated edited method still patches.
        /// </summary>
        [Test]
        public async Task Run_AddedMethodBodyFailure_IsolatesWithoutWipingFile()
        {
            string hostPath = ResolveAddedMemberHostPath();
            string onDisk = File.ReadAllText(hostPath);
            string edited = onDisk.Replace(
                "        public int ExistingValue()\n        {\n            return 1;\n        }",
                "        public int ExistingValue()\n        {\n            return 10;\n        }",
                StringComparison.Ordinal);
            edited = edited.Replace(
                "        public int ExistingCaller(int value)\n        {\n            return value;\n        }",
                "        public int ExistingCaller(int value)\n        {\n            return AddedPing(value);\n        }",
                StringComparison.Ordinal);
            edited = edited.Replace(
                "        public int ReadPrivateSeed()\n        {\n            return _privateSeed;\n        }",
                "        public int ReadPrivateSeed()\n        {\n            return _privateSeed;\n        }\n\n"
                + "        public int AddedPing(int value)\n        {\n            return MissingHelperAddedByEdit(value);\n        }",
                StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk));
            string editedPath = WriteEditedSource("AddedMethodBodyFailure.cs", edited);

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { hostPath },
                editedPath,
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            AssertHasPatched(result, nameof(HotReloadAddedMemberHost.ExistingValue));

            bool addedFailed = false;
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Failed
                    && outcome.Method.Contains("AddedPing"))
                {
                    addedFailed = true;
                }
            }

            Assert.That(
                addedFailed,
                Is.True,
                "AddedPing must isolate as a per-method Failed.\n" + FormatOutcomes(result));
            AssertHasSkipped(
                result,
                nameof(HotReloadAddedMemberHost.ExistingCaller),
                HotReloadConstants.IsolatedAddedMethodCallerSkipReason);

            HotReloadAddedMemberHost host = new HotReloadAddedMemberHost();
            Assert.That(host.ExistingValue(), Is.EqualTo(10));
        }

        /// <summary>
        /// What: when isolation excludes a failed added method A and its direct added caller B,
        /// the retry worker skip for transitive caller C (C calls B, not A) appears in the
        /// response as Skipped with IsolatedAddedMethodCallerSkipReason, while independent
        /// edited method D still patches.
        /// </summary>
        [Test]
        public async Task Run_IsolationRetry_ReportsTransitiveCallerOfExcludedAddedMethodAsSkipped()
        {
            string hostPath = ResolveAddedMemberHostPath();
            string onDisk = File.ReadAllText(hostPath);
            string edited = onDisk.Replace(
                "        public int ExistingValue()\n        {\n            return 1;\n        }",
                "        public int ExistingValue()\n        {\n            return 10;\n        }",
                StringComparison.Ordinal);
            edited = edited.Replace(
                "        public int ExistingCaller(int value)\n        {\n            return value;\n        }",
                "        public int ExistingCaller(int value)\n        {\n            return AddedHealthy(value);\n        }",
                StringComparison.Ordinal);
            edited = edited.Replace(
                "        public int ReadPrivateSeed()\n        {\n            return _privateSeed;\n        }",
                "        public int ReadPrivateSeed()\n        {\n            return _privateSeed;\n        }\n\n"
                + "        public int AddedBroken(int value)\n        {\n            return MissingHelperAddedByEdit(value);\n        }\n\n"
                + "        public int AddedHealthy(int value)\n        {\n            return AddedBroken(value);\n        }",
                StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk));
            string editedPath = WriteEditedSource("IsolationRetryTransitiveCaller.cs", edited);

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { hostPath },
                editedPath,
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            Assert.That(
                FindSkippedReason(result, nameof(HotReloadAddedMemberHost.ExistingCaller)),
                Is.EqualTo(HotReloadConstants.IsolatedAddedMethodCallerSkipReason));
            AssertHasPatched(result, nameof(HotReloadAddedMemberHost.ExistingValue));
        }

        /// <summary>
        /// What: a two-hop indirect caller chain of a failed added method is rewritten to
        /// IsolatedAddedMethodCallerSkipReason on both hops.
        /// </summary>
        [Test]
        public async Task Run_IsolationRetry_TwoHopIndirectCallers_UseIsolatedAddedMethodCallerSkipReason()
        {
            string hostPath = ResolveAddedMemberHostPath();
            string onDisk = File.ReadAllText(hostPath);
            string edited = onDisk.Replace(
                "        public int ExistingValue()\n        {\n            return 1;\n        }",
                "        public int ExistingValue()\n        {\n            return 10;\n        }",
                StringComparison.Ordinal);
            edited = edited.Replace(
                "        public int ExistingCaller(int value)\n        {\n            return value;\n        }",
                "        public int ExistingCaller(int value)\n        {\n            return AddedOuter(value);\n        }",
                StringComparison.Ordinal);
            edited = edited.Replace(
                "        public int ReadPrivateSeed()\n        {\n            return _privateSeed;\n        }",
                "        public int ReadPrivateSeed()\n        {\n            return _privateSeed;\n        }\n\n"
                + "        public int AddedBroken(int value)\n        {\n            return MissingHelperAddedByEdit(value);\n        }\n\n"
                + "        public int AddedMid(int value)\n        {\n            return AddedBroken(value);\n        }\n\n"
                + "        public int AddedOuter(int value)\n        {\n            return AddedMid(value);\n        }",
                StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk));
            string editedPath = WriteEditedSource("IsolationRetryTwoHopIndirectCallers.cs", edited);

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { hostPath },
                editedPath,
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            Assert.That(
                FindSkippedReason(result, "AddedOuter"),
                Is.EqualTo(HotReloadConstants.IsolatedAddedMethodCallerSkipReason));
            Assert.That(
                FindSkippedReason(result, nameof(HotReloadAddedMemberHost.ExistingCaller)),
                Is.EqualTo(HotReloadConstants.IsolatedAddedMethodCallerSkipReason));
            AssertHasPatched(result, nameof(HotReloadAddedMemberHost.ExistingValue));
        }

        /// <summary>
        /// What: a signature-change gate retry that excludes an added replacement and its
        /// direct added caller still reports the transitive caller as Skipped with
        /// UnavailableAddedCall, while an independent edited method still patches.
        /// </summary>
        [Test]
        public async Task Run_SignatureChangeGateRetry_ReportsTransitiveCallerOfExcludedAddedMethodAsSkipped()
        {
            string fixturePath = ResolveSignatureChangeExternalHostPath();
            string onDisk = File.ReadAllText(fixturePath);
            string edited = onDisk
                .Replace(
                    "        public int Target(int value)\n        {\n            return value;\n        }",
                    "        public long Target(int value)\n        {\n            return value + 1L;\n        }",
                    StringComparison.Ordinal)
                .Replace(
                    "            return Target(value);\n        }",
                    "            return AddedBridge(value);\n        }",
                    StringComparison.Ordinal)
                .Replace(
                    "        public int Unrelated(int value)\n        {\n            return value;\n        }",
                    "        public int Unrelated(int value)\n        {\n            return value + 1;\n        }",
                    StringComparison.Ordinal)
                .Replace(
                    "        public int ToDelete(int value)\n        {\n            return value;\n        }",
                    "        public int ToDelete(int value)\n        {\n            return value;\n        }\n\n"
                    + "        public int AddedBridge(int value)\n        {\n            return (int)Target(value);\n        }",
                    StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk));

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("SignatureChangeGateTransitiveCaller.cs", edited),
                CancellationToken.None);

            AssertNoFileLevelFailure(result);
            Assert.That(
                FindSkippedReason(
                    result,
                    nameof(HotReloadSignatureChangeExternalHost.SameFileCaller)),
                Is.EqualTo(UnavailableAddedCallSkipReason));
            AssertHasPatched(result, nameof(HotReloadSignatureChangeExternalHost.Unrelated));
        }

        /// <summary>
        /// What: a successful apply records file-start, worker-result, and apply-summary
        /// VibeLogger operations that share one non-empty correlation id.
        /// </summary>
        [Test]
        public async Task Run_SuccessfulApply_RecordsFileStartWorkerResultAndApplySummary()
        {
            string fixturePath = ResolveE2EFixturePath();
            string editedPath = WriteEditedSource(
                "VibeLogSuccess.cs",
                BuildFixtureSource(
                    computeWithPrivateMethod:
                    "public int ComputeWithPrivate(int delta)\n        {\n            return _secret + delta;\n        }",
                    sumGridMethod:
                    "public int SumGrid(int[,] grid)\n        {\n            return 42;\n        }"));
            VibeLogger.ClearMemoryLogs();

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);

            AssertHasPatched(result, nameof(HotReloadE2EFixture.SumGrid));
            List<JObject> logs = ReadHotReloadVibeLogs();
            JObject fileStart = FindVibeLog(logs, HotReloadConstants.VibeLogFileStart);
            JObject workerResult = FindVibeLog(logs, HotReloadConstants.VibeLogWorkerResult);
            JObject applySummary = FindVibeLog(logs, HotReloadConstants.VibeLogApplySummary);
            AssertSameHotReloadCorrelation(fileStart, workerResult, applySummary);
            Assert.That(
                (int)applySummary["context"]["addedCount"],
                Is.EqualTo(CountOutcomeKind(result, HotReloadMethodOutcomeKind.Added)));
        }

        /// <summary>
        /// What: an empty-entries run that clears added members records the empty-entries
        /// VibeLogger operation with the same correlation id as file-start.
        /// </summary>
        [Test]
        public async Task Run_EmptyEntriesClear_RecordsEmptyEntriesClearVibeLog()
        {
            string fixturePath = ResolveAddedMethodApplyFixturePath();
            string onDisk = File.ReadAllText(fixturePath);
            await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("VibeLogEmptyEntries1.cs", WithWorkingAddedPing(onDisk)),
                CancellationToken.None);
            VibeLogger.ClearMemoryLogs();

            await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("VibeLogEmptyEntries2.cs", WithVirtualAddedPing(onDisk)),
                CancellationToken.None);

            List<JObject> logs = ReadHotReloadVibeLogs();
            JObject fileStart = FindVibeLog(logs, HotReloadConstants.VibeLogFileStart);
            JObject emptyEntriesClear = FindVibeLog(logs, HotReloadConstants.VibeLogEmptyEntriesClear);
            AssertSameHotReloadCorrelation(fileStart, emptyEntriesClear);
        }

        /// <summary>
        /// What: a shim compile failure that isolates one method records compile-failed and
        /// isolation-retry VibeLogger operations that share the file-start correlation id.
        /// </summary>
        [Test]
        public async Task Run_ShimCompileFailureWithIsolation_RecordsCompileFailedAndIsolationRetry()
        {
            string fixturePath = ResolveE2EFixturePath();
            string editedSource = BuildFixtureSource(
                computeWithPrivateMethod:
                "public int ComputeWithPrivate(int delta)\n        {\n            return _secret + delta + 100;\n        }",
                callsMissingHelperMethod:
                "public int CallsMissingHelper(int value)\n        {\n            return MissingHelperAddedByEdit(value);\n        }");
            VibeLogger.ClearMemoryLogs();

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                WriteEditedSource("VibeLogIsolation.cs", editedSource),
                CancellationToken.None);

            AssertHasPatched(result, nameof(HotReloadE2EFixture.ComputeWithPrivate));
            List<JObject> logs = ReadHotReloadVibeLogs();
            JObject fileStart = FindVibeLog(logs, HotReloadConstants.VibeLogFileStart);
            JObject shimCompileFailed = FindVibeLog(logs, HotReloadConstants.VibeLogShimCompileFailed);
            JObject isolationRetry = FindVibeLog(logs, HotReloadConstants.VibeLogIsolationRetry);
            AssertSameHotReloadCorrelation(fileStart, shimCompileFailed, isolationRetry);
            Assert.That(
                (string)shimCompileFailed["context"]["stage"],
                Is.EqualTo(HotReloadConstants.VibeLogShimCompileStageFirstPass));
            Assert.That(
                (string)isolationRetry["context"]["trigger"],
                Is.EqualTo(HotReloadConstants.VibeLogIsolationTriggerShimCompileFailure));
            Assert.That((bool)isolationRetry["context"]["retryWorkerSuccess"], Is.True);
        }

        private static List<JObject> ReadHotReloadVibeLogs()
        {
            JArray entries = JArray.Parse(VibeLogger.GetLogsForAi());
            List<JObject> hotReloadLogs = new List<JObject>();
            foreach (JToken token in entries)
            {
                JObject entry = (JObject)token;
                string operation = (string)entry["operation"];
                if (operation != null
                    && operation.StartsWith("hot_reload_", StringComparison.Ordinal))
                {
                    hotReloadLogs.Add(entry);
                }
            }

            return hotReloadLogs;
        }

        private static JObject FindVibeLog(IReadOnlyList<JObject> logs, string operation)
        {
            foreach (JObject entry in logs)
            {
                if ((string)entry["operation"] == operation)
                {
                    return entry;
                }
            }

            Assert.Fail("Missing VibeLogger operation " + operation);
            return null;
        }

        private static void AssertSameHotReloadCorrelation(JObject fileStart, params JObject[] others)
        {
            string correlationId = (string)fileStart["correlation_id"];
            Assert.That(correlationId, Is.Not.Null.And.Not.Empty);
            for (int index = 0; index < others.Length; index++)
            {
                Assert.That(
                    (string)others[index]["correlation_id"],
                    Is.EqualTo(correlationId));
            }
        }

        private static int CountOutcomeKind(
            HotReloadOrchestratorResult result,
            HotReloadMethodOutcomeKind kind)
        {
            int count = 0;
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == kind)
                {
                    count++;
                }
            }

            return count;
        }

        private static int FindLineNumberContaining(string source, string fragment)
        {
            string[] lines = source.Replace("\r\n", "\n").Split('\n');
            for (int index = 0; index < lines.Length; index++)
            {
                if (lines[index].Contains(fragment))
                {
                    return index + 1;
                }
            }

            return -1;
        }

        private static void AssertAddedFieldsLifetimeWarningMatchesAddedFields(
            HotReloadOrchestratorResult result)
        {
            string[] addedFields = result.AddedFields ?? Array.Empty<string>();
            string prefix = HotReloadConstants.AddedFieldsLifetimeWarningFormat.Substring(
                0,
                HotReloadConstants.AddedFieldsLifetimeWarningFormat.IndexOf("{0}", StringComparison.Ordinal));
            List<string> lifetimeWarnings = new List<string>();
            IReadOnlyList<string> warnings = result.Warnings ?? Array.Empty<string>();
            foreach (string warning in warnings)
            {
                if (warning != null && warning.StartsWith(prefix, StringComparison.Ordinal))
                {
                    lifetimeWarnings.Add(warning);
                }
            }

            if (addedFields.Length == 0)
            {
                Assert.That(
                    lifetimeWarnings,
                    Is.Empty,
                    "Empty AddedFields must not emit a lifetime warning.\n"
                    + string.Join("\n", warnings));
                return;
            }

            string expected = string.Format(
                HotReloadConstants.AddedFieldsLifetimeWarningFormat,
                string.Join(", ", addedFields));
            Assert.That(
                lifetimeWarnings.Count,
                Is.EqualTo(1),
                "A non-empty AddedFields run must emit exactly one lifetime warning.\n"
                + string.Join("\n", warnings));
            Assert.That(lifetimeWarnings[0], Is.EqualTo(expected));
        }

        private static void AssertNoFileLevelFailure(HotReloadOrchestratorResult result)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Failed
                    && (outcome.Method == "(file)" || outcome.Method == "(shim-compile)"))
                {
                    Assert.Fail("Unexpected file-level failure: " + outcome.Reason);
                }
            }
        }

        private static string FindSkippedReason(HotReloadOrchestratorResult result, string methodName)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Skipped
                    && outcome.Method.Contains(methodName)
                    && outcome.Reason != null)
                {
                    return outcome.Reason;
                }
            }

            Assert.Fail("Expected a Skipped outcome for " + methodName + ".\n" + FormatOutcomes(result));
            return null;
        }

        private static void AssertHasSkipped(
            HotReloadOrchestratorResult result,
            string methodName,
            string reasonFragment)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Skipped
                    && outcome.Method.Contains(methodName)
                    && outcome.Reason != null
                    && outcome.Reason.Contains(reasonFragment))
                {
                    return;
                }
            }

            Assert.Fail(
                "Expected Skipped outcome for " + methodName + " with reason containing '"
                + reasonFragment + "'.\n" + FormatOutcomes(result));
        }

        private static void AssertHasAdded(HotReloadOrchestratorResult result, string methodName)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Added
                    && outcome.Method.Contains(methodName))
                {
                    return;
                }
            }

            Assert.Fail("Expected Added outcome for " + methodName + ".\n" + FormatOutcomes(result));
        }

        private static void AssertHasPatched(HotReloadOrchestratorResult result, string methodName)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Patched
                    && outcome.Method.Contains(methodName))
                {
                    return;
                }
            }

            Assert.Fail("Expected Patched outcome for " + methodName + ".\n" + FormatOutcomes(result));
        }

        private static void AssertHasAlreadyActive(
            HotReloadOrchestratorResult result,
            string methodName,
            string expectedReason = null)
        {
            string reason = expectedReason ?? HotReloadConstants.AlreadyActiveReason;
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.AlreadyActive
                    && outcome.Method.Contains(methodName))
                {
                    Assert.That(outcome.Reason, Is.EqualTo(reason));
                    return;
                }
            }

            Assert.Fail(
                "Expected AlreadyActive outcome for " + methodName + ".\n" + FormatOutcomes(result));
        }

        private static void AssertHasFailed(HotReloadOrchestratorResult result, string methodName)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Failed
                    && outcome.Method.Contains(methodName))
                {
                    return;
                }
            }

            Assert.Fail("Expected Failed outcome for " + methodName + ".\n" + FormatOutcomes(result));
        }

        private static void AssertNoAlreadyActive(HotReloadOrchestratorResult result)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                Assert.That(
                    outcome.Kind,
                    Is.Not.EqualTo(HotReloadMethodOutcomeKind.AlreadyActive),
                    "Did not expect AlreadyActive.\n" + FormatOutcomes(result));
            }
        }

        private static string FormatUnchangedSourceNonBaselineWarning()
        {
            return string.Format(
                HotReloadConstants.UnchangedSourceNonBaselineWarningFormat,
                "Assets/Tests/Editor/HotReload/HotReloadE2EFixtures.cs");
        }

        private static void AssertHasUnchangedSourceNonBaselineWarning(HotReloadOrchestratorResult result)
        {
            string expected = FormatUnchangedSourceNonBaselineWarning();
            int matchCount = 0;
            foreach (string warning in result.Warnings)
            {
                if (string.Equals(warning, expected, StringComparison.Ordinal))
                {
                    matchCount++;
                }
            }

            Assert.That(
                matchCount,
                Is.EqualTo(1),
                "Expected exactly one unchanged-source non-baseline warning.\n"
                + string.Join("\n", result.Warnings));
        }

        private static void AssertNoUnchangedSourceNonBaselineWarning(HotReloadOrchestratorResult result)
        {
            string expected = FormatUnchangedSourceNonBaselineWarning();
            foreach (string warning in result.Warnings)
            {
                Assert.That(
                    warning,
                    Is.Not.EqualTo(expected),
                    "Did not expect the unchanged-source non-baseline warning.\n"
                    + string.Join("\n", result.Warnings));
            }
        }

        private static int CountOccurrences(string text, string token)
        {
            return text.Split(new[] { token }, StringSplitOptions.None).Length - 1;
        }

        private static string FormatOutcomes(HotReloadOrchestratorResult result)
        {
            List<string> lines = new List<string>();
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                lines.Add(outcome.Kind + " " + outcome.Method + " :: " + outcome.Reason);
            }

            return string.Join("\n", lines);
        }

        private static string ResolveAddedPrivateAccessFixturePath()
        {
            string path = Path.Combine(
                Application.dataPath,
                "Tests",
                "Editor",
                "HotReload",
                "HotReloadAddedPrivateAccessFixture.cs");
            Assert.That(
                File.Exists(path),
                Is.True,
                "Added private-access fixture source missing: " + path);
            return Path.GetFullPath(path);
        }

        private static string ResolveAddedMethodApplyFixturePath()
        {
            string path = Path.Combine(
                Application.dataPath,
                "Tests",
                "Editor",
                "HotReload",
                "HotReloadAddedMethodApplyFixture.cs");
            Assert.That(File.Exists(path), Is.True, "Added-method apply fixture source missing: " + path);
            return Path.GetFullPath(path);
        }

        private static string ResolveSignatureChangeSameFileFixturePath()
        {
            string path = Path.Combine(
                Application.dataPath,
                "Tests",
                "Editor",
                "HotReload",
                "HotReloadSignatureChangeSameFileFixture.cs");
            Assert.That(
                File.Exists(path),
                Is.True,
                "Signature-change same-file fixture source missing: " + path);
            return Path.GetFullPath(path);
        }

        private static string ResolveSignatureChangeTwoCallerFixturePath()
        {
            string path = Path.Combine(
                Application.dataPath,
                "Tests",
                "Editor",
                "HotReload",
                "HotReloadSignatureChangeTwoCallerFixture.cs");
            Assert.That(
                File.Exists(path),
                Is.True,
                "Signature-change two-caller fixture source missing: " + path);
            return Path.GetFullPath(path);
        }

        private static string ResolveSignatureChangeAlreadyActiveFixturePath()
        {
            string path = Path.Combine(
                Application.dataPath,
                "Tests",
                "Editor",
                "HotReload",
                "HotReloadSignatureChangeAlreadyActiveFixture.cs");
            Assert.That(
                File.Exists(path),
                Is.True,
                "Signature-change already-active fixture source missing: " + path);
            return Path.GetFullPath(path);
        }

        private static string ResolveSignatureChangeExternalHostPath()
        {
            string path = Path.Combine(
                Application.dataPath,
                "Tests",
                "Editor",
                "HotReload",
                "HotReloadSignatureChangeExternalHost.cs");
            Assert.That(
                File.Exists(path),
                Is.True,
                "Signature-change external host source missing: " + path);
            return Path.GetFullPath(path);
        }

        private static string ResolveSignatureChangeUnchangedCallerFixturePath()
        {
            string path = Path.Combine(
                Application.dataPath,
                "Tests",
                "Editor",
                "HotReload",
                "HotReloadSignatureChangeUnchangedCallerFixture.cs");
            Assert.That(
                File.Exists(path),
                Is.True,
                "Signature-change unchanged-caller fixture source missing: " + path);
            return Path.GetFullPath(path);
        }

        private static string ResolveSignatureChangeSameNameHostsPath()
        {
            string path = Path.Combine(
                Application.dataPath,
                "Tests",
                "Editor",
                "HotReload",
                "HotReloadSignatureChangeSameNameHosts.cs");
            Assert.That(
                File.Exists(path),
                Is.True,
                "Signature-change same-name hosts source missing: " + path);
            return Path.GetFullPath(path);
        }

        private static string ResolveSignatureChangeHelperDeleteFixturePath()
        {
            string path = Path.Combine(
                Application.dataPath,
                "Tests",
                "Editor",
                "HotReload",
                "HotReloadSignatureChangeHelperDeleteFixture.cs");
            Assert.That(
                File.Exists(path),
                Is.True,
                "Signature-change helper-delete fixture source missing: " + path);
            return Path.GetFullPath(path);
        }

        private static string ResolveSignatureChangeGenericCallerFixturePath()
        {
            string path = Path.Combine(
                Application.dataPath,
                "Tests",
                "Editor",
                "HotReload",
                "HotReloadSignatureChangeGenericCallerFixture.cs");
            Assert.That(
                File.Exists(path),
                Is.True,
                "Signature-change generic-caller fixture source missing: " + path);
            return Path.GetFullPath(path);
        }

        private static string ResolveSignatureChangeMultiReplacementHostPath()
        {
            string path = Path.Combine(
                Application.dataPath,
                "Tests",
                "Editor",
                "HotReload",
                "HotReloadSignatureChangeMultiReplacementHost.cs");
            Assert.That(
                File.Exists(path),
                Is.True,
                "Signature-change multi-replacement host source missing: " + path);
            return Path.GetFullPath(path);
        }

        private static TransformWorkerEntryDto CreateReplacementEntry(string typeName, string methodName)
        {
            return new TransformWorkerEntryDto
            {
                typeMetadataName = typeName,
                methodName = methodName,
                parameterTypeFullNames = new[] { "System.Int32" },
                replacesCompiledMethod = true,
                patchKind = HotReloadConstants.PatchKindAddedMethod
            };
        }

        private static TransformWorkerEntryDto CreateOrdinaryEntry(string typeName, string methodName)
        {
            return new TransformWorkerEntryDto
            {
                typeMetadataName = typeName,
                methodName = methodName,
                parameterTypeFullNames = new[] { "System.Int32" },
                replacesCompiledMethod = false
            };
        }

        private static HotReloadCallSiteScanner.CallSiteHit CreateCallSiteHit(
            string callerMethodKey,
            string targetMethodKey)
        {
            return new HotReloadCallSiteScanner.CallSiteHit
            {
                CallerMethodKey = callerMethodKey,
                TargetMethodKey = targetMethodKey
            };
        }

        private static string WithWorkingAddedPing(string onDisk)
        {
            return onDisk.Replace(
                "        public int ExistingCaller(int value)\n        {\n            return value;\n        }",
                "        public int ExistingCaller(int value)\n        {\n            return AddedPing(value);\n        }\n\n"
                + "        public int AddedPing(int value)\n        {\n            return value + 1;\n        }",
                StringComparison.Ordinal);
        }

        private static string WithBrokenAddedPing(string onDisk)
        {
            return onDisk.Replace(
                "        public int ExistingCaller(int value)\n        {\n            return value;\n        }",
                "        public int ExistingCaller(int value)\n        {\n            return AddedPing(value);\n        }\n\n"
                + "        public int AddedPing(int value)\n        {\n            return MissingHelperAddedByEdit(value);\n        }",
                StringComparison.Ordinal);
        }

        private static string WithVirtualAddedPing(string onDisk)
        {
            return onDisk.Replace(
                "        public int ExistingCaller(int value)\n        {\n            return value;\n        }",
                "        public int ExistingCaller(int value)\n        {\n            return AddedPing(value);\n        }\n\n"
                + "        public virtual int AddedPing(int value)\n        {\n            return value + 1;\n        }",
                StringComparison.Ordinal);
        }

        private static string WithWorkingAddedPingAndPong(string onDisk)
        {
            return onDisk.Replace(
                "        public int ExistingCaller(int value)\n        {\n            return value;\n        }",
                "        public int ExistingCaller(int value)\n        {\n            return AddedPong(value) + AddedPing(value);\n        }\n\n"
                + "        public int AddedPong(int value)\n        {\n            return value + 2;\n        }\n\n"
                + "        public int AddedPing(int value)\n        {\n            return value + 1;\n        }",
                StringComparison.Ordinal);
        }

        private static string WithBrokenAddedPingAndPong(string onDisk)
        {
            return onDisk.Replace(
                "        public int ExistingCaller(int value)\n        {\n            return value;\n        }",
                "        public int ExistingCaller(int value)\n        {\n            return AddedPong(value) + AddedPing(value);\n        }\n\n"
                + "        public int AddedPong(int value)\n        {\n            return MissingHelperAddedByEdit(value);\n        }\n\n"
                + "        public int AddedPing(int value)\n        {\n            return MissingHelperAddedByEdit(value);\n        }",
                StringComparison.Ordinal);
        }

        private static string AddedPingMethodLabel()
        {
            return HotReloadPatcher.FormatMethodKeyParts(
                typeof(HotReloadAddedMethodApplyFixture).FullName,
                "AddedPing",
                new[] { "System.Int32" },
                0);
        }

        private static string AddedPongMethodLabel()
        {
            return HotReloadPatcher.FormatMethodKeyParts(
                typeof(HotReloadAddedMethodApplyFixture).FullName,
                "AddedPong",
                new[] { "System.Int32" },
                0);
        }

        private static string ExpectedDeactivatedAddedMembersWarning(string joinedLabels)
        {
            return string.Format(HotReloadConstants.DeactivatedAddedMembersWarningFormat, joinedLabels);
        }

        private static bool IsDeactivatedPatchesWarning(string warning)
        {
            return warning.StartsWith(
                    DeactivatedWarningPrefix(HotReloadConstants.DeactivatedPatchesWarningFormat),
                    StringComparison.Ordinal)
                || warning.StartsWith(
                    DeactivatedWarningPrefix(HotReloadConstants.DeactivatedAddedMembersWarningFormat),
                    StringComparison.Ordinal);
        }

        private static string DeactivatedWarningPrefix(string format)
        {
            int placeholderIndex = format.IndexOf("{0}", StringComparison.Ordinal);
            Assert.That(placeholderIndex, Is.GreaterThanOrEqualTo(0));
            return format.Substring(0, placeholderIndex);
        }

        private static List<string> FilterDeactivatedPatchesWarnings(IReadOnlyList<string> warnings)
        {
            List<string> filtered = new List<string>();
            foreach (string warning in warnings)
            {
                if (IsDeactivatedPatchesWarning(warning))
                {
                    filtered.Add(warning);
                }
            }

            return filtered;
        }

        private static void AssertDeactivatedPatchesWarningsEqual(
            HotReloadOrchestratorResult result,
            params string[] expectedWarnings)
        {
            Assert.That(
                FilterDeactivatedPatchesWarnings(result.Warnings),
                Is.EqualTo(expectedWarnings),
                FormatOutcomes(result) + "\n" + string.Join("\n", result.Warnings));
        }

        private static void AssertNoDeactivatedPatchesWarning(HotReloadOrchestratorResult result)
        {
            AssertDeactivatedPatchesWarningsEqual(result);
        }

        private static string WithSameFileReturnTypeChange(string onDisk)
        {
            string edited = onDisk
                .Replace(
                    "        public int Target(int value)\n        {\n            return value;\n        }",
                    "        public long Target(int value)\n        {\n            return value + 1L;\n        }",
                    StringComparison.Ordinal)
                .Replace(
                    "            return Target(value);\n        }",
                    "            return (int)Target(value);\n        }",
                    StringComparison.Ordinal);
            Assert.That(edited, Is.Not.EqualTo(onDisk));
            return edited;
        }

        private static int CountAddedMembersContaining(string methodName)
        {
            int count = 0;
            foreach (HotReloadAddedMemberInfo added in HotReloadAddedMemberRegistry.Describe())
            {
                if (added.MethodKey.Contains(methodName))
                {
                    count++;
                }
            }

            return count;
        }

        private static string ResolveGenericMethodSkipFixturePath()
        {
            string path = Path.Combine(
                Application.dataPath,
                "Tests",
                "Editor",
                "HotReload",
                "HotReloadGenericMethodSkipFixture.cs");
            Assert.That(
                File.Exists(path),
                Is.True,
                "Generic-method skip fixture source missing: " + path);
            return Path.GetFullPath(path);
        }

        private static HotReloadMethodResult FindResponseMethod(
            HotReloadResponse response,
            string methodName)
        {
            Assert.That(response, Is.Not.Null);
            Assert.That(response.Methods, Is.Not.Null);
            foreach (HotReloadMethodResult method in response.Methods)
            {
                if (method.Method != null && method.Method.Contains(methodName))
                {
                    return method;
                }
            }

            List<string> rows = new List<string>();
            foreach (HotReloadMethodResult method in response.Methods)
            {
                rows.Add(method.Kind + " " + method.Method + " :: " + method.Reason);
            }

            Assert.Fail("Expected response method containing '" + methodName + "'.\n" + string.Join("\n", rows));
            return null;
        }

        private static string WithPrivateAddedFields(string onDisk)
        {
            return onDisk.Replace(
                "        public int ReadAdded()\n        {\n            return 0;\n        }\n\n"
                + "        [MethodImpl(MethodImplOptions.NoInlining)]\n"
                + "        public void WriteAdded(int value)\n        {\n        }",
                "        private int AlphaScratch;\n"
                + "        private int BetaScratch;\n\n"
                + "        [MethodImpl(MethodImplOptions.NoInlining)]\n"
                + "        public int ReadAdded()\n        {\n            return AlphaScratch + BetaScratch;\n        }\n\n"
                + "        [MethodImpl(MethodImplOptions.NoInlining)]\n"
                + "        public void WriteAdded(int value)\n        {\n            AlphaScratch = value;\n            BetaScratch = value;\n        }",
                StringComparison.Ordinal);
        }

        private static string ResolveAddedFieldApplyFixturePath()
        {
            string path = Path.Combine(
                Application.dataPath,
                "Tests",
                "Editor",
                "HotReload",
                "HotReloadAddedFieldApplyFixture.cs");
            Assert.That(File.Exists(path), Is.True, "Added-field apply fixture source missing: " + path);
            return Path.GetFullPath(path);
        }

        private static string WithE2EAddedField(string source, string fieldName)
        {
            return source.Replace(
                "        private int _secret = 10;",
                "        private int _secret = 10;\n        public int " + fieldName + ";",
                StringComparison.Ordinal);
        }

        private static string WithAddedFieldAccesses(string onDisk)
        {
            return onDisk.Replace(
                "        public int ReadAdded()\n        {\n            return 0;\n        }\n\n"
                + "        [MethodImpl(MethodImplOptions.NoInlining)]\n"
                + "        public void WriteAdded(int value)\n        {\n        }",
                "        public int AddedCount;\n\n"
                + "        [MethodImpl(MethodImplOptions.NoInlining)]\n"
                + "        public int ReadAdded()\n        {\n            return AddedCount;\n        }\n\n"
                + "        [MethodImpl(MethodImplOptions.NoInlining)]\n"
                + "        public void WriteAdded(int value)\n        {\n            AddedCount = value;\n        }",
                StringComparison.Ordinal);
        }

        private static string WithAddedFieldAccessesCallingMissingHelper(string onDisk)
        {
            return onDisk.Replace(
                "        public int ReadAdded()\n        {\n            return 0;\n        }\n\n"
                + "        [MethodImpl(MethodImplOptions.NoInlining)]\n"
                + "        public void WriteAdded(int value)\n        {\n        }",
                "        public int AddedCount;\n\n"
                + "        [MethodImpl(MethodImplOptions.NoInlining)]\n"
                + "        public int ReadAdded()\n        {\n            return AddedCount + MissingHelperAddedByEdit(0);\n        }\n\n"
                + "        [MethodImpl(MethodImplOptions.NoInlining)]\n"
                + "        public void WriteAdded(int value)\n        {\n            AddedCount = value + MissingHelperAddedByEdit(0);\n        }",
                StringComparison.Ordinal);
        }

        private static string ResolveAddedMemberHostPath()
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

        private static string ResolveUnsupportedKindFixturePath()
        {
            string path = Path.Combine(
                Application.dataPath,
                "Tests",
                "Editor",
                "HotReload",
                "HotReloadUnsupportedMemberKindFixtures.cs");
            Assert.That(File.Exists(path), Is.True, "Unsupported-kind fixture source missing: " + path);
            return Path.GetFullPath(path);
        }

        private static string ResolveE2EFixturePath()
        {
            string path = Path.Combine(
                Application.dataPath,
                "Tests",
                "Editor",
                "HotReload",
                "HotReloadE2EFixtures.cs");
            Assert.That(File.Exists(path), Is.True, "E2E fixture source missing: " + path);
            return Path.GetFullPath(path);
        }

        private static string ResolveCoreFixturePath()
        {
            string path = Path.Combine(
                Application.dataPath,
                "Tests",
                "Editor",
                "HotReload",
                "HotReloadCoreFixtures.cs");
            Assert.That(File.Exists(path), Is.True, "Core fixture source missing: " + path);
            return Path.GetFullPath(path);
        }

        private static string ResolveShapeFixturePath()
        {
            string path = Path.Combine(
                Application.dataPath,
                "Tests",
                "Editor",
                "HotReload",
                "HotReloadShapeFixtures.cs");
            Assert.That(File.Exists(path), Is.True, "Shape fixture source missing: " + path);
            return Path.GetFullPath(path);
        }

        private static string ResolveEventFixturePath()
        {
            string path = Path.Combine(
                Application.dataPath, "Tests", "Editor", "HotReload", "HotReloadEventFixtures.cs");
            Assert.That(File.Exists(path), Is.True, "Event fixture source missing: " + path);
            return Path.GetFullPath(path);
        }

        private static string BuildEventFixtureSource(string handleScoreChangedMethod)
        {
            return @"using System;
using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    public class HotReloadEventFixture
    {
        public event Action ScoreChanged;

        public int HandledCount;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void EnableCounting()
        {
            // Net two subscriptions: the patched edited body yields HandledCount == 10,
            // while the original single-subscribe body yields 5, so the assertion can
            // tell a Patched subscriber from a Skipped one. The -= line additionally
            // pins the SubtractAssignment exemption in the worker's event gate.
            ScoreChanged += HandleScoreChanged;
            ScoreChanged += HandleScoreChanged;
            ScoreChanged += HandleScoreChanged;
            ScoreChanged -= HandleScoreChanged;
        }

        " + handleScoreChangedMethod + @"

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void RaiseScore()
        {
            // Double Invoke so the edited template differs from on-disk and stays Skipped
            // (single Invoke would be unchanged and disappear from Methods after D).
            ScoreChanged?.Invoke();
            ScoreChanged?.Invoke();
        }
    }
}
";
        }

        private static string WriteEditedSource(string fileName, string contents)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string directory = Path.Combine(
                projectRoot,
                HotReloadConstants.TestSourcesRelativeDirectory);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, fileName);
            File.WriteAllText(path, contents);
            return path;
        }

        private static string ResolveSiblingConstDefinitionsPath()
        {
            string path = Path.Combine(
                Application.dataPath,
                "Tests",
                "Editor",
                "HotReload",
                "HotReloadSiblingConstDefinitions.cs");
            Assert.That(File.Exists(path), Is.True, "Sibling const holder fixture missing: " + path);
            return Path.GetFullPath(path);
        }

        private static string ResolveSiblingConstUserPath()
        {
            string path = Path.Combine(
                Application.dataPath,
                "Tests",
                "Editor",
                "HotReload",
                "HotReloadSiblingConstUser.cs");
            Assert.That(File.Exists(path), Is.True, "Sibling const user fixture missing: " + path);
            return Path.GetFullPath(path);
        }

        private const string ExpectedSiblingTuningDriftWarning =
            "const io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadSiblingConstDefinitions.SiblingTuning is 7 in the edited source but 6 in the compiled assembly; edits outside method bodies never take effect through hot reload. Run 'uloop compile' to apply this change.";

        private static IDisposable MutateSiblingTuningValue(int newValue)
        {
            string path = ResolveSiblingConstDefinitionsPath();
            string original = File.ReadAllText(path);
            string compiledDeclaration = "public const int SiblingTuning = 6;";
            Assert.That(
                original.Contains(compiledDeclaration),
                Is.True,
                "Precondition: compiled sibling const declaration must still be on disk.");
            EditorApplication.LockReloadAssemblies();
            File.WriteAllText(
                path,
                original.Replace(
                    compiledDeclaration,
                    "public const int SiblingTuning = " + newValue + ";"));
            return new FileRestoreScope(new[] { path }, new[] { original });
        }

        private static IDisposable TouchSmallestSiblingsWithTrailingComment(
            string editedAbsolutePath,
            int siblingCount)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            UnityEditor.Compilation.Assembly compilationAssembly = null;
            foreach (UnityEditor.Compilation.Assembly assembly in CompilationPipeline.GetAssemblies())
            {
                if (assembly.name == "UnityCLILoop.Tests.Editor.HotReload")
                {
                    compilationAssembly = assembly;
                    break;
                }
            }

            Assert.That(compilationAssembly, Is.Not.Null, "HotReload test assembly missing from CompilationPipeline.");
            Assert.That(compilationAssembly.sourceFiles, Is.Not.Null);

            List<string> siblingPaths = new List<string>();
            foreach (string relative in compilationAssembly.sourceFiles)
            {
                string absolute = Path.GetFullPath(
                    Path.Combine(projectRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
                if (string.Equals(absolute, editedAbsolutePath, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!File.Exists(absolute))
                {
                    continue;
                }

                siblingPaths.Add(absolute);
            }

            Assert.That(
                siblingPaths.Count,
                Is.GreaterThanOrEqualTo(siblingCount),
                "Need at least " + siblingCount + " sibling sources in the HotReload test assembly.");

            // Why shortest first: the worker parses every changed sibling, and this assembly
            // contains multi-thousand-line test files that would dominate runtime without
            // changing what the cap warning asserts.
            siblingPaths.Sort(CompareByFileLengthThenPath);
            string[] paths = new string[siblingCount];
            string[] originals = new string[siblingCount];
            EditorApplication.LockReloadAssemblies();
            for (int index = 0; index < siblingCount; index++)
            {
                paths[index] = siblingPaths[index];
                originals[index] = File.ReadAllText(paths[index]);
                File.WriteAllText(paths[index], originals[index] + "\n// sibling-scan-cap-probe");
            }

            return new FileRestoreScope(paths, originals);
        }

        private static int CompareByFileLengthThenPath(string left, string right)
        {
            long leftLength = new FileInfo(left).Length;
            long rightLength = new FileInfo(right).Length;
            int lengthCompare = leftLength.CompareTo(rightLength);
            if (lengthCompare != 0)
            {
                return lengthCompare;
            }

            return string.CompareOrdinal(left, right);
        }

        private static IDisposable HideAssemblySnapshotDirectory()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string targetDllPath = Path.Combine(
                projectRoot,
                HotReloadConstants.ScriptAssembliesRelativeDirectory,
                "UnityCLILoop.Tests.Editor.HotReload"
                + HotReloadConstants.CompiledAssemblyExtension);
            string mvid = HotReloadSourceSnapshotter.ReadAssemblyMvid(targetDllPath);
            string snapshotDirectory = Path.Combine(
                projectRoot,
                HotReloadConstants.SourceSnapshotRelativeDirectory,
                "UnityCLILoop.Tests.Editor.HotReload-" + mvid);
            Assert.That(
                Directory.Exists(snapshotDirectory),
                Is.True,
                "Precondition: assembly snapshot directory must exist to hide: " + snapshotDirectory);
            string hiddenDirectory = snapshotDirectory + ".hidden-for-test";
            if (Directory.Exists(hiddenDirectory))
            {
                Directory.Delete(hiddenDirectory, recursive: true);
            }

            Directory.Move(snapshotDirectory, hiddenDirectory);
            return new DirectoryRestoreScope(snapshotDirectory, hiddenDirectory);
        }

        private sealed class FileRestoreScope : IDisposable
        {
            private readonly string[] _paths;
            private readonly string[] _originals;
            private bool _disposed;

            public FileRestoreScope(string[] paths, string[] originals)
            {
                Debug.Assert(paths != null, "paths must not be null.");
                Debug.Assert(originals != null, "originals must not be null.");
                Debug.Assert(paths.Length == originals.Length, "paths and originals must be the same length.");
                _paths = paths;
                _originals = originals;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                for (int index = 0; index < _paths.Length; index++)
                {
                    File.WriteAllText(_paths[index], _originals[index]);
                }

                EditorApplication.UnlockReloadAssemblies();
            }
        }

        private sealed class DirectoryRestoreScope : IDisposable
        {
            private readonly string _originalDirectory;
            private readonly string _hiddenDirectory;
            private bool _disposed;

            public DirectoryRestoreScope(string originalDirectory, string hiddenDirectory)
            {
                _originalDirectory = originalDirectory;
                _hiddenDirectory = hiddenDirectory;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                if (!Directory.Exists(_hiddenDirectory))
                {
                    return;
                }

                if (Directory.Exists(_originalDirectory))
                {
                    Directory.Delete(_originalDirectory, recursive: true);
                }

                Directory.Move(_hiddenDirectory, _originalDirectory);
            }
        }

        /// <summary>
        /// Temporarily moves a verified snapshot aside so LoadVerifiedSnapshotSource returns null.
        /// Restores the file on dispose.
        /// </summary>
        private static IDisposable HideVerifiedSnapshot(string projectRelativePath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string targetDllPath = Path.Combine(
                projectRoot,
                HotReloadConstants.ScriptAssembliesRelativeDirectory,
                "UnityCLILoop.Tests.Editor.HotReload"
                + HotReloadConstants.CompiledAssemblyExtension);
            string mvid = HotReloadSourceSnapshotter.ReadAssemblyMvid(targetDllPath);
            string snapshotFileName =
                HotReloadSourceSnapshotter.HashProjectRelativePath(
                    projectRelativePath.Replace('\\', '/')) + ".cs";
            string snapshotPath = Path.Combine(
                projectRoot,
                HotReloadConstants.SourceSnapshotRelativeDirectory,
                "UnityCLILoop.Tests.Editor.HotReload-" + mvid,
                snapshotFileName);
            Assert.That(
                File.Exists(snapshotPath),
                Is.True,
                "Precondition: verified snapshot must exist to hide: " + snapshotPath);
            // Why LoadVerifiedSnapshotSource (not File.Exists alone): a stale/checksum-invalid
            // snapshot file already yields null, so hiding it would not change the precondition.
            Assert.That(
                HotReloadSourceBaseline.LoadVerifiedSnapshotSource(projectRelativePath, targetDllPath),
                Is.Not.Null,
                "Precondition: snapshot must be loadable before hide: " + projectRelativePath);

            string hiddenPath = snapshotPath + ".hidden-for-test";
            if (File.Exists(hiddenPath))
            {
                File.Delete(hiddenPath);
            }

            File.Move(snapshotPath, hiddenPath);
            return new SnapshotHideScope(snapshotPath, hiddenPath);
        }

        private sealed class SnapshotHideScope : IDisposable
        {
            private readonly string _snapshotPath;
            private readonly string _hiddenPath;
            private bool _disposed;

            public SnapshotHideScope(string snapshotPath, string hiddenPath)
            {
                _snapshotPath = snapshotPath;
                _hiddenPath = hiddenPath;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                if (File.Exists(_hiddenPath))
                {
                    if (File.Exists(_snapshotPath))
                    {
                        File.Delete(_snapshotPath);
                    }

                    File.Move(_hiddenPath, _snapshotPath);
                }
            }
        }

        private static int CountWarningsContaining(IReadOnlyList<string> warnings, string token)
        {
            int count = 0;
            foreach (string warning in warnings)
            {
                if (warning.Contains(token))
                {
                    count++;
                }
            }

            return count;
        }

        private static string BuildFixtureSource(
            string computeWithPrivateMethod,
            string sumGridMethod = null,
            string callsMissingHelperMethod = null,
            string queryPrivateMethod = null,
            string asyncPrivateFieldAndMethod = null,
            string iteratePrivateMethod = null,
            string lambdaPrivateMethod = null,
            string propertyPrivateRoundTripMethod = null,
            string asyncUsesInternalTypeMethod = null,
            string tuningConstDeclaration = null,
            string modeEnumDeclaration = null,
            string centerOfCellMethod = null,
            string callsBaseMethod = null,
            string explicitAccessorsBlock = null,
            string inlineRiskAlphaMethod = null,
            string inlineRiskBetaMethod = null)
        {
            string sumGrid = sumGridMethod ??
                "public int SumGrid(int[,] grid)\n        {\n            return -1;\n        }";
            string callsMissingHelper = callsMissingHelperMethod ??
                "public int CallsMissingHelper(int value)\n        {\n            return value;\n        }";
            string queryPrivate = queryPrivateMethod ??
                "public int QueryPrivate()\n        {\n            int[] values = { 1, 2, 3 };\n"
                + "            return (from value in values where value < _secret select value).Count();\n        }";
            string asyncPrivate = asyncPrivateFieldAndMethod ??
                "public async Task<int> AsyncPrivateFieldAndMethod(int delta)\n        {\n"
                + "            await Task.Yield();\n"
                + "            return _secret + delta;\n"
                + "        }";
            string iteratePrivate = iteratePrivateMethod ??
                "public IEnumerator IteratePrivate(int delta)\n        {\n"
                + "            yield return _secret + delta;\n"
                + "        }";
            string lambdaPrivate = lambdaPrivateMethod ??
                "public int LambdaPrivate(int threshold)\n        {\n"
                + "            Func<int, bool> pred = v => v < _secret;\n"
                + "            return pred(threshold) ? 1 : 0;\n"
                + "        }";
            string propertyPrivate = propertyPrivateRoundTripMethod ??
                "public int PropertyPrivateRoundTrip(int value)\n        {\n"
                + "            HiddenScore = value;\n"
                + "            return HiddenScore;\n"
                + "        }";
            string asyncInternal = asyncUsesInternalTypeMethod ??
                "public async Task<int> AsyncUsesInternalType()\n        {\n"
                + "            await Task.Yield();\n"
                + "            HotReloadE2EInternalToken token = new HotReloadE2EInternalToken { N = 1 };\n"
                + "            return token.N;\n"
                + "        }";
            string tuningConst = tuningConstDeclaration ??
                "private const int TuningConst = 3;";
            string modeEnum = modeEnumDeclaration ??
                "public enum HotReloadE2EMode\n    {\n        Idle = 0,\n        Active = 1\n    }";
            string centerOfCell = centerOfCellMethod ??
                "[MethodImpl(MethodImplOptions.NoInlining)]\n"
                + "        public Vector3 CenterOfCell(Vector3Int cell)\n"
                + "        {\n"
                + "            return Vector3.zero;\n"
                + "        }";
            string callsBase = callsBaseMethod ??
                "public int CallsBase()\n        {\n            return base.BaseSeed() + 1;\n        }";
            string explicitAccessors = explicitAccessorsBlock ??
                "// Explicit-body getter — worker must report get_ExplicitBodyGetter as Skipped (not silent).\n"
                + "        public int ExplicitBodyGetter\n"
                + "        {\n"
                + "            get { return _secret; }\n"
                + "        }\n"
                + "\n"
                + "        // Explicit-body setter — worker must report set_ExplicitBodySetter as Skipped (not silent).\n"
                + "        public int ExplicitBodySetter\n"
                + "        {\n"
                + "            set { _secret = value; }\n"
                + "        }\n"
                + "\n"
                + "        public int Counter;\n"
                + "\n"
                + "        private int this[int index] => _secret + index;";
            string inlineRiskAlpha = inlineRiskAlphaMethod ??
                "[MethodImpl(MethodImplOptions.AggressiveInlining)]\n"
                + "        public int InlineRiskAlpha()\n"
                + "        {\n"
                + "            return 1;\n"
                + "        }";
            string inlineRiskBeta = inlineRiskBetaMethod ??
                "[MethodImpl(MethodImplOptions.AggressiveInlining)]\n"
                + "        public int InlineRiskBeta()\n"
                + "        {\n"
                + "            return 2;\n"
                + "        }";

            return @"using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    " + modeEnum + @"

    public class HotReloadE2EBase
    {
        protected int BaseSeed()
        {
            return 1;
        }
    }

    public class HotReloadE2ESibling
    {
        public int Value;
    }

    public interface IHotReloadE2EMarker
    {
        int ExplicitPing();
    }

    public class HotReloadE2EFixture : HotReloadE2EBase, IHotReloadE2EMarker
    {
        private int _secret = 10;
        " + tuningConst + @"
        // Why keep these compiled properties: omitting them makes property-removed
        // warnings fire and breaks exact Warnings.Count asserts in this template.
        private int? Score { get; set; }
        private int Value { get; set; }

        public int SecretForAssert => _secret;

        public HotReloadE2EFixture Current
        {
            get { return this; }
        }

        " + explicitAccessors + @"

        private int HiddenScore { get; set; } = 3;

        private void BumpSecretBy(int amount)
        {
            _secret += amount;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        " + computeWithPrivateMethod + @"

        " + callsBase + @"

        " + callsMissingHelper + @"

        int IHotReloadE2EMarker.ExplicitPing()
        {
            return _secret;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        " + queryPrivate + @"

        public async Task<int> AsyncReadPrivateIndexer()
        {
            await Task.Yield();
            return this[0];
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        " + sumGrid + @"

        " + centerOfCell + @"

        " + inlineRiskAlpha + @"

        " + inlineRiskBeta + @"

        public int CountEnumerator(List<int>.Enumerator enumerator)
        {
            return 0;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        " + asyncPrivate + @"

        [MethodImpl(MethodImplOptions.NoInlining)]
        " + iteratePrivate + @"

        [MethodImpl(MethodImplOptions.NoInlining)]
        " + lambdaPrivate + @"

        [MethodImpl(MethodImplOptions.NoInlining)]
        " + propertyPrivate + @"

        " + asyncInternal + @"
    }

    internal class HotReloadE2EInternalToken
    {
        public int N;
    }
}
";
        }

        private sealed class OrchestratorPausePointPauseController : IUloopPausePointPauseController
        {
            public bool IsPlaying => true;

            public bool IsPaused => false;

            public void Pause()
            {
            }

            public void Resume()
            {
            }
        }
    }

    /// <summary>
    /// Shim-shaped fixture whose binder throws, so the BindShimAccessors failure path can be
    /// pinned without fabricating a shim assembly that compiles but fails to bind.
    /// </summary>
    internal static class HotReloadBindFailShim
    {
        public static void __BindAccessors()
        {
            throw new System.MissingMethodException("no such member");
        }
    }

    /// <summary>
    /// Shim-shaped fixture whose binder succeeds; counts invocations so the test can prove a
    /// healthy binder is invoked and leaves no failure entry.
    /// </summary>
    internal static class HotReloadBindProbeShim
    {
        public static int BindCalls;

        public static void __BindAccessors()
        {
            BindCalls++;
        }
    }
}
