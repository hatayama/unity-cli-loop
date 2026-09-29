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
    /// reload patches the real bodies in before the type is activated.
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

        private const string Namespace = "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload";
        private const string ValueSimpleName = "HotReloadCallsAddedMemberValue";
        private const string UserSimpleName = "HotReloadCallsAddedMemberUser";
        private const string UserMetadataName = Namespace + "." + UserSimpleName;
        private const string HostValueAnchor = "        public int Value()";
        private const string CompiledTypeAddedMethodName = "AddedForTheNewType";
        private const int CompiledTypeAddedValue = 41;
        private const string IntroducedTypeAddedMethodName = "Pong";
        private const int IntroducedTypeAddedValue = 9;
        private const int PlainValue = 7;
        private const int EditedOffset = 100;
        private const string NoExtraMembers = "";
        private const string StubMessageCore = "calls members that a hot reload added";

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

        private static readonly string IntroducedTypeAddedMember =
            "\n"
            + "        public int " + IntroducedTypeAddedMethodName + "()\n"
            + "        {\n"
            + "            return " + IntroducedTypeAddedValue + ";\n"
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
        private static int Invoke(HotReloadIntroducedTypeArtifact artifact, string methodName)
        {
            Assert.That(artifact, Is.Not.Null, "A reload had to prepare the type before this call.");
            Type userType = artifact.Assembly.GetType(UserMetadataName, throwOnError: false);
            Assert.That(userType, Is.Not.Null, "The artifact must hold " + UserMetadataName + ".");
            MethodInfo method = userType.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
            Assert.That(method, Is.Not.Null, UserMetadataName + " must declare " + methodName + ".");
            return (int)method.Invoke(Activator.CreateInstance(userType), Array.Empty<object>());
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
                + "\n"
                + "        public int Answer => " + expression + " + 1;\n"
                + "\n"
                + "        public int Plain()\n"
                + "        {\n"
                + "            return " + PlainValue + ";\n"
                + "        }\n"
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
