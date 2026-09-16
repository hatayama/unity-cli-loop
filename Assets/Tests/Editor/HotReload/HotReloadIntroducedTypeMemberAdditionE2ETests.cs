using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// End-to-end coverage of members added to a type an earlier reload introduced: the method,
    /// the field and the property arrive on the assembly that reload retained, and the type keeps
    /// running from it instead of the run demanding a compile.
    /// </summary>
    public class HotReloadIntroducedTypeMemberAdditionE2ETests : HotReloadIntroducedTypeE2ETestBase
    {
        private const string HostTypeAnchor = "    public sealed class HotReloadCrossFileAddedMemberHost";
        private const string CallerBodyAnchor = "return host.Value();";
        private const string IntroducedTypeSimpleName = "HotReloadMemberAdditionIntroducedValue";
        private const string IntroducedTypeMetadataName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload." + IntroducedTypeSimpleName;
        private const int IntroducedSeed = 5;
        private const int AddedFieldValue = 3;
        private const int ComputedAfterAddition = IntroducedSeed + AddedFieldValue;
        private const int HostValue = 1;
        private const string SeedExpression = "_seed";
        private const string AddedMethodExpression = "Extra()";
        private const string NoAddedMembers = "";

        // The members the second reload adds: an ordinary method that reads both the compiled
        // private field and the added one, the added field itself, and an auto-property whose
        // accessors have to arrive as added methods of their own.
        private static readonly string AddedMembers =
            "\n"
            + "        public int Extra()\n"
            + "        {\n"
            + "            return _seed + extra;\n"
            + "        }\n"
            + "\n"
            + "        private int extra = " + AddedFieldValue.ToString() + ";\n"
            + "\n"
            + "        public int Count { get; set; }\n";

        /// <summary>
        /// What: adding a method, a field and an auto-property to an already introduced type,
        /// while editing a body of it in the same reload, keeps the type active and applies every
        /// addition: the type row says the reload bound what the domain already holds, the edited
        /// body is patched, each added member is reported as added, and a call into the retained
        /// assembly runs the new code.
        /// </summary>
        [Test]
        public async Task Run_MembersAddedToIntroducedType_AppliesThemOnTheRetainedArtifact()
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");
            string callerPath = FixturePath("HotReloadCrossFileAddedMemberCaller.cs");

            await RunInIntroducedTypeDomainAsync(async readArtifact =>
            {
                await RunReloadAsync(hostPath, callerPath, CreateIntroducingEdits(hostPath, callerPath));
                Assert.That(
                    ReadComputedValue(readArtifact()),
                    Is.EqualTo(IntroducedSeed),
                    "Precondition: the retained assembly must run the body the first reload compiled.");

                HotReloadOrchestratorResult added = await RunReloadAsync(
                    hostPath,
                    callerPath,
                    CreateMembersAddedEdits(hostPath, callerPath));

                Assert.That(
                    CountFailures(added),
                    Is.EqualTo(0),
                    "Adding members to an introduced type must not fail the reload.\n"
                    + DescribeOutcomes(added));
                AssertTypeIsStillActive(added);
                AssertOutcome(added, HotReloadMethodOutcomeKind.Patched, IntroducedTypeMetadataName + ".Compute()");
                AssertOutcome(added, HotReloadMethodOutcomeKind.Added, "Extra");
                AssertOutcome(added, HotReloadMethodOutcomeKind.Added, "get_Count");
                AssertOutcome(added, HotReloadMethodOutcomeKind.Added, "set_Count");
                Assert.That(
                    ReadComputedValue(readArtifact()),
                    Is.EqualTo(ComputedAfterAddition),
                    "The edited body must run the added method, which reads the added field.\n"
                    + DescribeOutcomes(added));
            });
        }

        /// <summary>
        /// What: reloading the very same addition a second time reports the same rows and still
        /// fails nothing, so a repeated reload of an unchanged edit converges instead of finding
        /// the added members already present and refusing them.
        /// </summary>
        [Test]
        public async Task Run_SameAdditionReloadedAgain_ReportsTheSameRowsAndFailsNothing()
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");
            string callerPath = FixturePath("HotReloadCrossFileAddedMemberCaller.cs");

            await RunInIntroducedTypeDomainAsync(async readArtifact =>
            {
                await RunReloadAsync(hostPath, callerPath, CreateIntroducingEdits(hostPath, callerPath));
                await RunReloadAsync(hostPath, callerPath, CreateMembersAddedEdits(hostPath, callerPath));
                Assert.That(
                    ReadComputedValue(readArtifact()),
                    Is.EqualTo(ComputedAfterAddition),
                    "Precondition: the second reload must have applied the additions.");

                HotReloadOrchestratorResult again = await RunReloadAsync(
                    hostPath,
                    callerPath,
                    CreateMembersAddedAgainEdits(hostPath, callerPath));

                Assert.That(
                    CountFailures(again),
                    Is.EqualTo(0),
                    "Reloading the same addition again must not fail the reload.\n"
                    + DescribeOutcomes(again));
                AssertTypeIsStillActive(again);
                AssertOutcome(again, HotReloadMethodOutcomeKind.Added, "Extra");
                AssertOutcome(again, HotReloadMethodOutcomeKind.Added, "get_Count");
                AssertOutcome(again, HotReloadMethodOutcomeKind.Added, "set_Count");
                Assert.That(
                    ReadComputedValue(readArtifact()),
                    Is.EqualTo(ComputedAfterAddition),
                    "The retained assembly must still run the added code after the repeat.\n"
                    + DescribeOutcomes(again));
            });
        }

        /// <summary>
        /// What: taking the added members away again leaves the introduced type active with
        /// nothing added, because the declaration is back to the one the retained assembly was
        /// compiled from - a member this run never added is not one it has to remove.
        /// </summary>
        [Test]
        public async Task Run_AddedMembersRemovedAgain_LeavesTheTypeActiveWithNothingAdded()
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");
            string callerPath = FixturePath("HotReloadCrossFileAddedMemberCaller.cs");

            await RunInIntroducedTypeDomainAsync(async readArtifact =>
            {
                await RunReloadAsync(hostPath, callerPath, CreateIntroducingEdits(hostPath, callerPath));
                await RunReloadAsync(hostPath, callerPath, CreateMembersAddedEdits(hostPath, callerPath));
                Assert.That(
                    ReadComputedValue(readArtifact()),
                    Is.EqualTo(ComputedAfterAddition),
                    "Precondition: the second reload must have applied the additions.");

                HotReloadOrchestratorResult restored = await RunReloadAsync(
                    hostPath,
                    callerPath,
                    CreateAdditionsRemovedEdits(hostPath, callerPath));

                Assert.That(
                    CountFailures(restored),
                    Is.EqualTo(0),
                    "Restoring the declaration the retained assembly holds must not fail.\n"
                    + DescribeOutcomes(restored));
                AssertTypeIsStillActive(restored);
                Assert.That(
                    CountAddedMethods(restored),
                    Is.EqualTo(0),
                    "A declaration that matches the retained assembly adds nothing.\n"
                    + DescribeOutcomes(restored));
                Assert.That(
                    ReadComputedValue(readArtifact()),
                    Is.EqualTo(IntroducedSeed),
                    "The retained assembly must run its own body again.\n"
                    + DescribeOutcomes(restored));
            });
        }

        /// <summary>
        /// What: a compiled type in another file of the same group can call a method added to the
        /// introduced type in that very reload: both files are applied, the caller's body is
        /// patched, and calling it runs the added method.
        /// </summary>
        [Test]
        public async Task Run_CompiledCallerUsesAMemberAddedToAnIntroducedType_AppliesBothFiles()
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");
            string callerPath = FixturePath("HotReloadCrossFileAddedMemberCaller.cs");

            await RunInIntroducedTypeDomainAsync(async readArtifact =>
            {
                await RunReloadAsync(hostPath, callerPath, CreateIntroducingEdits(hostPath, callerPath));
                Assert.That(
                    ReadComputedValue(readArtifact()),
                    Is.EqualTo(IntroducedSeed),
                    "Precondition: the retained assembly must run the body the first reload compiled.");

                HotReloadOrchestratorResult crossFile = await RunReloadAsync(
                    hostPath,
                    callerPath,
                    CreateCallerUsesAddedMemberEdits(hostPath, callerPath));

                Assert.That(
                    CountFailures(crossFile),
                    Is.EqualTo(0),
                    "A caller bound against an added member must not fail the reload.\n"
                    + DescribeOutcomes(crossFile));
                AssertTypeIsStillActive(crossFile);
                AssertOutcome(crossFile, HotReloadMethodOutcomeKind.Added, "Extra");
                AssertOutcome(crossFile, HotReloadMethodOutcomeKind.Patched, "Call");
                Assert.That(
                    new HotReloadCrossFileAddedMemberCaller().Call(new HotReloadCrossFileAddedMemberHost()),
                    Is.EqualTo(ComputedAfterAddition + HostValue),
                    "The patched caller must run the member added to the introduced type.\n"
                    + DescribeOutcomes(crossFile));
            });
        }

        private static void AssertTypeIsStillActive(HotReloadOrchestratorResult result)
        {
            HotReloadResponse response = BuildResponse(result);
            Assert.That(
                response.IntroducedTypes.Count,
                Is.EqualTo(1),
                "The reload bound one declaration from the retained artifact.\n" + DescribeOutcomes(result));
            Assert.That(
                response.IntroducedTypes[0].Kind,
                Is.EqualTo("AlreadyActive"),
                "A declaration the domain already holds was not introduced by this run.\n"
                + DescribeOutcomes(result));
            Assert.That(
                response.IntroducedTypes[0].TypeName,
                Is.EqualTo(IntroducedTypeMetadataName),
                "The type row must name the declaration the reload bound.\n" + DescribeOutcomes(result));
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

            Assert.Fail(
                "No " + kind + " row mentions " + methodFragment + ".\n" + DescribeOutcomes(result));
        }

        private static int CountAddedMethods(HotReloadOrchestratorResult result)
        {
            int count = 0;
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Added)
                {
                    count++;
                }
            }

            return count;
        }

        private static int ReadComputedValue(HotReloadIntroducedTypeArtifact artifact)
        {
            Assert.That(artifact, Is.Not.Null, "A reload had to introduce the type before this check.");
            Type introducedType = artifact.Assembly.GetType(IntroducedTypeMetadataName, throwOnError: false);
            Assert.That(introducedType, Is.Not.Null, "The artifact must hold " + IntroducedTypeMetadataName + ".");
            MethodInfo compute = introducedType.GetMethod(
                "Compute",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(compute, Is.Not.Null, "The introduced type must declare Compute().");

            return (int)compute.Invoke(Activator.CreateInstance(introducedType), Array.Empty<object>());
        }

        private static Dictionary<string, string> CreateIntroducingEdits(string hostPath, string callerPath)
        {
            return CreateEdits(
                hostPath,
                callerPath,
                "MemberAdditionIntroducing",
                SeedExpression,
                NoAddedMembers,
                CallComputeOfIntroducedType(File.ReadAllText(callerPath)));
        }

        // The declaration with the three additions and an edited body that reads one of them,
        // which is what the reload has to apply onto the assembly the first reload retained.
        private static Dictionary<string, string> CreateMembersAddedEdits(string hostPath, string callerPath)
        {
            return CreateEdits(
                hostPath,
                callerPath,
                "MemberAdditionApplied",
                AddedMethodExpression,
                AddedMembers,
                CallComputeOfIntroducedType(File.ReadAllText(callerPath)));
        }

        // Why distinct written file names for the same source: the run has to be told about an
        // edit, and reusing the previous reload's path would let a cached read decide the answer
        // instead of the comparison this test is about.
        private static Dictionary<string, string> CreateMembersAddedAgainEdits(string hostPath, string callerPath)
        {
            return CreateEdits(
                hostPath,
                callerPath,
                "MemberAdditionRepeated",
                AddedMethodExpression,
                AddedMembers,
                CallComputeOfIntroducedType(File.ReadAllText(callerPath)));
        }

        private static Dictionary<string, string> CreateAdditionsRemovedEdits(string hostPath, string callerPath)
        {
            return CreateEdits(
                hostPath,
                callerPath,
                "MemberAdditionRestored",
                SeedExpression,
                NoAddedMembers,
                CallComputeOfIntroducedType(File.ReadAllText(callerPath)));
        }

        // The same additions, with the compiled caller of the other file reaching the added
        // method rather than the one the artifact already holds.
        private static Dictionary<string, string> CreateCallerUsesAddedMemberEdits(string hostPath, string callerPath)
        {
            return CreateEdits(
                hostPath,
                callerPath,
                "MemberAdditionCrossFile",
                AddedMethodExpression,
                AddedMembers,
                CallExtraOfIntroducedType(File.ReadAllText(callerPath)));
        }

        private static Dictionary<string, string> CreateEdits(
            string hostPath,
            string callerPath,
            string writtenNamePrefix,
            string computedExpression,
            string addedMembers,
            string callerSource)
        {
            return new Dictionary<string, string>
            {
                [hostPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    writtenNamePrefix + "Host.cs",
                    InsertIntroducedType(File.ReadAllText(hostPath), computedExpression, addedMembers)),
                [callerPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    writtenNamePrefix + "Caller.cs",
                    callerSource)
            };
        }

        // Why NoInlining: the test reads the patched body back through a reflection call, and the
        // declaration must stay byte-identical between the reloads apart from what they change.
        private static string InsertIntroducedType(
            string hostSource,
            string computedExpression,
            string addedMembers)
        {
            Assert.That(hostSource, Does.Contain(HostTypeAnchor), "Precondition: host type anchor must exist.");
            string introduced =
                "    public sealed class " + IntroducedTypeSimpleName + "\n"
                + "    {\n"
                + "        private readonly int _seed = " + IntroducedSeed.ToString() + ";\n"
                + "\n"
                + "        [System.Runtime.CompilerServices.MethodImpl(\n"
                + "            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]\n"
                + "        public int Compute()\n"
                + "        {\n"
                + "            return " + computedExpression + ";\n"
                + "        }\n"
                + addedMembers
                + "    }\n"
                + "\n";
            return hostSource.Replace(HostTypeAnchor, introduced + HostTypeAnchor, StringComparison.Ordinal);
        }

        private static string CallComputeOfIntroducedType(string callerSource)
        {
            Assert.That(callerSource, Does.Contain(CallerBodyAnchor), "Precondition: caller body anchor must exist.");
            return callerSource.Replace(
                CallerBodyAnchor,
                "return new " + IntroducedTypeSimpleName + "().Compute() + host.Value();",
                StringComparison.Ordinal);
        }

        private static string CallExtraOfIntroducedType(string callerSource)
        {
            Assert.That(callerSource, Does.Contain(CallerBodyAnchor), "Precondition: caller body anchor must exist.");
            return callerSource.Replace(
                CallerBodyAnchor,
                "return new " + IntroducedTypeSimpleName + "().Extra() + host.Value();",
                StringComparison.Ordinal);
        }
    }
}
