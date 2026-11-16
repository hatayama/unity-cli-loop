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
    /// End-to-end coverage for hot reloading edited explicit interface method implementations: a call
    /// through the interface runs the edited body, only the edited implementation changes, and
    /// RevertAll restores the compiled body.
    /// </summary>
    public class HotReloadExplicitInterfaceE2ETests
    {
        private const string FixtureFileName = "HotReloadExplicitInterfaceFixtures.cs";

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
        /// What: an edited explicit implementation of a namespaced interface is Patched, a call through
        /// the interface runs the edited body, and RevertAll restores the compiled body.
        /// </summary>
        [Test]
        public async Task Run_EditedExplicitImplementation_ApplyAndRevert_RunsTheEditedBody()
        {
            IHotReloadExplicitRunner runner = new HotReloadExplicitRunnerFixture();
            Assert.That(runner.Run(), Is.EqualTo(8101));

            await ApplyEditAsync("ExplicitE2ERunner.cs", "8101", "9101", "IHotReloadExplicitRunner.Run(");

            Assert.That(runner.Run(), Is.EqualTo(9101));
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            Assert.That(runner.Run(), Is.EqualTo(8101), "RevertAll must restore the compiled body.");
        }

        /// <summary>
        /// What: an edited explicit implementation of a closed generic interface is Patched and a call
        /// through the interface runs the edited body until RevertAll.
        /// </summary>
        [Test]
        public async Task Run_EditedExplicitImplementationOfGenericInterface_ApplyAndRevert_RunsTheEditedBody()
        {
            IHotReloadExplicitBox<int> box = new HotReloadExplicitBoxFixture();
            Assert.That(box.Take(1), Is.EqualTo(1 + 8102));

            await ApplyEditAsync("ExplicitE2EBox.cs", "8102", "9102", "IHotReloadExplicitBox<System.Int32>.Take(");

            Assert.That(box.Take(1), Is.EqualTo(1 + 9102));
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            Assert.That(box.Take(1), Is.EqualTo(1 + 8102), "RevertAll must restore the compiled body.");
        }

        /// <summary>
        /// What: an edited explicit implementation of an interface nested in a type is Patched and a
        /// call through the interface runs the edited body until RevertAll.
        /// </summary>
        [Test]
        public async Task Run_EditedExplicitImplementationOfNestedInterface_ApplyAndRevert_RunsTheEditedBody()
        {
            HotReloadExplicitOuter.IInner inner = new HotReloadExplicitNestedFixture();
            Assert.That(inner.Run(), Is.EqualTo(8103));

            await ApplyEditAsync("ExplicitE2ENested.cs", "8103", "9103", "HotReloadExplicitOuter.IInner.Run(");

            Assert.That(inner.Run(), Is.EqualTo(9103));
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            Assert.That(inner.Run(), Is.EqualTo(8103), "RevertAll must restore the compiled body.");
        }

        /// <summary>
        /// What: with IA.Run, IB.Run and an ordinary Run on one class, editing one of them patches only
        /// that one; the other two keep their compiled values, and RevertAll restores all three.
        /// </summary>
        [TestCase("8104", "9104", "IHotReloadExplicitA.Run(")]
        [TestCase("8105", "9105", "IHotReloadExplicitB.Run(")]
        [TestCase("8106", "9106", "HotReloadExplicitPairFixture.Run(")]
        public async Task Run_OneOfThreeSameNamedImplementationsEdited_PatchesOnlyThatOne(
            string token,
            string replacement,
            string patchedMethodFragment)
        {
            HotReloadExplicitPairFixture fixture = new HotReloadExplicitPairFixture();
            Dictionary<string, Func<int>> calls = new Dictionary<string, Func<int>>
            {
                { "8104", () => ((IHotReloadExplicitA)fixture).Run() },
                { "8105", () => ((IHotReloadExplicitB)fixture).Run() },
                { "8106", () => fixture.Run() }
            };

            await ApplyEditAsync("ExplicitE2EPair" + token + ".cs", token, replacement, patchedMethodFragment);

            foreach (KeyValuePair<string, Func<int>> call in calls)
            {
                int expected = call.Key == token ? int.Parse(replacement) : int.Parse(call.Key);
                Assert.That(call.Value(), Is.EqualTo(expected), "Implementation with compiled value " + call.Key);
            }

            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            foreach (KeyValuePair<string, Func<int>> call in calls)
            {
                Assert.That(call.Value(), Is.EqualTo(int.Parse(call.Key)), "RevertAll must restore " + call.Key);
            }
        }

        /// <summary>
        /// What: an async explicit implementation whose lambda reads a private field is Patched through
        /// accessors and returns the edited value until RevertAll.
        /// </summary>
        [Test]
        public async Task Run_EditedAsyncExplicitImplementationReadingPrivateField_ApplyAndRevert_RunsTheEditedBody()
        {
            IHotReloadExplicitAsync fixture = new HotReloadExplicitAsyncFixture();
            Assert.That(fixture.RunAsync().GetAwaiter().GetResult(), Is.EqualTo(5 + 8107));

            await ApplyEditAsync("ExplicitE2EAsync.cs", "8107", "9107", "IHotReloadExplicitAsync.RunAsync(");

            Assert.That(fixture.RunAsync().GetAwaiter().GetResult(), Is.EqualTo(5 + 9107));
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            Assert.That(
                fixture.RunAsync().GetAwaiter().GetResult(),
                Is.EqualTo(5 + 8107),
                "RevertAll must restore the compiled body.");
        }

        /// <summary>
        /// What: a synchronous explicit implementation whose lambda reads a private field is Patched
        /// through closure accessors and returns the edited value until RevertAll.
        /// </summary>
        [Test]
        public async Task Run_EditedExplicitImplementationWithClosureOverPrivateField_ApplyAndRevert_RunsTheEditedBody()
        {
            IHotReloadExplicitClosure fixture = new HotReloadExplicitClosureFixture();
            Assert.That(fixture.Read(), Is.EqualTo(7 + 8109));

            await ApplyEditAsync("ExplicitE2EClosure.cs", "8109", "9109", "IHotReloadExplicitClosure.Read(");

            Assert.That(fixture.Read(), Is.EqualTo(7 + 9109));
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            Assert.That(fixture.Read(), Is.EqualTo(7 + 8109), "RevertAll must restore the compiled body.");
        }

        /// <summary>
        /// What: respelling the interface with global:: while editing the body still patches the
        /// compiled implementation, and RevertAll restores it.
        /// </summary>
        [Test]
        public async Task Run_ExplicitImplementationRespelledWithGlobal_ApplyAndRevert_RunsTheEditedBody()
        {
            IHotReloadExplicitRunner runner = new HotReloadExplicitRunnerFixture();
            Assert.That(runner.Run(), Is.EqualTo(8101));

            await ApplyEditAsync(
                "ExplicitE2EGlobal.cs",
                "int IHotReloadExplicitRunner.Run() { return 8101; }",
                "int global::io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.IHotReloadExplicitRunner.Run() { return 9101; }",
                "IHotReloadExplicitRunner.Run(");

            Assert.That(runner.Run(), Is.EqualTo(9101));
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            Assert.That(runner.Run(), Is.EqualTo(8101), "RevertAll must restore the compiled body.");
        }

        /// <summary>
        /// What: a global:: respelled explicit implementation patched by one reload and Skipped by the
        /// next is named in the warning that its earlier patch is still what runs.
        /// </summary>
        [Test]
        public async Task Run_RespelledExplicitImplementationSkippedAfterPatch_WarnsThatEarlierPatchStaysActive()
        {
            const string original = "int IHotReloadExplicitBaseCaller.Value(int input) { return base.Compute(input) + 8108; }";
            const string respelledSpecifier =
                "int global::io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.IHotReloadExplicitBaseCaller.Value(int input) ";
            IHotReloadExplicitBaseCaller caller = new HotReloadExplicitBaseCallerFixture();

            await ApplyEditAsync(
                "ExplicitE2EEarlierPatch1.cs",
                original,
                respelledSpecifier + "{ return base.Compute(input) + 9108; }",
                "IHotReloadExplicitBaseCaller.Value(");
            Assert.That(caller.Value(1), Is.EqualTo(1 + 100 + 9108));

            string fixturePath = ResolveFixturePath();
            string onDisk = File.ReadAllText(fixturePath);
            string skippedEdit = onDisk.Replace(
                original,
                respelledSpecifier + "{ Func<int> call = () => base.Compute(input); return call() + 9208; }",
                StringComparison.Ordinal);
            Assert.That(skippedEdit, Is.Not.EqualTo(onDisk), "Precondition: the fixture must hold the original body.");
            HotReloadOrchestratorResult second = await HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                new[] { fixturePath },
                HotReloadTestSourceWriter.WriteEditedSource("ExplicitE2EEarlierPatch2.cs", skippedEdit),
                CancellationToken.None);

            string expectedLabel = HotReloadMethodKeys.FormatMethodLabelParts(
                new HotReloadMetadataTypeName(typeof(HotReloadExplicitBaseCallerFixture).FullName),
                "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.IHotReloadExplicitBaseCaller.Value",
                new[] { "System.Int32" },
                0);
            Assert.That(
                second.Warnings,
                Does.Contain(string.Format(HotReloadConstants.SkippedMethodKeepsActivePatchWarningFormat, expectedLabel)),
                "Warnings were:\n" + string.Join("\n", second.Warnings) + "\n" + FormatOutcomes(second));
            Assert.That(caller.Value(1), Is.EqualTo(1 + 100 + 9108), "The earlier patch is still what runs.");
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            Assert.That(caller.Value(1), Is.EqualTo(1 + 100 + 8108));
        }

        private static async Task ApplyEditAsync(
            string editedFileName,
            string token,
            string replacement,
            string patchedMethodFragment)
        {
            string fixturePath = ResolveFixturePath();
            string onDisk = File.ReadAllText(fixturePath);
            int index = onDisk.IndexOf(token, StringComparison.Ordinal);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), "Precondition: the fixture must hold " + token);
            Assert.That(
                onDisk.LastIndexOf(token, StringComparison.Ordinal),
                Is.EqualTo(index),
                "Precondition: the fixture must hold " + token + " exactly once.");
            string editedPath = HotReloadTestSourceWriter.WriteEditedSource(
                editedFileName,
                onDisk.Replace(token, replacement, StringComparison.Ordinal));

            HotReloadOrchestratorResult result = await HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);

            AssertHasPatched(result, patchedMethodFragment);
        }

        private static void AssertHasPatched(HotReloadOrchestratorResult result, string methodFragment)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Patched
                    && outcome.Method.Contains(methodFragment, StringComparison.Ordinal))
                {
                    return;
                }
            }

            Assert.Fail("Expected Patched outcome for " + methodFragment + ".\n" + FormatOutcomes(result));
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

        private static string ResolveFixturePath()
        {
            string path = Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", FixtureFileName);
            Assert.That(File.Exists(path), Is.True, "Explicit-interface fixture source missing: " + path);
            return Path.GetFullPath(path);
        }
    }
}
