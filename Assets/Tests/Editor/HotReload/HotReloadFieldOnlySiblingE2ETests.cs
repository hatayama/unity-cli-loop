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
    /// End-to-end EditMode coverage for a file whose only hot-reloaded change is an added field or
    /// an added const: a later reload of another file that reads the added member must bring that
    /// file back, because the member exists only in its edited source.
    /// </summary>
    public class HotReloadFieldOnlySiblingE2ETests
    {
        private const string PayloadFileName = "HotReloadBindingSplitPayload.cs";
        private const string RegistryFileName = "HotReloadBindingSplitRegistry.cs";
        private const string PayloadAnchor = "        public int Value;";
        private const string RaiseInvoke = "_handler?.Invoke(new HotReloadBindingSplitPayload { Value = value });";

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
        /// What: a file that only gained a static readonly array comes back when a later reload
        /// passes only the file that reads the array, so that file is Patched instead of Failed and
        /// the field-only file is listed as re-applied with no unapplied warning.
        /// </summary>
        [Test]
        public async Task Run_ReloadOfTheReaderOnly_ReappliesTheFieldOnlyFile()
        {
            await AssertReaderOnlyReloadReappliesPayloadAsync(
                "public static readonly int[] AddedOffsets = { 5 };",
                "AddedOffsets[0]",
                expectedAddedField: true,
                expectedSummaryStart: "Also re-applied 1 unchanged file(s)");
        }

        /// <summary>
        /// What: a file that only gained a const comes back when a later reload passes only the
        /// file that reads the const, so that file is Patched instead of Failed and the const-only
        /// file is listed as re-applied with no unapplied warning.
        /// </summary>
        [Test]
        public async Task Run_ReloadOfTheReaderOnly_ReappliesTheConstOnlyFile()
        {
            await AssertReaderOnlyReloadReappliesPayloadAsync(
                "public const int AddedOffset = 5;",
                "AddedOffset",
                expectedAddedField: false,
                expectedSummaryStart: "Also brought back 1 unchanged file(s)");
        }

        /// <summary>
        /// What: passing the unchanged field-only file again still processes it rather than
        /// reporting it already active, because the hash its first reload recorded holds no
        /// active method to short-circuit on.
        /// </summary>
        [Test]
        public async Task Run_UnchangedFieldOnlyFilePassedAgain_IsProcessedAgain()
        {
            string payloadPath = FixturePath(PayloadFileName);
            string registryPath = FixturePath(RegistryFileName);
            Dictionary<string, string> overrides = new Dictionary<string, string>
            {
                [payloadPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "FieldOnlySiblingE2EPayload.cs",
                    ReplaceOnce(
                        File.ReadAllText(payloadPath),
                        PayloadAnchor,
                        PayloadAnchor + "\n        public static readonly int[] AddedOffsets = { 5 };")),
                [registryPath] = RegistryReading(registryPath, "value + HotReloadBindingSplitPayload.AddedOffsets[0]", "1")
            };
            HotReloadOrchestratorResult first = await RunAsync(new[] { payloadPath, registryPath }, overrides);
            Assert.That(first.AddedFields, Is.Not.Empty, "Precondition: the field must be added.\n" + Format(first));

            HotReloadOrchestratorResult again = await RunAsync(new[] { payloadPath }, overrides);

            Assert.That(again.AddedFields, Is.Not.Empty, Format(again));
            Assert.That(CountKind(again, HotReloadMethodOutcomeKind.AlreadyActive), Is.Zero, Format(again));
            Assert.That(CountKind(again, HotReloadMethodOutcomeKind.Failed), Is.Zero, Format(again));
            Assert.That(RaiseValue(1), Is.EqualTo(6), Format(again));
        }

        private static async Task AssertReaderOnlyReloadReappliesPayloadAsync(
            string addedMember,
            string memberRead,
            bool expectedAddedField,
            string expectedSummaryStart)
        {
            string payloadPath = FixturePath(PayloadFileName);
            string registryPath = FixturePath(RegistryFileName);
            Dictionary<string, string> overrides = new Dictionary<string, string>
            {
                [payloadPath] = HotReloadTestSourceWriter.WriteEditedSource(
                    "FieldOnlySiblingE2EPayload.cs",
                    ReplaceOnce(File.ReadAllText(payloadPath), PayloadAnchor, PayloadAnchor + "\n        " + addedMember)),
                [registryPath] = RegistryReading(registryPath, "value + HotReloadBindingSplitPayload." + memberRead, "1")
            };
            HotReloadOrchestratorResult first = await RunAsync(new[] { payloadPath, registryPath }, overrides);
            Assert.That(CountKind(first, HotReloadMethodOutcomeKind.Patched), Is.EqualTo(1), "Precondition: Raise must be Patched.\n" + Format(first));
            Assert.That(OutcomesFor(first, PayloadFileName), Is.Zero, "Precondition: the payload must write no row.\n" + Format(first));
            // Why a const-only file reports no AddedConsts: the reader compiles against the edited
            // declaration and folds the value itself, so no shim rewrite names the const.
            Assert.That(
                expectedAddedField ? first.AddedFields : first.AddedConsts,
                expectedAddedField ? Is.Not.Empty : Is.Empty,
                "Precondition: added-member report.\n" + Format(first));

            overrides[registryPath] = RegistryReading(registryPath, "value * 10 + HotReloadBindingSplitPayload." + memberRead, "2");
            HotReloadOrchestratorResult second = await RunAsync(new[] { registryPath }, overrides);

            Assert.That(CountKind(second, HotReloadMethodOutcomeKind.Failed), Is.Zero, Format(second));
            Assert.That(CountKind(second, HotReloadMethodOutcomeKind.Patched), Is.EqualTo(1), Format(second));
            Assert.That(second.ReappliedSiblingPaths, Does.Contain(ProjectRelativePath(PayloadFileName)), Format(second));
            Assert.That(
                second.Warnings,
                Has.Some.Matches<string>(
                    warning => warning.StartsWith(expectedSummaryStart)
                        && warning.Contains(ProjectRelativePath(PayloadFileName))),
                Format(second));
            Assert.That(second.Warnings, Has.None.Contains("was pulled in to re-bind"), Format(second));
            Assert.That(RaiseValue(1), Is.EqualTo(15), Format(second));
        }

        private static string RegistryReading(string registryPath, string valueExpression, string suffix)
        {
            return HotReloadTestSourceWriter.WriteEditedSource(
                "FieldOnlySiblingE2ERegistry" + suffix + ".cs",
                ReplaceOnce(
                    File.ReadAllText(registryPath),
                    RaiseInvoke,
                    "_handler?.Invoke(new HotReloadBindingSplitPayload { Value = " + valueExpression + " });"));
        }

        private static int RaiseValue(int value)
        {
            int received = 0;
            HotReloadBindingSplitRegistry registry = new HotReloadBindingSplitRegistry();
            registry.Register(payload => received = payload.Value);
            registry.Raise(value);
            return received;
        }

        private static Task<HotReloadOrchestratorResult> RunAsync(string[] files, Dictionary<string, string> overrides)
        {
            return HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                files,
                contentPathOverride: null,
                CancellationToken.None,
                new Dictionary<string, string>(overrides));
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

        private static int OutcomesFor(HotReloadOrchestratorResult result, string fileName)
        {
            int count = 0;
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.FilePath != null && outcome.FilePath.EndsWith(fileName))
                {
                    count++;
                }
            }

            return count;
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

        private static string Format(HotReloadOrchestratorResult result)
        {
            List<string> lines = new List<string>();
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                lines.Add(outcome.Kind + " " + outcome.Method + " @" + outcome.FilePath + " :: " + outcome.Reason);
            }

            lines.Add("AddedFields=" + string.Join(",", result.AddedFields));
            lines.Add("AddedConsts=" + string.Join(",", result.AddedConsts));
            lines.Add("ReappliedSiblingPaths=" + string.Join(",", result.ReappliedSiblingPaths));
            lines.AddRange(result.Warnings ?? new List<string>());
            return string.Join("\n", lines);
        }
    }
}
