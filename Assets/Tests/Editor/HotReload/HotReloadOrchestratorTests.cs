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
        /// What: a method containing base. is reported as Skipped with an explanatory reason.
        /// </summary>
        [Test]
        public async Task Run_MethodWithBaseCall_IsSkippedWithReason()
        {
            string fixturePath = ResolveE2EFixturePath();

            HotReloadOrchestratorResult result = await HotReloadOrchestrator.RunAsync(
                new[] { fixturePath },
                contentPathOverride: null,
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

        private static string BuildFixtureSource(
            string computeWithPrivateMethod,
            string sumGridMethod = null,
            string callsMissingHelperMethod = null,
            string queryPrivateMethod = null)
        {
            string sumGrid = sumGridMethod ??
                "public int SumGrid(int[,] grid)\n        {\n            return -1;\n        }";
            string callsMissingHelper = callsMissingHelperMethod ??
                "public int CallsMissingHelper(int value)\n        {\n            return value;\n        }";
            string queryPrivate = queryPrivateMethod ??
                "public int QueryPrivate()\n        {\n            int[] values = { 1, 2, 3 };\n"
                + "            return (from value in values where value < _secret select value).Count();\n        }";

            return @"using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
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

        public int SecretForAssert => _secret;

        public int Counter;

        private int this[int index] => _secret + index;

        " + computeWithPrivateMethod + @"

        public int CallsBase()
        {
            return base.BaseSeed() + 1;
        }

        " + callsMissingHelper + @"

        int IHotReloadE2EMarker.ExplicitPing()
        {
            return _secret;
        }

        " + queryPrivate + @"

        public async Task<int> AsyncReadPrivateIndexer()
        {
            await Task.Yield();
            return this[0];
        }

        " + sumGrid + @"

        public int CountEnumerator(List<int>.Enumerator enumerator)
        {
            return 0;
        }
    }
}
";
        }
    }
}
