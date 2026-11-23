using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.@event;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// End-to-end coverage for hot reload of types declared in a namespace with a keyword-named
    /// segment (@event): a method body, a property getter, a call to an added method and a nested
    /// type's method are all patched, and RevertAll restores the compiled values.
    /// </summary>
    public class HotReloadKeywordNamespaceE2ETests
    {
        private const string FixtureFileName = "HotReloadKeywordNamespaceFixtures.cs";

        private const string AddedMethodAnchor = "        public int CallsAddedMethod()";

        private const string AddedMethodSource =
            "        private int AddedKeywordNamespaceValue()\n"
            + "        {\n"
            + "            return 35998;\n"
            + "        }\n\n";

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
        /// What: an edited method body of a type in a keyword-named namespace is Patched and returns
        /// the edited value; RevertAll restores the compiled value.
        /// </summary>
        [Test]
        public async Task Run_MethodOfTypeInKeywordNamedNamespace_IsPatchedAndReverts()
        {
            HotReloadKeywordNamespaceHost host = new HotReloadKeywordNamespaceHost();
            Assert.That(host.MethodValue(), Is.EqualTo(35990));

            HotReloadOrchestratorResult result = await RunEditAsync(
                onDisk => onDisk.Replace("return 35990;", "return 35991;"));

            AssertKind(result, "MethodValue", HotReloadMethodOutcomeKind.Patched);
            Assert.That(host.MethodValue(), Is.EqualTo(35991));
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            Assert.That(host.MethodValue(), Is.EqualTo(35990));
        }

        /// <summary>
        /// What: an edited property getter of a type in a keyword-named namespace is Patched and
        /// returns the edited value, so the getter shim declared in that namespace compiles.
        /// </summary>
        [Test]
        public async Task Run_PropertyGetterOfTypeInKeywordNamedNamespace_IsPatched()
        {
            HotReloadKeywordNamespaceHost host = new HotReloadKeywordNamespaceHost();
            Assert.That(host.GetterValue, Is.EqualTo(35993));

            HotReloadOrchestratorResult result = await RunEditAsync(
                onDisk => onDisk.Replace("return 35993;", "return 35994;"));

            AssertKind(result, "get_GetterValue", HotReloadMethodOutcomeKind.Patched);
            Assert.That(host.GetterValue, Is.EqualTo(35994));
        }

        /// <summary>
        /// What: a method of a type in a keyword-named namespace that now calls a method this reload
        /// adds is Patched and returns what the added method computes, so the call the rewrite
        /// qualifies with the shim's namespace compiles.
        /// </summary>
        [Test]
        public async Task Run_CallToAddedMethodOfTypeInKeywordNamedNamespace_IsPatched()
        {
            HotReloadKeywordNamespaceHost host = new HotReloadKeywordNamespaceHost();
            Assert.That(host.CallsAddedMethod(), Is.EqualTo(35996));

            HotReloadOrchestratorResult result = await RunEditAsync(
                onDisk => onDisk
                    .Replace(AddedMethodAnchor, AddedMethodSource + AddedMethodAnchor)
                    .Replace("return 35996;", "return AddedKeywordNamespaceValue();"));

            AssertKind(result, "CallsAddedMethod", HotReloadMethodOutcomeKind.Patched);
            Assert.That(host.CallsAddedMethod(), Is.EqualTo(35998));
        }

        /// <summary>
        /// What: an edited method body of a type nested in a type of a keyword-named namespace is
        /// Patched and returns the edited value; RevertAll restores the compiled value.
        /// </summary>
        [Test]
        public async Task Run_MethodOfNestedTypeInKeywordNamedNamespace_IsPatchedAndReverts()
        {
            HotReloadKeywordNamespaceHost.Nested nested = new HotReloadKeywordNamespaceHost.Nested();
            Assert.That(nested.NestedValue(), Is.EqualTo(35999));

            HotReloadOrchestratorResult result = await RunEditAsync(
                onDisk => onDisk.Replace("return 35999;", "return 36000;"));

            AssertKind(result, "NestedValue", HotReloadMethodOutcomeKind.Patched);
            Assert.That(nested.NestedValue(), Is.EqualTo(36000));
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            Assert.That(nested.NestedValue(), Is.EqualTo(35999));
        }

        /// <summary>
        /// What: a changed const of a type in a keyword-named namespace produces the const drift
        /// warning naming that const, so the compiled type is found by its metadata name.
        /// </summary>
        [Test]
        public async Task Run_ConstChangedInTypeOfKeywordNamedNamespace_WarnsAboutTheDrift()
        {
            HotReloadOrchestratorResult result = await RunEditAsync(
                onDisk => onDisk.Replace("KeywordTuning = 4;", "KeywordTuning = 6;"));

            Assert.That(
                FindWarningContaining(result, "KeywordTuning"),
                Does.Contain("is 6 in the edited source but 4 in the compiled assembly"),
                string.Join("\n", result.Warnings));
        }

        /// <summary>
        /// What: removing a compiled property of a type in a keyword-named namespace produces the
        /// kind-change warning naming that property, and no outside-method-body drift warning,
        /// because the removed declaration is matched to the kind-change key and left out of the
        /// drift comparison.
        /// </summary>
        [Test]
        public async Task Run_PropertyRemovedFromTypeOfKeywordNamedNamespace_WarnsAboutTheKindChangeOnly()
        {
            HotReloadOrchestratorResult result = await RunEditAsync(
                onDisk => onDisk.Replace("        public int KindChangeValue => 36002;\n", string.Empty));

            Assert.That(
                FindWarningContaining(result, "KindChangeValue"),
                Does.StartWith("Compiled property '"),
                string.Join("\n", result.Warnings));
            AssertNoOutsideMethodBodyDriftWarning(result);
        }

        /// <summary>
        /// What: removing a compiled property of a keyword-named type (@class) in a keyword-named
        /// namespace produces the kind-change warning naming that property and no
        /// outside-method-body drift warning.
        /// </summary>
        [Test]
        public async Task Run_PropertyRemovedFromKeywordNamedType_WarnsAboutTheKindChangeOnly()
        {
            HotReloadOrchestratorResult result = await RunEditAsync(
                onDisk => onDisk.Replace("        public int ClassKindChangeValue => 36005;\n", string.Empty));

            Assert.That(
                FindWarningContaining(result, "ClassKindChangeValue"),
                Does.StartWith("Compiled property '"),
                string.Join("\n", result.Warnings));
            AssertNoOutsideMethodBodyDriftWarning(result);
        }

        private static void AssertNoOutsideMethodBodyDriftWarning(HotReloadOrchestratorResult result)
        {
            foreach (string warning in result.Warnings)
            {
                Assert.That(
                    warning,
                    Does.Not.StartWith("Edits outside method bodies"),
                    string.Join("\n", result.Warnings));
            }
        }

        private static string FindWarningContaining(HotReloadOrchestratorResult result, string fragment)
        {
            foreach (string warning in result.Warnings)
            {
                if (warning.Contains(fragment))
                {
                    return warning;
                }
            }

            Assert.Fail("No warning names " + fragment + ".\n" + string.Join("\n", result.Warnings));
            return null;
        }

        private static async Task<HotReloadOrchestratorResult> RunEditAsync(Func<string, string> edit)
        {
            string fixturePath = Path.GetFullPath(
                Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", FixtureFileName));
            string onDisk = File.ReadAllText(fixturePath);
            string edited = edit(onDisk);
            Assert.That(edited, Is.Not.EqualTo(onDisk), "Precondition: the edit must change the fixture.");
            string editedPath = HotReloadTestSourceWriter.WriteEditedSource("KeywordNamespaceE2E.cs", edited);
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

            Assert.Fail("No row for " + methodName + ". Rows: " + DescribeRows(result));
        }

        private static string DescribeRows(HotReloadOrchestratorResult result)
        {
            string rows = string.Empty;
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                rows += outcome.Method + " = " + outcome.Kind + " (" + outcome.Reason + "); ";
            }

            return rows;
        }
    }
}
