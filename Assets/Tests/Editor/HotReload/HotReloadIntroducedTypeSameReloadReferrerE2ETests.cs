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
    /// End-to-end coverage of a reload that changes a type an earlier reload introduced while it
    /// also introduces, from a new file, a type naming it in a signature. The new type is compiled
    /// against the loaded definition, so the change would split the changed type between the two.
    /// </summary>
    /// <remarks>
    /// Why the owners are not fixtures on disk: a .cs under Assets/ is compiled into the test
    /// assembly, and a type the compiler already lists is never introduced.
    /// </remarks>
    public class HotReloadIntroducedTypeSameReloadReferrerE2ETests : HotReloadIntroducedTypeE2ETestBase
    {
        private const string ValueOwnerPath =
            "Assets/Tests/Editor/HotReload/UncompiledSameReloadValueOwner.cs";

        private const string FactoryOwnerPath =
            "Assets/Tests/Editor/HotReload/UncompiledSameReloadFactoryOwner.cs";

        private const string KeeperOwnerPath =
            "Assets/Tests/Editor/HotReload/UncompiledSameReloadKeeperOwner.cs";

        private const string ValueSimpleName = "HotReloadSameReloadValue";
        private const string FactorySimpleName = "HotReloadSameReloadFactory";
        private const string Namespace = "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload";
        private const string ValueMetadataName = Namespace + "." + ValueSimpleName;
        private const string FactoryMetadataName = Namespace + "." + FactorySimpleName;
        private const string KeeperSimpleName = "HotReloadSameReloadKeeper";
        private const string KeeperMetadataName = Namespace + "." + KeeperSimpleName;
        private const string CallerBodyAnchor = "return host.Value();";
        private const int PingValue = 4;
        private const int PongValue = 9;
        private const int HostValue = 1;
        private const string NoExtraMembers = "";

        private static readonly string PongMember =
            "\n"
            + "        [System.Runtime.CompilerServices.MethodImpl(\n"
            + "            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]\n"
            + "        public int Pong()\n"
            + "        {\n"
            + "            return " + PongValue.ToString() + ";\n"
            + "        }\n";

        /// <summary>
        /// What: a reload that adds a method to an introduced type and introduces a new type that
        /// returns it is refused, and the refusal names the new type as introduced by this same
        /// reload instead of calling it a type an earlier reload retained and this edit leaves
        /// unchanged.
        /// </summary>
        [Test]
        public async Task Run_MemberAddedToIntroducedTypeWhileANewTypeReturningItIsIntroduced_RefusesNamingTheNewTypeAsIntroducedHere()
        {
            string callerPath = FixturePath("HotReloadCrossFileAddedMemberCaller.cs");

            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                HotReloadOrchestratorResult introducing = await RunAsync(
                    callerPath,
                    Owners(BuildValueSource(NoExtraMembers), null),
                    "new " + ValueSimpleName + "().Ping()",
                    "Introducing");
                Assert.That(CountFailures(introducing), Is.EqualTo(0), DescribeOutcomes(introducing));

                HotReloadOrchestratorResult combined = await RunAsync(
                    callerPath,
                    Owners(BuildValueSource(PongMember), BuildFactorySource(DefaultMakeBody)),
                    "new " + FactorySimpleName + "().Make().Pong()",
                    "Combined");

                string description = DescribeOutcomes(combined);
                Assert.That(CountFailures(combined), Is.GreaterThan(0), description);
                Assert.That(description, Does.Contain("'" + ValueMetadataName + "'"), description);
                Assert.That(description, Does.Contain("'" + FactoryMetadataName + "'"), description);
                Assert.That(description, Does.Not.Contain("an earlier reload retained"), description);
                Assert.That(description, Does.Contain("introduced by this reload"), description);
                Assert.That(CallTheCaller(), Is.EqualTo(PingValue + HostValue), "A refused run must leave the previous patch in place.");
            });
        }

        /// <summary>
        /// What: the two-step order the refusal recommends works. Introducing the new type first,
        /// with the introduced type unchanged, and then adding the method together with a method
        /// body edit of the new type lets the caller call the added method on a value the new type
        /// made and on one made from the edited source.
        /// </summary>
        [Test]
        public async Task Run_NewTypeIntroducedFirstThenMemberAddedWithItsBodyEdited_CallerReachesTheAddedMember()
        {
            string callerPath = FixturePath("HotReloadCrossFileAddedMemberCaller.cs");

            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                HotReloadOrchestratorResult introducing = await RunAsync(
                    callerPath,
                    Owners(BuildValueSource(NoExtraMembers), null),
                    "new " + ValueSimpleName + "().Ping()",
                    "StepIntroducing");
                Assert.That(CountFailures(introducing), Is.EqualTo(0), DescribeOutcomes(introducing));

                HotReloadOrchestratorResult factoryFirst = await RunAsync(
                    callerPath,
                    Owners(BuildValueSource(NoExtraMembers), BuildFactorySource(DefaultMakeBody)),
                    "new " + FactorySimpleName + "().Make().Ping()",
                    "StepFactory");
                Assert.That(CountFailures(factoryFirst), Is.EqualTo(0), DescribeOutcomes(factoryFirst));
                Assert.That(CallTheCaller(), Is.EqualTo(PingValue + HostValue), DescribeOutcomes(factoryFirst));

                HotReloadOrchestratorResult memberAdded = await RunAsync(
                    callerPath,
                    Owners(BuildValueSource(PongMember), BuildFactorySource(EditedMakeBody)),
                    "new " + FactorySimpleName + "().Make().Pong() + new " + ValueSimpleName + "().Pong()",
                    "StepMemberAdded");

                Assert.That(CountFailures(memberAdded), Is.EqualTo(0), DescribeOutcomes(memberAdded));
                Assert.That(
                    CallTheCaller(),
                    Is.EqualTo(PongValue + PongValue + HostValue),
                    "The added method must run on a value the new type made and on one the caller made.\n"
                    + DescribeOutcomes(memberAdded));
            });
        }

        /// <summary>
        /// What: when a type an earlier reload retained also names the changed type, the refusal
        /// names only that retained type with the same-reload edit advice, because that is the
        /// referrer an edit in this reload can fix; the type this reload introduces is not listed
        /// as retained.
        /// </summary>
        [Test]
        public async Task Run_MemberAddedWhileARetainedAndANewTypeBothReturnIt_RefusesNamingOnlyTheRetainedType()
        {
            string callerPath = FixturePath("HotReloadCrossFileAddedMemberCaller.cs");

            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                Dictionary<string, string> introducingOwners = Owners(BuildValueSource(NoExtraMembers), null);
                introducingOwners[KeeperOwnerPath] = BuildReturningTypeSource(KeeperSimpleName, DefaultMakeBody);
                HotReloadOrchestratorResult introducing = await RunAsync(
                    callerPath,
                    introducingOwners,
                    "new " + KeeperSimpleName + "().Make().Ping()",
                    "MixedIntroducing");
                Assert.That(CountFailures(introducing), Is.EqualTo(0), DescribeOutcomes(introducing));

                HotReloadOrchestratorResult combined = await RunAsync(
                    callerPath,
                    Owners(BuildValueSource(PongMember), BuildFactorySource(DefaultMakeBody)),
                    "new " + FactorySimpleName + "().Make().Pong()",
                    "MixedCombined");

                string description = DescribeOutcomes(combined);
                Assert.That(CountFailures(combined), Is.GreaterThan(0), description);
                Assert.That(
                    description,
                    Does.Contain("appears in member signatures of '" + KeeperMetadataName
                        + "', which an earlier reload retained"),
                    description);
                Assert.That(description, Does.Not.Contain("'" + FactoryMetadataName + "'"), description);
                Assert.That(description, Does.Not.Contain("introduced by this reload"), description);
            });
        }

        private const string DefaultMakeBody = "            return new " + ValueSimpleName + "();\n";

        private const string EditedMakeBody =
            "            " + ValueSimpleName + " made = new " + ValueSimpleName + "();\n"
            + "            return made;\n";

        // Only the owners named in the map take part in the run, which is how a reload before a
        // type exists, or one that leaves its file out, looks.
        private static Task<HotReloadOrchestratorResult> RunAsync(
            string callerPath,
            Dictionary<string, string> ownerSources,
            string callerExpression,
            string label)
        {
            List<string> paths = new List<string> { callerPath };
            Dictionary<string, string> edits = new Dictionary<string, string>
            {
                [callerPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "SameReloadCaller" + label + ".cs",
                    CallExpression(File.ReadAllText(callerPath), callerExpression))
            };
            foreach (KeyValuePair<string, string> owner in ownerSources)
            {
                paths.Add(owner.Key);
                edits[owner.Key] = HotReloadTestSourceWriter.WriteEditedSource(
                    Path.GetFileNameWithoutExtension(owner.Key) + label + ".cs",
                    owner.Value);
            }

            return HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                paths.ToArray(),
                contentPathOverride: null,
                CancellationToken.None,
                edits);
        }

        private static Dictionary<string, string> Owners(string valueSource, string factorySource)
        {
            Dictionary<string, string> owners = new Dictionary<string, string> { [ValueOwnerPath] = valueSource };
            if (factorySource != null)
            {
                owners[FactoryOwnerPath] = factorySource;
            }

            return owners;
        }

        private static int CallTheCaller()
        {
            return new HotReloadCrossFileAddedMemberCaller().Call(new HotReloadCrossFileAddedMemberHost());
        }

        // Why NoInlining: the test reads the values back through a direct call on the patched
        // caller, which an inlined copy at the call site would not observe.
        private static string BuildValueSource(string extraMembers)
        {
            return
                "namespace " + Namespace + "\n"
                + "{\n"
                + "    public sealed class " + ValueSimpleName + "\n"
                + "    {\n"
                + "        [System.Runtime.CompilerServices.MethodImpl(\n"
                + "            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]\n"
                + "        public int Ping()\n"
                + "        {\n"
                + "            return " + PingValue.ToString() + ";\n"
                + "        }\n"
                + extraMembers
                + "    }\n"
                + "}\n";
        }

        private static string BuildFactorySource(string makeBody)
        {
            return BuildReturningTypeSource(FactorySimpleName, makeBody);
        }

        private static string BuildReturningTypeSource(string simpleName, string makeBody)
        {
            return
                "namespace " + Namespace + "\n"
                + "{\n"
                + "    public sealed class " + simpleName + "\n"
                + "    {\n"
                + "        [System.Runtime.CompilerServices.MethodImpl(\n"
                + "            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]\n"
                + "        public " + ValueSimpleName + " Make()\n"
                + "        {\n"
                + makeBody
                + "        }\n"
                + "    }\n"
                + "}\n";
        }

        private static string CallExpression(string callerSource, string expression)
        {
            Assert.That(callerSource, Does.Contain(CallerBodyAnchor), "Precondition: caller body anchor must exist.");
            return callerSource.Replace(
                CallerBodyAnchor,
                "return " + expression + " + host.Value();",
                StringComparison.Ordinal);
        }
    }
}
