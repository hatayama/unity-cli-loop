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
    /// Transform worker coverage for property patterns. A pattern member name reads the matched
    /// value, so the shim must keep it as written: it cannot take a receiver, an accessor call, or an
    /// added-field read. A body whose pattern needs one of those is skipped on its own instead.
    /// </summary>
    public class TransformWorkerSubpatternNameTests
    {
        private const string FixtureFileName = "HotReloadSubpatternNameFixtures.cs";
        private const string FixtureProjectRelativePath = "Assets/Tests/Editor/HotReload/" + FixtureFileName;
        private const string AccessorRewriteUnavailable = " Accessor rewrite unavailable: ";
        private const string ClosureReason =
            "Lambda, local-function, or query-expression bodies that access private/internal members "
            + "are skipped (closure methods JIT-compile normally and fail accessibility checks)."
            + AccessorRewriteUnavailable;
        private const string AsyncReason =
            "Async or iterator methods whose bodies access private/internal members are skipped "
            + "(state-machine MoveNext JIT-compiles normally and fails accessibility checks)."
            + AccessorRewriteUnavailable;
        private const string AddedMethodReason =
            "Added methods whose bodies access private/internal members are skipped when the access "
            + "has no accessor rewrite (the added method JIT-compiles normally and fails accessibility "
            + "checks)."
            + AccessorRewriteUnavailable;
        private const string AddedFieldReason =
            "Property patterns that match an added field are skipped; a pattern member name cannot be "
            + "replaced by a read of the field's store. Read the field with a member access instead "
            + "('value.Field'), which the store rewrite handles. Run 'uloop compile'.";
        private const string AddedFields =
            " private int _addedSecret; public int AddedVisible; private HotReloadSubpatternNameHost _addedSelf;";
        private const string SecretFieldAnchor = "private int _secret;";
        private const string AddedMethodAnchor = "// Bodies the shim assembly compiles as code of its own.";

        /// <summary>
        /// What: a lambda, switch arm or async body whose property pattern names a member the shim
        /// assembly cannot reach is skipped on its own with a reason naming that member, instead of
        /// failing the whole file. The member may be a field, a property, a property whose getter
        /// alone is private, or either end of a nested pattern.
        /// </summary>
        [TestCase("8061", "PrivateFieldPatternInLambda", ClosureReason, "_secret")]
        [TestCase("8071", "PrivatePropertyPatternInLambda", ClosureReason, "Secret")]
        [TestCase("8081", "PrivateGetterPatternInLambda", ClosureReason, "VisibleWithPrivateGetter")]
        [TestCase("8091", "PrivateNestedPatternInLambda", ClosureReason, "Self")]
        [TestCase("8121", "PrivatePatternOnThisInLambda", ClosureReason, "_secret")]
        [TestCase("8131", "PrivatePatternInSwitchArmInLambda", ClosureReason, "Secret")]
        [TestCase("8141", "PrivatePatternInAsync", AsyncReason, "Secret")]
        public async Task PatternNamingAnUnreachableMemberInShimCompiledCode_IsSkippedNamingTheMember(
            string token,
            string methodName,
            string expectedPrefix,
            string memberName)
        {
            TransformWorkerClientResult result = await RunEditAsync(token, token.Substring(0, 3) + "2");

            Assert.That(FindSkipReason(result, methodName), Is.EqualTo(expectedPrefix + PatternFragment(memberName)));
            Assert.That(FindEntry(result, methodName), Is.Null, methodName + " must not be an entry.");
        }

        /// <summary>
        /// What: an extended property pattern in a lambda is skipped on its own when either link of
        /// its member path is a member the shim assembly cannot reach, and the reason names that link.
        /// </summary>
        [TestCase("{ Self: { Visible: 3 } } ? 8101", "{ Self.Visible: 3 } ? 8102", "PrivateHeadPatternInLambda", "Self")]
        [TestCase("{ Me: { Secret: 3 } } ? 8111", "{ Me.Secret: 3 } ? 8112", "PrivateTailPatternInLambda", "Secret")]
        public async Task ExtendedPatternReachingAnUnreachableMemberInLambda_IsSkippedNamingTheMember(
            string fragment,
            string replacement,
            string methodName,
            string memberName)
        {
            TransformWorkerClientResult result = await RunEditAsync(fragment, replacement);

            Assert.That(FindSkipReason(result, methodName), Is.EqualTo(ClosureReason + PatternFragment(memberName)));
        }

        /// <summary>
        /// What: a pattern the shim can run is an entry whose shim keeps every member name as written,
        /// with no receiver, accessor call or qualification added: a private member in the method's own
        /// statements, a public one in a lambda, and the extended form of either.
        /// </summary>
        [TestCase("8011", "8012", "PrivatePatternInOwnStatements", "other is { Secret: 3 } ? 8012")]
        [TestCase("{ Me: { Visible: 3 } } ? 8021", "{ Me.Visible: 3 } ? 8022", "PublicNestedPatternInOwnStatements", "other is { Me.Visible: 3 } ? 8022")]
        [TestCase("{ Self: { Secret: 3 } } ? 8031", "{ Self.Secret: 3 } ? 8032", "PrivateNestedPatternInOwnStatements", "other is { Self.Secret: 3 } ? 8032")]
        [TestCase("8041", "8042", "PublicPatternInLambda", "other is { Visible: 3 } ? 8042")]
        [TestCase("{ Me: { Visible: 3 } } ? 8051", "{ Me.Visible: 3 } ? 8052", "PublicNestedPatternInLambda", "other is { Me.Visible: 3 } ? 8052")]
        public async Task PatternTheShimCanRun_IsAnEntryKeepingTheMemberNames(
            string fragment,
            string replacement,
            string methodName,
            string expectedShimText)
        {
            TransformWorkerClientResult result = await RunEditAsync(fragment, replacement);

            TransformWorkerEntryDto entry = FindEntry(result, methodName);
            Assert.That(entry, Is.Not.Null, methodName + " must be an entry. " + FormatSkipped(result));
            Assert.That(SliceShimMethod(result.Output.shimSource, entry), Does.Contain(expectedShimText));
        }

        /// <summary>
        /// What: a body whose property pattern names a field added in the same edit is skipped on its
        /// own with the added-field reason, whether the field is the whole name, the head of an extended
        /// pattern, or its tail, in the method's own statements or, for a public field, in a lambda.
        /// </summary>
        [TestCase("{ Secret: 3 } ? 8011", "{ _addedSecret: 0 } ? 8012", "PrivatePatternInOwnStatements")]
        [TestCase("{ Me: { Visible: 3 } } ? 8021", "{ _addedSelf.Visible: 3 } ? 8022", "PublicNestedPatternInOwnStatements")]
        [TestCase("{ Me: { Visible: 3 } } ? 8021", "{ Me._addedSecret: 0 } ? 8022", "PublicNestedPatternInOwnStatements")]
        [TestCase("{ Visible: 3 } ? 8041", "{ AddedVisible: 0 } ? 8042", "PublicPatternInLambda")]
        public async Task PatternNamingAnAddedField_IsSkippedWithTheAddedFieldReason(
            string fragment,
            string replacement,
            string methodName)
        {
            TransformWorkerClientResult result = await RunEditsAsync(
                (fragment, replacement),
                (SecretFieldAnchor, SecretFieldAnchor + AddedFields));

            Assert.That(FindSkipReason(result, methodName), Is.EqualTo(AddedFieldReason));
        }

        /// <summary>
        /// What: a lambda whose property pattern names a private field added in the same edit is
        /// skipped on its own, with the reason a private member named in a lambda's pattern gives.
        /// </summary>
        [Test]
        public async Task LambdaPatternNamingAPrivateAddedField_IsSkippedNamingTheField()
        {
            TransformWorkerClientResult result = await RunEditsAsync(
                ("{ Visible: 3 } ? 8041", "{ _addedSecret: 0 } ? 8042"),
                (SecretFieldAnchor, SecretFieldAnchor + AddedFields));

            Assert.That(FindSkipReason(result, "PublicPatternInLambda"), Is.EqualTo(ClosureReason + PatternFragment("_addedSecret")));
        }

        /// <summary>
        /// What: the same test written with a member access outside the pattern, as both skip reasons
        /// advise, is an entry: a private property read in a lambda, and an added field read in the
        /// method's own statements.
        /// </summary>
        [TestCase("other is { Secret: 3 } ? 8071", "other.Secret == 3 ? 8072", "PrivatePropertyPatternInLambda")]
        [TestCase("other is { Secret: 3 } ? 8011", "other._addedSecret == 0 ? 8012", "PrivatePatternInOwnStatements")]
        public async Task TestWrittenAsTheSkipReasonAdvises_IsAnEntry(
            string fragment,
            string replacement,
            string methodName)
        {
            TransformWorkerClientResult result = await RunEditsAsync(
                (fragment, replacement),
                (SecretFieldAnchor, SecretFieldAnchor + AddedFields));

            Assert.That(FindEntry(result, methodName), Is.Not.Null, methodName + " must be an entry. " + FormatSkipped(result));
        }

        /// <summary>
        /// What: a method added since the last compile whose property pattern names a private member
        /// is skipped with the added-method reason naming that member, because every statement of an
        /// added method runs in the shim assembly.
        /// </summary>
        [Test]
        public async Task AddedMethodWithPatternNamingAPrivateMember_IsSkippedNamingTheMember()
        {
            const string methodName = "AddedPrivatePattern";
            TransformWorkerClientResult result = await RunEditAsync(
                AddedMethodAnchor,
                AddedMethod(methodName, "{ Secret: 3 }") + AddedMethodAnchor);

            Assert.That(FindSkipReason(result, methodName), Does.StartWith(AddedMethodReason + PatternFragment("Secret")));
        }

        /// <summary>
        /// What: a method added since the last compile whose extended property pattern names only
        /// public members is an entry whose shim keeps the member path as written.
        /// </summary>
        [Test]
        public async Task AddedMethodWithPatternNamingPublicMembers_IsAnEntryKeepingTheMemberNames()
        {
            const string methodName = "AddedPublicPattern";
            TransformWorkerClientResult result = await RunEditAsync(
                AddedMethodAnchor,
                AddedMethod(methodName, "{ Me.Visible: 3 }") + AddedMethodAnchor);

            TransformWorkerEntryDto entry = FindEntry(result, methodName);
            Assert.That(entry, Is.Not.Null, methodName + " must be an entry. " + FormatSkipped(result));
            Assert.That(SliceShimMethod(result.Output.shimSource, entry), Does.Contain("other is { Me.Visible: 3 }"));
        }

        private static string PatternFragment(string memberName)
        {
            return "inaccessible member '" + memberName + "' named in a property pattern has no accessor "
                + "rewrite shape; read it with a member access such as 'value." + memberName + "' instead.";
        }

        // Why one line: an anchor the edit replaces must not hold a line break, or a checkout with
        // CRLF line endings would not find it.
        private static string AddedMethod(string methodName, string pattern)
        {
            return "public int " + methodName + "() { HotReloadSubpatternNameHost other = "
                + "new HotReloadSubpatternNameHost(3); return other is " + pattern + " ? 1 : 0; } ";
        }

        private static Task<TransformWorkerClientResult> RunEditAsync(string fragment, string replacement)
        {
            return RunEditsAsync((fragment, replacement));
        }

        private static async Task<TransformWorkerClientResult> RunEditsAsync(
            params (string Fragment, string Replacement)[] edits)
        {
            string fixturePath = Path.GetFullPath(
                Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", FixtureFileName));
            string onDisk = File.ReadAllText(fixturePath);
            string edited = onDisk;
            foreach ((string fragment, string replacement) in edits)
            {
                edited = ReplaceOnce(edited, fragment, replacement);
            }

            string editedPath = HotReloadTestSourceWriter.WriteEditedSource(
                "SubpatternName" + Math.Abs(edited.GetHashCode()) + ".cs",
                edited);
            TransformWorkerInputDto input = TransformWorkerClientTests.BuildInputForSource(
                editedPath,
                FixtureProjectRelativePath,
                snapshotSource: onDisk);
            TransformWorkerClientResult result =
                await HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(input, CancellationToken.None);
            Assert.That(result.Success, Is.True, "The worker must not fail the file: " + result.ErrorMessage);
            return result;
        }

        // Why the uniqueness check: a fragment that also matched another member would edit a method
        // the test does not look at, and the assert would pass or fail for the wrong reason.
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

        private static TransformWorkerEntryDto FindEntry(TransformWorkerClientResult result, string methodName)
        {
            foreach (TransformWorkerEntryDto entry in result.Output.entries ?? Array.Empty<TransformWorkerEntryDto>())
            {
                if (entry.methodName == methodName)
                {
                    return entry;
                }
            }

            return null;
        }

        // Why the parenthesis: a bare method name would also match a longer name that contains it.
        private static string FindSkipReason(TransformWorkerClientResult result, string methodName)
        {
            foreach (TransformWorkerSkippedDto skipped in result.Output.skipped ?? Array.Empty<TransformWorkerSkippedDto>())
            {
                if (skipped.method != null && skipped.method.Contains("." + methodName + "("))
                {
                    return HotReloadWorkerReasonText.Render(skipped.reason);
                }
            }

            return null;
        }

        private static string SliceShimMethod(string shimSource, TransformWorkerEntryDto entry)
        {
            int start = shimSource.IndexOf(entry.shimMethodName + "(", StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0), "Shim method missing: " + entry.shimMethodName);
            int end = shimSource.IndexOf("#line default", start, StringComparison.Ordinal);
            return end < 0 ? shimSource.Substring(start) : shimSource.Substring(start, end - start);
        }

        private static string FormatSkipped(TransformWorkerClientResult result)
        {
            List<string> lines = new List<string>();
            foreach (TransformWorkerSkippedDto skipped in result.Output.skipped ?? Array.Empty<TransformWorkerSkippedDto>())
            {
                lines.Add(skipped.method + ": " + HotReloadWorkerReasonText.Render(skipped.reason));
            }

            return "Skipped: " + string.Join(" | ", lines);
        }
    }
}
