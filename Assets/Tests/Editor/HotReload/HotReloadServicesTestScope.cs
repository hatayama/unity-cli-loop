using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Opens a hot-reload services replacement for a test that has to substitute one collaborator.
    /// </summary>
    /// <remarks>
    /// Why one place: the collaborators a test replaces are constructor arguments now, so every
    /// substitution builds a whole services graph. These helpers stay separate from
    /// <see cref="HotReloadDomainTestScope"/>: they substitute one collaborator on the installed
    /// graph, while the domain scope brings a whole graph of its own.
    /// </remarks>
    internal static class HotReloadServicesTestScope
    {
        /// <summary>Replaces the group-run stages, leaving every other collaborator production.</summary>
        internal static IDisposable BeginWithDependencies(
            Func<HotReloadGroupStageCollaborators, HotReloadGroupProcessorDependencies>
                buildDependencies)
        {
            HotReloadServices installed = HotReloadCompositionRoot.Services;
            return BeginWith(
                installed.EditorStateSnapshotCapture,
                installed.TransformWorkerClient.Host,
                buildDependencies);
        }

        /// <summary>Replaces the Editor state the new-source admission path reads.</summary>
        internal static IDisposable BeginWithEditorState(IHotReloadEditorStateSnapshotCapture capture)
        {
            return BeginWith(
                capture,
                HotReloadCompositionRoot.Services.TransformWorkerClient.Host,
                HotReloadGroupProcessorDependencies.CreateProduction);
        }

        /// <summary>Replaces the run the tool applies through.</summary>
        internal static IDisposable BeginWithOrchestrator(IHotReloadOrchestrator orchestrator)
        {
            return HotReloadCompositionRoot.BeginReplacement(
                HotReloadCompositionRoot.Services.WithOrchestrator(orchestrator));
        }

        /// <summary>Replaces the changed-file report the tool selects omitted files from.</summary>
        internal static IDisposable BeginWithChangeDetector(IHotReloadChangeDetector changeDetector)
        {
            return HotReloadCompositionRoot.BeginReplacement(
                HotReloadCompositionRoot.Services.WithChangeDetector(changeDetector));
        }

        /// <summary>Replaces the transform worker host the client routes through.</summary>
        internal static IDisposable BeginWithWorkerHost(TransformWorkerHost host)
        {
            return BeginWith(
                HotReloadCompositionRoot.Services.EditorStateSnapshotCapture,
                host,
                HotReloadGroupProcessorDependencies.CreateProduction);
        }

        /// <remarks>
        /// Why the installed domain, harmony and package roots: these helpers substitute a stage
        /// or a capture, not the patch engine, and a second domain would leave the test asserting
        /// against generations the run never touched. Why the helpers above pass the installed
        /// collaborators they do not replace: a scope opened inside another one would otherwise
        /// put the enclosing substitution back to production.
        /// </remarks>
        internal static IDisposable BeginWith(
            IHotReloadEditorStateSnapshotCapture editorStateSnapshotCapture,
            TransformWorkerHost transformWorkerHost,
            Func<HotReloadGroupStageCollaborators, HotReloadGroupProcessorDependencies>
                buildDependencies)
        {
            HotReloadServices installed = HotReloadCompositionRoot.Services;
            // The capture is main-thread only, and a run inside the scope normalizes script paths
            // against these roots on the background threads it switches to.
            installed.PackageRootCapture.CaptureCurrent();
            return HotReloadCompositionRoot.BeginReplacement(
                HotReloadCompositionRoot.CreateServices(
                    installed.Domain,
                    installed.Harmony,
                    installed.PackageRootCapture,
                    editorStateSnapshotCapture,
                    transformWorkerHost,
                    buildDependencies));
        }
    }

    /// <summary>
    /// An Editor state a test decides, so the admission path can be shown refusing each state.
    /// </summary>
    internal sealed class HotReloadStubEditorStateSnapshotCapture : IHotReloadEditorStateSnapshotCapture
    {
        internal HotReloadStubEditorStateSnapshotCapture(Func<HotReloadEditorStateSnapshot> capture)
        {
            Capture = capture;
        }

        /// <summary>The state the next capture reports. Settable so a test can change it mid-run.</summary>
        internal Func<HotReloadEditorStateSnapshot> Capture { get; set; }

        public HotReloadEditorStateSnapshot CaptureCurrent()
        {
            return Capture();
        }
    }
    /// <summary>
    /// A changed-file report a test decides, so the omitted-files selection can be driven without
    /// a compile snapshot.
    /// </summary>
    internal sealed class HotReloadStubChangeDetector : IHotReloadChangeDetector
    {
        private readonly Func<HotReloadChangedFileAggregationResult> _detect;

        internal HotReloadStubChangeDetector(Func<HotReloadChangedFileAggregationResult> detect)
        {
            _detect = detect;
        }

        public HotReloadChangedFileAggregationResult Detect()
        {
            return _detect();
        }
    }

    /// <summary>
    /// A run a test decides the result of, so the tool's selection, response and recovery steps
    /// can be exercised without applying anything.
    /// </summary>
    internal sealed class HotReloadStubOrchestrator : IHotReloadOrchestrator
    {
        private readonly Func<IReadOnlyList<string>, CancellationToken, Task<HotReloadOrchestratorResult>> _run;

        internal HotReloadStubOrchestrator(
            Func<IReadOnlyList<string>, CancellationToken, Task<HotReloadOrchestratorResult>> run)
        {
            _run = run;
        }

        public Task<HotReloadOrchestratorResult> RunAsync(
            IReadOnlyList<string> files,
            string contentPathOverride,
            CancellationToken ct,
            IReadOnlyDictionary<string, string> contentPathOverrideByFile = null)
        {
            return _run(files, ct);
        }
    }
}
