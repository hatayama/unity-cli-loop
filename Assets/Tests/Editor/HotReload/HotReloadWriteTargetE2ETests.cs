using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// End-to-end coverage of writes whose target an assignment reaches through parentheses or a
    /// deconstruction. The accessor and added-member rewrites have to treat such a target as
    /// written, never read, and have to write the instance the target names.
    /// </summary>
    /// <remarks>
    /// Why the compiled-host edits that use accessors write inside a closure: a plain synchronous
    /// body is transplanted without the accessor rewrite, so only a closure, an async or iterator
    /// body, or an added method reaches the paths under test. The edits that use added members
    /// need no closure, because the added-member rewrite runs on a transplanted body too.
    /// </remarks>
    public sealed class HotReloadWriteTargetE2ETests : HotReloadIntroducedTypeCallerE2ETestBase
    {
        private const string OwnerPath = "Assets/Tests/Editor/HotReload/UncompiledHiddenGetterOwner.cs";
        private const string HostValueAnchor = "        public int Value()";
        private const string WriteTargetMembersAnchor = "        public int HiddenValue => Hidden;\n";
        private const string CopyIntoBodyAnchor =
            "            other._stored = 0;\n"
            + "            other.Tally = 0;\n"
            + "            other.Hidden = 0;\n";
        private const string CopyIntoMethod = "HotReloadWriteTargetHost.CopyInto";

        // Why NoInlining on the bodies a later reload edits: the test reads the edited body back
        // through a direct call, which an inlined copy at the call site would not observe.
        private const string NoInlining =
            "[System.Runtime.CompilerServices.MethodImpl("
            + "System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] ";

        /// <summary>
        /// Verifies that added methods writing a property of an introduced type whose getter is
        /// internal, through parentheses or through a deconstruction, alone or beside a read of
        /// the host's private field, are added and write the value on the reload that introduces
        /// the type, on one that edits it, and on one that leaves it alone.
        /// </summary>
        // Why the cases beside a private field read: that read alone sends the added methods to the
        // accessor rewrite, so the writes beside it reach the accessor plan and the rewrite, while
        // alone they reach only the scan that decides whether the rewrite runs.
        [TestCase("public", "")]
        [TestCase("internal", "")]
        [TestCase("public", " + _stored")]
        [TestCase("internal", " + _stored")]
        public async Task Run_AddedMethodsWritingAHiddenGetterThroughParenthesesOrDeconstruction_AreApplied(
            string access,
            string privateFieldRead)
        {
            const string callerExpression = "host.AddedWritesParenthesized() * 100 + host.AddedWritesByDeconstruction()";
            string addedMethods = AddedMethodsWritingTheHiddenGetter(privateFieldRead);
            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                HotReloadOrchestratorResult introducing = await RunAsync(
                    "HiddenFirst",
                    callerExpression,
                    WithHostAdditions(Owner(OwnerPath, HiddenGetterTarget(access, 1)), addedMethods));
                AssertIntroduced(introducing, "HiddenGetterTarget");
                AssertHiddenGetterWritersApplied(introducing);

                HotReloadOrchestratorResult edited = await RunAsync(
                    "HiddenSecond",
                    callerExpression,
                    WithHostAdditions(Owner(OwnerPath, HiddenGetterTarget(access, 2)), addedMethods));
                AssertOutcome(edited, HotReloadMethodOutcomeKind.Patched, Namespace + ".HiddenGetterTarget.Read");
                AssertHiddenGetterWritersApplied(edited);

                HotReloadOrchestratorResult hostOnly = await RunAsync(
                    "HiddenThird",
                    callerExpression,
                    WithHostAdditions(new Dictionary<string, string>(), addedMethods));
                AssertHiddenGetterWritersApplied(hostOnly);
            });
        }

        /// <summary>
        /// Verifies that a closure writing through parentheses a property with a private getter
        /// and a public setter is patched and sets it on the instance it names.
        /// </summary>
        [Test]
        public async Task Run_ClosureWritingAPrivateGetterPropertyThroughParentheses_WritesThatInstance()
        {
            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                HotReloadOrchestratorResult result = await RunCopyIntoAsync(
                    "ParenthesizedPrivateGetter",
                    string.Empty,
                    InClosure("(other.Hidden) = 3;"));
                AssertCopyIntoWrites(result, 0, 0, 3);
            });
        }

        /// <summary>
        /// Verifies that a closure that already takes the accessor rewrite, writing through
        /// parentheses its own property with a private getter and a public setter, is patched and
        /// sets that property on the instance running it.
        /// </summary>
        [Test]
        public async Task Run_ClosureWritingItsOwnPrivateGetterPropertyThroughParentheses_SetsIt()
        {
            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                // Why the private setter write beside it: it alone sends the closure to the
                // accessor rewrite, which is where a bare name could be rewritten into a read.
                HotReloadOrchestratorResult result = await RunCopyIntoAsync(
                    "ParenthesizedOwnPrivateGetter",
                    string.Empty,
                    InClosure("other.Tally = 0; (Hidden) = 3;"));
                AssertOutcome(result, HotReloadMethodOutcomeKind.Patched, CopyIntoMethod);
                HotReloadWriteTargetHost writer = new HotReloadWriteTargetHost();
                HotReloadWriteTargetHost target = new HotReloadWriteTargetHost();
                writer.CopyInto(target);
                AssertWritten(writer, 0, 0, 3, result);
                AssertWritten(target, 0, 0, 0, result);
            });
        }

        /// <summary>
        /// Verifies that a closure writing another instance through its indexer with a private
        /// getter and a public setter, through parentheses or a deconstruction, is patched and
        /// writes that instance.
        /// </summary>
        // Why beside a private setter write: it alone sends the closure to the accessor plan, which
        // rejects every indexer use it counts as a read.
        [TestCase("Parenthesized", "(other[2]) = 3;")]
        [TestCase("Deconstructed", "int ignored; (other[2], ignored) = (3, 1);")]
        public async Task Run_ClosureWritingThroughAPrivateGetterIndexer_WritesThatInstance(
            string label,
            string statement)
        {
            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                HotReloadOrchestratorResult result = await RunCopyIntoAsync(
                    "Indexer" + label,
                    string.Empty,
                    InClosure("other.Tally = 0; " + statement));
                AssertCopyIntoWrites(result, 5, 0, 0);
            });
        }

        /// <summary>
        /// Verifies that a closure deconstructing into another instance's private field, or into
        /// its property with a private getter and a public setter, is patched and writes that
        /// instance rather than the one running the method.
        /// </summary>
        [TestCase("Field", "(other._stored, ignored) = (5, 1);", 5, 0)]
        [TestCase("PrivateGetter", "(other.Hidden, ignored) = (3, 1);", 0, 3)]
        public async Task Run_ClosureDeconstructingIntoAnotherInstance_WritesThatInstance(
            string label,
            string statement,
            int stored,
            int hidden)
        {
            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                HotReloadOrchestratorResult result = await RunCopyIntoAsync(
                    "Deconstructed" + label,
                    string.Empty,
                    InClosure("int ignored; " + statement));
                AssertCopyIntoWrites(result, stored, 0, hidden);
            });
        }

        /// <summary>
        /// Verifies that a closure deconstructing into a property whose setter only the declaring
        /// type may call, alone or beside a private field write, is skipped with a reason naming
        /// the deconstruction, and the compiled body keeps running.
        /// </summary>
        // Why both cases: the field write alone sends the closure to the accessor rewrite, so
        // beside it the deconstruction reaches the accessor plan even when the scan misses it,
        // while alone it reaches the plan only when the scan counts it.
        [TestCase("Alone", "int ignored; (other.Tally, ignored) = (7, 1);")]
        [TestCase("BesideAFieldWrite", "int ignored; other._stored = 1; (other.Tally, ignored) = (7, 1);")]
        public async Task Run_ClosureDeconstructingThroughAPrivateSetter_IsSkippedAndKeepsTheCompiledBody(
            string label,
            string statements)
        {
            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                HotReloadOrchestratorResult result = await RunCopyIntoAsync(
                    "DeconstructedPrivateSetter" + label,
                    string.Empty,
                    InClosure(statements));
                AssertOutcome(result, HotReloadMethodOutcomeKind.Skipped, CopyIntoMethod);
                AssertReasonContains(result, CopyIntoMethod, "deconstruction");
                AssertCompiledCopyIntoRuns(result);
            });
        }

        /// <summary>
        /// Verifies that a closure writing another instance's private field, or its property with
        /// a private setter, through parentheses is patched and writes that instance rather than
        /// the one running the method.
        /// </summary>
        [TestCase("Field", "(other._stored) = 5;", 5, 0)]
        [TestCase("PrivateSetter", "(other.Tally) = 7;", 0, 7)]
        public async Task Run_ClosureWritingAnotherInstanceThroughParentheses_WritesThatInstance(
            string label,
            string statement,
            int stored,
            int tally)
        {
            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                HotReloadOrchestratorResult result = await RunCopyIntoAsync(
                    "Parenthesized" + label,
                    string.Empty,
                    InClosure(statement));
                AssertCopyIntoWrites(result, stored, tally, 0);
            });
        }

        /// <summary>
        /// Verifies that a closure compound-assigning through parentheses a property with a
        /// private setter, on a receiver that calls a method, is skipped rather than rewritten to
        /// call the method twice, and the compiled body keeps running.
        /// </summary>
        [Test]
        public async Task Run_ClosureCompoundWritingThroughParenthesesOnACallReceiver_IsSkipped()
        {
            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                HotReloadOrchestratorResult result = await RunCopyIntoAsync(
                    "ParenthesizedCallReceiver",
                    string.Empty,
                    "HotReloadWriteTargetHost Pick() { return other; }\n"
                    + "            " + InClosure("(Pick().Tally) += 7;"));
                AssertOutcome(result, HotReloadMethodOutcomeKind.Skipped, CopyIntoMethod);
                AssertReasonContains(result, CopyIntoMethod, "evaluated twice");
                AssertCompiledCopyIntoRuns(result);
            });
        }

        /// <summary>
        /// Verifies that a body writing another instance's added field or added auto-property
        /// through parentheses stores the value on that instance rather than on the one running
        /// the method.
        /// </summary>
        [TestCase("Field", "public int Added;")]
        [TestCase("AutoProperty", "public int Added { get; set; }")]
        public async Task Run_BodyWritingAnAddedMemberOfAnotherInstanceThroughParentheses_WritesThatInstance(
            string label,
            string declaration)
        {
            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                HotReloadOrchestratorResult result = await RunCopyIntoAsync(
                    "ParenthesizedAdded" + label,
                    "\n        " + declaration + "\n",
                    "(other.Added) = 5;\n"
                    + "            other.Tally = other.Added;\n"
                    + "            Tally = Added;");
                AssertCopyIntoWrites(result, 0, 5, 0);
            });
        }

        // Added host methods that write a property whose getter the host cannot call, one through
        // parentheses and one through a deconstruction. Neither calls the getter. The private
        // field read reads zero on the fresh host the caller is handed, so it leaves the values.
        private static string AddedMethodsWritingTheHiddenGetter(string privateFieldRead)
        {
            return "        public int AddedWritesParenthesized()\n"
                + "        {\n"
                + "            HiddenGetterTarget target = new HiddenGetterTarget();\n"
                + "            (target.Stored) = 5;\n"
                + "            return target.Report()" + privateFieldRead + ";\n"
                + "        }\n"
                + "\n"
                + "        public int AddedWritesByDeconstruction()\n"
                + "        {\n"
                + "            HiddenGetterTarget target = new HiddenGetterTarget();\n"
                + "            int other;\n"
                + "            (target.Stored, other) = (7, 1);\n"
                + "            return target.Report() + other" + privateFieldRead + ";\n"
                + "        }\n"
                + "\n";
        }

        private static string HiddenGetterTarget(string access, int value)
        {
            return access + " sealed class HiddenGetterTarget\n"
                + "    {\n"
                + "        public int Stored { internal get; set; }\n"
                + "\n"
                + "        " + NoInlining + "public int Report() { return Stored; }\n"
                + "\n"
                + "        " + NoInlining + "public int Read() { return " + value.ToString() + "; }\n"
                + "    }";
        }

        private static string InClosure(string statements)
        {
            return "System.Action write = () => { " + statements + " };\n"
                + "            write();";
        }

        // Reloads the compiled write-target host with the members added to it and CopyInto's body
        // replaced by the statements.
        private static Task<HotReloadOrchestratorResult> RunCopyIntoAsync(
            string label,
            string members,
            string copyIntoStatements)
        {
            string hostPath = FixturePath("HotReloadWriteTargetHost.cs");
            string hostSource = File.ReadAllText(hostPath);
            Assert.That(hostSource, Does.Contain(WriteTargetMembersAnchor), "Precondition: members anchor must exist.");
            Assert.That(hostSource, Does.Contain(CopyIntoBodyAnchor), "Precondition: CopyInto body anchor must exist.");
            string edited = hostSource
                .Replace(WriteTargetMembersAnchor, WriteTargetMembersAnchor + members, StringComparison.Ordinal)
                .Replace(CopyIntoBodyAnchor, "            " + copyIntoStatements + "\n", StringComparison.Ordinal);
            return RunAsync(label, "host.Value()", new Dictionary<string, string> { [hostPath] = edited });
        }

        // Adds the members to a copy of the compiled public host, which every run of a test names
        // again so the additions are judged on each of them.
        private static Dictionary<string, string> WithHostAdditions(Dictionary<string, string> sources, string members)
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");
            string hostSource = File.ReadAllText(hostPath);
            Assert.That(hostSource, Does.Contain(HostValueAnchor), "Precondition: host value anchor must exist.");
            sources[hostPath] = hostSource.Replace(HostValueAnchor, members + HostValueAnchor, StringComparison.Ordinal);
            return sources;
        }

        // Both added writers are added, and each returns the value it wrote: 5 through the
        // parentheses, and 7 plus the 1 deconstructed beside it.
        private static void AssertHiddenGetterWritersApplied(HotReloadOrchestratorResult result)
        {
            AssertOutcome(result, HotReloadMethodOutcomeKind.Added, "AddedWritesParenthesized");
            AssertOutcome(result, HotReloadMethodOutcomeKind.Added, "AddedWritesByDeconstruction");
            Assert.That(CallTheCaller(), Is.EqualTo(5 * 100 + 7 + 1), DescribeOutcomes(result));
        }

        // CopyInto is patched and writes the values to the instance passed in, leaving the one
        // running it untouched.
        private static void AssertCopyIntoWrites(HotReloadOrchestratorResult result, int stored, int tally, int hidden)
        {
            AssertOutcome(result, HotReloadMethodOutcomeKind.Patched, CopyIntoMethod);
            HotReloadWriteTargetHost writer = new HotReloadWriteTargetHost();
            HotReloadWriteTargetHost target = new HotReloadWriteTargetHost();
            writer.CopyInto(target);
            AssertWritten(target, stored, tally, hidden, result);
            AssertWritten(writer, 0, 0, 0, result);
        }

        // The compiled CopyInto resets the instance passed in, which only it does.
        private static void AssertCompiledCopyIntoRuns(HotReloadOrchestratorResult result)
        {
            HotReloadWriteTargetHost writer = new HotReloadWriteTargetHost();
            HotReloadWriteTargetHost target = new HotReloadWriteTargetHost();
            target.Hidden = 9;
            writer.CopyInto(target);
            AssertWritten(target, 0, 0, 0, result);
        }

        private static void AssertWritten(
            HotReloadWriteTargetHost instance,
            int stored,
            int tally,
            int hidden,
            HotReloadOrchestratorResult result)
        {
            string description = DescribeOutcomes(result);
            Assert.That(instance.Stored, Is.EqualTo(stored), "_stored\n" + description);
            Assert.That(instance.Tally, Is.EqualTo(tally), "Tally\n" + description);
            Assert.That(instance.HiddenValue, Is.EqualTo(hidden), "Hidden\n" + description);
        }

        private static void AssertOutcome(
            HotReloadOrchestratorResult result,
            HotReloadMethodOutcomeKind kind,
            string methodFragment)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == kind
                    && outcome.Method != null
                    && outcome.Method.Contains(methodFragment, StringComparison.Ordinal))
                {
                    return;
                }
            }

            Assert.Fail("No " + kind + " row mentions " + methodFragment + ".\n" + DescribeOutcomes(result));
        }

        private static void AssertReasonContains(
            HotReloadOrchestratorResult result,
            string methodFragment,
            string reasonFragment)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Method != null && outcome.Method.Contains(methodFragment, StringComparison.Ordinal))
                {
                    Assert.That(outcome.Reason, Does.Contain(reasonFragment), DescribeOutcomes(result));
                    return;
                }
            }

            Assert.Fail("No row mentions " + methodFragment + ".\n" + DescribeOutcomes(result));
        }
    }
}
