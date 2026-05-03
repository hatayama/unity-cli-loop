using UnityEngine;

namespace io.github.hatayama.UnityCliLoop
{
    // Orchestrator that owns the shared Canvas and holds references to all input visualization overlays.
    public class InputVisualizationCanvas : MonoBehaviour
    {
        [SerializeField] private SimulateKeyboardOverlay _keyboardOverlay = null!;
        [SerializeField] private SimulateMouseUiOverlay _mouseUiOverlay = null!;
        [SerializeField] private SimulateMouseInputOverlay _mouseInputOverlay = null!;
        [SerializeField] private RecordInputOverlayPresenter _recordInputOverlayPresenter = null!;
        [SerializeField] private ReplayInputOverlay _replayInputOverlay = null!;

        public SimulateKeyboardOverlay KeyboardOverlay => _keyboardOverlay;
        public SimulateMouseUiOverlay MouseUiOverlay => _mouseUiOverlay;
        public SimulateMouseInputOverlay MouseInputOverlay => _mouseInputOverlay;
        public RecordInputOverlayPresenter RecordInputOverlayPresenter => _recordInputOverlayPresenter;
        public ReplayInputOverlay ReplayInputOverlay => _replayInputOverlay;

        private void Awake()
        {
            Debug.Assert(_keyboardOverlay != null, "_keyboardOverlay must be assigned in prefab");
            Debug.Assert(_mouseUiOverlay != null, "_mouseUiOverlay must be assigned in prefab");
            Debug.Assert(_mouseInputOverlay != null, "_mouseInputOverlay must be assigned in prefab");
            Debug.Assert(_recordInputOverlayPresenter != null, "_recordInputOverlayPresenter must be assigned in prefab");
            Debug.Assert(_replayInputOverlay != null, "_replayInputOverlay must be assigned in prefab");
        }
    }
}
