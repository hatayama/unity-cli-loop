using UnityEngine.UIElements;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// UI section displaying server running status.
    /// Part of McpEditorWindowUI's component hierarchy.
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

            _statusValueLabel.RemoveFromClassList("mcp-server-status__text--running");
            _statusValueLabel.RemoveFromClassList("mcp-server-status__text--stopped");
            _statusIndicator.RemoveFromClassList("mcp-server-status__indicator--running");
            _statusIndicator.RemoveFromClassList("mcp-server-status__indicator--stopped");

            if (data.IsRunning)
            {
                _statusValueLabel.AddToClassList("mcp-server-status__text--running");
                _statusIndicator.AddToClassList("mcp-server-status__indicator--running");
            }
            else
            {
                _statusValueLabel.AddToClassList("mcp-server-status__text--stopped");
                _statusIndicator.AddToClassList("mcp-server-status__indicator--stopped");
            }
        }
    }
}
