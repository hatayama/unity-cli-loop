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
    /// End-to-end EditMode coverage for an edited call to an internal extension method of an
    /// internal class the reload was not given, when a public extension method of another class
    /// in the same assembly shares its name but never applies to the call.
    /// </summary>
    public class HotReloadSameNameExtensionCallE2ETests
    {
        private const string OwnStatementsValue = "OwnStatementsValue";

        // Public only because a test case argument has to be as visible as the test method.
        public enum FixtureKind
        {
            Plain,
            Partial
        }

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

        // Calls the shim assembly compiles as code of its own: in a lambda, through conditional
        // access, on a receiver that is itself a member access, beside a same-named public extension
        // that takes an int with another argument type, in an async body and in an iterator body.
        private static IEnumerable<TestCaseData> CallsInShimCompiledBodies()
        {
            yield return new TestCaseData("LambdaValue", "() => 20", "() => 50.AddDouble(1)", 20, 101);
            yield return new TestCaseData("ConditionalAccessLambdaValue", "() => 30", "() => ((int?)50)?.AddDouble(1) ?? 0", 30, 101);
            yield return new TestCaseData("MemberAccessReceiverLambdaValue", "() => this._seed + 10", "() => this._seed.AddDouble(1)", 60, 101);
            yield return new TestCaseData("IntOverloadLambdaValue", "() => 70", "() => 50.AddTriple(1)", 70, 151);
            yield return new TestCaseData("AsyncValue", "return 40;", "return 50.AddDouble(1);", 40, 101);
            yield return new TestCaseData("IteratorValues", "yield return 50;", "yield return 50.AddDouble(1);", 50, 101);
        }

        /// <summary>
        /// What: a plain type's own statements calling the internal extension method are patched, and
        /// the method returns the edited value, so the public method of the same name does not get in
        /// the way of the in-place call.
        /// </summary>
        [Test]
        public async Task Run_PlainOwnStatementsCallingInternalExtensionBesideSameNamePublicOne_PatchesBehavior()
        {
            HotReloadOrchestratorResult result = await RunEditAsync(FixtureKind.Plain, OwnStatementsValue, "return 10;", "return 50.AddDouble(1);");

            Assert.That(FindRow(result, FixtureKind.Plain, OwnStatementsValue).Kind, Is.EqualTo(HotReloadMethodOutcomeKind.Patched), FormatOutcomes(result));
            Assert.That(await CallAsync(FixtureKind.Plain, OwnStatementsValue), Is.EqualTo(101), FormatOutcomes(result));
        }

        /// <summary>
        /// What: a partial type's own statements calling the internal extension method are either
        /// patched and return the edited value, or skipped and keep the compiled value. They are never
        /// reported as patched and then throw when called.
        /// </summary>
        [Test]
        public async Task Run_PartialOwnStatementsCallingInternalExtensionBesideSameNamePublicOne_IsAppliedAsEditedOrSkipped()
        {
            HotReloadOrchestratorResult result = await RunEditAsync(FixtureKind.Partial, OwnStatementsValue, "return 10;", "return 50.AddDouble(1);");

            HotReloadMethodOutcomeKind kind = FindRow(result, FixtureKind.Partial, OwnStatementsValue).Kind;
            Assert.That(
                kind,
                Is.EqualTo(HotReloadMethodOutcomeKind.Patched).Or.EqualTo(HotReloadMethodOutcomeKind.Skipped),
                FormatOutcomes(result));
            int expected = kind == HotReloadMethodOutcomeKind.Patched ? 101 : 10;
            Assert.That(await CallAsync(FixtureKind.Partial, OwnStatementsValue), Is.EqualTo(expected), FormatOutcomes(result));
        }

        /// <summary>
        /// What: on a plain type, a call to the internal extension method that the shim assembly
        /// compiles as code of its own is either patched and returns the edited value, or skipped and
        /// keeps the compiled value. It is never reported as patched and then throws when called.
        /// </summary>
        [TestCaseSource(nameof(CallsInShimCompiledBodies))]
        public async Task Run_PlainCallInShimCompiledBodyBesideSameNamePublicExtension_IsAppliedAsEditedOrSkipped(
            string methodName,
            string fragment,
            string replacement,
            int compiledValue,
            int editedValue)
        {
            HotReloadOrchestratorResult result = await RunEditAsync(FixtureKind.Plain, methodName, fragment, replacement);

            HotReloadMethodOutcomeKind kind = FindRow(result, FixtureKind.Plain, methodName).Kind;
            Assert.That(
                kind,
                Is.EqualTo(HotReloadMethodOutcomeKind.Patched).Or.EqualTo(HotReloadMethodOutcomeKind.Skipped),
                FormatOutcomes(result));
            int expected = kind == HotReloadMethodOutcomeKind.Patched ? editedValue : compiledValue;
            Assert.That(await CallAsync(FixtureKind.Plain, methodName), Is.EqualTo(expected), FormatOutcomes(result));
        }

        /// <summary>
        /// What: on a partial type, the same calls are skipped and the method keeps the compiled
        /// value, instead of being reported as patched and then throwing when called.
        /// </summary>
        [TestCaseSource(nameof(CallsInShimCompiledBodies))]
        public async Task Run_PartialCallInShimCompiledBodyBesideSameNamePublicExtension_IsSkipped(
            string methodName,
            string fragment,
            string replacement,
            int compiledValue,
            int editedValue)
        {
            HotReloadOrchestratorResult result = await RunEditAsync(FixtureKind.Partial, methodName, fragment, replacement);

            Assert.That(FindRow(result, FixtureKind.Partial, methodName).Kind, Is.EqualTo(HotReloadMethodOutcomeKind.Skipped), FormatOutcomes(result));
            Assert.That(await CallAsync(FixtureKind.Partial, methodName), Is.EqualTo(compiledValue), FormatOutcomes(result));
        }

        private static async Task<HotReloadOrchestratorResult> RunEditAsync(
            FixtureKind fixture,
            string methodName,
            string fragment,
            string replacement)
        {
            string path = FixturePath(TypeName(fixture) + ".cs");
            string edited = HotReloadTestSourceWriter.WriteEditedSource(
                "SameNameExtension" + fixture + methodName + ".cs",
                ReplaceOnce(File.ReadAllText(path), fragment, replacement));
            return await HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                new[] { path },
                contentPathOverride: null,
                CancellationToken.None,
                new Dictionary<string, string> { [path] = edited });
        }

        // Why async: the async fixture method is awaited rather than waited on, because an EditMode
        // test must not block the main thread on a task.
        private static async Task<int> CallAsync(FixtureKind fixture, string methodName)
        {
            if (fixture == FixtureKind.Plain)
            {
                HotReloadSameNameExtensionCaller plain = new HotReloadSameNameExtensionCaller();
                switch (methodName)
                {
                    case OwnStatementsValue:
                        return plain.OwnStatementsValue();
                    case "LambdaValue":
                        return plain.LambdaValue();
                    case "ConditionalAccessLambdaValue":
                        return plain.ConditionalAccessLambdaValue();
                    case "MemberAccessReceiverLambdaValue":
                        return plain.MemberAccessReceiverLambdaValue();
                    case "IntOverloadLambdaValue":
                        return plain.IntOverloadLambdaValue();
                    case "AsyncValue":
                        return await plain.AsyncValue();
                    case "IteratorValues":
                        return First(plain.IteratorValues());
                }
            }
            else
            {
                HotReloadSameNameExtensionPartialCaller partial = new HotReloadSameNameExtensionPartialCaller();
                switch (methodName)
                {
                    case OwnStatementsValue:
                        return partial.OwnStatementsValue();
                    case "LambdaValue":
                        return partial.LambdaValue();
                    case "ConditionalAccessLambdaValue":
                        return partial.ConditionalAccessLambdaValue();
                    case "MemberAccessReceiverLambdaValue":
                        return partial.MemberAccessReceiverLambdaValue();
                    case "IntOverloadLambdaValue":
                        return partial.IntOverloadLambdaValue();
                    case "AsyncValue":
                        return await partial.AsyncValue();
                    case "IteratorValues":
                        return First(partial.IteratorValues());
                }
            }

            throw new ArgumentException("Unknown caller method: " + methodName);
        }

        private static int First(IEnumerable<int> values)
        {
            foreach (int value in values)
            {
                return value;
            }

            throw new InvalidOperationException("The iterator yielded nothing.");
        }

        private static string TypeName(FixtureKind fixture)
        {
            return fixture == FixtureKind.Plain ? nameof(HotReloadSameNameExtensionCaller) : nameof(HotReloadSameNameExtensionPartialCaller);
        }

        // Why the type and the parenthesis: a bare method name would also match a longer name that
        // contains it, or the same method on the other fixture type.
        private static HotReloadMethodOutcome FindRow(HotReloadOrchestratorResult result, FixtureKind fixture, string methodName)
        {
            string labelPart = "." + TypeName(fixture) + "." + methodName + "(";
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Method != null && outcome.Method.Contains(labelPart))
                {
                    return outcome;
                }
            }

            Assert.Fail("No row for " + TypeName(fixture) + "." + methodName + ".\n" + FormatOutcomes(result));
            return null;
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

        private static string FixturePath(string fileName)
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", fileName));
            Assert.That(File.Exists(path), Is.True, "Fixture missing: " + path);
            return path;
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
