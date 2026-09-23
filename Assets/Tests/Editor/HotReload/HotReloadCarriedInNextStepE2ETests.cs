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
    /// End-to-end coverage of the next step on a skipped added method that calls a type an
    /// earlier reload introduced against the compiled copy of an enum this reload builds from
    /// source. Which step works depends on what the run recorded for the enum's file, so each case
    /// sets that record up through real reloads.
    /// </summary>
    /// <remarks>
    /// Why the introduced type is not a fixture on disk: a .cs under Assets/ is compiled into the
    /// test assembly, and a type the compiler already lists is never introduced.
    /// </remarks>
    public class HotReloadCarriedInNextStepE2ETests : HotReloadIntroducedTypeE2ETestBase
    {
        private const string SinkOwnerPath = "Assets/Tests/Editor/HotReload/UncompiledCarriedInNextStepSink.cs";
        private const string EnumFileName = "HotReloadSiblingEnumDefinitions.cs";
        private const string EnumProjectRelativePath = "Assets/Tests/Editor/HotReload/" + EnumFileName;
        private const string RegistryProjectRelativePath =
            "Assets/Tests/Editor/HotReload/HotReloadCarriedInNextStepRegistry.cs";
        private const string Namespace = "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload";
        private const string SinkSimpleName = "HotReloadCarriedInNextStepSink";
        private const string EnumLastMemberAnchor = "        Second = 2";
        private const string HostValueBody = "            return 1;";
        private const string HostValueAnchor = "        public int Value()";
        private const string RegistryBody = "            return (int)kind;";
        private const string LeaveOutStart = "To hot reload it without a compile, leave '" + EnumProjectRelativePath + "' out of --files";
        private const string UndoStart = "To hot reload it without a compile, undo the edit in '" + EnumProjectRelativePath + "'";

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
        /// What: when another file of the run applies a change, the passed enum file is recorded
        /// at its current source, so the skipped row asks to undo the enum edit and leave the file
        /// out rather than only to leave it out.
        /// </summary>
        [Test]
        public async Task Run_EnumFileBesideAnAppliedChange_AsksToUndoAndLeaveOut()
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");
            string enumPath = FixturePath(EnumFileName);

            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                await IntroduceSinkAsync();

                HotReloadOrchestratorResult result = await RunAsync(
                    new Dictionary<string, string>
                    {
                        [enumPath] = InsertEnumMember(File.ReadAllText(enumPath)),
                        [hostPath] = EditHost(File.ReadAllText(hostPath), TakeKindMember, changeValue: true)
                    },
                    "UndoBesideApplied");

                string reason = FindSkippedReason(result, ".TakeKind(");
                Assert.That(reason, Does.Contain(UndoStart), DescribeOutcomes(result));
            });
        }

        /// <summary>
        /// What: when nothing in the run applies, the passed enum file is not recorded, so the
        /// skipped row asks only to leave it out, because an undo is not needed.
        /// </summary>
        [Test]
        public async Task Run_EnumFileInARunThatAppliesNothing_AsksToLeaveOutWithoutUndo()
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");
            string enumPath = FixturePath(EnumFileName);

            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                await IntroduceSinkAsync();

                HotReloadOrchestratorResult result = await RunAsync(
                    new Dictionary<string, string>
                    {
                        [enumPath] = InsertEnumMember(File.ReadAllText(enumPath)),
                        [hostPath] = EditHost(File.ReadAllText(hostPath), TakeKindMember, changeValue: false)
                    },
                    "LeaveOutNothingApplied");

                string reason = FindSkippedReason(result, ".TakeKind(");
                Assert.That(reason, Does.Contain(LeaveOutStart), DescribeOutcomes(result));
                Assert.That(reason, Does.Not.Contain("undo"), DescribeOutcomes(result));
            });
        }

        /// <summary>
        /// What: a record of the enum file's unedited source left by an earlier run does not turn
        /// the step into an undo. Undoing the edit would restore those bytes and bring the file
        /// back, so the row still asks only to leave it out.
        /// </summary>
        [Test]
        public async Task Run_EnumFileWithARecordOfItsUneditedSource_AsksToLeaveOutWithoutUndo()
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");
            string enumPath = FixturePath(EnumFileName);
            string registryPath = FixturePath("HotReloadCarriedInNextStepRegistry.cs");

            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                await IntroduceSinkAsync();
                // Why the applied change sits in another file than the host: the host has to be
                // passed again below, and passing a file holding patches applies them again, which
                // would record the edited enum file at its current source.
                HotReloadOrchestratorResult recording = await RunAsync(
                    new Dictionary<string, string>
                    {
                        [enumPath] = File.ReadAllText(enumPath),
                        [registryPath] = EditRegistry(File.ReadAllText(registryPath))
                    },
                    "StaleRecording");
                Assert.That(CountFailures(recording), Is.EqualTo(0), DescribeOutcomes(recording));
                Assert.That(
                    HotReloadCompositionRoot.Services.Domain.CompanionSources.TryGetHash(EnumProjectRelativePath),
                    Is.Not.Null,
                    "Precondition: the first run must record the unedited enum file.\n" + DescribeOutcomes(recording));

                HotReloadOrchestratorResult result = await RunAsync(
                    new Dictionary<string, string>
                    {
                        [enumPath] = InsertEnumMember(File.ReadAllText(enumPath)),
                        [hostPath] = EditHost(File.ReadAllText(hostPath), TakeKindMember, changeValue: false)
                    },
                    "StaleLeaveOut");

                string reason = FindSkippedReason(result, ".TakeKind(");
                Assert.That(reason, Does.Contain(LeaveOutStart), DescribeOutcomes(result));
                Assert.That(reason, Does.Not.Contain("undo"), DescribeOutcomes(result));
            });
        }

        /// <summary>
        /// What: an added method skipped because a compiled API still takes the compiled enum
        /// points to the row that leaves the enum file out, since that one step resolves both,
        /// instead of asking for the API's file to be passed as well.
        /// </summary>
        [Test]
        public async Task Run_CompiledSignatureRowForTheSameEnum_PointsToTheCarriedInRow()
        {
            string hostPath = FixturePath("HotReloadCrossFileAddedMemberHost.cs");
            string enumPath = FixturePath(EnumFileName);

            await RunInIntroducedTypeDomainAsync(async _ =>
            {
                await IntroduceSinkAsync();

                HotReloadOrchestratorResult result = await RunAsync(
                    new Dictionary<string, string>
                    {
                        [enumPath] = InsertEnumMember(File.ReadAllText(enumPath)),
                        [hostPath] = EditHost(
                            File.ReadAllText(hostPath),
                            TakeKindMember + AcceptKindMember,
                            changeValue: true)
                    },
                    "Pointer");

                string reason = FindSkippedReason(result, ".AcceptKind(");
                Assert.That(
                    reason,
                    Does.Contain("The step on the Skipped row for "),
                    DescribeOutcomes(result));
                Assert.That(reason, Does.Contain("TakeKind"), DescribeOutcomes(result));
                Assert.That(
                    reason,
                    Does.Not.Contain("Pass '" + RegistryProjectRelativePath + "'"),
                    DescribeOutcomes(result));
            });
        }

        private static async Task IntroduceSinkAsync()
        {
            HotReloadOrchestratorResult introducing = await RunAsync(
                new Dictionary<string, string> { [SinkOwnerPath] = BuildSinkSource() },
                "Introducing");
            Assert.That(CountFailures(introducing), Is.EqualTo(0), DescribeOutcomes(introducing));
        }

        private static string FindSkippedReason(HotReloadOrchestratorResult result, string methodFragment)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Skipped
                    && outcome.Method != null
                    && outcome.Method.Contains(methodFragment, StringComparison.Ordinal))
                {
                    return outcome.Reason;
                }
            }

            Assert.Fail("No skipped row for " + methodFragment + ".\n" + DescribeOutcomes(result));
            return null;
        }

        private static string EditHost(string hostSource, string addedMembers, bool changeValue)
        {
            Assert.That(hostSource, Does.Contain(HostValueAnchor), "Precondition: host value anchor must exist.");
            Assert.That(hostSource, Does.Contain(HostValueBody), "Precondition: host value body must exist.");
            string edited = hostSource.Replace(HostValueAnchor, addedMembers + HostValueAnchor, StringComparison.Ordinal);
            return changeValue
                ? edited.Replace(HostValueBody, "            return 2;", StringComparison.Ordinal)
                : edited;
        }

        private static string EditRegistry(string registrySource)
        {
            Assert.That(registrySource, Does.Contain(RegistryBody), "Precondition: registry body must exist.");
            return registrySource.Replace(RegistryBody, "            return (int)kind + 1;", StringComparison.Ordinal);
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
    }
}
