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
    /// End-to-end coverage for edited bodies that use a member whose class also declares a private
    /// member the code there cannot reach: an overload that fits the arguments better, or a field that
    /// hides the one the code reads. The patch is compiled against a copy of the assembly where every
    /// member is public, so such a body must be skipped and keep the compiled value rather than call
    /// the private member or throw, while uses the private member is reachable from still patch.
    /// </summary>
    public class HotReloadHiddenOverloadE2ETests
    {
        private const string FixtureFileName = "HotReloadHiddenOverloadFixtures.cs";
        private const string MemberAnchor = "private int[] _ints = { 1, 2 };\n#pragma warning restore CS0414";
        private const string HiddenMemberReasonFragment = "where every member is public";

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
        /// What: a body whose call, construction, indexer access or extension call fits a private
        /// overload better than the public one Unity's compiler calls, or that reads a field a private
        /// field hides, is skipped and keeps the compiled value, in its own statements, a lambda and
        /// an async body, whether the argument is a BCL type a referenced assembly also declares
        /// internally (Memory) or not (ArraySegment), and also when two public extension overloads leave
        /// Unity's pick unknown here, or the private overload takes a params array. So is a derived
        /// type's call that fits a protected overload of its base better, made on a value typed as the
        /// base, or that constructs its base with arguments a protected constructor fits better. A call
        /// whose argument has no type (null, where Unity's binding picks the public overload) is skipped
        /// too, because which candidates it fits is not told.
        /// </summary>
        [TestCase("own", "return HotReloadHiddenOverloads.Pick(new Memory<int>(_ints)) + 7002;")]
        [TestCase("own", "return HotReloadHiddenOverloads.Pick(new ArraySegment<int>(_ints)) + 7002;")]
        [TestCase("own", "return new HotReloadHiddenOverloadBox(new Memory<int>(_ints)).Value + 7002;")]
        [TestCase("own", "return new HotReloadHiddenOverloadBox(new ArraySegment<int>(_ints)).Value + 7002;")]
        [TestCase("own", "return new HotReloadHiddenOverloadIndexed()[new Memory<int>(_ints)] + 7002;")]
        [TestCase("own", "return new HotReloadHiddenOverloadIndexed()[new ArraySegment<int>(_ints)] + 7002;")]
        [TestCase("own", "return _ints.Length.Choose(new Memory<int>(_ints)) + 7002;")]
        [TestCase("own", "return _ints.Length.Choose(new ArraySegment<int>(_ints)) + 7002;")]
        [TestCase("own", "return new HotReloadHiddenFieldDerived().Count + 7002;")]
        [TestCase("own", "return _ints.Length.Prefer(new ArraySegment<int>(_ints)) + 7002;")]
        [TestCase("own", "return HotReloadHiddenParamsOverloads.Pick(new Memory<int>(_ints)) + 7002;")]
        [TestCase("lambda", "return HotReloadHiddenOverloads.Pick(new Memory<int>(_ints)) + 7012;")]
        [TestCase("lambda", "return HotReloadHiddenOverloads.Pick(new ArraySegment<int>(_ints)) + 7012;")]
        [TestCase("lambda", "return new HotReloadHiddenOverloadBox(new Memory<int>(_ints)).Value + 7012;")]
        [TestCase("lambda", "return new HotReloadHiddenOverloadBox(new ArraySegment<int>(_ints)).Value + 7012;")]
        [TestCase("lambda", "return new HotReloadHiddenOverloadIndexed()[new Memory<int>(_ints)] + 7012;")]
        [TestCase("lambda", "return new HotReloadHiddenOverloadIndexed()[new ArraySegment<int>(_ints)] + 7012;")]
        [TestCase("lambda", "return _ints.Length.Choose(new Memory<int>(_ints)) + 7012;")]
        [TestCase("lambda", "return _ints.Length.Choose(new ArraySegment<int>(_ints)) + 7012;")]
        [TestCase("lambda", "return new HotReloadHiddenFieldDerived().Count + 7012;")]
        [TestCase("lambda", "return _ints.Length.Prefer(new ArraySegment<int>(_ints)) + 7012;")]
        [TestCase("lambda", "return HotReloadHiddenParamsOverloads.Pick(new Memory<int>(_ints)) + 7012;")]
        [TestCase("async", "return HotReloadHiddenOverloads.Pick(new Memory<int>(_ints)) + 7022;")]
        [TestCase("async", "return HotReloadHiddenOverloads.Pick(new ArraySegment<int>(_ints)) + 7022;")]
        [TestCase("async", "return new HotReloadHiddenOverloadBox(new ArraySegment<int>(_ints)).Value + 7022;")]
        [TestCase("async", "return new HotReloadHiddenFieldDerived().Count + 7022;")]
        [TestCase("derived-own", "return new HotReloadProtectedOverloadBase().Pick(new ArraySegment<int>(new int[2])) + 7052;")]
        [TestCase("derived-lambda", "return new HotReloadProtectedOverloadBase().Pick(new ArraySegment<int>(new int[2])) + 7062;")]
        [TestCase("derived-own", "return new HotReloadProtectedOverloadBase().Pick(new Memory<int>(new int[2])) + 7052;")]
        [TestCase("ctor-derived-own", "return new HotReloadProtectedConstructorBase(new ArraySegment<int>(new int[2])).Value + 7072;")]
        [TestCase("own", "return HotReloadHiddenConstantOverloads.Pick(1, new Memory<int>(_ints)) + 7002;")]
        [TestCase("own", "return new HotReloadHiddenDelegateIndexed()[new Memory<int>(_ints)](\"text\") + 7002;")]
        [TestCase("own", "return HotReloadHiddenNullOverloads.Pick(null) + 7002;")]
        public async Task Run_UseThePatchWouldResolveToAPrivateMember_IsSkippedAndKeepsTheCompiledValue(
            string site,
            string statements)
        {
            HotReloadOrchestratorResult result = await RunEditAsync(site, statements, string.Empty);

            HotReloadMethodOutcome row = FindRow(result, MethodFor(site));
            Assert.That(row.Kind, Is.EqualTo(HotReloadMethodOutcomeKind.Skipped), FormatOutcomes(result));
            Assert.That(row.Reason, Does.Contain(HiddenMemberReasonFragment), FormatOutcomes(result));
            Assert.That(await CallAsync(site), Is.EqualTo(CompiledValueFor(site)), FormatOutcomes(result));
        }

        /// <summary>
        /// What: an added field initialized by, or an added method that makes, a call fitting a private
        /// overload better than the public one keeps the body that uses it out of the patch, so the
        /// compiled value stays. The field is refused by the added-field initializer rule; the method
        /// is refused for the hidden overload, and the body calling it for calling a skipped method.
        /// </summary>
        [TestCase(
            "return _addedPicked + 7002;",
            " private int _addedPicked = HotReloadHiddenOverloads.Pick(new ArraySegment<int>(new int[3]));",
            "EditedInOwnStatements",
            "has an initializer that is not a literal")]
        [TestCase(
            "return AddedPick() + 7002;",
            " private int AddedPick() { return HotReloadHiddenOverloads.Pick(new ArraySegment<int>(_ints)); }",
            "AddedPick",
            HiddenMemberReasonFragment)]
        public async Task Run_AddedMemberThePatchWouldResolveToAPrivateMember_KeepsTheCompiledValue(
            string statements,
            string addedMembers,
            string refusedMethod,
            string reasonFragment)
        {
            HotReloadOrchestratorResult result = await RunEditAsync("own", statements, addedMembers);

            HotReloadMethodOutcome refused = FindRow(result, refusedMethod);
            Assert.That(refused.Kind, Is.EqualTo(HotReloadMethodOutcomeKind.Skipped), FormatOutcomes(result));
            Assert.That(refused.Reason, Does.Contain(reasonFragment), FormatOutcomes(result));
            Assert.That(FindRow(result, MethodFor("own")).Kind, Is.EqualTo(HotReloadMethodOutcomeKind.Skipped), FormatOutcomes(result));
            Assert.That(await CallAsync("own"), Is.EqualTo(CompiledValueFor("own")), FormatOutcomes(result));
        }

        /// <summary>
        /// What: a body is still patched and returns the edited value when the private overload is one
        /// the code may call (the host's own, its outer type's from a nested type, or its base's
        /// protected one called on itself or through base, or a class's own protected constructor), or
        /// when the call, an extension call included, fits only
        /// public overloads of a class that also has private ones.
        /// </summary>
        [TestCase("own", "return Own(new ArraySegment<int>(_ints)) + 7002;", 7004)]
        [TestCase("lambda", "return Own(new ArraySegment<int>(_ints)) + 7012;", 7014)]
        [TestCase("own", "return Own(new Memory<int>(_ints)) + 7002;", 7004)]
        [TestCase("lambda", "return Own(new Memory<int>(_ints)) + 7012;", 7014)]
        [TestCase("nested-own", "return new HotReloadHiddenOverloadHost().Own(new ArraySegment<int>(new int[2])) + 7032;", 7034)]
        [TestCase("nested-lambda", "return new HotReloadHiddenOverloadHost().Own(new ArraySegment<int>(new int[2])) + 7042;", 7044)]
        [TestCase("own", "return HotReloadHiddenOverloads.Pick(\"text\") + 7002;", 7003)]
        [TestCase("derived-own", "return Pick(new ArraySegment<int>(new int[2])) + 7052;", 7054)]
        [TestCase("derived-lambda", "return this.Pick(new ArraySegment<int>(new int[2])) + 7062;", 7064)]
        [TestCase("derived-own", "return base.Pick(new ArraySegment<int>(new int[2])) + 7052;", 7054)]
        [TestCase("derived-own", "return Pick(new Memory<int>(new int[2])) + 7052;", 7054)]
        [TestCase("ctor-base-own", "return new HotReloadProtectedConstructorBase(new ArraySegment<int>(new int[2])).Value + 7082;", 7084)]
        [TestCase("lambda", "return HotReloadHiddenOverloads.Pick(\"text\") + 7012;", 7013)]
        [TestCase("own", "return _ints.Length.Choose(\"text\") + 7002;", 7003)]
        [TestCase("lambda", "return _ints.Length.Choose(\"text\") + 7012;", 7013)]
        [TestCase("own", "return _ints.Length.Prefer(\"text\") + 7002;", 7004)]
        [TestCase("own", "int? count = _ints.Length; return count?.Choose(\"text\") + 7002 ?? 0;", 7003)]
        [TestCase("lambda", "return _ints.Length.Prefer(\"text\") + 7012;", 7014)]
        public async Task Run_UseOfAPrivateMemberTheCodeMayCall_IsPatchedAndReturnsTheEditedValue(
            string site,
            string statements,
            int editedValue)
        {
            HotReloadOrchestratorResult result = await RunEditAsync(site, statements, string.Empty);

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
                case "nested-own":
                    return "NestedEditedInOwnStatements";
                case "nested-lambda":
                    return "NestedEditedInLambda";
                case "derived-own":
                    return "DerivedEditedInOwnStatements";
                case "derived-lambda":
                    return "DerivedEditedInLambda";
                case "ctor-derived-own":
                    return "ConstructorDerivedEditedInOwnStatements";
                case "ctor-base-own":
                    return "BaseEditedInOwnStatements";
            }

            throw new ArgumentException("Unknown site: " + site, nameof(site));
        }

        private static int CompiledValueFor(string site)
        {
            switch (site)
            {
                case "own":
                    return 7001;
                case "lambda":
                    return 7011;
                case "async":
                    return 7021;
                case "derived-own":
                    return 7051;
                case "derived-lambda":
                    return 7061;
                case "ctor-derived-own":
                    return 7071;
            }

            throw new ArgumentException("Unknown site: " + site, nameof(site));
        }

        private static async Task<int> CallAsync(string site)
        {
            HotReloadHiddenOverloadHost host = new HotReloadHiddenOverloadHost();
            HotReloadHiddenOverloadHost.Nested nested = new HotReloadHiddenOverloadHost.Nested();
            HotReloadProtectedOverloadDerived derived = new HotReloadProtectedOverloadDerived();
            switch (site)
            {
                case "own":
                    return host.EditedInOwnStatements();
                case "lambda":
                    return host.EditedInLambda();
                case "async":
                    return await host.EditedInAsync();
                case "nested-own":
                    return nested.NestedEditedInOwnStatements();
                case "nested-lambda":
                    return nested.NestedEditedInLambda();
                case "derived-own":
                    return derived.DerivedEditedInOwnStatements();
                case "derived-lambda":
                    return derived.DerivedEditedInLambda();
                case "ctor-derived-own":
                    return new HotReloadProtectedConstructorDerived().ConstructorDerivedEditedInOwnStatements();
                case "ctor-base-own":
                    return new HotReloadProtectedConstructorBase(0).BaseEditedInOwnStatements();
            }

            throw new ArgumentException("Unknown site: " + site, nameof(site));
        }

        private static Task<HotReloadOrchestratorResult> RunEditAsync(string site, string statements, string addedMembers)
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", FixtureFileName));
            // Why normalized: the anchor spans a line break, and a checkout with CRLF endings would
            // not contain it as written.
            string edited = File.ReadAllText(path).Replace("\r\n", "\n");
            switch (site)
            {
                case "own":
                    edited = ReplaceOnce(edited, "return 7001;", statements);
                    break;
                case "lambda":
                    edited = ReplaceOnce(edited, "() => 7011;", "() => { " + statements + " };");
                    break;
                case "async":
                    edited = ReplaceOnce(edited, "return 7021;", statements);
                    break;
                case "nested-own":
                    edited = ReplaceOnce(edited, "return 7031;", statements);
                    break;
                case "nested-lambda":
                    edited = ReplaceOnce(edited, "() => 7041;", "() => { " + statements + " };");
                    break;
                case "derived-own":
                    edited = ReplaceOnce(edited, "return 7051;", statements);
                    break;
                case "ctor-derived-own":
                    edited = ReplaceOnce(edited, "return 7071;", statements);
                    break;
                case "ctor-base-own":
                    edited = ReplaceOnce(edited, "return 7081;", statements);
                    break;
                default:
                    edited = ReplaceOnce(edited, "() => 7061;", "() => { " + statements + " };");
                    break;
            }

            edited = ReplaceOnce(edited, MemberAnchor, MemberAnchor + "\n" + addedMembers);
            string editedPath = HotReloadTestSourceWriter.WriteEditedSource(
                "HiddenOverloadE2E" + Math.Abs(edited.GetHashCode()) + ".cs",
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
        // contains it, such as the nested type's methods.
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
