using System;
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
    /// End-to-end coverage for members added in the same edit and used with a BCL type a referenced
    /// assembly also declares internally (Memory, ReadOnlySpan). With accessibility ignored such a
    /// use binds to nothing, so each shape must get the row the same shape with ArraySegment or a
    /// user ref struct gets: patched shapes return the edited value, and refused ones keep the
    /// compiled value without failing the file.
    /// </summary>
    public class HotReloadBclTwinAddedMemberE2ETests
    {
        private const string FixtureFileName = "HotReloadBclTwinTypeFixtures.cs";
        private const string FixtureTypeName = "HotReloadBclTwinHost";
        private const string MemberAnchor = "private int _stored;";

        private const string AddedMemoryMethod =
            " private int AddedMemoryLength(Memory<int> memory) { return memory.Length; }";

        private const string AddedSpanMethod =
            " private int AddedSpanLength(ReadOnlySpan<int> span) { return span.Length; }";

        private const string AddedMemoryField =
            " private Memory<int> _addedMemory = new Memory<int>(new int[3]);";

        // Why a method group: an added method whose body converts a private method to a delegate is
        // skipped, which is what a call to it has to notice.
        private const string SkippedAddedMemoryMethod =
            " private int AddedGroupsPrivateMemory(Memory<int> memory)"
            + " { Func<Memory<int>, int> length = PrivateMemoryLength; return length(memory); }";

        private const string AddedFieldFromInternalMemoryCall =
            " private int _addedFromInternal = HotReloadBclTwinTargets.MemoryLength(new Memory<int>(new int[3]));";

        private HotReloadDomainTestScope _scope;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            HotReloadBclTwinTypePrecondition.AssertReferencedAssemblyCarriesInternalBclTwins();
        }

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
        /// What: a body that calls an added method with a Memory or ReadOnlySpan argument, from its own
        /// statements, a lambda or an async body, or reads and writes an added Memory field, is
        /// patched and returns the edited value instead of failing the file.
        /// </summary>
        [TestCase("own", "return AddedMemoryLength(new Memory<int>(_ints)) + 6902;", AddedMemoryMethod, 6904)]
        [TestCase("lambda", "return AddedMemoryLength(new Memory<int>(_ints)) + 6902;", AddedMemoryMethod, 6904)]
        [TestCase("async", "return AddedMemoryLength(new Memory<int>(_ints)) + 6902;", AddedMemoryMethod, 6904)]
        [TestCase("own", "return AddedSpanLength(new ReadOnlySpan<int>(_ints)) + 6902;", AddedSpanMethod, 6904)]
        [TestCase("lambda", "return AddedSpanLength(new ReadOnlySpan<int>(_ints)) + 6902;", AddedSpanMethod, 6904)]
        [TestCase("own", "return _addedMemory.Length + 6902;", AddedMemoryField, 6905)]
        [TestCase("lambda", "return _addedMemory.Length + 6902;", AddedMemoryField, 6905)]
        [TestCase("own", "_addedMemory = new Memory<int>(_ints); return _addedMemory.Length + 6902;", AddedMemoryField, 6904)]
        public async Task Run_TwinTypedUseOfAddedMember_IsPatchedAndReturnsTheEditedValue(
            string site,
            string statements,
            string addedMembers,
            int editedValue)
        {
            HotReloadOrchestratorResult result = await RunEditAsync(site, statements, addedMembers);

            Assert.That(FindRow(result, MethodFor(site)).Kind, Is.EqualTo(HotReloadMethodOutcomeKind.Patched), FormatOutcomes(result));
            Assert.That(await CallAsync(site), Is.EqualTo(editedValue), FormatOutcomes(result));
        }

        /// <summary>
        /// What: a body whose twin-typed use of an added member the shim cannot run is skipped with
        /// the reason its ArraySegment form gets and keeps the compiled value without failing the
        /// file: an added method converted to a delegate, a call to an added method this reload
        /// skipped, and a read of an added field whose initializer calls an internal method.
        /// </summary>
        [TestCase(
            "Func<Memory<int>, int> length = AddedMemoryLength; return length(new Memory<int>(_ints)) + 6902;",
            AddedMemoryMethod,
            "as a method group or delegate")]
        [TestCase(
            "return AddedGroupsPrivateMemory(new Memory<int>(_ints)) + 6902;",
            SkippedAddedMemoryMethod,
            "which this reload skipped")]
        [TestCase(
            "return _addedFromInternal + 6902;",
            AddedFieldFromInternalMemoryCall,
            "has an initializer that is not a literal")]
        public async Task Run_TwinTypedUseOfAddedMemberTheShimCannotRun_IsSkippedAndKeepsTheCompiledValue(
            string statements,
            string addedMembers,
            string reasonFragment)
        {
            HotReloadOrchestratorResult result = await RunEditAsync("own", statements, addedMembers);

            HotReloadMethodOutcome row = FindRow(result, MethodFor("own"));
            Assert.That(row.Kind, Is.EqualTo(HotReloadMethodOutcomeKind.Skipped), FormatOutcomes(result));
            Assert.That(row.Reason, Does.Contain(reasonFragment), FormatOutcomes(result));
            Assert.That(await CallAsync("own"), Is.EqualTo(6901), FormatOutcomes(result));
        }

        private static string MethodFor(string site)
        {
            switch (site)
            {
                case "own":
                    return "UsesAddedMemberInOwnStatements";
                case "lambda":
                    return "UsesAddedMemberInLambda";
                case "async":
                    return "UsesAddedMemberInAsync";
            }

            throw new ArgumentException("Unknown site: " + site, nameof(site));
        }

        // Why async: the async fixture method is awaited rather than waited on, because an EditMode
        // test must not block the main thread on a task.
        private static async Task<int> CallAsync(string site)
        {
            HotReloadBclTwinHost host = new HotReloadBclTwinHost();
            switch (site)
            {
                case "own":
                    return host.UsesAddedMemberInOwnStatements();
                case "lambda":
                    return host.UsesAddedMemberInLambda();
                case "async":
                    return await host.UsesAddedMemberInAsync();
            }

            throw new ArgumentException("Unknown site: " + site, nameof(site));
        }

        private static Task<HotReloadOrchestratorResult> RunEditAsync(string site, string statements, string addedMembers)
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", FixtureFileName));
            string edited = File.ReadAllText(path);
            switch (site)
            {
                case "own":
                    edited = ReplaceOnce(edited, "return 6901;", statements);
                    break;
                case "lambda":
                    edited = ReplaceOnce(edited, "() => 6911;", "() => { " + statements + " };");
                    break;
                default:
                    edited = ReplaceOnce(edited, "return 6921;", statements);
                    break;
            }

            edited = ReplaceOnce(edited, MemberAnchor, MemberAnchor + addedMembers);
            string editedPath = HotReloadTestSourceWriter.WriteEditedSource(
                "BclTwinAddedMemberE2E" + Math.Abs(edited.GetHashCode()) + ".cs",
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

            return string.Join("\n", lines);
        }
    }
}
