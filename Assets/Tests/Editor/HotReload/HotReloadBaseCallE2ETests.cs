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
    /// End-to-end coverage for hot reloading methods whose own statements call a base method:
    /// the edit is Patched and the patched body runs the base implementation, not the override.
    /// </summary>
    public class HotReloadBaseCallE2ETests
    {
        private const string FixtureFileName = "HotReloadBaseCallFixtures.cs";

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
        /// What: an override that calls its base method two types up is Patched, runs the base
        /// body without recursing into itself, and RevertAll restores the compiled body.
        /// </summary>
        [Test]
        public async Task Run_OverrideCallingBaseMethod_ApplyAndRevert_CallsTheBaseImplementation()
        {
            HotReloadBaseCallDerivedFixture fixture = new HotReloadBaseCallDerivedFixture();
            Assert.That(fixture.Compute(1), Is.EqualTo(1 + 100 + 6101));

            await ApplyEditAsync("BaseCallE2ECompute.cs", "6101", "7101", "HotReloadBaseCallDerivedFixture.Compute(");

            Assert.That(fixture.Compute(1), Is.EqualTo(1 + 100 + 7101));

            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            Assert.That(fixture.Compute(1), Is.EqualTo(1 + 100 + 6101), "RevertAll must restore the compiled body.");
        }

        /// <summary>What: a body calling a protected base method is Patched and runs the base body.</summary>
        [Test]
        public async Task Run_BaseCallToProtectedMethod_Applies()
        {
            HotReloadBaseCallDerivedFixture fixture = new HotReloadBaseCallDerivedFixture();
            Assert.That(fixture.CallProtected(1), Is.EqualTo(1 + 200 + 6102));

            await ApplyEditAsync("BaseCallE2EProtected.cs", "6102", "7102", "CallProtected");

            Assert.That(fixture.CallProtected(1), Is.EqualTo(1 + 200 + 7102));
        }

        /// <summary>What: a base call passing out and ref arguments is Patched and writes both back.</summary>
        [Test]
        public async Task Run_BaseCallWithOutAndRefArguments_Applies()
        {
            HotReloadBaseCallDerivedFixture fixture = new HotReloadBaseCallDerivedFixture();
            Assert.That(fixture.CallFill(1), Is.EqualTo((1 + 300) + 1 + 6103));

            await ApplyEditAsync("BaseCallE2EFill.cs", "6103", "7103", "CallFill");

            Assert.That(fixture.CallFill(1), Is.EqualTo((1 + 300) + 1 + 7103));
        }

        /// <summary>What: a base call to a non-virtual method is Patched and runs that method.</summary>
        [Test]
        public async Task Run_BaseCallToNonVirtualMethod_Applies()
        {
            HotReloadBaseCallDerivedFixture fixture = new HotReloadBaseCallDerivedFixture();
            Assert.That(fixture.CallNonVirtual(1), Is.EqualTo(1 + 500 + 6104));

            await ApplyEditAsync("BaseCallE2ENonVirtual.cs", "6104", "7104", "CallNonVirtual");

            Assert.That(fixture.CallNonVirtual(1), Is.EqualTo(1 + 500 + 7104));
        }

        /// <summary>What: a base call nested in another base call's argument is Patched and runs both.</summary>
        [Test]
        public async Task Run_NestedBaseCalls_Apply()
        {
            HotReloadBaseCallDerivedFixture fixture = new HotReloadBaseCallDerivedFixture();
            Assert.That(fixture.CallNested(1), Is.EqualTo(1 + 100 + 100 + 6105));

            await ApplyEditAsync("BaseCallE2ENested.cs", "6105", "7105", "CallNested");

            Assert.That(fixture.CallNested(1), Is.EqualTo(1 + 100 + 100 + 7105));
        }

        /// <summary>
        /// What: a base call to a method declared in another assembly (System.Object.ToString) is
        /// Patched and runs that method.
        /// </summary>
        [Test]
        public async Task Run_BaseCallToOtherAssemblyMethod_Applies()
        {
            HotReloadBaseCallDerivedFixture fixture = new HotReloadBaseCallDerivedFixture();
            string typeName = typeof(HotReloadBaseCallDerivedFixture).FullName;
            Assert.That(fixture.ToString(), Is.EqualTo(typeName + "6106"));

            await ApplyEditAsync("BaseCallE2EToString.cs", "\"6106\"", "\"7106\"", "HotReloadBaseCallDerivedFixture.ToString(");

            Assert.That(fixture.ToString(), Is.EqualTo(typeName + "7106"));
        }

        /// <summary>What: a base call through a constructed generic base type is Patched and runs the base body.</summary>
        [Test]
        public async Task Run_BaseCallOnGenericBaseType_Applies()
        {
            HotReloadBaseCallGenericDerivedFixture fixture = new HotReloadBaseCallGenericDerivedFixture();
            Assert.That(fixture.Pass(5), Is.EqualTo(5 + 6131));

            await ApplyEditAsync("BaseCallE2EGeneric.cs", "6131", "7131", "HotReloadBaseCallGenericDerivedFixture.Pass(");

            Assert.That(fixture.Pass(5), Is.EqualTo(5 + 7131));
        }

        /// <summary>
        /// What: a base call next to a lambda that reads a private field is Patched, and both the
        /// base call and the lambda run.
        /// </summary>
        [Test]
        public async Task Run_BaseCallBesidePrivateLambda_Applies()
        {
            HotReloadBaseCallDerivedFixture fixture = new HotReloadBaseCallDerivedFixture();
            Assert.That(fixture.CallBesidePrivateLambda(1), Is.EqualTo(1 + 100 + 3 + 6107));

            await ApplyEditAsync("BaseCallE2EBesideLambda.cs", "6107", "7107", "CallBesidePrivateLambda");

            Assert.That(fixture.CallBesidePrivateLambda(1), Is.EqualTo(1 + 100 + 3 + 7107));
        }

        /// <summary>
        /// What: a base call to a method whose parameter name is a C# keyword is Patched, runs the base
        /// body, and RevertAll restores the compiled body.
        /// </summary>
        [Test]
        public async Task Run_BaseCallToKeywordNamedParameter_ApplyAndRevert_CallsTheBaseImplementation()
        {
            HotReloadBaseCallDerivedFixture fixture = new HotReloadBaseCallDerivedFixture();
            Assert.That(fixture.CallKeywordParameter(1), Is.EqualTo(1 + 600 + 6125));

            await ApplyEditAsync("BaseCallE2EKeyword.cs", "6125", "7125", "CallKeywordParameter");

            Assert.That(fixture.CallKeywordParameter(1), Is.EqualTo(1 + 600 + 7125));

            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            Assert.That(fixture.CallKeywordParameter(1), Is.EqualTo(1 + 600 + 6125), "RevertAll must restore the compiled body.");
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
            Assert.That(File.Exists(path), Is.True, "Base-call fixture source missing: " + path);
            return Path.GetFullPath(path);
        }
    }
}
