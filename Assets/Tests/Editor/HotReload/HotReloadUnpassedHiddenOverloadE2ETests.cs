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
    /// End-to-end coverage for edited bodies that use a member of a type declared in a file the reload
    /// is not given, where that type also declares a private member the code there cannot reach: an
    /// overload that fits the arguments better, or a field that hides the one the code reads. The
    /// patch is compiled against a copy of the assembly where every member is public, so such a body
    /// must be skipped and keep the compiled value, while a private member the call cannot apply to
    /// leaves the body patched.
    /// </summary>
    public class HotReloadUnpassedHiddenOverloadE2ETests
    {
        private const string FixtureFileName = "HotReloadUnpassedHiddenOverloadHostFixtures.cs";
        private const string HiddenMemberReasonFragment = "where every member is public";

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
        /// What: a body whose call, construction, indexer access or extension call fits a private
        /// overload of an unpassed type better than the public one Unity's compiler calls, or that
        /// reads a field a private field of an unpassed type hides, is skipped with the hidden-member
        /// reason and keeps the compiled value, in its own statements, a lambda and an async body. So
        /// is a call that fits a private overload taking a params array in its expanded form, an
        /// optional parameter left out, a public nested type of the class, a constructed List, or the
        /// host the reload is given; a static-form extension call; a derived type's call that fits a
        /// protected overload of its base made on a value typed as the base; a call by bare name
        /// through a using static directive; a
        /// call written on a receiver whose type has a private instance method that fits better than
        /// the public extension method; a call that a private extension method of another class in
        /// scope fits better; a nested type's call by bare name that a private overload of its outer
        /// type's base fits better; those forms where the private member takes a type the reload is
        /// given, which the worker compiles from source, also through a type nested in a generic type
        /// constructed with it; and a call whose reachable overload is internal, which the worker's
        /// binding does not see.
        /// </summary>
        [TestCase("own", "return HotReloadHiddenOverloads.Pick(new ArraySegment<int>(_ints)) + 7102;")]
        [TestCase("own", "return new HotReloadHiddenOverloadBox(new ArraySegment<int>(_ints)).Value + 7102;")]
        [TestCase("own", "return new HotReloadHiddenOverloadIndexed()[new ArraySegment<int>(_ints)] + 7102;")]
        [TestCase("own", "return _ints.Length.Choose(new ArraySegment<int>(_ints)) + 7102;")]
        [TestCase("own", "return new HotReloadHiddenFieldDerived().Count + 7102;")]
        [TestCase("lambda", "return HotReloadHiddenOverloads.Pick(new ArraySegment<int>(_ints)) + 7112;")]
        [TestCase("lambda", "return new HotReloadHiddenOverloadBox(new ArraySegment<int>(_ints)).Value + 7112;")]
        [TestCase("lambda", "return new HotReloadHiddenOverloadIndexed()[new ArraySegment<int>(_ints)] + 7112;")]
        [TestCase("lambda", "return _ints.Length.Choose(new ArraySegment<int>(_ints)) + 7112;")]
        [TestCase("lambda", "return new HotReloadHiddenFieldDerived().Count + 7112;")]
        [TestCase("async", "return HotReloadHiddenOverloads.Pick(new ArraySegment<int>(_ints)) + 7122;")]
        [TestCase("async", "return new HotReloadHiddenOverloadBox(new ArraySegment<int>(_ints)).Value + 7122;")]
        [TestCase("async", "return new HotReloadHiddenOverloadIndexed()[new ArraySegment<int>(_ints)] + 7122;")]
        [TestCase("async", "return _ints.Length.Choose(new ArraySegment<int>(_ints)) + 7122;")]
        [TestCase("async", "return new HotReloadHiddenFieldDerived().Count + 7122;")]
        [TestCase("own", "return HotReloadUnpassedHiddenParamsOptionalOverloads.Expand(new ArraySegment<int>(_ints)) + 7102;")]
        [TestCase("own", "return HotReloadUnpassedHiddenParamsOptionalOverloads.Default(new ArraySegment<int>(_ints)) + 7102;")]
        [TestCase("own", "return HotReloadUnpassedHiddenMappedOverloads.Pick(new HotReloadUnpassedHiddenMappedOverloads.Key()) + 7102;")]
        [TestCase("own", "return HotReloadUnpassedHiddenMappedOverloads.Pick(new List<int>(_ints)) + 7102;")]
        [TestCase("own", "return HotReloadUnpassedHiddenMappedOverloads.Pick(this) + 7102;")]
        [TestCase("own", "return HotReloadHiddenOverloadExtensions.Choose(_ints.Length, new ArraySegment<int>(_ints)) + 7102;")]
        [TestCase("derived-own", "return new HotReloadProtectedOverloadBase().Pick(new ArraySegment<int>(new int[2])) + 7152;")]
        [TestCase("own", "return Pick(new ArraySegment<int>(_ints)) + 7102;")]
        [TestCase("lambda", "return Pick(new ArraySegment<int>(_ints)) + 7112;")]
        [TestCase("own", "return new HotReloadUnpassedHiddenInstanceTarget().Halve(new ArraySegment<int>(_ints)) + 7102;")]
        [TestCase("lambda", "return new HotReloadUnpassedHiddenInstanceTarget().Halve(new ArraySegment<int>(_ints)) + 7112;")]
        [TestCase("own", "return _ints.Length.Mark(new ArraySegment<int>(_ints)) + 7102;")]
        [TestCase("lambda", "return _ints.Length.Mark(new ArraySegment<int>(_ints)) + 7112;")]
        [TestCase("nested-own", "return Pick(new ArraySegment<int>(new int[2])) + 7162;")]
        [TestCase("own", "return Take(this) + 7102;")]
        [TestCase("own", "return _ints.Length.HostMark(this) + 7102;")]
        [TestCase("own", "return HotReloadUnpassedHostTwinExtensions.HostTwin(_ints.Length, this) + 7102;")]
        [TestCase("own", "return _ints.Length.HostTwin(this) + 7102;")]
        [TestCase("nested-own", "return Pick(new HotReloadUnpassedHiddenOverloadHost()) + 7162;")]
        [TestCase("own", "return HotReloadUnpassedEnvelopeOverloads.Pick(new HotReloadUnpassedEnvelope<HotReloadUnpassedHiddenOverloadHost>.Item()) + 7102;")]
        [TestCase("own", "return HotReloadUnpassedInternalPickOverloads.Pick(new ArraySegment<int>(_ints)) + 7102;")]
        [TestCase("lambda", "return HotReloadUnpassedInternalPickOverloads.Pick(new ArraySegment<int>(_ints)) + 7112;")]
        public async Task Run_UseOfAnUnpassedTypeThePatchWouldResolveToAPrivateMember_IsSkippedAndKeepsTheCompiledValue(
            string site,
            string statements)
        {
            HotReloadOrchestratorResult result = await RunEditAsync(site, statements);

            HotReloadMethodOutcome row = FindRow(result, MethodFor(site));
            Assert.That(row.Kind, Is.EqualTo(HotReloadMethodOutcomeKind.Skipped), FormatOutcomes(result));
            Assert.That(row.Reason, Does.Contain(HiddenMemberReasonFragment), FormatOutcomes(result));
            Assert.That(await CallAsync(site), Is.EqualTo(CompiledValueFor(site)), FormatOutcomes(result));
        }

        /// <summary>
        /// What: a body that calls a public overload of an unpassed type is patched and returns the
        /// edited value when no private overload of the same name can apply to the call: one takes
        /// more parameters than the call has arguments, or an argument (a string, also on an
        /// extension call written on its receiver) converts to none of their parameter types, a
        /// nested type and a constructed List included, also when the overload Unity's compiler calls
        /// is internal; or a named argument matches only the public overload's parameter name. So is a
        /// call whose better-fitting private overload belongs to another assembly, which the patch's
        /// compile keeps hidden. So is a derived type's call of its base's protected overload on
        /// itself, which Unity's compiler resolves too.
        /// </summary>
        [TestCase("own", "return HotReloadUnpassedHiddenArityOverloads.Pick(new ArraySegment<int>(_ints)) + 7102;", 7103)]
        [TestCase("lambda", "return HotReloadUnpassedHiddenArityOverloads.Pick(new ArraySegment<int>(_ints)) + 7112;", 7113)]
        [TestCase("own", "return HotReloadHiddenOverloads.Pick(\"text\") + 7102;", 7103)]
        [TestCase("own", "return _ints.Length.Choose(\"text\") + 7102;", 7103)]
        [TestCase("own", "return HotReloadUnpassedHiddenMappedOverloads.Pick(\"text\") + 7102;", 7103)]
        [TestCase("derived-own", "return Pick(new ArraySegment<int>(new int[2])) + 7152;", 7154)]
        [TestCase("derived-own", "return this.Pick(new Memory<int>(new int[2])) + 7152;", 7154)]
        [TestCase("own", "return HotReloadUnpassedHiddenArityOverloads.Pick(value: new ArraySegment<int>(_ints)) + 7102;", 7103)]
        [TestCase("own", "return HotReloadUnpassedInternalPickOverloads.Pick(\"text\") + 7102;", 7103)]
        [TestCase("own", "return ShimReferenceForeignInternal.ForeignHiddenPickOverloads.Pick(new ArraySegment<int>(_ints)) + 7102;", 7103)]
        [TestCase("lambda", "return ShimReferenceForeignInternal.ForeignHiddenPickOverloads.Pick(new ArraySegment<int>(_ints)) + 7112;", 7113)]
        public async Task Run_UseOfAnUnpassedTypeWhosePrivateMemberCannotApply_IsPatchedAndReturnsTheEditedValue(
            string site,
            string statements,
            int editedValue)
        {
            HotReloadOrchestratorResult result = await RunEditAsync(site, statements);

            Assert.That(FindRow(result, MethodFor(site)).Kind, Is.EqualTo(HotReloadMethodOutcomeKind.Patched), FormatOutcomes(result));
            Assert.That(await CallAsync(site), Is.EqualTo(editedValue), FormatOutcomes(result));
        }

        private static string MethodFor(string site)
        {
            switch (site)
            {
                case "own":
                    return "EditedInOwnStatements";
                case "lambda":
                    return "EditedInLambda";
                case "async":
                    return "EditedInAsync";
                case "derived-own":
                    return "DerivedEditedInOwnStatements";
                case "nested-own":
                    return "NestedEditedInOwnStatements";
            }

            throw new ArgumentException("Unknown site: " + site, nameof(site));
        }

        private static int CompiledValueFor(string site)
        {
            switch (site)
            {
                case "own":
                    return 7101;
                case "lambda":
                    return 7111;
                case "async":
                    return 7121;
                case "derived-own":
                    return 7151;
                case "nested-own":
                    return 7161;
            }

            throw new ArgumentException("Unknown site: " + site, nameof(site));
        }

        private static async Task<int> CallAsync(string site)
        {
            HotReloadUnpassedHiddenOverloadHost host = new HotReloadUnpassedHiddenOverloadHost();
            switch (site)
            {
                case "own":
                    return host.EditedInOwnStatements();
                case "lambda":
                    return host.EditedInLambda();
                case "async":
                    return await host.EditedInAsync();
                case "derived-own":
                    return new HotReloadUnpassedProtectedOverloadDerived().DerivedEditedInOwnStatements();
                case "nested-own":
                    return new HotReloadUnpassedOuterHost.Nested().NestedEditedInOwnStatements();
            }

            throw new ArgumentException("Unknown site: " + site, nameof(site));
        }

        // Why only the host file: the types the edited bodies use live in other files of this
        // assembly, and the reload must read them from the compiled assembly rather than from source.
        private static Task<HotReloadOrchestratorResult> RunEditAsync(string site, string statements)
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", FixtureFileName));
            string edited = File.ReadAllText(path).Replace("\r\n", "\n");
            switch (site)
            {
                case "own":
                    edited = ReplaceOnce(edited, "return 7101;", statements);
                    break;
                case "lambda":
                    edited = ReplaceOnce(edited, "() => 7111;", "() => { " + statements + " };");
                    break;
                case "async":
                    edited = ReplaceOnce(edited, "return 7121;", statements);
                    break;
                case "nested-own":
                    edited = ReplaceOnce(edited, "return 7161;", statements);
                    break;
                default:
                    edited = ReplaceOnce(edited, "return 7151;", statements);
                    break;
            }

            string editedPath = HotReloadTestSourceWriter.WriteEditedSource(
                "UnpassedHiddenOverloadE2E" + Math.Abs(edited.GetHashCode()) + ".cs",
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

        // Why the dot and the parenthesis: a bare method name would also match a longer name that
        // contains it.
        private static HotReloadMethodOutcome FindRow(HotReloadOrchestratorResult result, string methodName)
        {
            string labelPart = "." + methodName + "(";
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Method != null && outcome.Method.Contains(labelPart))
                {
                    return outcome;
                }
            }

            Assert.Fail("No row for " + methodName + ".\n" + FormatOutcomes(result));
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
