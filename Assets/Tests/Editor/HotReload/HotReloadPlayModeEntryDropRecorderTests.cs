using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        private HotReloadDomainTestScope _scope;

        [SetUp]
        public void SetUp()
        {
            _ledgerSessionScope = new HotReloadPlayModeEntryDropLedgerSessionScope();
            _scope = new HotReloadDomainTestScope();
            HotReloadAutoRefreshHold.SyncToActiveChanges();
        }

        // Why syncing rather than releasing: one test applies a real patch and introduces a type,
        // the Auto Refresh hold flag is shared by the whole run, and by this point the live
        // registry is back, which may hold introduced types of its own that still warrant a hold.
        [TearDown]
        public void TearDown()
        {
            _scope.Dispose();
            HotReloadAutoRefreshHold.SyncToActiveChanges();
            _ledgerSessionScope.Restore();
        }

        /// <summary>
        /// What: only ExitingEditMode with domain reload and at least one active change (a
        /// discardable identity or an added field) records.
        /// </summary>
        [Test]
        public void ShouldRecord_RequiresExitingEditModeEnabledReloadAndActiveChanges()
        {
            Assert.That(
                HotReloadPlayModeEntryDropRecorder.ShouldRecord(
                    PlayModeStateChange.ExitingEditMode,
                    isDomainReloadDisabledOnEnterPlayMode: false,
                    activeChangeCount: 2),
                Is.True);
            Assert.That(
                HotReloadPlayModeEntryDropRecorder.ShouldRecord(
                    PlayModeStateChange.EnteredPlayMode,
                    isDomainReloadDisabledOnEnterPlayMode: false,
                    activeChangeCount: 2),
                Is.False);
            Assert.That(
                HotReloadPlayModeEntryDropRecorder.ShouldRecord(
                    PlayModeStateChange.ExitingEditMode,
                    isDomainReloadDisabledOnEnterPlayMode: true,
                    activeChangeCount: 2),
                Is.False);
            Assert.That(
                HotReloadPlayModeEntryDropRecorder.ShouldRecord(
                    PlayModeStateChange.ExitingEditMode,
                    isDomainReloadDisabledOnEnterPlayMode: false,
                    activeChangeCount: 0),
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
                new List<HotReloadIntroducedTypeOutcome>(),
                Array.Empty<string>());

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

            HotReloadPlayModeEntryDropRecorder.NotifyRevertAll(new HotReloadPlayModeEntryDropSource[0], Array.Empty<string>());

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
                Array.Empty<HotReloadPlayModeEntryDropSource>(),
                Array.Empty<string>(),
                isDomainReloadDisabledOnEnterPlayMode: false);

            Assert.That(
                HotReloadPlayModeEntryDropLedger.GetIdentities(),
                Is.EqualTo(new[] { "Type.Active()", "Type.Old()" }));

            HotReloadPlayModeEntryDropRecorder.NotifyPlayModeStateChanged(
                PlayModeStateChange.EnteredEditMode,
                new[] { "Type.Active()" },
                Array.Empty<HotReloadPlayModeEntryDropSource>(),
                Array.Empty<string>(),
                isDomainReloadDisabledOnEnterPlayMode: false);

            Assert.That(
                HotReloadPlayModeEntryDropLedger.GetIdentities(),
                Is.EqualTo(new[] { "Type.Old()" }));
        }
        /// <summary>
        /// What: a recorded Play entry also records the owner file of each discarded introduced type.
        /// </summary>
        [Test]
        public void NotifyPlayModeStateChanged_WhenItRecords_AlsoRecordsIntroducedSources()
        {
            HotReloadPlayModeEntryDropRecorder.NotifyPlayModeStateChanged(
                PlayModeStateChange.ExitingEditMode,
                new[] { "Type.Active()", IntroducedIdentityA },
                new[] { new HotReloadPlayModeEntryDropSource(IntroducedIdentityA, "Assets/Introduced.cs") },
                Array.Empty<string>(),
                isDomainReloadDisabledOnEnterPlayMode: false);

            Assert.That(
                HotReloadPlayModeEntryDropSourceLedger.GetProjectRelativePaths(),
                Is.EqualTo(new[] { "Assets/Introduced.cs" }));
        }

        /// <summary>
        /// What: a cancelled Play entry forgets the owner files it just recorded and keeps older ones.
        /// </summary>
        [Test]
        public void NotifyPlayModeStateChanged_WhenPlayEntryIsCancelledInTheSameDomain_RemovesOnlyPendingSources()
        {
            HotReloadPlayModeEntryDropSourceLedger.Record(new[]
            {
                new HotReloadPlayModeEntryDropSource(IntroducedIdentityB, "Assets/Older.cs")
            });
            HotReloadPlayModeEntryDropRecorder.NotifyPlayModeStateChanged(
                PlayModeStateChange.ExitingEditMode,
                new[] { IntroducedIdentityA },
                new[] { new HotReloadPlayModeEntryDropSource(IntroducedIdentityA, "Assets/Introduced.cs") },
                Array.Empty<string>(),
                isDomainReloadDisabledOnEnterPlayMode: false);

            HotReloadPlayModeEntryDropRecorder.NotifyPlayModeStateChanged(
                PlayModeStateChange.EnteredEditMode,
                new[] { IntroducedIdentityA },
                new[] { new HotReloadPlayModeEntryDropSource(IntroducedIdentityA, "Assets/Introduced.cs") },
                Array.Empty<string>(),
                isDomainReloadDisabledOnEnterPlayMode: false);

            Assert.That(
                HotReloadPlayModeEntryDropSourceLedger.GetProjectRelativePaths(),
                Is.EqualTo(new[] { "Assets/Older.cs" }));
        }

        /// <summary>
        /// What: a cancelled Play entry keeps an owner file revert-all recorded for a type it left
        /// loaded, even though Play entry recorded the same type again, and leaves the identity
        /// ledger empty. The revert's row is not the cancelled entry's to take back, and losing it
        /// would leave the next omitted --files run without the file its callers need.
        /// </summary>
        [Test]
        public void NotifyPlayModeStateChanged_WhenPlayEntryAfterRevertAllIsCancelled_KeepsTheRevertsOwnerFile()
        {
            HotReloadPlayModeEntryDropSource stillLoaded =
                new HotReloadPlayModeEntryDropSource(IntroducedIdentityA, "Assets/StillLoaded.cs");
            HotReloadPlayModeEntryDropRecorder.NotifyRevertAll(new[] { stillLoaded }, Array.Empty<string>());
            HotReloadPlayModeEntryDropRecorder.NotifyPlayModeStateChanged(
                PlayModeStateChange.ExitingEditMode,
                new[] { IntroducedIdentityA },
                new[] { stillLoaded },
                Array.Empty<string>(),
                isDomainReloadDisabledOnEnterPlayMode: false);

            HotReloadPlayModeEntryDropRecorder.NotifyPlayModeStateChanged(
                PlayModeStateChange.EnteredEditMode,
                new[] { IntroducedIdentityA },
                new[] { stillLoaded },
                Array.Empty<string>(),
                isDomainReloadDisabledOnEnterPlayMode: false);

            Assert.That(
                HotReloadPlayModeEntryDropSourceLedger.GetProjectRelativePaths(),
                Is.EqualTo(new[] { "Assets/StillLoaded.cs" }));
            Assert.That(HotReloadPlayModeEntryDropLedger.GetIdentities(), Is.Empty);
        }

        /// <summary>
        /// What: an apply forgets the owner files of types it introduced or found already active,
        /// and keeps the file of a type it failed to introduce.
        /// </summary>
        [Test]
        public void NotifyApplyRecovered_RemovesSourcesOfIntroducedAndAlreadyActiveTypesOnly()
        {
            HotReloadPlayModeEntryDropSourceLedger.Record(new[]
            {
                new HotReloadPlayModeEntryDropSource(
                    HotReloadPlayModeEntryDropIdentity.ForType("Fixture.Assembly", "Fixture.T1"),
                    "Assets/T1.cs"),
                new HotReloadPlayModeEntryDropSource(
                    HotReloadPlayModeEntryDropIdentity.ForType("Fixture.Assembly", "Fixture.T2"),
                    "Assets/T2.cs"),
                new HotReloadPlayModeEntryDropSource(
                    HotReloadPlayModeEntryDropIdentity.ForType("Fixture.Assembly", "Fixture.T3"),
                    "Assets/T3.cs")
            });

            HotReloadPlayModeEntryDropRecorder.NotifyApplyRecovered(
                new List<HotReloadMethodOutcome>(),
                new List<HotReloadIntroducedTypeOutcome>
                {
                    HotReloadIntroducedTypeOutcome.Introduced("Fixture.T1", "Fixture.Assembly", "Assets/T1.cs"),
                    HotReloadIntroducedTypeOutcome.AlreadyActive(
                        "Fixture.T2",
                        "Fixture.Assembly",
                        "Assets/T2.cs",
                        bodyEdited: false),
                    HotReloadIntroducedTypeOutcome.Failed("Fixture.T3", "Fixture.Assembly", "Assets/T3.cs", "reason")
                },
                Array.Empty<string>());

            Assert.That(
                HotReloadPlayModeEntryDropSourceLedger.GetProjectRelativePaths(),
                Is.EqualTo(new[] { "Assets/T3.cs" }));
        }

        /// <summary>
        /// What: a successful compile and a revert-all each forget every recorded owner file.
        /// </summary>
        [Test]
        public void NotifyCompilationFinishedWithoutErrorsAndNotifyRevertAll_ClearSources()
        {
            HotReloadPlayModeEntryDropSourceLedger.Record(new[]
            {
                new HotReloadPlayModeEntryDropSource(IntroducedIdentityA, "Assets/Introduced.cs")
            });

            HotReloadPlayModeEntryDropRecorder.NotifyCompilationFinished(0);

            Assert.That(HotReloadPlayModeEntryDropSourceLedger.GetProjectRelativePaths(), Is.Empty);

            HotReloadPlayModeEntryDropSourceLedger.Record(new[]
            {
                new HotReloadPlayModeEntryDropSource(IntroducedIdentityA, "Assets/Introduced.cs")
            });

            HotReloadPlayModeEntryDropRecorder.NotifyRevertAll(new HotReloadPlayModeEntryDropSource[0], Array.Empty<string>());

            Assert.That(HotReloadPlayModeEntryDropSourceLedger.GetProjectRelativePaths(), Is.Empty);
        }

        /// <summary>
        /// What: revert-all replaces every recorded owner file with the owner files of the types it
        /// leaves loaded, and records no identity for them. A revert drops what later reloads added
        /// to those types, so an omitted --files run has to select their files again; the identity
        /// ledger only reports what Play entry discarded, which a revert did not.
        /// </summary>
        [Test]
        public void NotifyRevertAll_RecordsOnlyTheOwnerFilesOfTypesThatStayLoaded()
        {
            HotReloadPlayModeEntryDropLedger.Record(new[] { IntroducedIdentityA });
            HotReloadPlayModeEntryDropSourceLedger.Record(new[]
            {
                new HotReloadPlayModeEntryDropSource(IntroducedIdentityA, "Assets/Earlier.cs")
            });

            HotReloadPlayModeEntryDropRecorder.NotifyRevertAll(new[]
            {
                new HotReloadPlayModeEntryDropSource(IntroducedIdentityB, "Assets/StillLoaded.cs")
            }, Array.Empty<string>());

            Assert.That(
                HotReloadPlayModeEntryDropSourceLedger.GetProjectRelativePaths(),
                Is.EqualTo(new[] { "Assets/StillLoaded.cs" }));
            Assert.That(HotReloadPlayModeEntryDropLedger.Count, Is.EqualTo(0));
        }

        /// <summary>
        /// What: a Play entry that keeps the domain records no owner file, as it records no identity.
        /// </summary>
        [Test]
        public void NotifyPlayModeStateChanged_WhenDomainReloadIsDisabled_DoesNotRecordSources()
        {
            HotReloadPlayModeEntryDropRecorder.NotifyPlayModeStateChanged(
                PlayModeStateChange.ExitingEditMode,
                new[] { IntroducedIdentityA },
                new[] { new HotReloadPlayModeEntryDropSource(IntroducedIdentityA, "Assets/Introduced.cs") },
                Array.Empty<string>(),
                isDomainReloadDisabledOnEnterPlayMode: true);

            Assert.That(HotReloadPlayModeEntryDropSourceLedger.GetProjectRelativePaths(), Is.Empty);
        }

        /// <summary>
        /// What: a reload that both patched a method and introduced a type offers both to the
        /// ledger, so Play entry records the type it is about to unload as well as the patch,
        /// and offers the type's owner file under the same identity.
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

                    IReadOnlyList<HotReloadPlayModeEntryDropSource> sources =
                        HotReloadPlayModeEntryDropRecorder.CollectActiveIntroducedSources(
                            HotReloadCompositionRoot.Services.Domain);

                    Assert.That(sources.Count, Is.EqualTo(1), "The introduced type must offer its owner file.");
                    Assert.That(
                        sources[0].Identity,
                        Is.EqualTo(
                            HotReloadPlayModeEntryDropIdentity.ForType(
                                introduced.OriginalAssemblyName,
                                introduced.MetadataName)),
                        "The owner file must be keyed by the identity a later apply recovers.");
                    Assert.That(
                        sources[0].ProjectRelativePath,
                        Is.EqualTo(introduced.OwnerProjectRelativePath),
                        "The owner file must be the file the run declared the type in.");
                    Assert.That(
                        sources[0].ProjectRelativePath,
                        Is.EqualTo("Assets/Tests/Editor/HotReload/" + HostFileName),
                        "The owner file must be the fixture passed to the run.");
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
        /// What: the next apply after a revert-all names the added fields the revert dropped, and
        /// not a field it adds for the first time.
        /// </summary>
        [Test]
        public void NotifyApplyRecovered_AfterRevertAll_ReturnsTheFieldsTheRevertDropped()
        {
            HotReloadPlayModeEntryDropRecorder.NotifyRevertAll(
                new HotReloadPlayModeEntryDropSource[0],
                new[] { "Ns.Host.speed" });

            IReadOnlyList<string> rewireFields = HotReloadPlayModeEntryDropRecorder.NotifyApplyRecovered(
                new List<HotReloadMethodOutcome>(),
                new List<HotReloadIntroducedTypeOutcome>(),
                new[] { "Ns.Host.speed", "Ns.Host.fresh" });

            Assert.That(rewireFields, Is.EqualTo(new[] { "Ns.Host.speed" }));
        }

        /// <summary>
        /// What: a field the revert-all did not drop is not named by the next apply, even when an
        /// earlier Play entry recorded it, because the revert starts the records over.
        /// </summary>
        [Test]
        public void NotifyApplyRecovered_AfterRevertAll_DoesNotReturnAFieldTheRevertDidNotDrop()
        {
            HotReloadRewireLedger.Record(new[] { "Ns.Host.older" });
            HotReloadPlayModeEntryDropRecorder.NotifyRevertAll(
                new HotReloadPlayModeEntryDropSource[0],
                new[] { "Ns.Host.speed" });

            IReadOnlyList<string> rewireFields = HotReloadPlayModeEntryDropRecorder.NotifyApplyRecovered(
                new List<HotReloadMethodOutcome>(),
                new List<HotReloadIntroducedTypeOutcome>(),
                new[] { "Ns.Host.older" });

            Assert.That(rewireFields, Is.Empty);
        }

        /// <summary>
        /// What: a revert-all that drops added fields says which ones and that values wired into
        /// them are gone, and records them for the next apply.
        /// </summary>
        [Test]
        public void ExecuteRevertAll_WithAddedFields_WarnsThatWiredValuesAreDropped()
        {
            new HotReloadDomainTestAccess().ReplaceAddedFields(
                "Assets/RevertHost.cs",
                new[] { "Ns.Host.speed", "Ns.Outer+Inner.count" });

            HotReloadResponse response = HotReloadCompositionRoot.Services.StatusExecutor.ExecuteRevertAll();

            Assert.That(
                response.Warnings,
                Does.Contain(
                    string.Format(
                        HotReloadConstants.RevertDroppedAddedFieldValuesWarningFormat,
                        2,
                        "Ns.Host.speed, Ns.Outer+Inner.count")));
            Assert.That(
                HotReloadRewireLedger.GetFields(),
                Is.EqualTo(new[] { "Ns.Host.speed", "Ns.Outer+Inner.count" }));
        }

        /// <summary>
        /// What: a revert-all with no added field adds no dropped-field warning.
        /// </summary>
        [Test]
        public void ExecuteRevertAll_WithoutAddedFields_DoesNotWarnAboutWiredValues()
        {
            HotReloadResponse response = HotReloadCompositionRoot.Services.StatusExecutor.ExecuteRevertAll();

            Assert.That(
                response.Warnings.Any(warning => warning.Contains("added field(s)")),
                Is.False,
                string.Join(" | ", response.Warnings));
        }

        /// <summary>
        /// What: through the tool entry, a field added by one apply and dropped by revert-all is
        /// named by the next apply as a field to wire again, in words that cover the revert.
        /// </summary>
        [Test]
        public async Task ExecuteAsync_ReapplyAfterRevertAll_WarnsToWireTheDroppedFieldAgain()
        {
            string hostPath = FixturePath(HostFileName);
            string editedPath = HotReloadTestSourceWriter.WriteEditedSource(
                "RevertRewireHost.cs",
                InsertAddedField(File.ReadAllText(hostPath)));
            using (HotReloadCompositionRoot.BeginReplacement(HotReloadCompositionRoot.CreateProductionServices()))
            {
                try
                {
                    IHotReloadOrchestrator productionOrchestrator = HotReloadCompositionRoot.Services.Orchestrator;
                    using IDisposable orchestratorScope = HotReloadServicesTestScope.BeginWithOrchestrator(
                        new HotReloadStubOrchestrator(
                            (files, ct) => productionOrchestrator.RunAsync(files, editedPath, ct)));
                    HotReloadResponse adding = await ExecuteApplyAsync(hostPath);
                    Assert.That(
                        adding.AddedFields,
                        Does.Contain(AddedFieldDisplayName),
                        "Precondition: the first run had to add the field. " + adding.Message);

                    HotReloadCompositionRoot.Services.StatusExecutor.ExecuteRevertAll();
                    HotReloadResponse reapplied = await ExecuteApplyAsync(hostPath);

                    string warning = reapplied.Warnings.FirstOrDefault(entry => entry.Contains("wire them again"));
                    Assert.That(warning, Is.Not.Null, string.Join(" | ", reapplied.Warnings));
                    Assert.That(warning, Does.Contain(AddedFieldDisplayName));
                    Assert.That(warning, Does.Contain("domain reload or revert"));
                }
                finally
                {
                    // The patch belongs to the replacement domain, which the outer TearDown revert
                    // no longer sees.
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
                    HotReloadIntroducedTypeOutcome.AlreadyActive(
                        "Fixture.T2",
                        "Fixture.Assembly",
                        "Assets/A.cs",
                        bodyEdited: false),
                    HotReloadIntroducedTypeOutcome.Failed("Fixture.T3", "Fixture.Assembly", "Assets/A.cs", "reason")
                },
                Array.Empty<string>());

            Assert.That(
                HotReloadPlayModeEntryDropLedger.GetIdentities(),
                Is.EquivalentTo(new[] { failedIdentity, "Type.Kept()" }),
                "A type the apply could not introduce is still lost, so its record stays.");
        }

        /// <summary>
        /// What: an apply returns only the added fields a domain reload had discarded, not one it
        /// adds for the first time, and takes the returned ones off the rewire ledger so the next
        /// apply does not ask again.
        /// </summary>
        [Test]
        public void NotifyApplyRecovered_ReturnsOnlyAddedFieldsTheRewireLedgerHeld()
        {
            HotReloadRewireLedger.Record(new[] { "Ns.Host.speed", "Ns.Other.count" });

            IReadOnlyList<string> rewireFields = HotReloadPlayModeEntryDropRecorder.NotifyApplyRecovered(
                new List<HotReloadMethodOutcome>(),
                new List<HotReloadIntroducedTypeOutcome>(),
                new[] { "Ns.Host.speed", "Ns.Host.fresh" });

            Assert.That(rewireFields, Is.EqualTo(new[] { "Ns.Host.speed" }));
            Assert.That(HotReloadRewireLedger.GetFields(), Is.EqualTo(new[] { "Ns.Other.count" }));
        }

        /// <summary>
        /// What: a field of the same type under another name is not returned. The warning names
        /// fields, so a match on the declaring type alone would name one that never held a value.
        /// </summary>
        [Test]
        public void NotifyApplyRecovered_SameTypeOtherFieldName_IsNotReturned()
        {
            HotReloadRewireLedger.Record(new[] { "Ns.Host.speed" });

            IReadOnlyList<string> rewireFields = HotReloadPlayModeEntryDropRecorder.NotifyApplyRecovered(
                new List<HotReloadMethodOutcome> { HotReloadMethodOutcome.Patched("Ns.Host.Tick()", "Assets/A.cs") },
                new List<HotReloadIntroducedTypeOutcome>(),
                new[] { "Ns.Host.fresh" });

            Assert.That(rewireFields, Is.Empty);
        }

        /// <summary>
        /// What: a field added to a nested type is recorded from the domain at Play entry and
        /// matched by the next apply's display name, because both spell the nested type with '+'.
        /// </summary>
        [Test]
        public void NotifyApplyRecovered_NestedTypeFieldRecordedAtPlayEntry_IsReturned()
        {
            new HotReloadDomainTestAccess().ReplaceAddedFields("Assets/Nested.cs", new[] { "Ns.Outer+Inner.count" });
            HotReloadPlayModeEntryDropRecorder.NotifyPlayModeStateChanged(
                PlayModeStateChange.ExitingEditMode,
                Array.Empty<string>(),
                Array.Empty<HotReloadPlayModeEntryDropSource>(),
                HotReloadPlayModeEntryDropRecorder.CollectActiveAddedFields(HotReloadCompositionRoot.Services.Domain),
                isDomainReloadDisabledOnEnterPlayMode: false);
            HotReloadPlayModeEntryDropRecorder.ResetPendingForTesting();

            IReadOnlyList<string> rewireFields = HotReloadPlayModeEntryDropRecorder.NotifyApplyRecovered(
                new List<HotReloadMethodOutcome>(),
                new List<HotReloadIntroducedTypeOutcome>(),
                new[] { "Ns.Outer+Inner.count" });

            Assert.That(rewireFields, Is.EqualTo(new[] { "Ns.Outer+Inner.count" }));
        }

        /// <summary>
        /// What: a field added to a generic type keeps its arity marker from the domain at Play
        /// entry through to the next apply's display name, so the two still match.
        /// </summary>
        [Test]
        public void NotifyApplyRecovered_GenericTypeFieldRecordedAtPlayEntry_IsReturned()
        {
            new HotReloadDomainTestAccess().ReplaceAddedFields("Assets/Generic.cs", new[] { "Ns.Box`1.count" });
            HotReloadPlayModeEntryDropRecorder.NotifyPlayModeStateChanged(
                PlayModeStateChange.ExitingEditMode,
                Array.Empty<string>(),
                Array.Empty<HotReloadPlayModeEntryDropSource>(),
                HotReloadPlayModeEntryDropRecorder.CollectActiveAddedFields(HotReloadCompositionRoot.Services.Domain),
                isDomainReloadDisabledOnEnterPlayMode: false);
            HotReloadPlayModeEntryDropRecorder.ResetPendingForTesting();

            IReadOnlyList<string> rewireFields = HotReloadPlayModeEntryDropRecorder.NotifyApplyRecovered(
                new List<HotReloadMethodOutcome>(),
                new List<HotReloadIntroducedTypeOutcome>(),
                new[] { "Ns.Box`1.count" });

            Assert.That(rewireFields, Is.EqualTo(new[] { "Ns.Box`1.count" }));
        }

        /// <summary>
        /// What: a Play entry that discards only added fields records them for rewiring and leaves
        /// the identity ledger, which --status counts as discarded changes, empty.
        /// </summary>
        [Test]
        public void NotifyPlayModeStateChanged_FieldsOnly_RecordsRewireFieldsButNoIdentity()
        {
            HotReloadPlayModeEntryDropRecorder.NotifyPlayModeStateChanged(
                PlayModeStateChange.ExitingEditMode,
                Array.Empty<string>(),
                Array.Empty<HotReloadPlayModeEntryDropSource>(),
                new[] { "Ns.Host.speed" },
                isDomainReloadDisabledOnEnterPlayMode: false);

            Assert.That(HotReloadRewireLedger.GetFields(), Is.EqualTo(new[] { "Ns.Host.speed" }));
            Assert.That(HotReloadPlayModeEntryDropLedger.Count, Is.EqualTo(0));
        }

        /// <summary>
        /// What: a cancelled Play entry takes back the fields it just recorded and keeps one an
        /// earlier entry recorded, whose value is still gone.
        /// </summary>
        [Test]
        public void NotifyPlayModeStateChanged_WhenPlayEntryIsCancelledInTheSameDomain_RemovesOnlyPendingRewireFields()
        {
            HotReloadRewireLedger.Record(new[] { "Ns.Host.older" });
            HotReloadPlayModeEntryDropRecorder.NotifyPlayModeStateChanged(
                PlayModeStateChange.ExitingEditMode,
                Array.Empty<string>(),
                Array.Empty<HotReloadPlayModeEntryDropSource>(),
                new[] { "Ns.Host.older", "Ns.Host.speed" },
                isDomainReloadDisabledOnEnterPlayMode: false);

            HotReloadPlayModeEntryDropRecorder.NotifyPlayModeStateChanged(
                PlayModeStateChange.EnteredEditMode,
                Array.Empty<string>(),
                Array.Empty<HotReloadPlayModeEntryDropSource>(),
                new[] { "Ns.Host.older", "Ns.Host.speed" },
                isDomainReloadDisabledOnEnterPlayMode: false);

            Assert.That(HotReloadRewireLedger.GetFields(), Is.EqualTo(new[] { "Ns.Host.older" }));
        }

        /// <summary>
        /// What: a successful compile and a revert-all both clear the rewire ledger, because after
        /// either one no later apply re-adds a field whose value a domain reload discarded.
        /// </summary>
        [Test]
        public void NotifyCompilationFinishedAndRevertAll_ClearRewireFields()
        {
            HotReloadRewireLedger.Record(new[] { "Ns.Host.speed" });
            HotReloadPlayModeEntryDropRecorder.NotifyCompilationFinished(0);
            Assert.That(HotReloadRewireLedger.GetFields(), Is.Empty, "compile");

            HotReloadRewireLedger.Record(new[] { "Ns.Host.speed" });
            HotReloadPlayModeEntryDropRecorder.NotifyRevertAll(new HotReloadPlayModeEntryDropSource[0], Array.Empty<string>());
            Assert.That(HotReloadRewireLedger.GetFields(), Is.Empty, "revert-all");
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
                        activeChangeCount: identities.Count),
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
            using (HotReloadCompositionRoot.BeginReplacement(HotReloadCompositionRoot.CreateProductionServices()))
            {
                // Why the substitution: only the tool entry route is under test, and the run needs
                // the edited copy as its content source, which the tool cannot be told about.
                IHotReloadOrchestrator productionOrchestrator = HotReloadCompositionRoot.Services.Orchestrator;
                using IDisposable orchestratorScope = HotReloadServicesTestScope.BeginWithOrchestrator(
                    new HotReloadStubOrchestrator(
                        (files, ct) => productionOrchestrator.RunAsync(files, editedPath, ct)));
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
            return await HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
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

        private static string InsertAddedField(string hostSource)
        {
            Assert.That(hostSource, Does.Contain(HostBodyAnchor), "Precondition: host body anchor must exist.");
            Assert.That(hostSource, Does.Contain(ScaledBodyAnchor), "Precondition: scaled body anchor must exist.");
            // Why the body reads the field too: an edit that only declares a field patches no
            // method, so the run would report the file unchanged.
            return hostSource
                .Replace(
                    HostBodyAnchor,
                    HostBodyAnchor + "\n        private int revertRewireCount;\n",
                    StringComparison.Ordinal)
                .Replace(ScaledBodyAnchor, "return factor + revertRewireCount;", StringComparison.Ordinal);
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

        private static readonly string IntroducedIdentityA =
            HotReloadPlayModeEntryDropIdentity.ForType("Fixture.Assembly", "Fixture.IntroducedA");

        private static readonly string IntroducedIdentityB =
            HotReloadPlayModeEntryDropIdentity.ForType("Fixture.Assembly", "Fixture.IntroducedB");

        private const string HostFileName = "HotReloadCrossFileAddedMemberHost.cs";

        private const string HostTypeAnchor = "    public sealed class HotReloadCrossFileAddedMemberHost";

        private const string ScaledBodyAnchor = "return factor;";

        private const string HostBodyAnchor = "    public sealed class HotReloadCrossFileAddedMemberHost\n    {";

        private const string AddedFieldDisplayName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadCrossFileAddedMemberHost.revertRewireCount";
    }
}
