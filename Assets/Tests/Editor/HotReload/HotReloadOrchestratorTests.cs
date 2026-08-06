using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

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
        /// What: one orchestrator run over the fixture reports the explicit-body property getter,
        /// the explicit-body property setter, and the expression-bodied indexer getter as Skipped
        /// with the v1 accessor reason, while auto-property accessors stay unlisted.
        /// </summary>
        [Test]
        public async Task Run_ExplicitAccessorsSkipped_AutoPropertyAccessorsUnlisted()
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
                    "// Explicit-body getter — worker must report get_ExplicitBodyGetter as Skipped (not silent).\n"
                    + "        public int ExplicitBodyGetter\n"
                    + "        {\n"
                    + "            get { return _secret + 1; }\n"
                    + "        }\n"
                    + "\n"
                    + "        // Explicit-body setter — worker must report set_ExplicitBodySetter as Skipped (not silent).\n"
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
                "Property and indexer accessors are out of scope for v1; run 'uloop compile' to apply accessor edits.";
            bool foundPropertyGetter = false;
            bool foundIndexerGetter = false;
            bool foundPropertySetter = false;
            bool foundAutoPropertyAccessor = false;
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                bool skippedWithAccessorReason = outcome.Kind == HotReloadMethodOutcomeKind.Skipped
                    && outcome.Reason == expectedReason;
                if (skippedWithAccessorReason && outcome.Method.Contains("get_ExplicitBodyGetter"))
                {
                    foundPropertyGetter = true;
                }

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
                foundPropertyGetter,
                Is.True,
                "Expected get_ExplicitBodyGetter to be Skipped with the accessor out-of-scope reason.");
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
        /// What: patching multiple small methods emits one aggregated inline-risk warning and
        /// leaves each Patched outcome's Reason empty.
        /// </summary>
        [Test]
        public async Task Run_MultipleSmallPatchedMethods_AggregatesInlineRiskWarningOnce()
        {
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

            int patchedWithEmptyReason = 0;
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind != HotReloadMethodOutcomeKind.Patched)
                {
                    continue;
                }

                Assert.That(outcome.Reason, Is.Empty, "Patched Reason must not carry per-method inline-risk text.");
                patchedWithEmptyReason++;
            }

            Assert.That(patchedWithEmptyReason, Is.GreaterThanOrEqualTo(2));

            int aggregatedInlineRiskCount = 0;
            foreach (string warning in result.Warnings)
            {
                if (warning.Contains("patched methods had pre-patch bodies")
                    && warning.Contains(nameof(HotReloadE2EFixture.ComputeWithPrivate))
                    && warning.Contains(nameof(HotReloadE2EFixture.CenterOfCell)))
                {
                    aggregatedInlineRiskCount++;
                }
            }

            Assert.That(
                aggregatedInlineRiskCount,
                Is.EqualTo(1),
                "Expected exactly one aggregated inline-risk warning listing the at-risk methods.");

            int declarationDriftCount = CountWarningsContaining(
                result.Warnings,
                "Edits outside method bodies");
            Assert.That(
                result.Warnings,
                Has.Count.EqualTo(1 + declarationDriftCount),
                "Expected the aggregated inline-risk warning plus any declaration-drift warning(s).");
        }

        /// <summary>
        /// Verifies duplicate file inputs re-patch the same method yet the aggregated warning lists it once.
        /// </summary>
        [Test]
        public async Task Run_DuplicateFileInputs_ListsEachAtRiskMethodOnceInAggregatedWarning()
        {
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

            int declarationDriftCount = CountWarningsContaining(
                result.Warnings,
                "Edits outside method bodies");
            Assert.That(
                result.Warnings,
                Has.Count.EqualTo(1 + declarationDriftCount),
                "Expected the aggregated inline-risk warning plus one declaration-drift warning per duplicate file input.");

            string aggregatedWarning = null;
            foreach (string warning in result.Warnings)
            {
                if (warning.Contains("patched methods had pre-patch bodies")
                    && warning.Contains(nameof(HotReloadE2EFixture.ComputeWithPrivate)))
                {
                    aggregatedWarning = warning;
                    break;
                }
            }

            Assert.That(aggregatedWarning, Is.Not.Null, "Expected an aggregated inline-risk warning.");
            Assert.That(
                CountOccurrences(aggregatedWarning, nameof(HotReloadE2EFixture.ComputeWithPrivate)),
                Is.EqualTo(1),
                "The aggregated warning must list a re-patched method once, not once per patch operation.");
            Assert.That(
                declarationDriftCount,
                Is.EqualTo(2),
                "Duplicate file inputs each emit a declaration-drift warning.");
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
            string explicitAccessorsBlock = null)
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
