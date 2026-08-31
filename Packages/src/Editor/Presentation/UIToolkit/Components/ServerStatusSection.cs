using UnityEngine.UIElements;

namespace io.github.hatayama.UnityCliLoop.Presentation
{
    /// <summary>
    /// UI section displaying server running status.
    /// Part of UnityCliLoopSettingsWindowUI's component hierarchy.
    /// </summary>
    public class ServerStatusSection
    {
        private readonly Label _statusValueLabel;
        private readonly VisualElement _statusIndicator;
        private ServerStatusData _lastData;

        public ServerStatusSection(VisualElement root)
        {
            _statusValueLabel = root.Q<Label>("status-value");
            _statusIndicator = root.Q<VisualElement>("server-status-indicator");
        }

        public void Update(ServerStatusData data)
        {
            if (_lastData != null && _lastData.Equals(data))
            {
                return;
            }

            _lastData = data;

            _statusValueLabel.text = data.Status;

            _statusValueLabel.RemoveFromClassList("unity-cli-loop-server-status__text--running");
            _statusValueLabel.RemoveFromClassList("unity-cli-loop-server-status__text--stopped");
            _statusIndicator.RemoveFromClassList("unity-cli-loop-server-status__indicator--running");
            _statusIndicator.RemoveFromClassList("unity-cli-loop-server-status__indicator--stopped");

            if (data.IsRunning)
            {
                _statusValueLabel.AddToClassList("unity-cli-loop-server-status__text--running");
                _statusIndicator.AddToClassList("unity-cli-loop-server-status__indicator--running");
            }
            else
            {
                _statusValueLabel.AddToClassList("unity-cli-loop-server-status__text--stopped");
                _statusIndicator.AddToClassList("unity-cli-loop-server-status__indicator--stopped");
            }
        }
    }
}
