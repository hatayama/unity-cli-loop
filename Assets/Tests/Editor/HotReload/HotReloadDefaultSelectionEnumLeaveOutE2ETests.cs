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
    /// End-to-end coverage of a default-selection run that picks up a file whose only edit adds
    /// enum members. Such a file cannot apply anything, and building its enum from source would
    /// skip the added methods of the other files that pass the enum to compiled code or an
    /// introduced type, so the run leaves it out instead of asking the caller to.
    /// </summary>
    /// <remarks>
    /// Why the introduced type is not a fixture on disk: a .cs under Assets/ is compiled into the
    /// test assembly, and a type the compiler already lists is never introduced.
    /// </remarks>
    public class HotReloadDefaultSelectionEnumLeaveOutE2ETests : HotReloadIntroducedTypeE2ETestBase
    {
        private const string SinkOwnerPath = "Assets/Tests/Editor/HotReload/UncompiledDefaultSelectionSink.cs";
        private const string EnumFileName = "HotReloadSiblingEnumDefinitions.cs";
        private const string EnumProjectRelativePath = "Assets/Tests/Editor/HotReload/" + EnumFileName;
        private const string HostFileName = "HotReloadCrossFileAddedMemberHost.cs";
        private const string RegistryFileName = "HotReloadCarriedInNextStepRegistry.cs";
        private const string Namespace = "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload";
        private const string SinkSimpleName = "HotReloadDefaultSelectionSink";
        private const string HelperSimpleName = "HotReloadDefaultSelectionIntroducedHelper";
        private const string EnumLastMemberAnchor = "        Second = 2";
        private const string HostValueAnchor = "        public int Value()";
        private const string LeftOutWarningStart = "Left '" + EnumProjectRelativePath + "' out of this reload";

        private static readonly string TakeKindMember =
            "        public int TakeKind()\n"
            + "        {\n"
            + "            return " + SinkSimpleName + ".Take(HotReloadSiblingEnum.First);\n"
            + "        }\n"
            + "\n";

        private static readonly string AcceptKindMember =
            "        public int AcceptKind()\n"
            + "        {\n"
            + "            return HotReloadCarriedInNextStepRegistry.Accept(HotReloadSiblingEnum.First);\n"
            + "        }\n"
            + "\n";

        /// <summary>
        /// What: beside an added method that passes the enum to a type an earlier reload
        /// introduced, a default selection leaves the enum-only file out, so the added method
        /// applies, the file is named once as left out, its enum-member warning stays, and nothing
        /// is left for a compile fallback. A second default selection of the same edits leaves the
        /// file out again instead of skipping the method that now holds a patch.
        /// </summary>
        [Test]
        public async Task Run_DefaultSelectionBesideAnIntroducedTypeCaller_LeavesTheEnumFileOut()
        {
            string hostPath = FixturePath(HostFileName);
            string enumPath = FixturePath(EnumFileName);

            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                await IntroduceSinkAsync();
                Dictionary<string, string> edits = WriteEdits(
                    new Dictionary<string, string>
                    {
                        [enumPath] = InsertEnumMember(File.ReadAllText(enumPath)),
                        [hostPath] = AddHostMembers(File.ReadAllText(hostPath), TakeKindMember)
                    },
                    "DefaultLeaveOut");

                HotReloadOrchestratorResult result = await RunDefaultSelectionAsync(new[] { enumPath, hostPath }, edits);

                Assert.That(FindOutcomeKind(result, ".TakeKind("), Is.EqualTo(HotReloadMethodOutcomeKind.Added), DescribeRun(result));
                Assert.That(CountRows(result, HotReloadMethodOutcomeKind.Skipped), Is.EqualTo(0), DescribeRun(result));
                Assert.That(CountFailures(result), Is.EqualTo(0), DescribeRun(result));
                Assert.That(CountWarnings(result, LeftOutWarningStart), Is.EqualTo(1), DescribeRun(result));
                Assert.That(CountWarnings(result, "enum member "), Is.EqualTo(1), DescribeRun(result));
                // Nothing the run was asked to apply stays unapplied, so --compile-on-skip auto
                // in Edit Mode no longer compiles for this run.
                Assert.That(
                    HotReloadCompileFallbackDecider.HasUnappliedEdit(
                        result,
                        new HotReloadReappliedSiblingFiles(Array.Empty<string>(), path => path)),
                    Is.False,
                    DescribeRun(result));

                HotReloadOrchestratorResult again = await RunDefaultSelectionAsync(new[] { enumPath, hostPath }, edits);

                Assert.That(CountRows(again, HotReloadMethodOutcomeKind.Skipped), Is.EqualTo(0), DescribeRun(again));
                Assert.That(CountFailures(again), Is.EqualTo(0), DescribeRun(again));
                Assert.That(CountWarnings(again, LeftOutWarningStart), Is.EqualTo(1), DescribeRun(again));
            });
        }

        /// <summary>
        /// What: beside an added method that passes the enum to a compiled API, a default selection
        /// leaves the enum-only file out, so the added method applies instead of asking for the
        /// API's file to be passed.
        /// </summary>
        [Test]
        public async Task Run_DefaultSelectionBesideACompiledApiCaller_LeavesTheEnumFileOut()
        {
            string hostPath = FixturePath(HostFileName);
            string enumPath = FixturePath(EnumFileName);

            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                Dictionary<string, string> edits = WriteEdits(
                    new Dictionary<string, string>
                    {
                        [enumPath] = InsertEnumMember(File.ReadAllText(enumPath)),
                        [hostPath] = AddHostMembers(File.ReadAllText(hostPath), AcceptKindMember)
                    },
                    "DefaultCompiledApi");

                HotReloadOrchestratorResult result = await RunDefaultSelectionAsync(new[] { enumPath, hostPath }, edits);

                Assert.That(FindOutcomeKind(result, ".AcceptKind("), Is.EqualTo(HotReloadMethodOutcomeKind.Added), DescribeRun(result));
                Assert.That(CountRows(result, HotReloadMethodOutcomeKind.Skipped), Is.EqualTo(0), DescribeRun(result));
                Assert.That(CountWarnings(result, LeftOutWarningStart), Is.EqualTo(1), DescribeRun(result));
            });
        }

        /// <summary>
        /// What: a default selection that also introduces a type leaves the enum-only file out
        /// inside the run that prepared the type, so the type is still introduced and the added
        /// method that passes the enum to an earlier introduced type applies.
        /// </summary>
        [Test]
        public async Task Run_DefaultSelectionThatAlsoIntroducesAType_LeavesTheEnumFileOut()
        {
            string hostPath = FixturePath(HostFileName);
            string enumPath = FixturePath(EnumFileName);
            string registryPath = FixturePath(RegistryFileName);

            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                await IntroduceSinkAsync();
                Dictionary<string, string> edits = WriteEdits(
                    new Dictionary<string, string>
                    {
                        [enumPath] = InsertEnumMember(File.ReadAllText(enumPath)),
                        [hostPath] = AddHostMembers(File.ReadAllText(hostPath), TakeKindMember),
                        [registryPath] = File.ReadAllText(registryPath) + BuildHelperSource()
                    },
                    "DefaultPrepared");

                HotReloadOrchestratorResult result = await RunDefaultSelectionAsync(
                    new[] { enumPath, hostPath, registryPath },
                    edits);

                Assert.That(FindOutcomeKind(result, ".TakeKind("), Is.EqualTo(HotReloadMethodOutcomeKind.Added), DescribeRun(result));
                Assert.That(FindIntroducedKind(result, HelperSimpleName), Is.EqualTo(HotReloadIntroducedTypeOutcomeKind.Introduced), DescribeRun(result));
                Assert.That(CountFailures(result), Is.EqualTo(0), DescribeRun(result));
                Assert.That(CountWarnings(result, LeftOutWarningStart), Is.EqualTo(1), DescribeRun(result));
            });
        }

        private static async Task IntroduceSinkAsync()
        {
            Dictionary<string, string> edits = WriteEdits(
                new Dictionary<string, string> { [SinkOwnerPath] = BuildSinkSource() },
                "DefaultIntroducing");
            HotReloadOrchestratorResult introducing = await HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                new[] { SinkOwnerPath },
                contentPathOverride: null,
                CancellationToken.None,
                edits);
            Assert.That(CountFailures(introducing), Is.EqualTo(0), DescribeOutcomes(introducing));
        }

        private static HotReloadMethodOutcomeKind FindOutcomeKind(
            HotReloadOrchestratorResult result,
            string methodFragment)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Method != null && outcome.Method.Contains(methodFragment, StringComparison.Ordinal))
                {
                    return outcome.Kind;
                }
            }

            Assert.Fail("No row for " + methodFragment + ".\n" + DescribeRun(result));
            return default;
        }

        private static HotReloadIntroducedTypeOutcomeKind FindIntroducedKind(
            HotReloadOrchestratorResult result,
            string simpleName)
        {
            foreach (HotReloadIntroducedTypeOutcome outcome in result.IntroducedTypes)
            {
                if (outcome.MetadataName != null && outcome.MetadataName.EndsWith("." + simpleName, StringComparison.Ordinal))
                {
                    return outcome.Kind;
                }
            }

            Assert.Fail("No introduced type row for " + simpleName + ".\n" + DescribeRun(result));
            return default;
        }

        private static int CountRows(HotReloadOrchestratorResult result, HotReloadMethodOutcomeKind kind)
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

        private static string DescribeRun(HotReloadOrchestratorResult result)
        {
            return DescribeOutcomes(result) + "\nWarnings:\n  " + string.Join("\n  ", result.Warnings);
        }

        private static string AddHostMembers(string hostSource, string addedMembers)
        {
            Assert.That(hostSource, Does.Contain(HostValueAnchor), "Precondition: host value anchor must exist.");
            return hostSource.Replace(HostValueAnchor, addedMembers + HostValueAnchor, StringComparison.Ordinal);
        }

        private static string InsertEnumMember(string enumSource)
        {
            Assert.That(enumSource, Does.Contain(EnumLastMemberAnchor), "Precondition: enum anchor must exist.");
            return enumSource.Replace(
                EnumLastMemberAnchor,
                EnumLastMemberAnchor + ",\n        Third = 3",
                StringComparison.Ordinal);
        }

        private static string BuildSinkSource()
        {
            return
                "namespace " + Namespace + "\n"
                + "{\n"
                + "    public static class " + SinkSimpleName + "\n"
                + "    {\n"
                + "        public static int Take(HotReloadSiblingEnum kind)\n"
                + "        {\n"
                + "            return (int)kind;\n"
                + "        }\n"
                + "    }\n"
                + "}\n";
        }

        private static string BuildHelperSource()
        {
            return
                "\nnamespace " + Namespace + "\n"
                + "{\n"
                + "    public static class " + HelperSimpleName + "\n"
                + "    {\n"
                + "        public static int Seven()\n"
                + "        {\n"
                + "            return 7;\n"
                + "        }\n"
                + "    }\n"
                + "}\n";
        }

        private static Dictionary<string, string> WriteEdits(Dictionary<string, string> sources, string label)
        {
            Dictionary<string, string> edits = new Dictionary<string, string>();
            foreach (KeyValuePair<string, string> source in sources)
            {
                edits[source.Key] = HotReloadTestSourceWriter.WriteEditedSource(
                    Path.GetFileNameWithoutExtension(source.Key) + label + ".cs",
                    source.Value);
            }

            return edits;
        }

        private static Task<HotReloadOrchestratorResult> RunDefaultSelectionAsync(
            string[] files,
            Dictionary<string, string> edits)
        {
            return HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                files,
                contentPathOverride: null,
                CancellationToken.None,
                new Dictionary<string, string>(edits),
                isDefaultSelection: true);
        }
    }
}
