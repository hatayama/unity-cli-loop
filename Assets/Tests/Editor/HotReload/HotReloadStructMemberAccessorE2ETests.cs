using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// End-to-end EditMode coverage for edited closures that reach members of a struct held in a
    /// private field: inaccessible instance members are Skipped without taking down the file, and
    /// the shapes that already worked stay patched.
    /// </summary>
    public class HotReloadStructMemberAccessorE2ETests
    {
        private const string FixtureFileName = "HotReloadStructMemberAccessorFixture.cs";

        private const string StructMemberNoShapeReason =
            "inaccessible instance member of a struct has no accessor rewrite shape.";

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
        /// What: one closure reaching an internal struct field no longer fails the whole file; it is
        /// Skipped, while an ordinary edit in the same file is Patched and returns the edited value,
        /// and no row is Failed.
        /// </summary>
        [Test]
        public async Task Run_StructFieldClosureBesideOrdinaryEdit_SkipsOnlyTheClosureMethod()
        {
            HotReloadStructMemberAccessorHost host = new HotReloadStructMemberAccessorHost();
            Assert.That(host.Ordinary(), Is.EqualTo(8901));

            HotReloadOrchestratorResult result = await RunEditedAsync(
                "StructFieldBesideOrdinary.cs",
                ("_s.X + 8101;", "_s.X + 8111;"),
                ("return 8901;", "return 8911;"));

            AssertNoFailed(result);
            AssertSkippedForStructMember(result, "ReadField");
            AssertOutcome(result, HotReloadMethodOutcomeKind.Patched, "Ordinary");
            Assert.That(host.Ordinary(), Is.EqualTo(8911));
            Assert.That(host.ReadField(), Is.EqualTo(8101), "The skipped method keeps its compiled body.");
        }

        /// <summary>
        /// What: a closure setting and reading an internal struct property is Skipped and keeps
        /// returning the compiled value, instead of being patched with a setter that writes to a
        /// copy and returning 0.
        /// </summary>
        [Test]
        public async Task Run_StructPropertyClosureEdited_IsSkippedAndKeepsTheCompiledValue()
        {
            Assert.That(new HotReloadStructMemberAccessorHost().Property(), Is.EqualTo(8501));

            HotReloadOrchestratorResult result = await RunEditedAsync(
                "StructPropertyClosure.cs",
                ("_s.Value = 8501;", "_s.Value = 8511;"));

            AssertNoFailed(result);
            AssertSkippedForStructMember(result, "Property");
            Assert.That(new HotReloadStructMemberAccessorHost().Property(), Is.EqualTo(8501));
        }

        /// <summary>
        /// What: a closure writing an internal STATIC field of a struct stays patched (no struct
        /// receiver is involved), returns the edited value, and reverts.
        /// </summary>
        [Test]
        public async Task Run_StructStaticFieldClosureEdited_StaysPatchedAndReverts()
        {
            await AssertPatchedAndRevertedAsync(
                "StaticField", "Count = 8301;", "Count = 8311;", () => new HotReloadStructMemberAccessorHost().StaticField(), 8301, 8311);
        }

        /// <summary>
        /// What: a closure calling an internal STATIC method of a struct stays patched, returns the
        /// edited value, and reverts.
        /// </summary>
        [Test]
        public async Task Run_StructStaticMethodClosureEdited_StaysPatchedAndReverts()
        {
            await AssertPatchedAndRevertedAsync(
                "StaticMethod", "StaticCompute(8601)", "StaticCompute(8611)", () => new HotReloadStructMemberAccessorHost().StaticMethod(), 17202, 17222);
        }

        /// <summary>
        /// What: a closure writing a public field and calling a public method of a struct held in a
        /// private host field stays patched, keeps the write, returns the edited value, and reverts.
        /// </summary>
        [Test]
        public async Task Run_StructPublicMembersClosureEdited_StaysPatchedAndReverts()
        {
            await AssertPatchedAndRevertedAsync(
                "Control", "_s.Open = 8701;", "_s.Open = 8711;", () => new HotReloadStructMemberAccessorHost().Control(), 8701, 8711);
        }

        private static async Task AssertPatchedAndRevertedAsync(
            string methodName,
            string anchor,
            string replacement,
            Func<int> probe,
            int compiledValue,
            int editedValue)
        {
            Assert.That(probe(), Is.EqualTo(compiledValue), "Precondition: compiled value.");

            HotReloadOrchestratorResult result = await RunEditedAsync(
                "StructMember_" + methodName + ".cs",
                (anchor, replacement));

            AssertOutcome(result, HotReloadMethodOutcomeKind.Patched, methodName);
            Assert.That(probe(), Is.EqualTo(editedValue), "The patched body must run.");

            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            Assert.That(probe(), Is.EqualTo(compiledValue), "RevertAll must restore the compiled body.");
        }

        private static async Task<HotReloadOrchestratorResult> RunEditedAsync(
            string editedFileName,
            params (string Anchor, string Replacement)[] edits)
        {
            string fixturePath = FixturePath();
            string edited = File.ReadAllText(fixturePath);
            foreach ((string anchor, string replacement) in edits)
            {
                Assert.That(
                    edited.IndexOf(anchor, StringComparison.Ordinal),
                    Is.EqualTo(edited.LastIndexOf(anchor, StringComparison.Ordinal)).And.GreaterThanOrEqualTo(0),
                    "Precondition: the fixture must hold the anchor exactly once: " + anchor);
                edited = edited.Replace(anchor, replacement, StringComparison.Ordinal);
            }

            return await HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                new[] { fixturePath },
                contentPathOverride: null,
                CancellationToken.None,
                new Dictionary<string, string>
                {
                    [fixturePath] = HotReloadTestSourceWriter.WriteEditedSource(editedFileName, edited)
                });
        }

        private static string FixturePath()
        {
            string path = Path.GetFullPath(
                Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", FixtureFileName));
            Assert.That(File.Exists(path), Is.True, "Fixture missing: " + path);
            return path;
        }

        private static void AssertNoFailed(HotReloadOrchestratorResult result)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                Assert.That(
                    outcome.Kind,
                    Is.Not.EqualTo(HotReloadMethodOutcomeKind.Failed),
                    "Unexpected Failed row.\n" + FormatOutcomes(result));
            }
        }

        private static void AssertOutcome(
            HotReloadOrchestratorResult result,
            HotReloadMethodOutcomeKind kind,
            string methodName)
        {
            string marker = "." + methodName + "(";
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == kind && outcome.Method != null && outcome.Method.Contains(marker))
                {
                    return;
                }
            }

            Assert.Fail("Expected " + kind + " for " + methodName + ".\n" + FormatOutcomes(result));
        }

        // A Skipped row for the method whose reason is the struct-member refusal, so a skip for any
        // other reason does not pass.
        private static void AssertSkippedForStructMember(HotReloadOrchestratorResult result, string methodName)
        {
            string marker = "." + methodName + "(";
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Skipped
                    && outcome.Method != null
                    && outcome.Method.Contains(marker)
                    && outcome.Reason != null
                    && outcome.Reason.Contains(StructMemberNoShapeReason))
                {
                    return;
                }
            }

            Assert.Fail("Expected Skipped with the struct-member reason for " + methodName + ".\n" + FormatOutcomes(result));
        }

        private static string FormatOutcomes(HotReloadOrchestratorResult result)
        {
            StringBuilder builder = new StringBuilder();
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                builder.Append(outcome.Kind).Append(' ').Append(outcome.Method)
                    .Append(" :: ").Append(outcome.Reason).Append('\n');
            }

            return builder.ToString();
        }
    }
}
