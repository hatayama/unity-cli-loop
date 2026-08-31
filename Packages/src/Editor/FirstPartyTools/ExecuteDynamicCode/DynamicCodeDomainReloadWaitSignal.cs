using System;
using System.Threading;
using System.Threading.Tasks;

using UnityEditor;
using UnityEditor.Compilation;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Observes editor compile and reload signals while dynamic user code is running.
    /// </summary>
    internal sealed class DynamicCodeDomainReloadWaitSignal : IDisposable
    {
        private const int SIGNAL_SETTLE_FRAMES = 2;

        private readonly ExecuteDynamicCodeSchema _parameters;
        private readonly bool _isObserving;

        private bool _reloadSignalObserved;
        private int _isDisposed;

        private DynamicCodeDomainReloadWaitSignal(ExecuteDynamicCodeSchema parameters)
        {
            _parameters = parameters;
            _isObserving = ShouldObserve(parameters);
            if (!_isObserving)
            {
                return;
            }

            CompilationPipeline.compilationStarted += OnCompilationStarted;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        }

        public static DynamicCodeDomainReloadWaitSignal Start(ExecuteDynamicCodeSchema parameters)
        {
            return new DynamicCodeDomainReloadWaitSignal(parameters);
        }

        public async Task<bool> ShouldWaitAsync(CancellationToken ct)
        {
            if (!_isObserving)
            {
                return false;
            }

            if (Volatile.Read(ref _reloadSignalObserved))
            {
                return true;
            }

            await MainThreadSwitcher.SwitchToMainThread(ct);
            if (ShouldRequestWait(
                    _parameters,
                    EditorApplication.isCompiling,
                    reloadSignalObserved: Volatile.Read(ref _reloadSignalObserved)))
            {
                return true;
            }

            for (int frame = 0; frame < SIGNAL_SETTLE_FRAMES; frame++)
            {
                bool frameReady = await EditorFrameWaiter.WaitFramesOrTimeoutAsync(
                    1,
                    UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS,
                    ct).ConfigureAwait(false);
                if (!frameReady)
                {
                    // Why: a reload can stop editor frames before the follow-up EditorApplication check can run.
                    return ShouldRequestWait(
                        _parameters,
                        editorIsCompiling: false,
                        reloadSignalObserved: Volatile.Read(ref _reloadSignalObserved));
                }

                await MainThreadSwitcher.SwitchToMainThread(ct);
                if (ShouldRequestWait(
                        _parameters,
                        EditorApplication.isCompiling,
                        Volatile.Read(ref _reloadSignalObserved)))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool ShouldRequestWait(
            ExecuteDynamicCodeSchema parameters,
            bool editorIsCompiling,
            bool reloadSignalObserved)
        {
            if (!ShouldObserve(parameters))
            {
                return false;
            }

            return editorIsCompiling || reloadSignalObserved;
        }

        public void Dispose()
        {
            if (!_isObserving)
            {
                return;
            }

            if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
            {
                return;
            }

            if (MainThreadSwitcher.IsMainThread)
            {
                UnsubscribeFromEditorEvents();
                return;
            }

            // Why: timeout continuations can resume off-thread, but UnityEditor event removal belongs on the editor thread.
            UnsubscribeFromEditorEventsOnMainThreadAsync(CancellationToken.None).Forget();
        }

        private async Task UnsubscribeFromEditorEventsOnMainThreadAsync(CancellationToken ct)
        {
            await MainThreadSwitcher.SwitchToMainThread(ct);
            UnsubscribeFromEditorEvents();
        }

        private void UnsubscribeFromEditorEvents()
        {
            CompilationPipeline.compilationStarted -= OnCompilationStarted;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
        }

        private static bool ShouldObserve(ExecuteDynamicCodeSchema parameters)
        {
            return parameters != null
                && parameters.WaitForDomainReload
                && !parameters.CompileOnly;
        }

        private void OnCompilationStarted(object context)
        {
            Volatile.Write(ref _reloadSignalObserved, true);
        }

        private void OnBeforeAssemblyReload()
        {
            Volatile.Write(ref _reloadSignalObserved, true);
        }
    }
}
