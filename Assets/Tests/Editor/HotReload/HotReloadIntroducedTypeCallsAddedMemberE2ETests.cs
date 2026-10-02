using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// End-to-end coverage of a new type whose methods and getters call members hot reload adds,
    /// in the same reload or an earlier one, to a compiled type or to a type an earlier reload
    /// introduced. The artifact cannot compile those bodies, so it carries stubs, and the same
    /// reload patches the real bodies in before the type is activated. A new type that names such
    /// a type in its signatures is still refused, and that refusal has to say why. A later reload
    /// that retires a member those patches still call has to name the calls.
    /// </summary>
    /// <remarks>
    /// Why the owners are not fixtures on disk: a .cs under Assets/ is compiled into the test
    /// assembly, and a type the compiler already lists is never introduced.
    /// </remarks>
    public class HotReloadIntroducedTypeCallsAddedMemberE2ETests : HotReloadIntroducedTypeE2ETestBase
    {
        private const string ValueOwnerPath =
            "Assets/Tests/Editor/HotReload/UncompiledCallsAddedMemberValueOwner.cs";

        private const string UserOwnerPath =
            "Assets/Tests/Editor/HotReload/UncompiledCallsAddedMemberUserOwner.cs";

        private const string FactoryOwnerPath =
            "Assets/Tests/Editor/HotReload/UncompiledCallsAddedMemberFactoryOwner.cs";

        private const string Namespace = "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload";
        private const string ValueSimpleName = "HotReloadCallsAddedMemberValue";
        private const string UserSimpleName = "HotReloadCallsAddedMemberUser";
        private const string UserMetadataName = Namespace + "." + UserSimpleName;
        private const string FactorySimpleName = "HotReloadCallsAddedMemberFactory";
        private const string FactoryMetadataName = Namespace + "." + FactorySimpleName;
        private const string HostValueAnchor = "        public int Value()";
        private const string CompiledTypeAddedMethodName = "AddedForTheNewType";
        private const int CompiledTypeAddedValue = 41;
        private const string IntroducedTypeAddedMethodName = "Pong";
        private const int IntroducedTypeAddedValue = 9;
        private const int PlainValue = 7;
        private const int EditedOffset = 100;
        private const string NoExtraMembers = "";
        private const string StubMessageCore = "calls members that a hot reload added";
        private const string MixedParametersMethodName = "Mixed";
        private const int ReshapedAddedSeed = 2;
        private const int ReshapedAddedOffset = 50;

        private const string CompiledTypeAddedCall =
            "new HotReloadCrossFileAddedMemberHost()." + CompiledTypeAddedMethodName + "()";

        private const string IntroducedTypeAddedCall =
            "new " + ValueSimpleName + "()." + IntroducedTypeAddedMethodName + "()";

        private static readonly string CompiledTypeAddedMember =
            "        public int " + CompiledTypeAddedMethodName + "()\n"
            + "        {\n"
            + "            return " + CompiledTypeAddedValue + ";\n"
            + "        }\n"
            + "\n";

        // The same added method with a parameter, which a later reload registers under a new key
        // while retiring the one the new type's earlier patches call.
        private static readonly string ReshapedCompiledTypeAddedMember =
            "        public int " + CompiledTypeAddedMethodName + "(int seed)\n"
            + "        {\n"
            + "            return seed + " + ReshapedAddedOffset + ";\n"
            + "        }\n"
            + "\n";

        private static readonly string ReshapedCompiledTypeAddedCall =
            "new HotReloadCrossFileAddedMemberHost()." + CompiledTypeAddedMethodName + "(" + ReshapedAddedSeed + ")";

        private static readonly string IntroducedTypeAddedMember =
            "\n"
            + "        public int " + IntroducedTypeAddedMethodName + "()\n"
            + "        {\n"
            + "            return " + IntroducedTypeAddedValue + ";\n"
            + "        }\n";

        // A factory member naming the user type in its signature, and one naming it only inside
        // its body.
        private static readonly string FactoryMakeMember =
            "        public " + UserSimpleName + " Make()\n"
            + "        {\n"
            + "            return new " + UserSimpleName + "();\n"
            + "        }\n";

        private static readonly string FactoryTotalMember =
            "        public int Total()\n"
            + "        {\n"
            + "            return new " + UserSimpleName + "().Run();\n"
            + "        }\n";

        // A factory body that calls the addition itself, so the factory is stubbed as well.
        private static readonly string FactoryScaledMember =
            "\n"
            + "        public int Scaled()\n"
            + "        {\n"
            + "            return " + CompiledTypeAddedCall + " * 2;\n"
            + "        }\n";

        // A user member naming the factory in its signature.
        private static readonly string UserPairMember =
            "\n"
            + "        public " + FactorySimpleName + " Pair()\n"
            + "        {\n"
            + "            return new " + FactorySimpleName + "();\n"
            + "        }\n";

        // A stubbed user method with a parameter of each shape a method key spells in its own way:
        // an array, a multi-dimensional array, a nested type, a by-ref value and a constructed
        // generic type.
        private static readonly string UserMixedParametersMember =
            "\n"
            + "        public int " + MixedParametersMethodName + "(\n"
            + "            int[] values,\n"
            + "            int[,] grid,\n"
            + "            System.Environment.SpecialFolder folder,\n"
            + "            ref int counter,\n"
            + "            System.Collections.Generic.List<int> list)\n"
            + "        {\n"
            + "            counter++;\n"
            + "            return " + CompiledTypeAddedCall + " + values.Length + grid.Length + list.Count;\n"
            + "        }\n";

        /// <summary>
        /// What: a new type calling a method the same reload adds to a compiled type is introduced;
        /// its method and its getter run the added method, and a method that calls nothing added
        /// keeps running the body the artifact was compiled with.
        /// </summary>
        [Test]
        public async Task Run_NewTypeCallsAMemberTheSameReloadAddsToACompiledType_RunsTheAddition()
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");

            await RunInIntroducedTypeDomainAsync(async readArtifact =>
            {
                HotReloadOrchestratorResult result = await RunAsync(
                    new Dictionary<string, string>
                    {
                        [hostPath] = WriteSource(hostPath, "CompiledSameReload", InsertCompiledTypeMember(File.ReadAllText(hostPath))),
                        [UserOwnerPath] = WriteSource(UserOwnerPath, "CompiledSameReload", BuildUserSource(CompiledTypeAddedCall))
                    });

                AssertIntroduced(result);
                AssertMethodRow(result, HotReloadMethodOutcomeKind.Patched, UserSimpleName + ".Run(");
                AssertMethodRow(result, HotReloadMethodOutcomeKind.Patched, UserSimpleName + ".get_Answer(");
                Assert.That(Invoke(readArtifact(), "Run"), Is.EqualTo(CompiledTypeAddedValue), DescribeOutcomes(result));
                Assert.That(Invoke(readArtifact(), "get_Answer"), Is.EqualTo(CompiledTypeAddedValue + 1), DescribeOutcomes(result));
                Assert.That(Invoke(readArtifact(), "Plain"), Is.EqualTo(PlainValue), DescribeOutcomes(result));
            });
        }

        /// <summary>
        /// What: a new type calling a method an earlier reload added is introduced when the file
        /// that declares the addition is not passed but is pulled in as an unchanged sibling.
        /// </summary>
        [Test]
        public async Task Run_NewTypeCallsAMemberAnEarlierReloadAddedInAnUnpassedSibling_RunsTheAddition()
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");
            string hostWithAddition = WriteSource(
                hostPath,
                "SiblingAddition",
                InsertCompiledTypeMember(File.ReadAllText(hostPath)));

            await RunInIntroducedTypeDomainAsync(async readArtifact =>
            {
                HotReloadOrchestratorResult addition = await RunAsync(
                    new Dictionary<string, string> { [hostPath] = hostWithAddition });
                Assert.That(CountFailures(addition), Is.EqualTo(0), DescribeOutcomes(addition));

                // Why the host is left out of the files but kept in the overrides: the run has to
                // pull it in as the sibling an earlier reload changed, with the source that reload
                // applied rather than the fixture on disk.
                HotReloadOrchestratorResult result = await HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                    new[] { UserOwnerPath },
                    contentPathOverride: null,
                    CancellationToken.None,
                    new Dictionary<string, string>
                    {
                        [UserOwnerPath] = WriteSource(UserOwnerPath, "SiblingIntroducing", BuildUserSource(CompiledTypeAddedCall)),
                        [hostPath] = hostWithAddition
                    });

                AssertIntroduced(result);
                Assert.That(Invoke(readArtifact(), "Run"), Is.EqualTo(CompiledTypeAddedValue), DescribeOutcomes(result));
            });
        }

        /// <summary>
        /// What: a new type calling a method the same reload adds to a type an earlier reload
        /// introduced is introduced and runs that method.
        /// </summary>
        [Test]
        public async Task Run_NewTypeCallsAMemberTheSameReloadAddsToAnIntroducedType_RunsTheAddition()
        {
            await RunInIntroducedTypeDomainAsync(async readArtifact =>
            {
                HotReloadOrchestratorResult introducing = await RunAsync(
                    new Dictionary<string, string>
                    {
                        [ValueOwnerPath] = WriteSource(ValueOwnerPath, "IntroducedSameReloadIntroducing", BuildValueSource(NoExtraMembers))
                    });
                Assert.That(CountFailures(introducing), Is.EqualTo(0), DescribeOutcomes(introducing));

                HotReloadOrchestratorResult result = await RunAsync(
                    new Dictionary<string, string>
                    {
                        [ValueOwnerPath] = WriteSource(ValueOwnerPath, "IntroducedSameReload", BuildValueSource(IntroducedTypeAddedMember)),
                        [UserOwnerPath] = WriteSource(UserOwnerPath, "IntroducedSameReload", BuildUserSource(IntroducedTypeAddedCall))
                    });

                AssertIntroduced(result);
                Assert.That(Invoke(readArtifact(), "Run"), Is.EqualTo(IntroducedTypeAddedValue), DescribeOutcomes(result));
            });
        }

        /// <summary>
        /// What: once an earlier reload has added a method to an introduced type, a later reload
        /// introducing a new type that calls it introduces that type and runs the method.
        /// </summary>
        [Test]
        public async Task Run_NewTypeCallsAMemberAnEarlierReloadAddedToAnIntroducedType_RunsTheAddition()
        {
            await RunInIntroducedTypeDomainAsync(async readArtifact =>
            {
                HotReloadOrchestratorResult introducing = await RunAsync(
                    new Dictionary<string, string>
                    {
                        [ValueOwnerPath] = WriteSource(ValueOwnerPath, "IntroducedSplitIntroducing", BuildValueSource(NoExtraMembers))
                    });
                Assert.That(CountFailures(introducing), Is.EqualTo(0), DescribeOutcomes(introducing));

                string valueWithAddition = WriteSource(
                    ValueOwnerPath,
                    "IntroducedSplitAddition",
                    BuildValueSource(IntroducedTypeAddedMember));
                HotReloadOrchestratorResult addition = await RunAsync(
                    new Dictionary<string, string> { [ValueOwnerPath] = valueWithAddition });
                Assert.That(CountFailures(addition), Is.EqualTo(0), DescribeOutcomes(addition));

                HotReloadOrchestratorResult result = await RunAsync(
                    new Dictionary<string, string>
                    {
                        [ValueOwnerPath] = valueWithAddition,
                        [UserOwnerPath] = WriteSource(UserOwnerPath, "IntroducedSplit", BuildUserSource(IntroducedTypeAddedCall))
                    });

                AssertIntroduced(result);
                Assert.That(Invoke(readArtifact(), "Run"), Is.EqualTo(IntroducedTypeAddedValue), DescribeOutcomes(result));
            });
        }

        /// <summary>
        /// What: a stubbed body the transform skips, here a generic method, leaves its stub
        /// unpatched, so the type is not introduced, the row names the method, and the group is
        /// left unapplied instead of shipping a type that throws where its source works.
        /// </summary>
        [Test]
        public async Task Run_StubbedBodyTheTransformSkips_DoesNotIntroduceTheType()
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");

            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                HotReloadOrchestratorResult result = await RunAsync(
                    new Dictionary<string, string>
                    {
                        [hostPath] = WriteSource(hostPath, "SkippedStub", InsertCompiledTypeMember(File.ReadAllText(hostPath))),
                        [UserOwnerPath] = WriteSource(UserOwnerPath, "SkippedStub", BuildGenericUserSource(CompiledTypeAddedCall))
                    });

                HotReloadIntroducedTypeOutcome row = FindIntroducedTypeRow(result);
                Assert.That(row.Kind, Is.EqualTo(HotReloadIntroducedTypeOutcomeKind.Failed), DescribeOutcomes(result));
                Assert.That(
                    row.Reason,
                    Does.StartWith("Not introduced: " + UserMetadataName + ".Run`1() " + StubMessageCore),
                    DescribeOutcomes(result));
                Assert.That(IsActive(UserMetadataName), Is.False, "The held-back type must not be active.");
                Assert.That(
                    CountMethodRows(result, HotReloadMethodOutcomeKind.Added),
                    Is.EqualTo(0),
                    "The group must stay unapplied.\n" + DescribeOutcomes(result));
            });
        }

        /// <summary>
        /// What: later reloads keep the stubbed body patched while nothing changes, and patch the
        /// new body in when it is edited, because the record never reads the source as unchanged.
        /// </summary>
        [Test]
        public async Task Run_LaterReloads_KeepThePatchedBodyAndTakeItsEdits()
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");
            string hostWithAddition = WriteSource(
                hostPath,
                "LaterReloads",
                InsertCompiledTypeMember(File.ReadAllText(hostPath)));
            string user = WriteSource(UserOwnerPath, "LaterReloads", BuildUserSource(CompiledTypeAddedCall));

            await RunInIntroducedTypeDomainAsync(async readArtifact =>
            {
                HotReloadOrchestratorResult introducing = await RunAsync(
                    new Dictionary<string, string> { [hostPath] = hostWithAddition, [UserOwnerPath] = user });
                AssertIntroduced(introducing);

                HotReloadOrchestratorResult unchanged = await RunAsync(
                    new Dictionary<string, string> { [hostPath] = hostWithAddition, [UserOwnerPath] = user });
                Assert.That(CountFailures(unchanged), Is.EqualTo(0), DescribeOutcomes(unchanged));
                Assert.That(
                    FindIntroducedTypeRow(unchanged).Kind,
                    Is.EqualTo(HotReloadIntroducedTypeOutcomeKind.AlreadyActive),
                    DescribeOutcomes(unchanged));
                Assert.That(Invoke(readArtifact(), "Run"), Is.EqualTo(CompiledTypeAddedValue), DescribeOutcomes(unchanged));

                HotReloadOrchestratorResult edited = await RunAsync(
                    new Dictionary<string, string>
                    {
                        [hostPath] = hostWithAddition,
                        [UserOwnerPath] = WriteSource(
                            UserOwnerPath,
                            "LaterReloadsEdited",
                            BuildUserSource(CompiledTypeAddedCall + " + " + EditedOffset))
                    });
                Assert.That(CountFailures(edited), Is.EqualTo(0), DescribeOutcomes(edited));
                Assert.That(
                    Invoke(readArtifact(), "Run"),
                    Is.EqualTo(CompiledTypeAddedValue + EditedOffset),
                    DescribeOutcomes(edited));
            });
        }

        /// <summary>
        /// What: after revert-all the stubbed body runs its stub, which says to reload the owner
        /// file, and reloading it patches the body in again.
        /// </summary>
        [Test]
        public async Task RevertAll_ThenReloadingTheOwnerFile_RunsTheAdditionAgain()
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");
            string hostWithAddition = WriteSource(
                hostPath,
                "RevertAll",
                InsertCompiledTypeMember(File.ReadAllText(hostPath)));
            string user = WriteSource(UserOwnerPath, "RevertAll", BuildUserSource(CompiledTypeAddedCall));

            await RunInIntroducedTypeDomainAsync(async readArtifact =>
            {
                HotReloadOrchestratorResult introducing = await RunAsync(
                    new Dictionary<string, string> { [hostPath] = hostWithAddition, [UserOwnerPath] = user });
                AssertIntroduced(introducing);

                HotReloadCompositionRoot.Services.StatusExecutor.ExecuteRevertAll();
                TargetInvocationException stubbed = Assert.Throws<TargetInvocationException>(
                    () => Invoke(readArtifact(), "Run"));
                Assert.That(stubbed.InnerException, Is.TypeOf<InvalidOperationException>());
                Assert.That(stubbed.InnerException.Message, Does.Contain(StubMessageCore));
                Assert.That(stubbed.InnerException.Message, Does.Contain("Reload '" + UserOwnerPath + "' again"));

                HotReloadOrchestratorResult reloaded = await RunAsync(
                    new Dictionary<string, string> { [hostPath] = hostWithAddition, [UserOwnerPath] = user });
                Assert.That(CountFailures(reloaded), Is.EqualTo(0), DescribeOutcomes(reloaded));
                Assert.That(Invoke(readArtifact(), "Run"), Is.EqualTo(CompiledTypeAddedValue), DescribeOutcomes(reloaded));
            });
        }

        /// <summary>
        /// What: a new type naming a stubbed new type in a member signature is refused with the
        /// message that says the stubbed type runs through patches, not with the two-step advice
        /// for a type an earlier reload loaded, and moving the name into a method body lets both
        /// types be introduced.
        /// </summary>
        [Test]
        public async Task Run_NewTypeNamesAStubbedTypeInASignature_RefusesUntilTheNameMovesIntoABody()
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");
            string hostWithAddition = WriteSource(
                hostPath,
                "SignatureReferrer",
                InsertCompiledTypeMember(File.ReadAllText(hostPath)));
            string user = WriteSource(UserOwnerPath, "SignatureReferrer", BuildUserSource(CompiledTypeAddedCall));

            await RunInIntroducedTypeDomainAsync(async readArtifact =>
            {
                HotReloadOrchestratorResult refused = await RunAsync(
                    new Dictionary<string, string>
                    {
                        [hostPath] = hostWithAddition,
                        [UserOwnerPath] = user,
                        [FactoryOwnerPath] = WriteSource(
                            FactoryOwnerPath,
                            "SignatureReferrer",
                            BuildFactorySource(FactoryMakeMember))
                    });

                string description = DescribeOutcomes(refused);
                Assert.That(CountFailures(refused), Is.GreaterThan(0), description);
                Assert.That(
                    description,
                    Does.Contain("Introduced type '" + UserMetadataName + "' " + StubMessageCore
                        + ", so its method bodies run through hot reload patches, and it appears in member signatures of '"
                        + FactoryMetadataName + "'."),
                    description);
                Assert.That(
                    description,
                    Does.Contain("name '" + UserMetadataName + "' only inside method bodies of '" + FactoryMetadataName + "'"),
                    description);
                Assert.That(description, Does.Not.Contain("reload in two steps"), description);
                Assert.That(IsActive(UserMetadataName), Is.False, "A refused run must introduce nothing.");

                HotReloadOrchestratorResult bodyOnly = await RunAsync(
                    new Dictionary<string, string>
                    {
                        [hostPath] = hostWithAddition,
                        [UserOwnerPath] = user,
                        [FactoryOwnerPath] = WriteSource(
                            FactoryOwnerPath,
                            "BodyReferrer",
                            BuildFactorySource(FactoryTotalMember))
                    });

                AssertIntroduced(bodyOnly);
                Assert.That(IsActive(FactoryMetadataName), Is.True, DescribeOutcomes(bodyOnly));
                Assert.That(
                    Invoke(readArtifact(), "Total", FactoryMetadataName),
                    Is.EqualTo(CompiledTypeAddedValue),
                    DescribeOutcomes(bodyOnly));
            });
        }

        /// <summary>
        /// What: two new types that both call a member the reload adds and name each other in
        /// their signatures are not refused, because both stay in the source together, and both
        /// run their patched bodies.
        /// </summary>
        [Test]
        public async Task Run_TwoStubbedNewTypesNameEachOtherInSignatures_IntroducesBoth()
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");

            await RunInIntroducedTypeDomainAsync(async readArtifact =>
            {
                HotReloadOrchestratorResult result = await RunAsync(
                    new Dictionary<string, string>
                    {
                        [hostPath] = WriteSource(hostPath, "MutualReferrers", InsertCompiledTypeMember(File.ReadAllText(hostPath))),
                        [UserOwnerPath] = WriteSource(
                            UserOwnerPath,
                            "MutualReferrers",
                            BuildUserSource(CompiledTypeAddedCall, UserPairMember)),
                        [FactoryOwnerPath] = WriteSource(
                            FactoryOwnerPath,
                            "MutualReferrers",
                            BuildFactorySource(FactoryMakeMember + FactoryScaledMember))
                    });

                AssertIntroduced(result);
                Assert.That(IsActive(FactoryMetadataName), Is.True, DescribeOutcomes(result));
                AssertMethodRow(result, HotReloadMethodOutcomeKind.Patched, UserSimpleName + ".Run(");
                AssertMethodRow(result, HotReloadMethodOutcomeKind.Patched, FactorySimpleName + ".Scaled(");
                Assert.That(Invoke(readArtifact(), "Run"), Is.EqualTo(CompiledTypeAddedValue), DescribeOutcomes(result));
                Assert.That(
                    Invoke(readArtifact(), "Scaled", FactoryMetadataName),
                    Is.EqualTo(CompiledTypeAddedValue * 2),
                    DescribeOutcomes(result));
            });
        }

        /// <summary>
        /// What: a stubbed method taking an array, a multi-dimensional array, a nested type, a
        /// by-ref value and a constructed generic type is introduced and runs the addition. The
        /// artifact is activated only when the key the worker records for the stub is the key the
        /// transform gives the method's entry, so either side spelling one of these shapes in its
        /// own way leaves the type out.
        /// </summary>
        [Test]
        public async Task Run_StubbedMethodWithParametersOfEveryKeyShape_RunsTheAddition()
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");

            await RunInIntroducedTypeDomainAsync(async readArtifact =>
            {
                HotReloadOrchestratorResult result = await RunAsync(
                    new Dictionary<string, string>
                    {
                        [hostPath] = WriteSource(hostPath, "MixedParameters", InsertCompiledTypeMember(File.ReadAllText(hostPath))),
                        [UserOwnerPath] = WriteSource(
                            UserOwnerPath,
                            "MixedParameters",
                            BuildUserSource(CompiledTypeAddedCall, UserMixedParametersMember))
                    });

                AssertIntroduced(result);
                AssertMethodRow(
                    result,
                    HotReloadMethodOutcomeKind.Patched,
                    UserSimpleName + "." + MixedParametersMethodName + "(");
                object[] arguments =
                {
                    new[] { 1, 2 },
                    new int[2, 3],
                    Environment.SpecialFolder.Desktop,
                    0,
                    new List<int> { 5 }
                };
                Assert.That(
                    Invoke(readArtifact(), MixedParametersMethodName, arguments: arguments),
                    Is.EqualTo(CompiledTypeAddedValue + 2 + 6 + 1),
                    DescribeOutcomes(result));
                Assert.That(arguments[3], Is.EqualTo(1), "The patched body must write through the by-ref parameter.");
            });
        }

        /// <summary>
        /// What: when a reload changes the signature of the added method a new type calls and the
        /// new type's file fails in that reload, the new type keeps running the method's earlier
        /// body and the run names both of its calls; the reload that applies the new type on the
        /// new signature no longer does.
        /// </summary>
        [Test]
        public async Task Run_NewTypeFailsWhenTheAddedMethodItCallsChangesSignature_NamesItsCallsUntilItApplies()
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");
            string reshapedHost = WriteSource(
                hostPath,
                "StaleCallReshaped",
                InsertReshapedCompiledTypeMember(File.ReadAllText(hostPath)));

            await RunInIntroducedTypeDomainAsync(async readArtifact =>
            {
                HotReloadOrchestratorResult introducing = await RunAsync(
                    new Dictionary<string, string>
                    {
                        [hostPath] = WriteSource(hostPath, "StaleCallIntroducing", InsertCompiledTypeMember(File.ReadAllText(hostPath))),
                        [UserOwnerPath] = WriteSource(UserOwnerPath, "StaleCallIntroducing", BuildUserSource(CompiledTypeAddedCall))
                    });
                AssertIntroduced(introducing);
                HotReloadStaleAddedMemberCallsWarnings.AssertNone(introducing.Warnings);

                HotReloadOrchestratorResult failed = await RunAsync(
                    new Dictionary<string, string>
                    {
                        [hostPath] = reshapedHost,
                        [UserOwnerPath] = WriteSource(
                            UserOwnerPath,
                            "StaleCallFailed",
                            BreakPlainBody(BuildUserSource(ReshapedCompiledTypeAddedCall)))
                    });
                AssertMethodRow(failed, HotReloadMethodOutcomeKind.Failed, UserSimpleName + ".");
                AssertMethodRow(failed, HotReloadMethodOutcomeKind.Added, "." + CompiledTypeAddedMethodName + "(System.Int32)");
                Assert.That(failed.Warnings, Does.Contain(ExpectedStaleCallsFromTheNewType()), DescribeRun(failed));
                Assert.That(Invoke(readArtifact(), "Run"), Is.EqualTo(CompiledTypeAddedValue), DescribeRun(failed));

                HotReloadOrchestratorResult applied = await RunAsync(
                    new Dictionary<string, string>
                    {
                        [hostPath] = reshapedHost,
                        [UserOwnerPath] = WriteSource(UserOwnerPath, "StaleCallApplied", BuildUserSource(ReshapedCompiledTypeAddedCall))
                    });
                Assert.That(CountFailures(applied), Is.EqualTo(0), DescribeRun(applied));
                HotReloadStaleAddedMemberCallsWarnings.AssertNone(applied.Warnings);
                Assert.That(Invoke(readArtifact(), "Run"), Is.EqualTo(ReshapedAddedSeed + ReshapedAddedOffset), DescribeRun(applied));
                Assert.That(
                    Invoke(readArtifact(), "get_Answer"),
                    Is.EqualTo(ReshapedAddedSeed + ReshapedAddedOffset + 1),
                    DescribeRun(applied));
            });
        }

        /// <summary>
        /// What: a reload given only the file that changes the signature of the added method a new
        /// type calls leaves the new type's earlier patches running the method's earlier body, and
        /// the run names both of their calls.
        /// </summary>
        [Test]
        public async Task Run_OnlyTheHostChangesTheSignatureOfTheAddedMethodANewTypeCalls_NamesItsCalls()
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");
            string user = WriteSource(UserOwnerPath, "StaleCallHostOnly", BuildUserSource(CompiledTypeAddedCall));

            await RunInIntroducedTypeDomainAsync(async readArtifact =>
            {
                HotReloadOrchestratorResult introducing = await RunAsync(
                    new Dictionary<string, string>
                    {
                        [hostPath] = WriteSource(hostPath, "StaleCallHostOnlyIntroducing", InsertCompiledTypeMember(File.ReadAllText(hostPath))),
                        [UserOwnerPath] = user
                    });
                AssertIntroduced(introducing);

                // Why the new type's file stays in the overrides though it is not passed: it is not
                // on disk, so a run that pulls it back in has to read the source the last reload
                // applied from there.
                HotReloadOrchestratorResult result = await HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                    new[] { hostPath },
                    contentPathOverride: null,
                    CancellationToken.None,
                    new Dictionary<string, string>
                    {
                        [hostPath] = WriteSource(
                            hostPath,
                            "StaleCallHostOnlyReshaped",
                            InsertReshapedCompiledTypeMember(File.ReadAllText(hostPath))),
                        [UserOwnerPath] = user
                    });

                AssertMethodRow(result, HotReloadMethodOutcomeKind.Added, "." + CompiledTypeAddedMethodName + "(System.Int32)");
                // Why Skipped for both: the new type's file was pulled back in, not passed, so a
                // body of it that no longer binds is skipped with the file to pass rather than
                // failing the run with an error in code the reader did not touch.
                AssertMethodRow(result, HotReloadMethodOutcomeKind.Skipped, ".Run(");
                AssertMethodRow(result, HotReloadMethodOutcomeKind.Skipped, ".get_Answer(");
                Assert.That(CountMethodRows(result, HotReloadMethodOutcomeKind.Failed), Is.EqualTo(0), DescribeRun(result));
                Assert.That(result.Warnings, Does.Contain(ExpectedStaleCallsFromTheNewType()), DescribeRun(result));
                Assert.That(Invoke(readArtifact(), "Run"), Is.EqualTo(CompiledTypeAddedValue), DescribeRun(result));
            });
        }

        // Why both calls: the new type's method and its getter each call the added method, and the
        // warning lists every call left running into a retired member in ordinal order.
        private static string ExpectedStaleCallsFromTheNewType()
        {
            string retiredMember = HotReloadMethodKeys.FormatMethodLabelParts(
                new HotReloadMetadataTypeName(typeof(HotReloadCrossFileAddedMemberHost).FullName),
                CompiledTypeAddedMethodName,
                Array.Empty<string>(),
                0);
            return HotReloadStaleAddedMemberCallsWarnings.Expected(
                HotReloadStaleAddedMemberCallsWarnings.Pair(UserMethodLabel("Run"), retiredMember),
                HotReloadStaleAddedMemberCallsWarnings.Pair(UserMethodLabel("get_Answer"), retiredMember));
        }

        private static string UserMethodLabel(string methodName)
        {
            return HotReloadMethodKeys.FormatMethodLabelParts(
                new HotReloadMetadataTypeName(UserMetadataName),
                methodName,
                Array.Empty<string>(),
                0);
        }

        private static string DescribeRun(HotReloadOrchestratorResult result)
        {
            return DescribeOutcomes(result) + "\nWarnings:\n  " + string.Join("\n  ", result.Warnings);
        }

        private static void AssertIntroduced(HotReloadOrchestratorResult result)
        {
            Assert.That(CountFailures(result), Is.EqualTo(0), DescribeOutcomes(result));
            Assert.That(
                FindIntroducedTypeRow(result).Kind,
                Is.EqualTo(HotReloadIntroducedTypeOutcomeKind.Introduced),
                DescribeOutcomes(result));
        }

        private static HotReloadIntroducedTypeOutcome FindIntroducedTypeRow(HotReloadOrchestratorResult result)
        {
            foreach (HotReloadIntroducedTypeOutcome outcome in result.IntroducedTypes)
            {
                if (outcome.MetadataName == UserMetadataName)
                {
                    return outcome;
                }
            }

            Assert.Fail("No row for " + UserMetadataName + ".\n" + DescribeOutcomes(result));
            return null;
        }

        private static void AssertMethodRow(
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

        private static int CountMethodRows(HotReloadOrchestratorResult result, HotReloadMethodOutcomeKind kind)
        {
            int count = 0;
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == kind)
                {
                    count++;
                }
            }

            return count;
        }

        private static bool IsActive(string metadataName)
        {
            foreach (HotReloadIntroducedTypeDescriptor descriptor
                in HotReloadCompositionRoot.Services.Domain.IntroducedTypes.DescribeActive())
            {
                if (descriptor.MetadataName.Value == metadataName)
                {
                    return true;
                }
            }

            return false;
        }

        // Why the method is looked up by name, getters included: the patch replaces the method the
        // artifact compiled, so calling it through reflection runs whatever body is patched in.
        // Why the arguments are the caller's array: reflection writes a by-ref argument back into
        // it, which is how a caller reads what the body wrote.
        private static int Invoke(
            HotReloadIntroducedTypeArtifact artifact,
            string methodName,
            string metadataName = UserMetadataName,
            object[] arguments = null)
        {
            Assert.That(artifact, Is.Not.Null, "A reload had to prepare the type before this call.");
            Type type = artifact.Assembly.GetType(metadataName, throwOnError: false);
            Assert.That(type, Is.Not.Null, "The artifact must hold " + metadataName + ".");
            MethodInfo method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
            Assert.That(method, Is.Not.Null, metadataName + " must declare " + methodName + ".");
            return (int)method.Invoke(Activator.CreateInstance(type), arguments ?? Array.Empty<object>());
        }

        private static Task<HotReloadOrchestratorResult> RunAsync(Dictionary<string, string> edits)
        {
            return HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                new List<string>(edits.Keys).ToArray(),
                contentPathOverride: null,
                CancellationToken.None,
                edits);
        }

        private static string WriteSource(string ownerPath, string label, string contents)
        {
            return HotReloadTestSourceWriter.WriteEditedSource(
                "CallsAddedMember" + Path.GetFileNameWithoutExtension(ownerPath) + label + ".cs",
                contents);
        }

        private static string InsertCompiledTypeMember(string hostSource)
        {
            return InsertBeforeHostValue(hostSource, CompiledTypeAddedMember);
        }

        private static string InsertReshapedCompiledTypeMember(string hostSource)
        {
            return InsertBeforeHostValue(hostSource, ReshapedCompiledTypeAddedMember);
        }

        private static string InsertBeforeHostValue(string hostSource, string member)
        {
            Assert.That(hostSource, Does.Contain(HostValueAnchor), "Precondition: host value anchor must exist.");
            return hostSource.Replace(
                HostValueAnchor,
                member + HostValueAnchor,
                StringComparison.Ordinal);
        }

        // A type error in a body the new type already has fails the file's shim compile, so the
        // file keeps its earlier patches whatever its other bodies say.
        private static string BreakPlainBody(string userSource)
        {
            string plainBody = "            return " + PlainValue + ";\n";
            Assert.That(userSource, Does.Contain(plainBody), "Precondition: the plain body must exist.");
            return userSource.Replace(
                plainBody,
                "            int broken = \"not an int\";\n            return broken;\n",
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

        private static string BuildUserSource(string expression, string extraMembers = NoExtraMembers)
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
                + "\n"
                + "        public int Answer => " + expression + " + 1;\n"
                + "\n"
                + "        public int Plain()\n"
                + "        {\n"
                + "            return " + PlainValue + ";\n"
                + "        }\n"
                + extraMembers
                + "    }\n"
                + "}\n";
        }

        private static string BuildFactorySource(string members)
        {
            return
                "namespace " + Namespace + "\n"
                + "{\n"
                + "    public sealed class " + FactorySimpleName + "\n"
                + "    {\n"
                + members
                + "    }\n"
                + "}\n";
        }

        private static string BuildGenericUserSource(string expression)
        {
            return
                "namespace " + Namespace + "\n"
                + "{\n"
                + "    public sealed class " + UserSimpleName + "\n"
                + "    {\n"
                + "        public int Run<T>()\n"
                + "        {\n"
                + "            return " + expression + ";\n"
                + "        }\n"
                + "    }\n"
                + "}\n";
        }
    }
}
