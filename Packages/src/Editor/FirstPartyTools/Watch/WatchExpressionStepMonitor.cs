using UnityEditor;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Bridges Unity Editor update ticks to the watch registry's frame-change evaluator.
    /// </summary>
    public sealed class WatchExpressionStepMonitor
    {
        private readonly WatchExpressionRegistry _registry;
        private bool _isStarted;

        public WatchExpressionStepMonitor(WatchExpressionRegistry registry)
        {
            _registry = registry;
        }

        public void Start()
        {
            if (_isStarted)
            {
                return;
            }

            EditorApplication.update += OnEditorUpdate;
            _isStarted = true;
        }

        public void Stop()
        {
            if (!_isStarted)
            {
                return;
            }

            EditorApplication.update -= OnEditorUpdate;
            _isStarted = false;
        }

        private void OnEditorUpdate()
        {
            _registry.EvaluateIfFrameChanged();
        }
    }
}
