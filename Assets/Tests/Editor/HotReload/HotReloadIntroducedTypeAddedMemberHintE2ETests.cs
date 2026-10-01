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
    /// End-to-end coverage of a new type that names a member hot reload adds from a place no
    /// patch can replace, such as a constructor body or an enum member. The introduced-type
    /// compilation cannot see such a member, and the failure has to say so.
    /// </summary>
    /// <remarks>
    /// Why the owners are not fixtures on disk: a .cs under Assets/ is compiled into the test
    /// assembly, and a type the compiler already lists is never introduced.
    /// </remarks>
    public class HotReloadIntroducedTypeAddedMemberHintE2ETests : HotReloadIntroducedTypeE2ETestBase
    {
        private const string UserOwnerPath =
            "Assets/Tests/Editor/HotReload/UncompiledAddedMemberHintUserOwner.cs";

        private const string Namespace = "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload";
        private const string UserSimpleName = "HotReloadAddedMemberHintUser";
        private const string HostValueAnchor = "        public int Value()";
        private const string CompiledTypeAddedMethodName = "AddedInTheSameReload";
        private const string EnumLastMemberAnchor = "        Second = 2";
        private const string AddedEnumMemberName = "Third";

        // The part of the enum-member failure hint that sends the reader to the warning.
        private const string EnumMemberHintCore = "an enum member this reload adds";

        // The part of the hint that holds whether the addition came from this reload or an
        // earlier one, so each case below checks the same sentence.
        private const string HintCore =
            "share a name with a hot reload addition, from this reload or an earlier one";

        // The part of the hint that says a constructor is one of the places no patch can reach.
        private const string UnpatchableBodiesHintCore =
            "Constructors, initializers, setters, indexers, operators and event accessors cannot.";

        private static readonly string CompiledTypeAddedMember =
            "        public int " + CompiledTypeAddedMethodName + "()\n"
            + "        {\n"
            + "            return 41;\n"
            + "        }\n"
            + "\n";

        /// <summary>
        /// What: a new type whose constructor calls a method the same reload adds to a compiled
        /// type fails to compile, because no patch replaces a constructor body and the artifact
        /// therefore cannot stub it, and the failure carries the hint rather than only the bare
        /// CS1061.
        /// </summary>
        [Test]
        public async Task Run_NewTypeConstructorCallsAMemberTheSameReloadAdds_FailsWithTheHint()
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");

            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                HotReloadOrchestratorResult result = await RunAsync(
                    new Dictionary<string, string>
                    {
                        [hostPath] = InsertCompiledTypeMember(File.ReadAllText(hostPath)),
                        [UserOwnerPath] = BuildConstructorUserSource(
                            "new HotReloadCrossFileAddedMemberHost()." + CompiledTypeAddedMethodName + "()")
                    },
                    "ConstructorSameReload");

                AssertFailsWithHint(result);
            });
        }

        /// <summary>
        /// What: a new type naming an enum member the same reload adds to a compiled enum fails
        /// to compile (CS0117), and the run still carries the added-enum-member warning with its
        /// cast workaround instead of dropping it with the rest of the unapplied run. The failure
        /// row points at that warning rather than at the generic added-member hint.
        /// </summary>
        [Test]
        public async Task Run_NewTypeNamesAnEnumMemberTheSameReloadAdds_FailsAndWarnsWithTheCast()
        {
            string enumPath = FixturePath("HotReloadSiblingEnumDefinitions.cs");

            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                HotReloadOrchestratorResult result = await RunAsync(
                    new Dictionary<string, string>
                    {
                        [enumPath] = InsertEnumMember(File.ReadAllText(enumPath)),
                        [UserOwnerPath] = BuildUserSource("(int)HotReloadSiblingEnum." + AddedEnumMemberName)
                    },
                    "EnumSameReload");

                string reason = FindIntroducedTypeFailureReason(result, "CS0117");
                Assert.That(reason, Does.Contain(EnumMemberHintCore), DescribeOutcomes(result));
                Assert.That(reason, Does.Not.Contain(HintCore), DescribeOutcomes(result));
                string warning = FindWarning(result, "enum member");
                Assert.That(warning, Does.Contain(nameof(HotReloadSiblingEnum) + ")3"), warning);
                Assert.That(warning, Does.Not.Contain("needs no compile"), warning);
                Assert.That(
                    CountWarnings(result, "enum member"),
                    Is.EqualTo(1),
                    "The enum-member warning must be reported once.\n" + string.Join("\n", result.Warnings));
            });
        }

        private static void AssertFailsWithHint(HotReloadOrchestratorResult result)
        {
            string reason = FindIntroducedTypeFailureReason(result, "CS1061");
            Assert.That(reason, Does.Contain(HintCore), DescribeOutcomes(result));
            Assert.That(reason, Does.Contain(UnpatchableBodiesHintCore), DescribeOutcomes(result));
        }

        private static string FindIntroducedTypeFailureReason(
            HotReloadOrchestratorResult result,
            string errorCode)
        {
            foreach (HotReloadIntroducedTypeOutcome outcome in result.IntroducedTypes)
            {
                if (outcome.Kind == HotReloadIntroducedTypeOutcomeKind.Failed
                    && outcome.Reason.Contains(errorCode, StringComparison.Ordinal))
                {
                    return outcome.Reason;
                }
            }

            Assert.Fail("No introduced type failed with " + errorCode + ".\n" + DescribeOutcomes(result));
            return null;
        }

        private static int CountWarnings(HotReloadOrchestratorResult result, string fragment)
        {
            int count = 0;
            foreach (string warning in result.Warnings)
            {
                if (warning.Contains(fragment, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        private static string FindWarning(HotReloadOrchestratorResult result, string fragment)
        {
            foreach (string warning in result.Warnings)
            {
                if (warning.Contains(fragment, StringComparison.Ordinal))
                {
                    return warning;
                }
            }

            Assert.Fail(
                "No warning contains '" + fragment + "'.\n"
                + string.Join("\n", result.Warnings) + "\n" + DescribeOutcomes(result));
            return null;
        }

        private static string InsertEnumMember(string enumSource)
        {
            Assert.That(enumSource, Does.Contain(EnumLastMemberAnchor), "Precondition: enum anchor must exist.");
            return enumSource.Replace(
                EnumLastMemberAnchor,
                EnumLastMemberAnchor + ",\n        " + AddedEnumMemberName + " = 3",
                StringComparison.Ordinal);
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

        private static string BuildConstructorUserSource(string expression)
        {
            return
                "namespace " + Namespace + "\n"
                + "{\n"
                + "    public sealed class " + UserSimpleName + "\n"
                + "    {\n"
                + "        private readonly int _value;\n"
                + "\n"
                + "        public " + UserSimpleName + "()\n"
                + "        {\n"
                + "            _value = " + expression + ";\n"
                + "        }\n"
                + "\n"
                + "        public int Run()\n"
                + "        {\n"
                + "            return _value;\n"
                + "        }\n"
                + "    }\n"
                + "}\n";
        }
    }
}
