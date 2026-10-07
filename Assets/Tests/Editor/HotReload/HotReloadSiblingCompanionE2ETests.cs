using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// End-to-end EditMode coverage for the files a later reload of the same assembly brings back
    /// beside its active files: an unchanged file an earlier reload was given only so an added
    /// method could bind, and a file whose earlier reload left it Skipped or Failed.
    /// </summary>
    public class HotReloadSiblingCompanionE2ETests
    {
        private const string HostFileName = "HotReloadBindingSplitHost.cs";
        private const string PayloadFileName = "HotReloadBindingSplitPayload.cs";
        private const string RegistryFileName = "HotReloadBindingSplitRegistry.cs";
        private const string UnrelatedFileName = "HotReloadAddedFieldApplyFixture.cs";
        private const string HostInsertionAnchor = "        public int Handled => _handled;";
        private const string WireMethod =
            "\n\n        public void Wire()\n        {\n            _registry.Register(p => Handle(p));\n        }";
        private const string HandleBody = "_handled += payload.Value;";
        private const string PayloadScaledBody = "return Value * 2;";
        private const string UnrelatedBody = "return 0;";
        private const string RegistryAssignment = "_handler = handler;";

        private HotReloadDomainTestScope _scope;

        [SetUp]
        public void SetUp()
        {
            _scope = new HotReloadDomainTestScope();
            HotReloadAutoRefreshHold.SyncToActiveChanges();
        }

        [TearDown]
        public void TearDown()
        {
            _scope.Dispose();
            HotReloadAutoRefreshHold.SyncToActiveChanges();
            VibeLogger.ClearMemoryLogs();
        }

        /// <summary>
        /// What: an unchanged file passed only so an added method could bind comes back into a later
        /// reload of an unrelated file together with the active files it was passed beside, so the
        /// added method still binds instead of turning Skipped.
        /// </summary>
        [Test]
        public async Task Run_UnrelatedReloadAfterCompanionReload_ReadmitsTheCompanionSoTheAddedMethodStillBinds()
        {
            Dictionary<string, string> overrides = PayloadAndHostOverrides(WireMethod);
            await RunWireWithRegistryAsync(overrides);

            HotReloadOrchestratorResult second = await RunAsync(UnrelatedEdit(overrides, "return 1;"), overrides);

            Assert.That(second.ReappliedSiblingPaths, Does.Contain(ProjectRelativePath(RegistryFileName)), FormatOutcomes(second));
            Assert.That(FindWire(second, HotReloadMethodOutcomeKind.Skipped), Is.Null, FormatOutcomes(second));
        }

        /// <summary>
        /// What: a companion whose explicit reload Failed and whose source was then restored comes
        /// back into a later unrelated reload by its companion hash, even though its Failed record
        /// names the broken bytes, so the added method still binds.
        /// </summary>
        [Test]
        public async Task Run_CompanionRestoredAfterAFailedReload_IsReadmittedByItsCompanionHash()
        {
            Dictionary<string, string> overrides = PayloadAndHostOverrides(WireMethod);
            await RunWireWithRegistryAsync(overrides);
            string registryPath = FixturePath(RegistryFileName);
            overrides[registryPath] = HotReloadTestSourceWriter.WriteEditedSource(
                "SiblingCompanionE2EBrokenRegistry.cs",
                ReplaceOnce(File.ReadAllText(registryPath), RegistryAssignment, "_handler = 42;"));
            HotReloadOrchestratorResult broken = await RunAsync(new[] { registryPath }, overrides);
            Assert.That(CountKind(broken, HotReloadMethodOutcomeKind.Failed), Is.GreaterThan(0), "Precondition: Register must fail.\n" + FormatOutcomes(broken));
            overrides.Remove(registryPath);

            HotReloadOrchestratorResult restored = await RunAsync(UnrelatedEdit(overrides, "return 1;"), overrides);

            Assert.That(restored.ReappliedSiblingPaths, Does.Contain(ProjectRelativePath(RegistryFileName)), FormatOutcomes(restored));
            Assert.That(FindWire(restored, HotReloadMethodOutcomeKind.Skipped), Is.Null, FormatOutcomes(restored));
        }

        /// <summary>
        /// What: a companion recorded while its source was edited and then restored is warned
        /// about once by the next unrelated reload and forgotten, so the reload after that does
        /// not repeat the warning.
        /// </summary>
        [Test]
        public async Task Run_CompanionRestoredAfterItWasRecordedEdited_WarnsOnlyOnce()
        {
            Dictionary<string, string> overrides = PayloadAndHostOverrides(WireMethod);
            string registryPath = FixturePath(RegistryFileName);
            overrides[registryPath] = HotReloadTestSourceWriter.WriteEditedSource(
                "SiblingCompanionE2ECommentedRegistry.cs",
                ReplaceOnce(File.ReadAllText(registryPath), RegistryAssignment, "// edited\n            " + RegistryAssignment));
            await RunWireWithRegistryAsync(overrides);
            Assert.That(
                HotReloadCompositionRoot.Services.Domain.CompanionSources.TryGetHash(ProjectRelativePath(RegistryFileName)),
                Is.Not.Null,
                "Precondition: the edited registry must be recorded as a companion.");
            overrides.Remove(registryPath);

            HotReloadOrchestratorResult second = await RunAsync(UnrelatedEdit(overrides, "return 1;"), overrides);
            Assert.That(CountChangedCompanionWarnings(second), Is.EqualTo(1), FormatOutcomes(second));

            HotReloadOrchestratorResult third = await RunAsync(UnrelatedEdit(overrides, "return 2;"), overrides);
            Assert.That(CountChangedCompanionWarnings(third), Is.Zero, FormatOutcomes(third));
        }

        /// <summary>
        /// What: after --revert-all the companion is still remembered, so reloading the host and the
        /// payload again brings the registry back without the reader listing it a second time.
        /// </summary>
        [Test]
        public async Task Run_SiblingsReloadedAgainAfterRevertAll_ReadmitsTheCompanion()
        {
            Dictionary<string, string> overrides = PayloadAndHostOverrides(WireMethod);
            await RunWireWithRegistryAsync(overrides);
            HotReloadCompositionRoot.Services.StatusExecutor.ExecuteRevertAll();

            HotReloadOrchestratorResult second = await RunAsync(
                new[] { FixturePath(HostFileName), FixturePath(PayloadFileName) },
                overrides);

            Assert.That(second.ReappliedSiblingPaths, Does.Contain(ProjectRelativePath(RegistryFileName)), FormatOutcomes(second));
            Assert.That(FindWire(second, HotReloadMethodOutcomeKind.Added), Is.Not.Null, FormatOutcomes(second));
        }

        /// <summary>
        /// What: a host whose added method was Skipped as a sibling is tried again when a later
        /// reload passes the file the method needs, so the method is Added without the reader
        /// passing the host again.
        /// </summary>
        [Test]
        public async Task Run_ReloadThatCanBindASkippedSibling_RetriesTheSiblingAndAddsTheMethod()
        {
            Dictionary<string, string> overrides = PayloadAndHostOverrides(WireMethod);
            await SkipWireAsSiblingAsync(overrides);

            HotReloadOrchestratorResult third = await RunAsync(
                new[] { FixturePath(RegistryFileName), FixturePath(PayloadFileName) },
                overrides);

            Assert.That(third.ReappliedSiblingPaths, Does.Contain(ProjectRelativePath(HostFileName)), FormatOutcomes(third));
            Assert.That(FindWire(third, HotReloadMethodOutcomeKind.Added), Is.Not.Null, FormatOutcomes(third));
        }

        /// <summary>
        /// What: a Skipped sibling that stays Skipped when retried is not retried by the next
        /// unrelated reload, and passing it explicitly afterwards still processes its methods again
        /// instead of reporting them AlreadyActive.
        /// </summary>
        [Test]
        public async Task Run_RetryThatStaysSkipped_IsNotRetriedAgainAndAnExplicitReloadReprocessesTheFile()
        {
            Dictionary<string, string> overrides = PayloadAndHostOverrides(WireMethod);
            await SkipWireAsSiblingAsync(overrides);

            HotReloadOrchestratorResult retried = await RunAsync(UnrelatedEdit(overrides, "return 1;"), overrides);
            Assert.That(retried.ReappliedSiblingPaths, Does.Contain(ProjectRelativePath(HostFileName)), FormatOutcomes(retried));
            Assert.That(FindWire(retried, HotReloadMethodOutcomeKind.Skipped), Is.Not.Null, FormatOutcomes(retried));

            HotReloadOrchestratorResult next = await RunAsync(UnrelatedEdit(overrides, "return 2;"), overrides);
            Assert.That(next.ReappliedSiblingPaths, Does.Not.Contain(ProjectRelativePath(HostFileName)), FormatOutcomes(next));

            HotReloadOrchestratorResult explicitRun = await RunAsync(new[] { FixturePath(HostFileName) }, overrides);
            Assert.That(FindWire(explicitRun, HotReloadMethodOutcomeKind.Skipped), Is.Not.Null, FormatOutcomes(explicitRun));
            Assert.That(CountKind(explicitRun, HotReloadMethodOutcomeKind.AlreadyActive), Is.Zero, FormatOutcomes(explicitRun));
        }

        /// <summary>
        /// What: a file whose edited body Failed to compile is tried once by the next reload of the assembly
        /// and not again by the one after, while the unrelated file those reloads pass still applies.
        /// </summary>
        [Test]
        public async Task Run_FailedFile_IsRetriedOnceAndTheUnrelatedFileStillApplies()
        {
            Dictionary<string, string> overrides = await FailHostAsync();

            HotReloadOrchestratorResult retried = await RunAsync(UnrelatedEdit(overrides, "return 1;"), overrides);
            Assert.That(retried.ReappliedSiblingPaths, Does.Contain(ProjectRelativePath(HostFileName)), FormatOutcomes(retried));
            Assert.That(new HotReloadAddedFieldApplyFixture().ReadAdded(), Is.EqualTo(1), FormatOutcomes(retried));

            HotReloadOrchestratorResult next = await RunAsync(UnrelatedEdit(overrides, "return 2;"), overrides);
            Assert.That(next.ReappliedSiblingPaths, Does.Not.Contain(ProjectRelativePath(HostFileName)), FormatOutcomes(next));
            Assert.That(new HotReloadAddedFieldApplyFixture().ReadAdded(), Is.EqualTo(2), FormatOutcomes(next));
        }

        /// <summary>
        /// What: a file whose edited body Failed and that was then reverted to the source the assembly
        /// was compiled from is forgotten by the next reload of the assembly: no warning that its source
        /// changed since, no retry, and no record left, while the unrelated file still applies. The
        /// reload after that does not warn either.
        /// </summary>
        [Test]
        public async Task Run_FailedFileRevertedToItsCompiledSource_IsForgottenWithoutAWarning()
        {
            string hostRelative = ProjectRelativePath(HostFileName);
            Dictionary<string, string> overrides = await FailHostAsync();
            overrides.Remove(FixturePath(HostFileName));

            HotReloadOrchestratorResult reverted = await RunAsync(UnrelatedEdit(overrides, "return 1;"), overrides);
            Assert.That(ChangedSinceWarnings(reverted), Is.Empty, FormatOutcomes(reverted));
            Assert.That(reverted.ReappliedSiblingPaths, Does.Not.Contain(hostRelative), FormatOutcomes(reverted));
            Assert.That(new HotReloadAddedFieldApplyFixture().ReadAdded(), Is.EqualTo(1), FormatOutcomes(reverted));
            Assert.That(Domain.AppliedSources.TryGetAppliedSource(hostRelative), Is.Null, FormatOutcomes(reverted));

            HotReloadOrchestratorResult next = await RunAsync(UnrelatedEdit(overrides, "return 2;"), overrides);
            Assert.That(ChangedSinceWarnings(next), Is.Empty, FormatOutcomes(next));
            Assert.That(next.ReappliedSiblingPaths, Does.Not.Contain(hostRelative), FormatOutcomes(next));
            Assert.That(new HotReloadAddedFieldApplyFixture().ReadAdded(), Is.EqualTo(2), FormatOutcomes(next));
        }

        /// <summary>
        /// What: a file whose edited body Failed and that was then edited into something other than
        /// its compiled source is still reported as changed since, and its record is kept.
        /// </summary>
        [Test]
        public async Task Run_FailedFileEditedAgain_IsStillReportedAsChanged()
        {
            string hostPath = FixturePath(HostFileName);
            string hostRelative = ProjectRelativePath(HostFileName);
            Dictionary<string, string> overrides = await FailHostAsync();
            overrides[hostPath] = HotReloadTestSourceWriter.WriteEditedSource(
                "SiblingCompanionE2EEditedHost.cs",
                ReplaceOnce(File.ReadAllText(hostPath), HandleBody, "_handled += payload.Value + 1;"));

            HotReloadOrchestratorResult edited = await RunAsync(UnrelatedEdit(overrides, "return 1;"), overrides);

            Assert.That(
                edited.Warnings,
                Does.Contain(string.Format(HotReloadConstants.RetrySiblingChangedSinceSkipWarningFormat, hostRelative)),
                FormatOutcomes(edited));
            Assert.That(Domain.AppliedSources.TryGetAppliedSource(hostRelative), Is.Not.Null, FormatOutcomes(edited));
        }

        /// <summary>
        /// What: a companion recorded while edited, whose explicit reload then Failed and whose source
        /// was then reverted to its compiled source, is forgotten by the next reload of the assembly:
        /// no changed-since warning of any kind, and neither its applied-source record nor its
        /// companion record left. The reload after that does not warn either.
        /// </summary>
        [Test]
        public async Task Run_FailedCompanionRevertedToItsCompiledSource_IsForgottenWithoutAWarning()
        {
            Dictionary<string, string> overrides = PayloadAndHostOverrides(WireMethod);
            string registryPath = FixturePath(RegistryFileName);
            string registryRelative = ProjectRelativePath(RegistryFileName);
            overrides[registryPath] = HotReloadTestSourceWriter.WriteEditedSource(
                "SiblingCompanionE2ECommentedRegistry.cs",
                ReplaceOnce(File.ReadAllText(registryPath), RegistryAssignment, "// edited\n            " + RegistryAssignment));
            await RunWireWithRegistryAsync(overrides);
            Assert.That(Domain.CompanionSources.TryGetHash(registryRelative), Is.Not.Null, "Precondition: the edited registry must be recorded as a companion.");
            overrides[registryPath] = HotReloadTestSourceWriter.WriteEditedSource(
                "SiblingCompanionE2EBrokenRegistry.cs",
                ReplaceOnce(File.ReadAllText(registryPath), RegistryAssignment, "_handler = 42;"));
            HotReloadOrchestratorResult broken = await RunAsync(new[] { registryPath }, overrides);
            Assert.That(CountKind(broken, HotReloadMethodOutcomeKind.Failed), Is.GreaterThan(0), "Precondition: Register must fail.\n" + FormatOutcomes(broken));
            Assert.That(Domain.AppliedSources.ListNotFullyAppliedSourcePaths(), Does.Contain(registryRelative), "Precondition: the Failed reload must leave the registry to retry.");
            Assert.That(Domain.CompanionSources.TryGetHash(registryRelative), Is.Not.Null, "Precondition: the Failed reload must keep the companion record.");
            overrides.Remove(registryPath);

            HotReloadOrchestratorResult reverted = await RunAsync(UnrelatedEdit(overrides, "return 1;"), overrides);
            Assert.That(ChangedSinceWarnings(reverted), Is.Empty, FormatOutcomes(reverted));
            Assert.That(Domain.AppliedSources.TryGetAppliedSource(registryRelative), Is.Null, FormatOutcomes(reverted));
            Assert.That(Domain.CompanionSources.TryGetHash(registryRelative), Is.Null, FormatOutcomes(reverted));

            HotReloadOrchestratorResult next = await RunAsync(UnrelatedEdit(overrides, "return 2;"), overrides);
            Assert.That(ChangedSinceWarnings(next), Is.Empty, FormatOutcomes(next));
        }

        private static HotReloadDomain Domain => HotReloadCompositionRoot.Services.Domain;

        // Passes the host with a Handle body that does not compile, and returns the overrides that
        // still hold that broken host.
        private static async Task<Dictionary<string, string>> FailHostAsync()
        {
            string hostPath = FixturePath(HostFileName);
            Dictionary<string, string> overrides = new Dictionary<string, string>
            {
                [hostPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "SiblingCompanionE2EBrokenHost.cs",
                    ReplaceOnce(File.ReadAllText(hostPath), HandleBody, "int broken = \"not an int\";\n            _handled += broken;"))
            };
            HotReloadOrchestratorResult first = await RunAsync(new[] { hostPath }, overrides);
            Assert.That(CountKind(first, HotReloadMethodOutcomeKind.Failed), Is.GreaterThan(0), "Precondition: Handle must fail.\n" + FormatOutcomes(first));
            Assert.That(Domain.AppliedSources.ListNotFullyAppliedSourcePaths(), Does.Contain(ProjectRelativePath(HostFileName)), "Precondition: the Failed reload must leave the host to retry.");
            return overrides;
        }

        private static async Task RunWireWithRegistryAsync(Dictionary<string, string> overrides)
        {
            HotReloadOrchestratorResult first = await RunAsync(
                new[] { FixturePath(PayloadFileName), FixturePath(HostFileName), FixturePath(RegistryFileName) },
                overrides);
            Assert.That(FindWire(first, HotReloadMethodOutcomeKind.Added), Is.Not.Null, "Precondition: Wire must be Added.\n" + FormatOutcomes(first));
            TestContext.WriteLine("Companion reload:\n" + FormatOutcomes(first) + "\nUnchangedTotal=" + first.UnchangedTotal);
        }

        private static async Task SkipWireAsSiblingAsync(Dictionary<string, string> overrides)
        {
            HotReloadOrchestratorResult first = await RunAsync(new[] { FixturePath(HostFileName) }, overrides);
            Assert.That(FindWire(first, HotReloadMethodOutcomeKind.Added), Is.Not.Null, "Precondition: Wire must be Added.\n" + FormatOutcomes(first));

            HotReloadOrchestratorResult second = await RunAsync(new[] { FixturePath(PayloadFileName) }, overrides);
            Assert.That(second.ReappliedSiblingPaths, Does.Contain(ProjectRelativePath(HostFileName)), "Precondition: host must come back as a sibling.\n" + FormatOutcomes(second));
            Assert.That(FindWire(second, HotReloadMethodOutcomeKind.Skipped), Is.Not.Null, "Precondition: Wire must be Skipped.\n" + FormatOutcomes(second));
        }

        private static Dictionary<string, string> PayloadAndHostOverrides(string addedHostMember)
        {
            string hostPath = FixturePath(HostFileName);
            string payloadPath = FixturePath(PayloadFileName);
            return new Dictionary<string, string>
            {
                [payloadPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "SiblingCompanionE2EPayload.cs",
                    ReplaceOnce(File.ReadAllText(payloadPath), PayloadScaledBody, "return Value * 3;")),
                [hostPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "SiblingCompanionE2EHost.cs",
                    ReplaceOnce(File.ReadAllText(hostPath), HostInsertionAnchor, HostInsertionAnchor + addedHostMember))
            };
        }

        // Writes the unrelated file's edit into the override map and returns the files to pass.
        private static string[] UnrelatedEdit(Dictionary<string, string> overrides, string newBody)
        {
            string unrelatedPath = FixturePath(UnrelatedFileName);
            overrides[unrelatedPath] = HotReloadTestSourceWriter.WriteEditedSource(
                "SiblingCompanionE2EUnrelated.cs",
                ReplaceOnce(File.ReadAllText(unrelatedPath), UnrelatedBody, newBody));
            return new[] { unrelatedPath };
        }

        private static Task<HotReloadOrchestratorResult> RunAsync(string[] files, Dictionary<string, string> overrides)
        {
            return HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                files,
                contentPathOverride: null,
                CancellationToken.None,
                new Dictionary<string, string>(overrides));
        }

        private static HotReloadMethodOutcome FindWire(HotReloadOrchestratorResult result, HotReloadMethodOutcomeKind kind)
        {
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == kind && outcome.Method != null && outcome.Method.Contains(".Wire("))
                {
                    return outcome;
                }
            }

            return null;
        }

        private static int CountKind(HotReloadOrchestratorResult result, HotReloadMethodOutcomeKind kind)
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

        private static int CountChangedCompanionWarnings(HotReloadOrchestratorResult result)
        {
            string registryPath = ProjectRelativePath(RegistryFileName);
            int count = 0;
            foreach (string warning in result.Warnings ?? new List<string>())
            {
                if (warning.Contains(registryPath) && warning.Contains("but its source changed since"))
                {
                    count++;
                }
            }

            return count;
        }

        // Every changed-source warning a reload gives about a sibling holds this phrase, whether the
        // sibling had active patches, was left Skipped or Failed, or was a companion.
        private static List<string> ChangedSinceWarnings(HotReloadOrchestratorResult result)
        {
            List<string> warnings = new List<string>();
            foreach (string warning in result.Warnings ?? new List<string>())
            {
                if (warning.Contains("its source changed since"))
                {
                    warnings.Add(warning);
                }
            }

            return warnings;
        }

        private static string ReplaceOnce(string source, string anchor, string replacement)
        {
            Assert.That(source, Does.Contain(anchor), "Precondition: anchor must exist.");
            return source.Replace(anchor, replacement);
        }

        private static string FixturePath(string fileName)
        {
            string path = Path.GetFullPath(
                Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", fileName));
            Assert.That(File.Exists(path), Is.True, "Fixture missing: " + path);
            return path;
        }

        private static string ProjectRelativePath(string fileName)
        {
            return "Assets/Tests/Editor/HotReload/" + fileName;
        }

        private static string FormatOutcomes(HotReloadOrchestratorResult result)
        {
            List<string> lines = new List<string>();
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                lines.Add(outcome.Kind + " " + outcome.Method + " @" + outcome.FilePath + " :: " + outcome.Reason);
            }

            lines.Add("ReappliedSiblingPaths=" + string.Join(",", result.ReappliedSiblingPaths));
            lines.AddRange(result.Warnings ?? new List<string>());
            return string.Join("\n", lines);
        }
    }
}
