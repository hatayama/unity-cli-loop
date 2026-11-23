using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// End-to-end coverage for a method's own statements calling a method the same reload adds to
    /// an existing generic type, through a constructed instance of that type: the reload skips the
    /// added method, so the caller is skipped with the skipped-callee reason and the rest of the
    /// file still applies, whether or not the added method's parameters name a type parameter.
    /// </summary>
    public class HotReloadAddedGenericTypeMemberCallE2ETests
    {
        private const string FixtureFileName = "HotReloadAddedMemberHost.cs";

        private const string GenericHostAnchor =
            "        public int ExistingGenericValue()\n        {\n            return 1;\n        }\n";

        private const string CallerBody = "        public int ExistingCaller(int value)\n        {\n            return value;\n        }";

        private const string OtherMethodBody = "        public int ExistingValue()\n        {\n            return 1;\n        }";

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
        /// What: an existing method whose own statements call a method added to an existing generic
        /// type on a constructed instance ('new Host&lt;int&gt;().Added(...)') is Skipped with the
        /// skipped-callee reason and keeps its compiled value, no row is Failed, and another edited
        /// method of the file is Patched; with a parameter of the type parameter's type as with none.
        /// </summary>
        [TestCase("public int AddedGenericHostEcho(T value, int add) { return add + 1000; }", "AddedGenericHostEcho(T, int)")]
        [TestCase("public int AddedGenericHostEcho(int value, int add) { return add + 1000; }", "AddedGenericHostEcho(int, int)")]
        public async Task Run_OwnStatementCallToMethodAddedToAGenericType_IsSkippedAndTheFileApplies(
            string addedMethod,
            string calleeDisplay)
        {
            HotReloadAddedMemberHost host = new HotReloadAddedMemberHost();
            Assert.That(host.ExistingCaller(5), Is.EqualTo(5));
            Assert.That(host.ExistingValue(), Is.EqualTo(1));

            HotReloadOrchestratorResult result = await RunEditedFixtureAsync(
                GenericHostAnchor,
                GenericHostAnchor + "\n        " + addedMethod + "\n",
                CallerBody,
                "        public int ExistingCaller(int value)\n        {\n"
                + "            return new HotReloadAddedPropertyGenericHost<int>().AddedGenericHostEcho(value, 3);\n        }",
                OtherMethodBody,
                "        public int ExistingValue()\n        {\n            return 2;\n        }");

            AssertNoFailedRow(result);
            HotReloadMethodOutcome caller = FindOutcome(result, "ExistingCaller");
            Assert.That(caller.Kind, Is.EqualTo(HotReloadMethodOutcomeKind.Skipped));
            Assert.That(caller.Reason, Does.Contain(calleeDisplay + "', which this reload skipped"));
            Assert.That(FindOutcome(result, "ExistingValue").Kind, Is.EqualTo(HotReloadMethodOutcomeKind.Patched));
            Assert.That(host.ExistingCaller(5), Is.EqualTo(5));
            Assert.That(host.ExistingValue(), Is.EqualTo(2));
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            Assert.That(host.ExistingValue(), Is.EqualTo(1));
        }

        // Why pairs of strings: the replacements run in order, and each must find its text in the
        // fixture, so a typo fails the precondition instead of editing nothing.
        private static async Task<HotReloadOrchestratorResult> RunEditedFixtureAsync(params string[] replacements)
        {
            string fixturePath = Path.GetFullPath(
                Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", FixtureFileName));
            string edited = File.ReadAllText(fixturePath).Replace("\r\n", "\n");
            for (int index = 0; index < replacements.Length; index += 2)
            {
                Assert.That(edited.Contains(replacements[index]), "Precondition: the fixture must contain " + replacements[index]);
                edited = edited.Replace(replacements[index], replacements[index + 1]);
            }

            string editedPath = HotReloadTestSourceWriter.WriteEditedSource(
                "AddedGenericTypeMemberCallE2E" + System.Math.Abs(edited.GetHashCode()) + ".cs",
                edited);
            return await HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);
        }

        private static HotReloadMethodOutcome FindOutcome(HotReloadOrchestratorResult result, string hostMethod)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Method.Contains("HotReloadAddedMemberHost." + hostMethod + "("))
                {
                    return outcome;
                }
            }

            Assert.Fail("No outcome row for " + hostMethod + ".");
            return null;
        }

        private static void AssertNoFailedRow(HotReloadOrchestratorResult result)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                Assert.That(
                    outcome.Kind,
                    Is.Not.EqualTo(HotReloadMethodOutcomeKind.Failed),
                    outcome.Method + " :: " + outcome.Reason);
            }
        }
    }
}
