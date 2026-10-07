using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// End-to-end EditMode coverage for method edits on a partial type: each edit is applied and
    /// the patched method returns the new value at run time.
    /// </summary>
    public class HotReloadPartialTypeE2ETests
    {
        private const string FixtureFileName = "HotReloadPartialTypeFixture.cs";
        private const string OtherPartFileName = "HotReloadPartialTypeFixture.Other.cs";
        private const string OwnOnlyDeclaration =
            "        public int OwnOnly()\n        {\n            return 1;\n        }";
        private const string OwnOnlyEdited =
            "        public int OwnOnly()\n        {\n            return 2;\n        }";

        private HotReloadDomainTestScope _scope;

        [SetUp]
        public void SetUp()
        {
            _scope = new HotReloadDomainTestScope();
            HotReloadAutoRefreshHold.SyncToActiveChanges();
        }

        [TearDown]
        public void TearDown()
        {
            _scope.Dispose();
            HotReloadAutoRefreshHold.SyncToActiveChanges();
            VibeLogger.ClearMemoryLogs();
        }

        /// <summary>
        /// What: a method that reads a private field declared in another part of the type is
        /// patched and returns the edited value.
        /// </summary>
        [Test]
        public async Task Run_PartialTypeMethodReadingOtherPartPrivateField_PatchesBehavior()
        {
            HotReloadOrchestratorResult result = await RunEditedFixtureAsync(
                "PartialE2EReadsOtherPartField.cs",
                "return _otherSeed;",
                "return _otherSeed + 100;");

            AssertNoFileLevelFailure(result);
            AssertPatched(result, "ReadsOtherPartField");
            Assert.That(new HotReloadPartialTypeFixture().ReadsOtherPartField(), Is.EqualTo(105));
        }

        /// <summary>
        /// What: a method that calls a private method declared in another part of the type is
        /// patched and returns the edited value.
        /// </summary>
        [Test]
        public async Task Run_PartialTypeMethodCallingOtherPartPrivateMethod_PatchesBehavior()
        {
            HotReloadOrchestratorResult result = await RunEditedFixtureAsync(
                "PartialE2ECallsOtherPartMethod.cs",
                "return OtherPartValue();",
                "return OtherPartValue() + 100;");

            AssertNoFileLevelFailure(result);
            AssertPatched(result, "CallsOtherPartMethod");
            Assert.That(new HotReloadPartialTypeFixture().CallsOtherPartMethod(), Is.EqualTo(107));
        }

        /// <summary>
        /// What: in a partial type nested in a partial type, a method that reads a field declared in
        /// the nested type's other part is patched and returns the edited value.
        /// </summary>
        [Test]
        public async Task Run_NestedPartialTypeMethodReadingOtherPartField_PatchesBehavior()
        {
            HotReloadOrchestratorResult result = await RunEditedFixtureAsync(
                "PartialE2ENestedReadsOtherPartField.cs",
                "return _nestedSeed;",
                "return _nestedSeed + 100;");

            AssertNoFileLevelFailure(result);
            AssertPatched(result, "ReadsOtherPartNestedField");
            Assert.That(new HotReloadPartialTypeFixture.NestedPartial().ReadsOtherPartNestedField(), Is.EqualTo(113));
        }

        /// <summary>
        /// What: when both parts of the type are passed, a method edited in each part is patched and
        /// returns its edited value.
        /// </summary>
        [Test]
        public async Task Run_BothPartsOfAPartialTypeEdited_PatchesBoth()
        {
            string fixturePath = FixturePath(FixtureFileName);
            string otherPartPath = FixturePath(OtherPartFileName);

            HotReloadOrchestratorResult result = await RunAsync(
                new[] { fixturePath, otherPartPath },
                new Dictionary<string, string>
                {
                    [fixturePath] = WriteEdited(fixturePath, "PartialE2EBothPartsMain.cs", OwnOnlyDeclaration, OwnOnlyEdited),
                    [otherPartPath] = WriteEdited(
                        otherPartPath,
                        "PartialE2EBothPartsOther.cs",
                        "return PartialTuning - 1;",
                        "return PartialTuning - 1 + 100;")
                });

            AssertNoFileLevelFailure(result);
            AssertPatched(result, "OwnOnly");
            AssertPatched(result, "OtherPartOwnMethod");
            HotReloadPartialTypeFixture fixture = new HotReloadPartialTypeFixture();
            Assert.That(fixture.OwnOnly(), Is.EqualTo(2));
            Assert.That(fixture.OtherPartOwnMethod(), Is.EqualTo(103));
        }

        /// <summary>
        /// What: when both parts of the type are passed and one of them changes a const, the const
        /// drift is reported once, not once per part.
        /// </summary>
        [Test]
        public async Task Run_PartialTypeConstDriftWithBothPartsEdited_WarnsOnce()
        {
            string fixturePath = FixturePath(FixtureFileName);
            string otherPartPath = FixturePath(OtherPartFileName);

            HotReloadOrchestratorResult result = await RunAsync(
                new[] { fixturePath, otherPartPath },
                new Dictionary<string, string>
                {
                    [fixturePath] = WriteEdited(fixturePath, "PartialE2EConstDriftMain.cs", OwnOnlyDeclaration, OwnOnlyEdited),
                    [otherPartPath] = WriteEdited(
                        otherPartPath,
                        "PartialE2EConstDriftOther.cs",
                        "private const int PartialTuning = 4;",
                        "private const int PartialTuning = 6;")
                });

            AssertNoFileLevelFailure(result);
            int driftCount = 0;
            foreach (string warning in result.Warnings)
            {
                if (warning.Contains("PartialTuning"))
                {
                    driftCount++;
                }
            }

            Assert.That(
                driftCount,
                Is.EqualTo(1),
                "Expected exactly one drift warning for PartialTuning.\n" + string.Join("\n", result.Warnings));
        }

        /// <summary>
        /// What: a method that uses members of its own part is patched and returns the edited value.
        /// This pins that the edited file is never also read as another part of its own type: the
        /// second copy of _ownSeed would make the name ambiguous and fail the shim compile.
        /// </summary>
        [Test]
        public async Task Run_PartialTypeMethodUsingOwnPartMembers_PatchesBehavior()
        {
            HotReloadOrchestratorResult result = await RunEditedFixtureAsync(
                "PartialE2EUsesOwnPartMembers.cs",
                "return OwnOnly() + _ownSeed;",
                "return OwnOnly() + _ownSeed + 100;");

            AssertNoFileLevelFailure(result);
            AssertPatched(result, "CallsOwnPartMembers");
            Assert.That(new HotReloadPartialTypeFixture().CallsOwnPartMembers(), Is.EqualTo(103));
        }

        /// <summary>
        /// What: a method that passes its own instance to a compiled API of the same assembly is
        /// patched and returns the edited value.
        /// </summary>
        [Test]
        public async Task Run_PartialTypeMethodPassingItselfToACompiledApi_PatchesBehavior()
        {
            HotReloadOrchestratorResult result = await RunEditedFixtureAsync(
                "PartialE2EPassesThis.cs",
                "return HotReloadPartialTypeFixtureConsumer.Describe(this);",
                "return HotReloadPartialTypeFixtureConsumer.Describe(this) + 100;");

            AssertNoFileLevelFailure(result);
            AssertPatched(result, "PassesThisToCompiledApi");
            Assert.That(new HotReloadPartialTypeFixture().PassesThisToCompiledApi(), Is.EqualTo(103));
        }

        private static async Task<HotReloadOrchestratorResult> RunEditedFixtureAsync(
            string editedFileName,
            string fragment,
            string replacement)
        {
            string fixturePath = FixturePath(FixtureFileName);
            return await RunAsync(
                new[] { fixturePath },
                new Dictionary<string, string>
                {
                    [fixturePath] = WriteEdited(fixturePath, editedFileName, fragment, replacement)
                });
        }

        private static async Task<HotReloadOrchestratorResult> RunAsync(
            string[] files,
            Dictionary<string, string> contentPathOverrideByFile)
        {
            return await HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                files,
                contentPathOverride: null,
                CancellationToken.None,
                contentPathOverrideByFile);
        }

        // Why the uniqueness check: a fragment that also matched another member would edit a method
        // the test does not call, and the assert would pass or fail for the wrong reason.
        private static string WriteEdited(string sourcePath, string editedFileName, string fragment, string replacement)
        {
            string source = File.ReadAllText(sourcePath);
            int first = source.IndexOf(fragment, StringComparison.Ordinal);
            Assert.That(first, Is.GreaterThanOrEqualTo(0), "Fragment missing from the fixture: " + fragment);
            Assert.That(
                source.IndexOf(fragment, first + fragment.Length, StringComparison.Ordinal),
                Is.EqualTo(-1),
                "Fragment occurs more than once in the fixture: " + fragment);
            return HotReloadTestSourceWriter.WriteEditedSource(
                editedFileName,
                source.Replace(fragment, replacement, StringComparison.Ordinal));
        }

        private static string FixturePath(string fileName)
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", fileName));
            Assert.That(File.Exists(path), Is.True, "Fixture missing: " + path);
            return path;
        }

        private static void AssertNoFileLevelFailure(HotReloadOrchestratorResult result)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Failed
                    && (outcome.Method == "(file)" || outcome.Method == "(shim-compile)"))
                {
                    Assert.Fail("Unexpected file-level failure: " + outcome.Reason + "\n" + FormatOutcomes(result));
                }
            }
        }

        private static void AssertPatched(HotReloadOrchestratorResult result, string methodName)
        {
            // Why the dot and the parenthesis: a bare name would also match a longer method name
            // that contains it.
            string labelPart = "." + methodName + "(";
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Patched
                    && outcome.Method != null
                    && outcome.Method.Contains(labelPart))
                {
                    return;
                }
            }

            Assert.Fail("Expected Patched for " + methodName + ".\n" + FormatOutcomes(result));
        }

        private static string FormatOutcomes(HotReloadOrchestratorResult result)
        {
            List<string> lines = new List<string>();
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                lines.Add(outcome.Kind + " " + outcome.Method + " @" + outcome.FilePath + " :: " + outcome.Reason);
            }

            return string.Join("\n", lines) + "\nWarnings:\n" + string.Join("\n", result.Warnings);
        }
    }
}
