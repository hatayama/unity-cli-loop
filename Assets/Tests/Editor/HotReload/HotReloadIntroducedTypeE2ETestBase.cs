using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// What every end-to-end test of a type an earlier reload introduced needs before it can say
    /// anything: a domain the reloads share, a handle on the artifact assembly the introducing
    /// reload retained, and one way of wording what a run reported.
    /// </summary>
    /// <remarks>
    /// Why a base class rather than a static helper: the domain scope has to open before each test
    /// and close after it, and a test that forgot to do so would leave a patch installed on an
    /// assembly the next test reaches.
    /// </remarks>
    // Why private protected on the members that hand out a run's result: the result types are
    // internal to the package, and a protected member of a public class cannot expose them.
    public abstract class HotReloadIntroducedTypeE2ETestBase
    {
        private HotReloadDomainTestScope _scope;

        [SetUp]
        public void SetUp()
        {
            _scope = new HotReloadDomainTestScope();
            HotReloadAutoRefreshHold.SyncToActiveChanges();
        }

        // Why reverting here as well: these runs patch a method of an artifact assembly that
        // only lives while the run's scope is open, so leaving the patch active would fail the
        // first later test that reaches that assembly.
        [TearDown]
        public void TearDown()
        {
            _scope.Dispose();
            HotReloadAutoRefreshHold.SyncToActiveChanges();
        }

        // Why one helper owns both scopes: every reload of a run has to see the same domain and the
        // same captured artifact, and the artifact assembly only lives while the scopes are open.
        // Why the grant is optional: a test that leaves it out runs on the grant production builds.
        private protected static async Task RunInIntroducedTypeDomainAsync(
            Func<Func<HotReloadIntroducedTypeArtifact>, Task> runReloads,
            IHotReloadInternalAccessGrant internalAccessGrant = null,
            HotReloadIntroducedTypeStageProbe probe = null)
        {
            HotReloadIntroducedTypeArtifact artifact = null;

            using (HotReloadCompositionRoot.BeginReplacement(HotReloadCompositionRoot.CreateProductionServices()))
            {
                using (HotReloadServicesTestScope.BeginWithDependencies(collaborators =>
                    CreateArtifactCapturingDependencies(
                        collaborators,
                        internalAccessGrant,
                        probe ?? new HotReloadIntroducedTypeStageProbe(),
                        prepared =>
                        {
                            if (prepared != null)
                            {
                                artifact = prepared;
                            }
                        })))
                {
                    await runReloads(() => artifact);
                }
            }
        }

        /// <summary>
        /// The collaborators a preparation runs on, with only the internal access grant replaced
        /// when <paramref name="internalAccessGrant"/> is given.
        /// </summary>
        /// <remarks>
        /// Why a copy instead of a settable grant: the collaborators are fixed when the services
        /// are built, and every other stage of the run keeps the production collaborators.
        /// </remarks>
        private protected static HotReloadGroupStageCollaborators WithInternalAccessGrant(
            HotReloadGroupStageCollaborators collaborators,
            IHotReloadInternalAccessGrant internalAccessGrant)
        {
            if (internalAccessGrant == null)
            {
                return collaborators;
            }

            return new HotReloadGroupStageCollaborators(
                collaborators.Domain,
                collaborators.Patcher,
                collaborators.FileEntryApplier,
                collaborators.EntryApplier,
                collaborators.TransformWorkerClient,
                collaborators.PackageRootCapture,
                collaborators.EditorStateSnapshotCapture,
                collaborators.CommitPolicy,
                collaborators.PlayMode,
                internalAccessGrant,
                collaborators.CompiledCallers);
        }

        private protected static Task<HotReloadOrchestratorResult> RunReloadAsync(
            string hostPath,
            string callerPath,
            Dictionary<string, string> edits)
        {
            return HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                new[] { hostPath, callerPath },
                contentPathOverride: null,
                CancellationToken.None,
                edits);
        }

        private protected static HotReloadResponse BuildResponse(HotReloadOrchestratorResult result)
        {
            return HotReloadApplyResponseBuilder.Build(HotReloadCompositionRoot.Services, result, null, Array.Empty<string>(), Array.Empty<HotReloadWiredValueRestoreFailure>(), isPlaying: false, isPaused: false, selectedFiles: Array.Empty<string>());
        }

        // Why the preparation stage is the only one wrapped: the test reads the edited body back
        // through the artifact assembly, and the prepared artifact is the only handle on it. The
        // probe only watches the stages it is hung on and hands every input on unchanged.
        private static HotReloadGroupProcessorDependencies CreateArtifactCapturingDependencies(
            HotReloadGroupStageCollaborators collaborators,
            IHotReloadInternalAccessGrant internalAccessGrant,
            HotReloadIntroducedTypeStageProbe probe,
            Action<HotReloadIntroducedTypeArtifact> captureArtifact)
        {
            HotReloadGroupStageCollaborators preparationCollaborators =
                WithInternalAccessGrant(collaborators, internalAccessGrant);
            return HotReloadGroupProcessorDependencies.Create(
                files => HotReloadGroupProcessor.TryAppendNewSourceMembershipFailure(collaborators, files),
                async (files, input, ct) =>
                {
                    probe.BeforePrepare?.Invoke(input);
                    HotReloadIntroducedTypePreparationResult preparation =
                        await HotReloadIntroducedTypePreparation.PrepareAsync(
                            preparationCollaborators, files, input, ct);
                    probe.AfterPrepare?.Invoke(input, preparation);
                    captureArtifact(preparation.Prepared?.Artifact);
                    return preparation;
                },
                (input, ct) =>
                {
                    probe.BeforeTransform?.Invoke(input);
                    return HotReloadCompositionRoot.Services.TransformWorkerClient.RunAsync(input, ct);
                },
                (context, ct) =>
                {
                    probe.BeforeGate?.Invoke(context);
                    return HotReloadGroupProcessor.GateAndCompileAsync(collaborators, context, ct);
                },
                (context, compileResult, entriesToPatch) => HotReloadGroupEntryPreparation.PrepareGroup(
                    collaborators, context, compileResult, entriesToPatch),
                HotReloadCompositionRoot.Services.EntryApplier.ApplyPreparedEntries);
        }

        private protected static string DescribeOutcomes(HotReloadOrchestratorResult result)
        {
            string description = "Methods:";
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                description += "\n  " + outcome.Kind + " " + outcome.Method + " " + outcome.Reason;
            }

            description += "\nIntroducedTypes:";
            foreach (HotReloadIntroducedTypeOutcome outcome in result.IntroducedTypes)
            {
                description += "\n  " + outcome.Kind + " " + outcome.MetadataName + " " + outcome.Reason;
            }

            return description;
        }

        private protected static int CountFailures(HotReloadOrchestratorResult result)
        {
            int count = 0;
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                if (outcome.Kind == HotReloadMethodOutcomeKind.Failed)
                {
                    count++;
                }
            }

            foreach (HotReloadIntroducedTypeOutcome outcome in result.IntroducedTypes)
            {
                if (outcome.Kind == HotReloadIntroducedTypeOutcomeKind.Failed)
                {
                    count++;
                }
            }

            return count;
        }

        protected static string FixturePath(string fileName)
        {
            string path = Path.GetFullPath(
                Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", fileName));
            Assert.That(File.Exists(path), Is.True, "Fixture missing: " + path);
            return path;
        }
    }
}
