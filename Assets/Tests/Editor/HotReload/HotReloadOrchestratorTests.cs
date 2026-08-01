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
                BuildFixtureSourceWithComputeBody("return _secret + delta + 100;"));

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
        /// What: an edited body that calls a non-existent helper fails shim compile with the
        /// new-member hint (Failed, not a silent skip).
        /// </summary>
        [Test]
        public async Task Run_EditedBodyCallingMissingHelper_FailsShimCompileWithHint()
        {
            string fixturePath = ResolveE2EFixturePath();
            string editedPath = WriteEditedSource(
                "MissingHelper.cs",
                BuildFixtureSourceWithMissingHelperCall());

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

        private static string BuildFixtureSourceWithComputeBody(string computeBodyExpression)
        {
            return @"namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    public class HotReloadE2EBase
    {
        protected int BaseSeed()
        {
            return 1;
        }
    }

    public class HotReloadE2EFixture : HotReloadE2EBase
    {
        private int _secret = 10;

        public int SecretForAssert => _secret;

        public int ComputeWithPrivate(int delta)
        {
            " + computeBodyExpression + @"
        }

        public int CallsBase()
        {
            return base.BaseSeed() + 1;
        }

        public int CallsMissingHelper(int value)
        {
            return value;
        }
    }
}
";
        }

        private static string BuildFixtureSourceWithMissingHelperCall()
        {
            return @"namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    public class HotReloadE2EBase
    {
        protected int BaseSeed()
        {
            return 1;
        }
    }

    public class HotReloadE2EFixture : HotReloadE2EBase
    {
        private int _secret = 10;

        public int SecretForAssert => _secret;

        public int ComputeWithPrivate(int delta)
        {
            return _secret + delta;
        }

        public int CallsBase()
        {
            return base.BaseSeed() + 1;
        }

        public int CallsMissingHelper(int value)
        {
            return MissingHelperAddedByEdit(value);
        }
    }
}
";
        }
    }
}
