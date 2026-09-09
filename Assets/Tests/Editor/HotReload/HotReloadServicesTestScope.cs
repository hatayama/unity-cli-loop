using System;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Opens a hot-reload services replacement for a test that has to substitute one collaborator.
    /// </summary>
    /// <remarks>
    /// Why one place: the collaborators a test replaces are constructor arguments now, so every
    /// substitution builds a whole services graph. PR-8 replaces the body of these helpers with
    /// the domain test scope without touching the tests that call them.
    /// </remarks>
    internal static class HotReloadServicesTestScope
    {
        /// <summary>Replaces the group-run stages, leaving every other collaborator production.</summary>
        internal static IDisposable BeginWithDependencies(
            Func<TransformWorkerClient, HotReloadEntryApplier, HotReloadGroupProcessorDependencies>
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
            Func<TransformWorkerClient, HotReloadEntryApplier, HotReloadGroupProcessorDependencies>
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
}
