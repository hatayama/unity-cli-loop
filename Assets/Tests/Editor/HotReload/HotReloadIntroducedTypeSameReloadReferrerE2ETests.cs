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

        private static readonly string GenericEchoMember =
            "\n"
            + "        public T Echo<T>(T value)\n"
            + "        {\n"
            + "            return value;\n"
            + "        }\n";

        private const string AppliedChangesOnlyPhrase = "holds only what earlier reloads added to or edited in it";

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
        /// What: when a type an earlier reload retained and a type this reload introduces both
        /// name the changed type, the refusal names each with its own reason and recommends the
        /// two-step order, because editing only the retained type in this reload leaves the new
        /// type split. Following that order, introducing the new type first and then changing the
        /// type together with an edit of both referrers, lets the caller reach the added method.
        /// </summary>
        [Test]
        public async Task Run_MemberAddedWhileARetainedAndANewTypeBothReturnIt_RefusesNamingBothAndTheTwoStepOrderWorks()
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
                    Does.Contain("'" + KeeperMetadataName + "', which an earlier reload retained"),
                    description);
                Assert.That(
                    description,
                    Does.Contain("'" + FactoryMetadataName + "', introduced by this reload"),
                    description);
                Assert.That(description, Does.Contain("reload in two steps"), description);

                HotReloadOrchestratorResult factoryFirst = await RunAsync(
                    callerPath,
                    Owners(BuildValueSource(NoExtraMembers), BuildFactorySource(DefaultMakeBody)),
                    "new " + FactorySimpleName + "().Make().Ping()",
                    "MixedStepFactory");
                Assert.That(CountFailures(factoryFirst), Is.EqualTo(0), DescribeOutcomes(factoryFirst));

                Dictionary<string, string> memberAddedOwners =
                    Owners(BuildValueSource(PongMember), BuildFactorySource(EditedMakeBody));
                memberAddedOwners[KeeperOwnerPath] = BuildReturningTypeSource(KeeperSimpleName, EditedMakeBody);
                HotReloadOrchestratorResult memberAdded = await RunAsync(
                    callerPath,
                    memberAddedOwners,
                    "new " + FactorySimpleName + "().Make().Pong() + new " + KeeperSimpleName + "().Make().Pong()",
                    "MixedStepMemberAdded");

                Assert.That(CountFailures(memberAdded), Is.EqualTo(0), DescribeOutcomes(memberAdded));
                Assert.That(
                    CallTheCaller(),
                    Is.EqualTo(PongValue + PongValue + HostValue),
                    "The added method must run on values both referrers made.\n" + DescribeOutcomes(memberAdded));
            });
        }

        /// <summary>
        /// What: after an earlier reload added a method to an introduced type, a reload that
        /// passes only a new file whose type returns it re-applies the introduced type's file
        /// unchanged and is refused. The refusal does not tell the reader to reload first without
        /// a change to that type, because this reload holds none; it names compile instead.
        /// </summary>
        [Test]
        public async Task Run_NewTypeReturningAnIntroducedTypeWhoseAdditionAnEarlierReloadApplied_RefusesWithoutTheTwoStepOrder()
        {
            string callerPath = FixturePath("HotReloadCrossFileAddedMemberCaller.cs");

            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                HotReloadOrchestratorResult introducing = await RunAsync(
                    callerPath,
                    Owners(BuildValueSource(NoExtraMembers), null),
                    "new " + ValueSimpleName + "().Ping()",
                    "AppliedIntroducing");
                Assert.That(CountFailures(introducing), Is.EqualTo(0), DescribeOutcomes(introducing));

                string valueWithPong = BuildValueSource(PongMember);
                HotReloadOrchestratorResult memberAdded = await RunAsync(
                    callerPath,
                    Owners(valueWithPong, null),
                    "new " + ValueSimpleName + "().Pong()",
                    "AppliedMemberAdded");
                Assert.That(CountFailures(memberAdded), Is.EqualTo(0), DescribeOutcomes(memberAdded));
                Assert.That(CallTheCaller(), Is.EqualTo(PongValue + HostValue), DescribeOutcomes(memberAdded));

                HotReloadOrchestratorResult factoryOnly = await RunWithNewFactoryAsync(
                    callerPath,
                    Owners(valueWithPong, null),
                    NoListedOwnerPaths,
                    BuildFactorySource(DefaultMakeBody),
                    "new " + FactorySimpleName + "().Make().Ping()",
                    "AppliedFactoryOnly");

                string description = DescribeOutcomes(factoryOnly);
                Assert.That(
                    factoryOnly.ReappliedSiblingPaths,
                    Does.Contain(ValueOwnerPath),
                    "Precondition: the value type's file must come back in as a sibling.\n" + description);
                Assert.That(CountFailures(factoryOnly), Is.GreaterThan(0), description);
                Assert.That(description, Does.Contain("'" + ValueMetadataName + "'"), description);
                Assert.That(description, Does.Contain("'" + FactoryMetadataName + "'"), description);
                Assert.That(description, Does.Not.Contain("first without the change to"), description);
                Assert.That(description, Does.Contain(AppliedChangesOnlyPhrase), description);
                Assert.That(description, Does.Contain("uloop compile"), description);
                Assert.That(CallTheCaller(), Is.EqualTo(PongValue + HostValue), "A refused run must leave the previous patch in place.");
            });
        }

        /// <summary>
        /// What: when a retained type restored to the body it was introduced with also returns the
        /// introduced type whose changes earlier reloads applied, the refusal for a new type
        /// returning it names the retained type too
        /// and tells the reader to edit it in the same reload as well, because moving the use into
        /// the new type's bodies alone would leave the next reload refusing for the retained type.
        /// </summary>
        [Test]
        public async Task Run_NewTypeReturningAnIntroducedTypeWhoseAdditionAnEarlierReloadAppliedBesideARetainedReferrer_NamesTheRetainedTypeToEditToo()
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
                    "AppliedKeeperIntroducing");
                Assert.That(CountFailures(introducing), Is.EqualTo(0), DescribeOutcomes(introducing));

                string valueWithPong = BuildValueSource(PongMember);
                string editedKeeper = BuildReturningTypeSource(KeeperSimpleName, EditedMakeBody);
                Dictionary<string, string> memberAddedOwners = Owners(valueWithPong, null);
                memberAddedOwners[KeeperOwnerPath] = editedKeeper;
                HotReloadOrchestratorResult memberAdded = await RunAsync(
                    callerPath,
                    memberAddedOwners,
                    "new " + KeeperSimpleName + "().Make().Pong()",
                    "AppliedKeeperMemberAdded");
                Assert.That(CountFailures(memberAdded), Is.EqualTo(0), DescribeOutcomes(memberAdded));
                Assert.That(CallTheCaller(), Is.EqualTo(PongValue + HostValue), DescribeOutcomes(memberAdded));

                // Restoring the body the first reload introduced makes the retained type match its
                // artifact again, so it is bound from there instead of being rebuilt from source.
                Dictionary<string, string> factoryOwners = Owners(valueWithPong, null);
                factoryOwners[KeeperOwnerPath] = BuildReturningTypeSource(KeeperSimpleName, DefaultMakeBody);
                HotReloadOrchestratorResult factoryOnly = await RunWithNewFactoryAsync(
                    callerPath,
                    factoryOwners,
                    new[] { KeeperOwnerPath },
                    BuildFactorySource(DefaultMakeBody),
                    "new " + FactorySimpleName + "().Make().Ping()",
                    "AppliedKeeperFactoryOnly");

                string description = DescribeOutcomes(factoryOnly);
                Assert.That(CountFailures(factoryOnly), Is.GreaterThan(0), description);
                Assert.That(description, Does.Contain(AppliedChangesOnlyPhrase), description);
                Assert.That(
                    description,
                    Does.Contain("'" + KeeperMetadataName + "', which an earlier reload retained"),
                    description);
                Assert.That(description, Does.Contain("also edit '" + KeeperMetadataName + "'"), description);
                Assert.That(CallTheCaller(), Is.EqualTo(PongValue + HostValue), "A refused run must leave the previous patch in place.");
            });
        }

        /// <summary>
        /// What: when the earlier reload that changed an introduced type skipped part of that
        /// change, a later reload passing only a new file whose type returns it is refused without
        /// claiming that every change in the introduced type's source is already loaded.
        /// </summary>
        [Test]
        public async Task Run_NewTypeReturningAnIntroducedTypeWhoseChangeAnEarlierReloadSkippedInPart_DoesNotClaimTheChangeIsLoaded()
        {
            string callerPath = FixturePath("HotReloadCrossFileAddedMemberCaller.cs");

            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                HotReloadOrchestratorResult introducing = await RunAsync(
                    callerPath,
                    Owners(BuildValueSource(NoExtraMembers), null),
                    "new " + ValueSimpleName + "().Ping()",
                    "PartialIntroducing");
                Assert.That(CountFailures(introducing), Is.EqualTo(0), DescribeOutcomes(introducing));

                string valueWithPongAndGeneric = BuildValueSource(PongMember + GenericEchoMember);
                HotReloadOrchestratorResult partlySkipped = await RunAsync(
                    callerPath,
                    Owners(valueWithPongAndGeneric, null),
                    "new " + ValueSimpleName + "().Pong()",
                    "PartialMemberAdded");
                Assert.That(
                    HasSkippedRow(partlySkipped, "Echo"),
                    Is.True,
                    "Precondition: the added generic method must be skipped so the file is only partly applied.\n"
                    + DescribeOutcomes(partlySkipped));
                Assert.That(CallTheCaller(), Is.EqualTo(PongValue + HostValue), DescribeOutcomes(partlySkipped));

                HotReloadOrchestratorResult factoryOnly = await RunWithNewFactoryAsync(
                    callerPath,
                    Owners(valueWithPongAndGeneric, null),
                    NoListedOwnerPaths,
                    BuildFactorySource(DefaultMakeBody),
                    "new " + FactorySimpleName + "().Make().Ping()",
                    "PartialFactoryOnly");

                string description = DescribeOutcomes(factoryOnly);
                Assert.That(
                    factoryOnly.ReappliedSiblingPaths,
                    Does.Contain(ValueOwnerPath),
                    "Precondition: the value type's file must come back in as a sibling.\n" + description);
                Assert.That(CountFailures(factoryOnly), Is.GreaterThan(0), description);
                Assert.That(description, Does.Contain("'" + FactoryMetadataName + "'"), description);
                Assert.That(description, Does.Not.Contain(AppliedChangesOnlyPhrase), description);
            });
        }

        /// <summary>
        /// What: after an earlier reload added a method to an introduced type, a new type that
        /// uses it only inside a method body, not in a signature, is introduced by a reload that
        /// passes only the new file, and the caller reaches the value through it.
        /// </summary>
        [Test]
        public async Task Run_NewTypeUsingAnIntroducedTypeWhoseAdditionAnEarlierReloadAppliedOnlyInABody_IsIntroduced()
        {
            string callerPath = FixturePath("HotReloadCrossFileAddedMemberCaller.cs");

            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                HotReloadOrchestratorResult introducing = await RunAsync(
                    callerPath,
                    Owners(BuildValueSource(NoExtraMembers), null),
                    "new " + ValueSimpleName + "().Ping()",
                    "BodyOnlyIntroducing");
                Assert.That(CountFailures(introducing), Is.EqualTo(0), DescribeOutcomes(introducing));

                string valueWithPong = BuildValueSource(PongMember);
                HotReloadOrchestratorResult memberAdded = await RunAsync(
                    callerPath,
                    Owners(valueWithPong, null),
                    "new " + ValueSimpleName + "().Pong()",
                    "BodyOnlyMemberAdded");
                Assert.That(CountFailures(memberAdded), Is.EqualTo(0), DescribeOutcomes(memberAdded));

                HotReloadOrchestratorResult factoryOnly = await RunWithNewFactoryAsync(
                    callerPath,
                    Owners(valueWithPong, null),
                    NoListedOwnerPaths,
                    BuildBodyOnlyFactorySource(),
                    "new " + FactorySimpleName + "().MakePing()",
                    "BodyOnlyFactoryOnly");

                string description = DescribeOutcomes(factoryOnly);
                Assert.That(
                    factoryOnly.ReappliedSiblingPaths,
                    Does.Contain(ValueOwnerPath),
                    "Precondition: the value type's file must come back in as a sibling.\n" + description);
                Assert.That(CountFailures(factoryOnly), Is.EqualTo(0), description);
                Assert.That(CallTheCaller(), Is.EqualTo(PingValue + HostValue), description);
            });
        }

        private static readonly string[] NoListedOwnerPaths = new string[0];

        // Owner files outside listedOwnerPaths stay out of the requested paths, so only the
        // sibling re-apply brings them in, the way a reload naming just the new file does. Their
        // content stays in the override map because the files never exist on disk.
        private static Task<HotReloadOrchestratorResult> RunWithNewFactoryAsync(
            string callerPath,
            Dictionary<string, string> ownerSources,
            string[] listedOwnerPaths,
            string factorySource,
            string callerExpression,
            string label)
        {
            Dictionary<string, string> edits = new Dictionary<string, string>
            {
                [callerPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "SameReloadCaller" + label + ".cs",
                    CallExpression(File.ReadAllText(callerPath), callerExpression)),
                [FactoryOwnerPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    Path.GetFileNameWithoutExtension(FactoryOwnerPath) + label + ".cs",
                    factorySource)
            };
            foreach (KeyValuePair<string, string> owner in ownerSources)
            {
                edits[owner.Key] = HotReloadTestSourceWriter.WriteEditedSource(
                    Path.GetFileNameWithoutExtension(owner.Key) + label + ".cs",
                    owner.Value);
            }

            List<string> paths = new List<string> { callerPath, FactoryOwnerPath };
            paths.AddRange(listedOwnerPaths);
            return HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                paths.ToArray(),
                contentPathOverride: null,
                CancellationToken.None,
                edits);
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

        // The factory names the value type only inside its body, so no signature of the new
        // type refers to it.
        private static string BuildBodyOnlyFactorySource()
        {
            return
                "namespace " + Namespace + "\n"
                + "{\n"
                + "    public sealed class " + FactorySimpleName + "\n"
                + "    {\n"
                + "        [System.Runtime.CompilerServices.MethodImpl(\n"
                + "            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]\n"
                + "        public int MakePing()\n"
                + "        {\n"
                + "            return new " + ValueSimpleName + "().Ping();\n"
                + "        }\n"
                + "    }\n"
                + "}\n";
        }

        private static bool HasSkippedRow(HotReloadOrchestratorResult result, string methodFragment)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Skipped
                    && outcome.Method != null
                    && outcome.Method.Contains(methodFragment))
                {
                    return true;
                }
            }

            return false;
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
