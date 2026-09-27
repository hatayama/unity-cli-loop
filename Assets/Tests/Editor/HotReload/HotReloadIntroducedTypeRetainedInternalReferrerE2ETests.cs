using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// End-to-end coverage of code that names an internal or modifier-less introduced type while a
    /// later reload edits a body of that type. That reload keeps the declaration in the worker to
    /// transform it, and whatever names the type must be judged against the public artifact the
    /// domain runs, exactly as on a reload that leaves the declaration alone.
    /// </summary>
    public sealed class HotReloadIntroducedTypeRetainedInternalReferrerE2ETests : HotReloadIntroducedTypeCallerE2ETestBase
    {
        private const string HostValueAnchor = "        public int Value()";
        private const string HostTypeAnchor = "    public sealed class HotReloadCrossFileAddedMemberHost";

        // Why NoInlining on the bodies a later reload edits: the test reads the edited body back
        // through a direct call, which an inlined copy at the call site would not observe.
        private const string NoInlining =
            "[System.Runtime.CompilerServices.MethodImpl("
            + "System.Runtime.CompilerServices.MethodImplOptions.NoInlining)] ";

        // A compiled type's added auto-property and the added method that writes and reads it.
        private static readonly string AddedPropertyOnTheHost =
            "        public int AddedCount { get; set; }\n"
            + "\n"
            + "        public int AddedUsesCount()\n"
            + "        {\n"
            + "            AddedCount = 4;\n"
            + "            return AddedCount;\n"
            + "        }\n"
            + "\n";

        /// <summary>
        /// Verifies that a compiled type sharing a file with a retained internal introduced type
        /// gets its added auto-property applied, whether the reload leaves that declaration
        /// unchanged or edits a body of it: the worker binds the file from a rewritten copy in
        /// both cases, and the property accessors must still be emitted from that copy.
        /// </summary>
        [TestCase("unchanged")]
        [TestCase("bodyEdit")]
        public async Task Run_CompiledNeighbourOfRetainedType_GetsItsAddedPropertyApplied(string edit)
        {
            int readValue = edit == "bodyEdit" ? 2 : 1;
            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                HotReloadOrchestratorResult first = await RunAsync(
                    "NeighbourFirst" + edit,
                    "new RetainedNeighbour().Read()",
                    HostWithNeighbour(1, string.Empty));
                AssertIntroduced(first, "RetainedNeighbour");
                Assert.That(CallTheCaller(), Is.EqualTo(1), DescribeOutcomes(first));

                HotReloadOrchestratorResult second = await RunAsync(
                    "NeighbourSecond" + edit,
                    "new RetainedNeighbour().Read() + host.AddedUsesCount()",
                    HostWithNeighbour(readValue, AddedPropertyOnTheHost));

                AssertAppliedWithoutSkips(second);
                AssertOutcome(second, HotReloadMethodOutcomeKind.Added, "get_AddedCount");
                AssertOutcome(second, HotReloadMethodOutcomeKind.Added, "set_AddedCount");
                AssertOutcome(second, HotReloadMethodOutcomeKind.Added, "AddedUsesCount");
                Assert.That(CallTheCaller(), Is.EqualTo(readValue + 4), DescribeOutcomes(second));
            });
        }

        // The compiled host file with an internal introduced type declared above the host class,
        // and the given members added to the host.
        private static Dictionary<string, string> HostWithNeighbour(int readValue, string hostMembers)
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");
            string hostSource = File.ReadAllText(hostPath);
            Assert.That(hostSource, Does.Contain(HostTypeAnchor), "Precondition: host type anchor must exist.");
            Assert.That(hostSource, Does.Contain(HostValueAnchor), "Precondition: host value anchor must exist.");
            string neighbour = "    internal sealed class RetainedNeighbour { "
                + NoInlining + "public int Read() { return " + readValue.ToString() + "; } }\n\n";
            return new Dictionary<string, string>
            {
                [hostPath] = hostSource
                    .Replace(HostTypeAnchor, neighbour + HostTypeAnchor, StringComparison.Ordinal)
                    .Replace(HostValueAnchor, hostMembers + HostValueAnchor, StringComparison.Ordinal)
            };
        }

        private static void AssertAppliedWithoutSkips(HotReloadOrchestratorResult result)
        {
            string description = DescribeOutcomes(result);
            Assert.That(CountFailures(result), Is.Zero, description);
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                Assert.That(outcome.Kind, Is.Not.EqualTo(HotReloadMethodOutcomeKind.Skipped), description);
            }
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
    }
}
