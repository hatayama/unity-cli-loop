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
    /// End-to-end EditMode coverage for edited bodies with property patterns: a patched body matches
    /// the value it was given, not the running instance, and a skipped one keeps the compiled body
    /// without failing the file.
    /// </summary>
    public class HotReloadSubpatternNameE2ETests
    {
        private const string FixtureFileName = "HotReloadSubpatternNameFixtures.cs";
        private const string FixtureTypeName = "HotReloadSubpatternNameHost";
        private const string SecretFieldAnchor = "private int _secret;";

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
        /// What: a property pattern the shim can run is patched and the method returns the edited
        /// value, which it reaches only when the pattern reads the value it was given: a private member
        /// in the method's own statements, a public one in a lambda, the extended form of either, and a
        /// private member read with a member access in a lambda.
        /// </summary>
        [TestCase("8011", "8012", "PrivatePatternInOwnStatements", 8012)]
        [TestCase("{ Me: { Visible: 3 } } ? 8021", "{ Me.Visible: 3 } ? 8022", "PublicNestedPatternInOwnStatements", 8022)]
        [TestCase("{ Self: { Secret: 3 } } ? 8031", "{ Self.Secret: 3 } ? 8032", "PrivateNestedPatternInOwnStatements", 8032)]
        [TestCase("8041", "8042", "PublicPatternInLambda", 8042)]
        [TestCase("{ Me: { Visible: 3 } } ? 8051", "{ Me.Visible: 3 } ? 8052", "PublicNestedPatternInLambda", 8052)]
        [TestCase("other is { Secret: 3 } ? 8071", "other.Secret == 3 ? 8072", "PrivatePropertyPatternInLambda", 8072)]
        public async Task Run_PatternTheShimCanRun_PatchesBehavior(
            string fragment,
            string replacement,
            string methodName,
            int editedValue)
        {
            HotReloadOrchestratorResult result = await RunEditsAsync((fragment, replacement));

            Assert.That(FindRow(result, methodName).Kind, Is.EqualTo(HotReloadMethodOutcomeKind.Patched), FormatOutcomes(result));
            Assert.That(await CallAsync(methodName), Is.EqualTo(editedValue), FormatOutcomes(result));
        }

        /// <summary>
        /// What: a body whose property pattern the shim cannot run is skipped and keeps the compiled
        /// behavior, without failing the file: a private member named in a lambda or an async body, and
        /// a field added in the same edit.
        /// </summary>
        [TestCase("8061", "8062", "PrivateFieldPatternInLambda", 8061)]
        [TestCase("8141", "8142", "PrivatePatternInAsync", 8141)]
        [TestCase("{ Secret: 3 } ? 8011", "{ _addedSecret: 0 } ? 8012", "PrivatePatternInOwnStatements", 8011)]
        public async Task Run_PatternTheShimCannotRun_IsSkippedAndKeepsCompiledBehavior(
            string fragment,
            string replacement,
            string methodName,
            int compiledValue)
        {
            HotReloadOrchestratorResult result = await RunEditsAsync(
                (fragment, replacement),
                (SecretFieldAnchor, SecretFieldAnchor + " private int _addedSecret;"));

            Assert.That(FindRow(result, methodName).Kind, Is.EqualTo(HotReloadMethodOutcomeKind.Skipped), FormatOutcomes(result));
            Assert.That(await CallAsync(methodName), Is.EqualTo(compiledValue), FormatOutcomes(result));
        }

        // Why async: the async fixture method is awaited rather than waited on, because an EditMode
        // test must not block the main thread on a task.
        private static async Task<int> CallAsync(string methodName)
        {
            HotReloadSubpatternNameHost host = new HotReloadSubpatternNameHost();
            switch (methodName)
            {
                case "PrivatePatternInOwnStatements":
                    return host.PrivatePatternInOwnStatements();
                case "PublicNestedPatternInOwnStatements":
                    return host.PublicNestedPatternInOwnStatements();
                case "PrivateNestedPatternInOwnStatements":
                    return host.PrivateNestedPatternInOwnStatements();
                case "PublicPatternInLambda":
                    return host.PublicPatternInLambda();
                case "PublicNestedPatternInLambda":
                    return host.PublicNestedPatternInLambda();
                case "PrivateFieldPatternInLambda":
                    return host.PrivateFieldPatternInLambda();
                case "PrivatePropertyPatternInLambda":
                    return host.PrivatePropertyPatternInLambda();
                case "PrivatePatternInAsync":
                    return await host.PrivatePatternInAsync();
            }

            throw new ArgumentException("Unknown fixture method: " + methodName);
        }

        private static Task<HotReloadOrchestratorResult> RunEditsAsync(params (string Fragment, string Replacement)[] edits)
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", FixtureFileName));
            Assert.That(File.Exists(path), Is.True, "Fixture missing: " + path);
            string edited = File.ReadAllText(path);
            foreach ((string fragment, string replacement) in edits)
            {
                edited = ReplaceOnce(edited, fragment, replacement);
            }

            string editedPath = HotReloadTestSourceWriter.WriteEditedSource(
                "SubpatternNameE2E" + Math.Abs(edited.GetHashCode()) + ".cs",
                edited);
            return HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                new[] { path },
                contentPathOverride: null,
                CancellationToken.None,
                new Dictionary<string, string> { [path] = editedPath });
        }

        // Why the uniqueness check: a fragment that also matched another member would edit a method
        // the test does not call, and the assert would pass or fail for the wrong reason.
        private static string ReplaceOnce(string source, string fragment, string replacement)
        {
            int first = source.IndexOf(fragment, StringComparison.Ordinal);
            Assert.That(first, Is.GreaterThanOrEqualTo(0), "Fragment missing from the fixture: " + fragment);
            Assert.That(
                source.IndexOf(fragment, first + fragment.Length, StringComparison.Ordinal),
                Is.EqualTo(-1),
                "Fragment occurs more than once in the fixture: " + fragment);
            return source.Replace(fragment, replacement, StringComparison.Ordinal);
        }

        // Why the type and the parenthesis: a bare method name would also match a longer name that
        // contains it.
        private static HotReloadMethodOutcome FindRow(HotReloadOrchestratorResult result, string methodName)
        {
            string labelPart = "." + FixtureTypeName + "." + methodName + "(";
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Method != null && outcome.Method.Contains(labelPart))
                {
                    return outcome;
                }
            }

            Assert.Fail("No row for " + FixtureTypeName + "." + methodName + ".\n" + FormatOutcomes(result));
            return null;
        }

        private static string FormatOutcomes(HotReloadOrchestratorResult result)
        {
            List<string> lines = new List<string>();
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                lines.Add(outcome.Kind + " " + outcome.Method + " :: " + outcome.Reason);
            }

            return string.Join("\n", lines) + "\nWarnings:\n" + string.Join("\n", result.Warnings);
        }
    }
}
