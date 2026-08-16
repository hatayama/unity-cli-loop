using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using Newtonsoft.Json.Linq;

using HarmonyLib;

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
        }

        /// <summary>
        /// What: the added-field store assembly is injected only when the store flag is set,
        /// matching the Harmony optional-reference pattern.
        /// </summary>
        [Test]
        public void AppendOptionalShimAssemblyReferences_StoreFlag_AddsToolContractsAssembly()
        {
            List<string> references = new List<string>();
            HotReloadOrchestrator.AppendOptionalShimAssemblyReferences(
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
            HotReloadOrchestrator.AppendOptionalShimAssemblyReferences(
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
            HotReloadOrchestrator.AppendOptionalShimAssemblyReferences(
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

            HotReloadOrchestrator.AppendOptionalShimAssemblyReferences(
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
            bool includeStore = HotReloadOrchestrator.NeedsAddedFieldStoreReference(output);
            List<string> references = new List<string>();
            HotReloadOrchestrator.AppendOptionalShimAssemblyReferences(
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
                "Compiled code outside this hot reload still calls the removed signature");
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
                "Compiled code outside this hot reload still calls the removed signature");
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

            Dictionary<string, string> failures = HotReloadOrchestrator.BindShimAccessors(
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
        /// instance, and a second apply keeps the stored values.
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
            AssertHasPatched(second, nameof(HotReloadAddedFieldApplyFixture.ReadAdded));
            Assert.That(firstHost.ReadAdded(), Is.EqualTo(10));
            Assert.That(secondHost.ReadAdded(), Is.EqualTo(20));
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

            HotReloadAddedMethodApplyFixture host = new HotReloadAddedMethodApplyFixture();
            Assert.That(host.ExistingCaller(3), Is.EqualTo(13));
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

            HotReloadSignatureChangeSameFileFixture host = new HotReloadSignatureChangeSameFileFixture();
            Assert.That(host.ExistingCaller(3), Is.EqualTo(4));
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
            AssertHasSkipped(
                result,
                nameof(HotReloadSignatureChangeExternalHost.Target),
                "The return type of");
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
        /// What: re-applying the same same-file return-type change does not double the added-member
        /// registry or ActivePatchTotal.
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
            AssertHasAdded(second, nameof(HotReloadSignatureChangeSameFileFixture.Target));
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

            List<string> lost = HotReloadOrchestrator.FindSignatureChangeCoverageLosses(
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

            List<string> lost = HotReloadOrchestrator.FindSignatureChangeCoverageLosses(
                new[] { replacement },
                hits,
                new[] { "Host::Target(System.Int32)" });

            Assert.That(lost, Is.EqualTo(new[] { "Host::Target(System.Int32)" }));
        }

        /// <summary>
        /// What: an unchanged same-file caller that only needs an implicit int-to-long conversion
        /// is not an apply entry, so the return-type change is skipped with the hot-reload wording.
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
            AssertHasSkipped(
                result,
                nameof(HotReloadSignatureChangeUnchangedCallerFixture.Target),
                string.Format(
                    HotReloadConstants.SignatureChangedGateSkipReasonFormat,
                    expectedLabel));
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

        public int SecretForAssert => _secret;

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
