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
        // access (also followed by a call, a property or an element access), on a receiver that is
        // itself a member access, beside a same-named public extension that takes an int with another
        // argument type (also with a named argument), fewer parameters (CS1501, also after '?.') or
        // more parameters (CS7036), in an async body and in an iterator body.
        private static IEnumerable<TestCaseData> CallsInShimCompiledBodies()
        {
            yield return new TestCaseData("LambdaValue", "() => 20", "() => 50.AddDouble(1)", 20, 101);
            yield return new TestCaseData("ConditionalAccessLambdaValue", "() => 30", "() => ((int?)50)?.AddDouble(1) ?? 0", 30, 101);
            yield return new TestCaseData("MemberAccessReceiverLambdaValue", "() => this._seed + 10", "() => this._seed.AddDouble(1)", 60, 101);
            yield return new TestCaseData("IntOverloadLambdaValue", "() => 70", "() => 50.AddTriple(1)", 70, 151);
            yield return new TestCaseData("ChainedConditionalAccessLambdaValue", "() => 80", "() => ((int?)50)?.AddDouble(1).CompareTo(0) ?? 0", 80, 1);
            yield return new TestCaseData("NamedArgumentLambdaValue", "() => 90", "() => 50.AddTriple(add: 1)", 90, 151);
            yield return new TestCaseData("ConditionalAccessLambdaValue", "() => 30", "() => ((int?)50)?.AddDouble(1).ToString().Length ?? 0", 30, 3);
            yield return new TestCaseData("ConditionalAccessLambdaValue", "() => 30", "() => ((int?)50)?.AddDouble(1).ToString()[0] ?? 0", 30, (int)'1');
            yield return new TestCaseData("LambdaValue", "() => 20", "() => 50.AddQuadruple(1)", 20, 201);
            yield return new TestCaseData("ConditionalAccessLambdaValue", "() => 30", "() => ((int?)50)?.AddQuadruple(1) ?? 0", 30, 201);
            yield return new TestCaseData("LambdaValue", "() => 20", "() => 50.AddQuintuple(1)", 20, 251);
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

        /// <summary>
        /// What: a lambda calling the internal extension method is skipped with a reason that names
        /// the internal class, and the method keeps the compiled value. This holds beside a same-named
        /// public extension method, and beside a public instance method of the receiver that does not
        /// take the call, because the compiler then goes on to the extension methods.
        /// </summary>
        [TestCase(FixtureKind.Plain, "() => 50.AddDouble(1)")]
        [TestCase(FixtureKind.Plain, "() => new HotReloadSameNameInstanceTarget().Measure(1)")]
        [TestCase(FixtureKind.Partial, "() => new HotReloadSameNameInstanceTarget().Measure(1)")]
        public async Task Run_LambdaCallingInternalExtensionBesideSameNamedPublicMethod_IsSkippedNamingTheInternalClass(
            FixtureKind fixture,
            string replacement)
        {
            HotReloadOrchestratorResult result = await RunEditAsync(fixture, "LambdaValue", "() => 20", replacement);

            HotReloadMethodOutcome row = FindRow(result, fixture, "LambdaValue");
            Assert.That(row.Kind, Is.EqualTo(HotReloadMethodOutcomeKind.Skipped), FormatOutcomes(result));
            Assert.That(row.Reason, Does.Contain("is internal to 'HotReloadSameNameInternalExtensionMath'"), FormatOutcomes(result));
            Assert.That(await CallAsync(fixture, "LambdaValue"), Is.EqualTo(20), FormatOutcomes(result));
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
        private static Task<int> CallAsync(FixtureKind fixture, string methodName)
        {
            return fixture == FixtureKind.Plain ? CallPlainAsync(methodName) : CallPartialAsync(methodName);
        }

        private static async Task<int> CallPlainAsync(string methodName)
        {
            HotReloadSameNameExtensionCaller caller = new HotReloadSameNameExtensionCaller();
            switch (methodName)
            {
                case OwnStatementsValue:
                    return caller.OwnStatementsValue();
                case "LambdaValue":
                    return caller.LambdaValue();
                case "ConditionalAccessLambdaValue":
                    return caller.ConditionalAccessLambdaValue();
                case "ChainedConditionalAccessLambdaValue":
                    return caller.ChainedConditionalAccessLambdaValue();
                case "MemberAccessReceiverLambdaValue":
                    return caller.MemberAccessReceiverLambdaValue();
                case "IntOverloadLambdaValue":
                    return caller.IntOverloadLambdaValue();
                case "NamedArgumentLambdaValue":
                    return caller.NamedArgumentLambdaValue();
                case "AsyncValue":
                    return await caller.AsyncValue();
                case "IteratorValues":
                    return First(caller.IteratorValues());
                default:
                    throw new ArgumentException("Unknown caller method: " + methodName);
            }
        }

        private static async Task<int> CallPartialAsync(string methodName)
        {
            HotReloadSameNameExtensionPartialCaller caller = new HotReloadSameNameExtensionPartialCaller();
            switch (methodName)
            {
                case OwnStatementsValue:
                    return caller.OwnStatementsValue();
                case "LambdaValue":
                    return caller.LambdaValue();
                case "ConditionalAccessLambdaValue":
                    return caller.ConditionalAccessLambdaValue();
                case "ChainedConditionalAccessLambdaValue":
                    return caller.ChainedConditionalAccessLambdaValue();
                case "MemberAccessReceiverLambdaValue":
                    return caller.MemberAccessReceiverLambdaValue();
                case "IntOverloadLambdaValue":
                    return caller.IntOverloadLambdaValue();
                case "NamedArgumentLambdaValue":
                    return caller.NamedArgumentLambdaValue();
                case "AsyncValue":
                    return await caller.AsyncValue();
                case "IteratorValues":
                    return First(caller.IteratorValues());
                default:
                    throw new ArgumentException("Unknown caller method: " + methodName);
            }
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
