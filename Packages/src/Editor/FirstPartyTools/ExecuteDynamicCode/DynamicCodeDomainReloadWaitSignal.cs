using System;
using System.Threading;
using System.Threading.Tasks;

using UnityEditor;
using UnityEditor.Compilation;

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

            if (ShouldRequestWait(_parameters, EditorApplication.isCompiling, _reloadSignalObserved))
            {
                return true;
            }

            for (int frame = 0; frame < SIGNAL_SETTLE_FRAMES; frame++)
            {
                await EditorFrameWaiter.WaitFramesAsync(1, ct);
                if (ShouldRequestWait(_parameters, EditorApplication.isCompiling, _reloadSignalObserved))
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
            _reloadSignalObserved = true;
        }

        private void OnBeforeAssemblyReload()
        {
            _reloadSignalObserved = true;
        }
    }
}
