using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// End-to-end coverage for edited bodies that call extension methods, accessible ones and ones
    /// of an internal class alike: they are patched and run the edited body, which calls the
    /// overload the edited source calls.
    /// </summary>
    public class HotReloadExtensionCallE2ETests
    {
        private const string FixtureFileName = "HotReloadExtensionCallFixtures.cs";

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
        }

        /// <summary>
        /// What: a lambda that calls an internal extension method of an internal class in reduced
        /// form is Patched and returns the edited value; RevertAll restores the compiled value.
        /// </summary>
        [Test]
        public async Task Run_ClosureCallingInternalExtension_IsPatchedAndReverts()
        {
            HotReloadExtensionCallHost host = new HotReloadExtensionCallHost();
            Assert.That(host.ClosureReadsPrivateFieldAndCallsInternalExtension(), Is.EqualTo(4107));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync("4101", "4102");

            AssertKind(result, "ClosureReadsPrivateFieldAndCallsInternalExtension", HotReloadMethodOutcomeKind.Patched);
            Assert.That(host.ClosureReadsPrivateFieldAndCallsInternalExtension(), Is.EqualTo(4108));
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            Assert.That(host.ClosureReadsPrivateFieldAndCallsInternalExtension(), Is.EqualTo(4107));
        }

        /// <summary>
        /// What: a lambda that calls a public extension method of an internal class declared in the
        /// same file, which binds ambiguously against both copies of the class, is Patched and
        /// returns the edited value.
        /// </summary>
        [Test]
        public async Task Run_ClosureCallingPublicExtensionOfInternalClassInEditedFile_IsPatched()
        {
            HotReloadExtensionCallHost host = new HotReloadExtensionCallHost();
            Assert.That(host.ClosureCallsPublicExtensionOfInternalClass(), Is.EqualTo(5110));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync("5101", "5102");

            AssertKind(result, "ClosureCallsPublicExtensionOfInternalClass", HotReloadMethodOutcomeKind.Patched);
            Assert.That(host.ClosureCallsPublicExtensionOfInternalClass(), Is.EqualTo(5111));
        }

        /// <summary>
        /// What: a lambda that calls an overloaded extension method of an internal class declared in
        /// the same file is Patched, runs no shim that fails to compile, and returns the value of
        /// the overload the edited source calls.
        /// </summary>
        [Test]
        public async Task Run_ClosureCallingOverloadedExtensionOfInternalClassInEditedFile_IsPatched()
        {
            HotReloadExtensionCallHost host = new HotReloadExtensionCallHost();
            Assert.That(host.ClosureCallsOverloadedExtensionOfInternalClass(), Is.EqualTo(5410));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync("5401", "5402");

            AssertNoFailedRow(result);
            AssertKind(result, "ClosureCallsOverloadedExtensionOfInternalClass", HotReloadMethodOutcomeKind.Patched);
            Assert.That(host.ClosureCallsOverloadedExtensionOfInternalClass(), Is.EqualTo(5411));
        }

        /// <summary>
        /// What: a lambda that calls an extension method of an internal class in the same file, where
        /// an int and a long overload both apply, is Patched and runs the int overload the edited
        /// source calls, not the long one, which would return 1000 more.
        /// </summary>
        [Test]
        public async Task Run_ClosureCallingBetterOverloadOfExtensionOfInternalClass_RunsTheEditedOverload()
        {
            HotReloadExtensionCallHost host = new HotReloadExtensionCallHost();
            Assert.That(host.ClosureCallsBetterOverloadOfExtensionOfInternalClass(), Is.EqualTo(5504));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync("5501", "5502");

            AssertKind(result, "ClosureCallsBetterOverloadOfExtensionOfInternalClass", HotReloadMethodOutcomeKind.Patched);
            Assert.That(host.ClosureCallsBetterOverloadOfExtensionOfInternalClass(), Is.EqualTo(5505));
        }

        /// <summary>
        /// What: when one edit makes the internal extension class public and changes a lambda that
        /// calls it, the lambda is Patched and calls the compiled class, which is still internal
        /// while the Editor runs.
        /// </summary>
        [Test]
        public async Task Run_ClosureCallingExtensionOfClassMadePublicInSameEdit_IsPatched()
        {
            HotReloadExtensionCallHost host = new HotReloadExtensionCallHost();
            Assert.That(host.ClosureCallsPublicExtensionOfInternalClass(), Is.EqualTo(5110));

            HotReloadOrchestratorResult result = await RunEditAsync(onDisk => onDisk
                .Replace(
                    "    internal static class HotReloadInternalClassExtensionCallMath",
                    "    public static class HotReloadInternalClassExtensionCallMath")
                .Replace("5101", "5102"));

            AssertKind(result, "ClosureCallsPublicExtensionOfInternalClass", HotReloadMethodOutcomeKind.Patched);
            Assert.That(host.ClosureCallsPublicExtensionOfInternalClass(), Is.EqualTo(5111));
        }

        /// <summary>
        /// What: when one edit makes an internal class public and changes a lambda that calls an
        /// internal extension method of it, the lambda is Patched and calls the compiled class, which
        /// is still internal while the Editor runs.
        /// </summary>
        [Test]
        public async Task Run_ClosureCallingInternalExtensionOfClassMadePublicInSameEdit_IsPatched()
        {
            HotReloadExtensionCallHost host = new HotReloadExtensionCallHost();
            Assert.That(host.ClosureReadsPrivateFieldAndCallsInternalExtension(), Is.EqualTo(4107));

            HotReloadOrchestratorResult result = await RunEditAsync(onDisk => onDisk
                .Replace(
                    "    internal static class HotReloadInternalExtensionCallMath",
                    "    public static class HotReloadInternalExtensionCallMath")
                .Replace("4101", "4102"));

            AssertNoFailedRow(result);
            AssertKind(result, "ClosureReadsPrivateFieldAndCallsInternalExtension", HotReloadMethodOutcomeKind.Patched);
            Assert.That(host.ClosureReadsPrivateFieldAndCallsInternalExtension(), Is.EqualTo(4108));
        }

        /// <summary>
        /// What: a method added in this reload that calls an internal extension method of an
        /// internal class in reduced form runs through the accessor: the compiled method edited to
        /// call it returns what the added method computes.
        /// </summary>
        [Test]
        public async Task Run_AddedMethodCallingInternalExtension_RunsThroughTheAccessor()
        {
            HotReloadExtensionCallHost host = new HotReloadExtensionCallHost();
            Assert.That(host.OwnStatementCallsInternalExtension(), Is.EqualTo(4407));

            HotReloadOrchestratorResult result = await RunEditAsync(onDisk => onDisk.Replace(
                "            return _value.Twice(4401);\n        }",
                "            return AddedCallsInternalExtension();\n        }\n\n"
                + "        public int AddedCallsInternalExtension()\n        {\n"
                + "            return _value.Twice(4801);\n        }"));

            AssertNoFailedRow(result);
            AssertKind(result, "OwnStatementCallsInternalExtension", HotReloadMethodOutcomeKind.Patched);
            Assert.That(host.OwnStatementCallsInternalExtension(), Is.EqualTo(4807));
        }

        /// <summary>
        /// What: a lambda that reads a private field and calls an overloaded public extension
        /// method of a public class in the same file is Patched and returns the edited value;
        /// RevertAll restores the compiled value.
        /// </summary>
        [Test]
        public async Task Run_ClosureCallingOverloadedPublicExtensionOfPublicClass_IsPatchedAndReverts()
        {
            HotReloadExtensionCallHost host = new HotReloadExtensionCallHost();
            Assert.That(host.ClosureReadsPrivateFieldAndCallsOverloadedPublicExtension(), Is.EqualTo(5613));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync("5601", "5602");

            AssertKind(result, "ClosureReadsPrivateFieldAndCallsOverloadedPublicExtension", HotReloadMethodOutcomeKind.Patched);
            Assert.That(host.ClosureReadsPrivateFieldAndCallsOverloadedPublicExtension(), Is.EqualTo(5614));
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            Assert.That(host.ClosureReadsPrivateFieldAndCallsOverloadedPublicExtension(), Is.EqualTo(5613));
        }

        /// <summary>
        /// What: a method that calls an internal extension method from its own statements is
        /// Patched and returns the edited value; RevertAll restores the compiled value.
        /// </summary>
        [Test]
        public async Task Run_OwnStatementCallingInternalExtension_IsPatchedAndReverts()
        {
            HotReloadExtensionCallHost host = new HotReloadExtensionCallHost();
            Assert.That(host.OwnStatementCallsInternalExtension(), Is.EqualTo(4407));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync("4401", "4402");

            AssertKind(result, "OwnStatementCallsInternalExtension", HotReloadMethodOutcomeKind.Patched);
            Assert.That(host.OwnStatementCallsInternalExtension(), Is.EqualTo(4408));
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            Assert.That(host.OwnStatementCallsInternalExtension(), Is.EqualTo(4407));
        }

        /// <summary>
        /// What: a lambda that reads a private field and calls a public extension method is
        /// Patched and returns the edited value; RevertAll restores the compiled value.
        /// </summary>
        [Test]
        public async Task Run_ClosureCallingPublicExtension_IsPatchedAndReverts()
        {
            HotReloadExtensionCallHost host = new HotReloadExtensionCallHost();
            Assert.That(host.ClosureReadsPrivateFieldAndCallsPublicExtension(), Is.EqualTo(4607));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync("4601", "4602");

            AssertKind(result, "ClosureReadsPrivateFieldAndCallsPublicExtension", HotReloadMethodOutcomeKind.Patched);
            Assert.That(host.ClosureReadsPrivateFieldAndCallsPublicExtension(), Is.EqualTo(4608));
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            Assert.That(host.ClosureReadsPrivateFieldAndCallsPublicExtension(), Is.EqualTo(4607));
        }

        private static Task<HotReloadOrchestratorResult> RunEditedFixtureAsync(
            string originalToken,
            string editedToken)
        {
            return RunEditAsync(onDisk => onDisk.Replace(originalToken, editedToken));
        }

        private static async Task<HotReloadOrchestratorResult> RunEditAsync(Func<string, string> edit)
        {
            string fixturePath = Path.GetFullPath(
                Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", FixtureFileName));
            string onDisk = File.ReadAllText(fixturePath);
            string edited = edit(onDisk);
            Assert.That(edited, Is.Not.EqualTo(onDisk), "Precondition: the edit must change the fixture.");
            string editedPath = HotReloadTestSourceWriter.WriteEditedSource("ExtensionCallE2E.cs", edited);
            return await HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);
        }

        private static void AssertKind(
            HotReloadOrchestratorResult result,
            string methodName,
            HotReloadMethodOutcomeKind expectedKind)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Method.Contains("." + methodName + "("))
                {
                    Assert.That(outcome.Kind, Is.EqualTo(expectedKind), outcome.Method + ": " + outcome.Reason);
                    return;
                }
            }

            Assert.Fail("No row for " + methodName + ".");
        }

        private static void AssertNoFailedRow(HotReloadOrchestratorResult result)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                Assert.That(
                    outcome.Kind,
                    Is.Not.EqualTo(HotReloadMethodOutcomeKind.Failed),
                    outcome.Method + ": " + outcome.Reason);
            }
        }
    }
}
