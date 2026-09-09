using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;

using NUnit.Framework;

using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Verifies Play-entry drop record and recovery decisions without driving Editor events,
    /// including the recovery the hot-reload tool entry performs after an apply.
    /// </summary>
    [TestFixture]
    public sealed class HotReloadPlayModeEntryDropRecorderTests
    {
        private HotReloadPlayModeEntryDropLedgerSessionScope _ledgerSessionScope;
        private Func<IReadOnlyList<string>, CancellationToken, Task<HotReloadOrchestratorResult>> _previousApply;

        [SetUp]
        public void SetUp()
        {
            _ledgerSessionScope = new HotReloadPlayModeEntryDropLedgerSessionScope();
            _previousApply = HotReloadTool.RunApplyAsyncForTesting;
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            HotReloadAutoRefreshHold.SyncToActiveChanges();
        }

        // Why syncing rather than releasing: one test applies a real patch and introduces a type,
        // the Auto Refresh hold flag is shared by the whole run, and by this point the live
        // registry is back, which may hold introduced types of its own that still warrant a hold.
        [TearDown]
        public void TearDown()
        {
            HotReloadTool.RunApplyAsyncForTesting = _previousApply;
            HotReloadCompositionRoot.Services.Patcher.RevertAll();
            HotReloadAutoRefreshHold.SyncToActiveChanges();
            _ledgerSessionScope.Restore();
        }

        /// <summary>
        /// What: only ExitingEditMode with domain reload and at least one identity records.
        /// </summary>
        [Test]
        public void ShouldRecord_RequiresExitingEditModeEnabledReloadAndIdentities()
        {
            Assert.That(
                HotReloadPlayModeEntryDropRecorder.ShouldRecord(
                    PlayModeStateChange.ExitingEditMode,
                    isDomainReloadDisabledOnEnterPlayMode: false,
                    activeIdentityCount: 2),
                Is.True);
            Assert.That(
                HotReloadPlayModeEntryDropRecorder.ShouldRecord(
                    PlayModeStateChange.EnteredPlayMode,
                    isDomainReloadDisabledOnEnterPlayMode: false,
                    activeIdentityCount: 2),
                Is.False);
            Assert.That(
                HotReloadPlayModeEntryDropRecorder.ShouldRecord(
                    PlayModeStateChange.ExitingEditMode,
                    isDomainReloadDisabledOnEnterPlayMode: true,
                    activeIdentityCount: 2),
                Is.False);
            Assert.That(
                HotReloadPlayModeEntryDropRecorder.ShouldRecord(
                    PlayModeStateChange.ExitingEditMode,
                    isDomainReloadDisabledOnEnterPlayMode: false,
                    activeIdentityCount: 0),
                Is.False);
        }

        /// <summary>
        /// What: a failed compile keeps the leftover identities.
        /// </summary>
        [Test]
        public void NotifyCompilationFinished_WhenErrorCountIsPositive_KeepsIdentities()
        {
            HotReloadPlayModeEntryDropLedger.Record(new[] { "Type.A()", "Type.B()" });

            HotReloadPlayModeEntryDropRecorder.NotifyCompilationFinished(1);

            Assert.That(
                HotReloadPlayModeEntryDropLedger.GetIdentities(),
                Is.EqualTo(new[] { "Type.A()", "Type.B()" }));
        }

        /// <summary>
        /// What: a successful compile clears every leftover identity.
        /// </summary>
        [Test]
        public void NotifyCompilationFinished_WhenErrorCountIsZero_ClearsIdentities()
        {
            HotReloadPlayModeEntryDropLedger.Record(new[] { "Type.A()" });

            HotReloadPlayModeEntryDropRecorder.NotifyCompilationFinished(0);

            Assert.That(HotReloadPlayModeEntryDropLedger.Count, Is.EqualTo(0));
        }

        /// <summary>
        /// What: apply removes only Patched and Added identities from the leftover set.
        /// </summary>
        [Test]
        public void NotifyApplyRecovered_RemovesOnlyPatchedAndAddedIdentities()
        {
            HotReloadPlayModeEntryDropLedger.Record(
                new[] { "Type.Patched()", "Type.Added()", "Type.Failed()", "Type.Skipped()" });

            HotReloadPlayModeEntryDropRecorder.NotifyApplyRecovered(
                new List<HotReloadMethodOutcome>
                {
                    HotReloadMethodOutcome.Patched("Type.Patched()", "Assets/A.cs"),
                    HotReloadMethodOutcome.Added("Type.Added()", "Assets/A.cs"),
                    HotReloadMethodOutcome.Failed("Type.Failed()", "reason", "Assets/A.cs"),
                    HotReloadMethodOutcome.Skipped("Type.Skipped()", "reason", "Assets/A.cs")
                },
                new List<HotReloadIntroducedTypeOutcome>());

            Assert.That(
                HotReloadPlayModeEntryDropLedger.GetIdentities(),
                Is.EqualTo(new[] { "Type.Failed()", "Type.Skipped()" }));
        }

        /// <summary>
        /// What: revert-all clears every leftover identity.
        /// </summary>
        [Test]
        public void NotifyRevertAll_ClearsIdentities()
        {
            HotReloadPlayModeEntryDropLedger.Record(new[] { "Type.A()" });

            HotReloadPlayModeEntryDropRecorder.NotifyRevertAll();

            Assert.That(HotReloadPlayModeEntryDropLedger.Count, Is.EqualTo(0));
        }

        /// <summary>
        /// What: a same-domain EnteredEditMode after ExitingEditMode means Play entry
        /// was cancelled, so the just-recorded identities leave the ledger and older
        /// leftovers stay.
        /// </summary>
        [Test]
        public void NotifyPlayModeStateChanged_WhenEnteredEditModeFollowsExitingEditModeInSameDomain_RemovesOnlyPendingIdentities()
        {
            HotReloadPlayModeEntryDropLedger.Record(new[] { "Type.Old()" });

            HotReloadPlayModeEntryDropRecorder.NotifyPlayModeStateChanged(
                PlayModeStateChange.ExitingEditMode,
                new[] { "Type.Active()" },
                isDomainReloadDisabledOnEnterPlayMode: false);

            Assert.That(
                HotReloadPlayModeEntryDropLedger.GetIdentities(),
                Is.EqualTo(new[] { "Type.Active()", "Type.Old()" }));

            HotReloadPlayModeEntryDropRecorder.NotifyPlayModeStateChanged(
                PlayModeStateChange.EnteredEditMode,
                new[] { "Type.Active()" },
                isDomainReloadDisabledOnEnterPlayMode: false);

            Assert.That(
                HotReloadPlayModeEntryDropLedger.GetIdentities(),
                Is.EqualTo(new[] { "Type.Old()" }));
        }
        /// <summary>
        /// What: a reload that both patched a method and introduced a type offers both to the
        /// ledger, so Play entry records the type it is about to unload as well as the patch.
        /// </summary>
        [Test]
        public async Task CollectActiveIdentities_AfterARunThatPatchedAMethodAndIntroducedAType_ReturnsBoth()
        {
            using (HotReloadCompositionRoot.BeginReplacement(HotReloadCompositionRoot.CreateProductionServices()))
            {
                try
                {
                    HotReloadOrchestratorResult result = await RunPatchingABodyAndIntroducingATypeAsync();

                    Assert.That(
                        result.ActivePatchTotal,
                        Is.EqualTo(1),
                        "Precondition: the run must have patched exactly one method.");
                    Assert.That(
                        result.IntroducedTypes.Count,
                        Is.EqualTo(1),
                        "Precondition: the run must have introduced exactly one type.");
                    HotReloadIntroducedTypeOutcome introduced = result.IntroducedTypes[0];

                    List<string> identities =
                        new List<string>(HotReloadPlayModeEntryDropRecorder.CollectActiveIdentities());

                    Assert.That(
                        identities.Count,
                        Is.EqualTo(2),
                        "One patch and one type are two changes the reload would discard.");
                    Assert.That(
                        identities,
                        Does.Contain(
                            HotReloadPlayModeEntryDropIdentity.ForType(
                                introduced.OriginalAssemblyName,
                                introduced.MetadataName)),
                        "The type must be recorded in the shape a later apply can recover.");
                    Assert.That(
                        identities.FindAll(identity => identity.Contains("Scaled")).Count,
                        Is.EqualTo(1),
                        "The patched method must still be collected next to the type.");
                }
                finally
                {
                    // The patch belongs to the replacement domain, and the TearDown revert runs
                    // after the scope has already put the outer domain back, which knows nothing
                    // about it and would leave it live in Harmony.
                    HotReloadCompositionRoot.Services.Patcher.RevertAll();
                }
            }
        }

        /// <summary>
        /// What: an apply removes the types it introduced or found already active from the
        /// leftover set and keeps the one it failed to introduce.
        /// </summary>
        [Test]
        public void NotifyApplyRecovered_RemovesIntroducedAndAlreadyActiveTypesOnly()
        {
            string introducedIdentity = HotReloadPlayModeEntryDropIdentity.ForType("Fixture.Assembly", "Fixture.T1");
            string alreadyActiveIdentity = HotReloadPlayModeEntryDropIdentity.ForType("Fixture.Assembly", "Fixture.T2");
            string failedIdentity = HotReloadPlayModeEntryDropIdentity.ForType("Fixture.Assembly", "Fixture.T3");
            HotReloadPlayModeEntryDropLedger.Record(
                new[] { introducedIdentity, alreadyActiveIdentity, failedIdentity, "Type.Kept()" });

            HotReloadPlayModeEntryDropRecorder.NotifyApplyRecovered(
                new List<HotReloadMethodOutcome>(),
                new List<HotReloadIntroducedTypeOutcome>
                {
                    HotReloadIntroducedTypeOutcome.Introduced("Fixture.T1", "Fixture.Assembly", "Assets/A.cs"),
                    HotReloadIntroducedTypeOutcome.AlreadyActive("Fixture.T2", "Fixture.Assembly", "Assets/A.cs"),
                    HotReloadIntroducedTypeOutcome.Failed("Fixture.T3", "Fixture.Assembly", "Assets/A.cs", "reason")
                });

            Assert.That(
                HotReloadPlayModeEntryDropLedger.GetIdentities(),
                Is.EquivalentTo(new[] { failedIdentity, "Type.Kept()" }),
                "A type the apply could not introduce is still lost, so its record stays.");
        }

        /// <summary>
        /// What: a domain with no patch, no added member, and no introduced type collects nothing,
        /// which is what keeps Play entry from recording a drop that never happened.
        /// </summary>
        [Test]
        public void CollectActiveIdentities_WithNoActiveChange_ReturnsNothing()
        {
            using (HotReloadCompositionRoot.BeginReplacement(HotReloadCompositionRoot.CreateProductionServices()))
            {

                IReadOnlyList<string> identities =
                    HotReloadPlayModeEntryDropRecorder.CollectActiveIdentities();

                Assert.That(identities, Is.Empty);
                Assert.That(
                    HotReloadPlayModeEntryDropRecorder.ShouldRecord(
                        PlayModeStateChange.ExitingEditMode,
                        isDomainReloadDisabledOnEnterPlayMode: false,
                        activeIdentityCount: identities.Count),
                    Is.False);
            }
        }

        /// <summary>
        /// What: an apply run through the hot-reload tool entry recovers the ledger record of a
        /// type the domain already holds, so a re-applied type stops being reported as discarded.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_WhenAnApplyBindsATypeTheDomainHolds_RemovesItsDropRecord()
        {
            string hostPath = FixturePath(HostFileName);
            string editedPath = HotReloadTestSourceWriter.WriteEditedSource(
                "PlayModeEntryDropRecoveryHost.cs",
                InsertIntroducedType(File.ReadAllText(hostPath)));
            // Why the substitution: only the tool entry route is under test, and the run needs the
            // edited copy as its content source, which the production apply cannot be told about.
            HotReloadTool.RunApplyAsyncForTesting = (files, ct) =>
                HotReloadOrchestrator.RunAsync(files, editedPath, ct);

            using (HotReloadCompositionRoot.BeginReplacement(HotReloadCompositionRoot.CreateProductionServices()))
            {
                HotReloadResponse introducing = await ExecuteApplyAsync(hostPath);

                Assert.That(
                    introducing.IntroducedTypes.Count,
                    Is.EqualTo(1),
                    "Precondition: the first run had to introduce one type. " + introducing.Message);
                HotReloadIntroducedTypeResult row = introducing.IntroducedTypes[0];
                Assert.That(row.Kind, Is.EqualTo("Introduced"));
                string identity = HotReloadPlayModeEntryDropIdentity.ForType(row.AssemblyName, row.TypeName);
                HotReloadPlayModeEntryDropLedger.Record(new[] { identity, "Type.Unrelated()" });

                HotReloadResponse binding = await ExecuteApplyAsync(hostPath);

                Assert.That(
                    binding.IntroducedTypes[0].Kind,
                    Is.EqualTo("AlreadyActive"),
                    "Precondition: the second run had to bind the active type. " + binding.Message);
                Assert.That(
                    HotReloadPlayModeEntryDropLedger.GetIdentities(),
                    Is.EquivalentTo(new[] { "Type.Unrelated()" }),
                    "The tool entry must hand the introduced types to the drop recorder.");
            }
        }

        private static async Task<HotReloadResponse> ExecuteApplyAsync(string hostPath)
        {
            HotReloadTool tool = new HotReloadTool();
            UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(
                new JObject { ["Files"] = new JArray(hostPath) },
                CancellationToken.None);
            HotReloadResponse response = baseResponse as HotReloadResponse;
            Assert.That(response, Is.Not.Null);
            return response;
        }

        private static async Task<HotReloadOrchestratorResult> RunPatchingABodyAndIntroducingATypeAsync()
        {
            string hostPath = FixturePath(HostFileName);
            return await HotReloadOrchestrator.RunAsync(
                new[] { hostPath },
                HotReloadTestSourceWriter.WriteEditedSource(
                    "PlayModeEntryDropIdentityHost.cs",
                    EditTheScaledBody(InsertIntroducedType(File.ReadAllText(hostPath)))),
                CancellationToken.None);
        }

        private static string EditTheScaledBody(string hostSource)
        {
            Assert.That(hostSource, Does.Contain(ScaledBodyAnchor), "Precondition: scaled body anchor must exist.");
            return hostSource.Replace(ScaledBodyAnchor, "return factor * 5;", StringComparison.Ordinal);
        }

        private static string InsertIntroducedType(string hostSource)
        {
            Assert.That(hostSource, Does.Contain(HostTypeAnchor), "Precondition: host type anchor must exist.");
            string introduced =
                "    public sealed class HotReloadPlayModeEntryDropIntroducedValue\n"
                + "    {\n"
                + "        public int Read()\n"
                + "        {\n"
                + "            return 13;\n"
                + "        }\n"
                + "    }\n"
                + "\n";
            return hostSource.Replace(HostTypeAnchor, introduced + HostTypeAnchor, StringComparison.Ordinal);
        }

        private static string FixturePath(string fileName)
        {
            string path = Path.GetFullPath(
                Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", fileName));
            Assert.That(File.Exists(path), Is.True, "Fixture missing: " + path);
            return path;
        }

        private const string HostFileName = "HotReloadCrossFileAddedMemberHost.cs";

        private const string HostTypeAnchor = "    public sealed class HotReloadCrossFileAddedMemberHost";

        private const string ScaledBodyAnchor = "return factor;";
    }
}
