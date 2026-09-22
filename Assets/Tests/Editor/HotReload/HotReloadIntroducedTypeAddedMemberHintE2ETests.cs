using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// End-to-end coverage of a new type whose body calls a member hot reload adds, in the same
    /// reload or an earlier one, to a compiled type or to a type an earlier reload introduced. The
    /// introduced-type compilation cannot see such a member, and the failure has to say so.
    /// </summary>
    /// <remarks>
    /// Why the owners are not fixtures on disk: a .cs under Assets/ is compiled into the test
    /// assembly, and a type the compiler already lists is never introduced.
    /// </remarks>
    public class HotReloadIntroducedTypeAddedMemberHintE2ETests : HotReloadIntroducedTypeE2ETestBase
    {
        private const string ValueOwnerPath =
            "Assets/Tests/Editor/HotReload/UncompiledAddedMemberHintValueOwner.cs";

        private const string UserOwnerPath =
            "Assets/Tests/Editor/HotReload/UncompiledAddedMemberHintUserOwner.cs";

        private const string Namespace = "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload";
        private const string ValueSimpleName = "HotReloadAddedMemberHintValue";
        private const string UserSimpleName = "HotReloadAddedMemberHintUser";
        private const string HostValueAnchor = "        public int Value()";
        private const string CompiledTypeAddedMethodName = "AddedInTheSameReload";
        private const string IntroducedTypeAddedMethodName = "Pong";
        private const string NoExtraMembers = "";

        // The part of the hint that holds whether the addition came from this reload or an
        // earlier one, so each case below checks the same sentence.
        private const string HintCore =
            "share a name with a hot reload addition, from this reload or an earlier one";

        private static readonly string CompiledTypeAddedMember =
            "        public int " + CompiledTypeAddedMethodName + "()\n"
            + "        {\n"
            + "            return 41;\n"
            + "        }\n"
            + "\n";

        private static readonly string IntroducedTypeAddedMember =
            "\n"
            + "        public int " + IntroducedTypeAddedMethodName + "()\n"
            + "        {\n"
            + "            return 9;\n"
            + "        }\n";

        /// <summary>
        /// What: a new type calling a method the same reload adds to a compiled type fails to
        /// compile, and the failure carries the hint that the introduced type cannot see a hot
        /// reload addition, not only the bare CS1061.
        /// </summary>
        [Test]
        public async Task Run_NewTypeCallsAMemberTheSameReloadAddsToACompiledType_FailsWithTheHint()
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");

            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                HotReloadOrchestratorResult result = await RunAsync(
                    new Dictionary<string, string>
                    {
                        [hostPath] = InsertCompiledTypeMember(File.ReadAllText(hostPath)),
                        [UserOwnerPath] = BuildUserSource(
                            "new HotReloadCrossFileAddedMemberHost()." + CompiledTypeAddedMethodName + "()")
                    },
                    "CompiledSameReload");

                AssertFailsWithHint(result);
            });
        }

        /// <summary>
        /// What: a new type calling a method the same reload adds to a type an earlier reload
        /// introduced fails to compile with the same hint.
        /// </summary>
        [Test]
        public async Task Run_NewTypeCallsAMemberTheSameReloadAddsToAnIntroducedType_FailsWithTheHint()
        {
            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                HotReloadOrchestratorResult introducing = await RunAsync(
                    new Dictionary<string, string> { [ValueOwnerPath] = BuildValueSource(NoExtraMembers) },
                    "IntroducedSameReloadIntroducing");
                Assert.That(CountFailures(introducing), Is.EqualTo(0), DescribeOutcomes(introducing));

                HotReloadOrchestratorResult result = await RunAsync(
                    new Dictionary<string, string>
                    {
                        [ValueOwnerPath] = BuildValueSource(IntroducedTypeAddedMember),
                        [UserOwnerPath] = BuildUserSource(
                            "new " + ValueSimpleName + "()." + IntroducedTypeAddedMethodName + "()")
                    },
                    "IntroducedSameReload");

                AssertFailsWithHint(result);
            });
        }

        /// <summary>
        /// What: splitting the edit does not help. Once an earlier reload has added the method to
        /// an introduced type, a later reload introducing a new type that calls it still fails to
        /// compile, with the same hint.
        /// </summary>
        [Test]
        public async Task Run_NewTypeCallsAMemberAnEarlierReloadAddedToAnIntroducedType_FailsWithTheHint()
        {
            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                HotReloadOrchestratorResult introducing = await RunAsync(
                    new Dictionary<string, string> { [ValueOwnerPath] = BuildValueSource(NoExtraMembers) },
                    "IntroducedSplitIntroducing");
                Assert.That(CountFailures(introducing), Is.EqualTo(0), DescribeOutcomes(introducing));

                HotReloadOrchestratorResult addition = await RunAsync(
                    new Dictionary<string, string> { [ValueOwnerPath] = BuildValueSource(IntroducedTypeAddedMember) },
                    "IntroducedSplitAddition");
                Assert.That(CountFailures(addition), Is.EqualTo(0), DescribeOutcomes(addition));

                HotReloadOrchestratorResult result = await RunAsync(
                    new Dictionary<string, string>
                    {
                        [ValueOwnerPath] = BuildValueSource(IntroducedTypeAddedMember),
                        [UserOwnerPath] = BuildUserSource(
                            "new " + ValueSimpleName + "()." + IntroducedTypeAddedMethodName + "()")
                    },
                    "IntroducedSplit");

                AssertFailsWithHint(result);
            });
        }

        private static void AssertFailsWithHint(HotReloadOrchestratorResult result)
        {
            string reason = FindIntroducedTypeFailureReason(result);
            Assert.That(reason, Does.Contain("CS1061"), DescribeOutcomes(result));
            Assert.That(reason, Does.Contain(HintCore), DescribeOutcomes(result));
        }

        private static string FindIntroducedTypeFailureReason(HotReloadOrchestratorResult result)
        {
            foreach (HotReloadIntroducedTypeOutcome outcome in result.IntroducedTypes)
            {
                if (outcome.Kind == HotReloadIntroducedTypeOutcomeKind.Failed
                    && outcome.Reason.Contains("CS1061", StringComparison.Ordinal))
                {
                    return outcome.Reason;
                }
            }

            Assert.Fail("No introduced type failed on a missing member.\n" + DescribeOutcomes(result));
            return null;
        }

        private static Task<HotReloadOrchestratorResult> RunAsync(
            Dictionary<string, string> sources,
            string label)
        {
            List<string> paths = new List<string>();
            Dictionary<string, string> edits = new Dictionary<string, string>();
            foreach (KeyValuePair<string, string> source in sources)
            {
                paths.Add(source.Key);
                edits[source.Key] = HotReloadTestSourceWriter.WriteEditedSource(
                    Path.GetFileNameWithoutExtension(source.Key) + label + ".cs",
                    source.Value);
            }

            return HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                paths.ToArray(),
                contentPathOverride: null,
                CancellationToken.None,
                edits);
        }

        private static string InsertCompiledTypeMember(string hostSource)
        {
            Assert.That(hostSource, Does.Contain(HostValueAnchor), "Precondition: host value anchor must exist.");
            return hostSource.Replace(
                HostValueAnchor,
                CompiledTypeAddedMember + HostValueAnchor,
                StringComparison.Ordinal);
        }

        private static string BuildValueSource(string extraMembers)
        {
            return
                "namespace " + Namespace + "\n"
                + "{\n"
                + "    public sealed class " + ValueSimpleName + "\n"
                + "    {\n"
                + "        public int Ping()\n"
                + "        {\n"
                + "            return 4;\n"
                + "        }\n"
                + extraMembers
                + "    }\n"
                + "}\n";
        }

        private static string BuildUserSource(string expression)
        {
            return
                "namespace " + Namespace + "\n"
                + "{\n"
                + "    public sealed class " + UserSimpleName + "\n"
                + "    {\n"
                + "        public int Run()\n"
                + "        {\n"
                + "            return " + expression + ";\n"
                + "        }\n"
                + "    }\n"
                + "}\n";
        }
    }
}
