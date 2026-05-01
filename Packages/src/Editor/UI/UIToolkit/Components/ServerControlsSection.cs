using System;
using UnityEngine.UIElements;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// UI section for server start/stop controls.
    /// Part of McpEditorWindowUI's component hierarchy.
    /// </summary>
    public class ServerControlsSection
    {
        private readonly Button _toggleServerButton;

        private ServerControlsData _lastData;

        public event Action OnToggleServer;

        public ServerControlsSection(VisualElement root)
        {
            _toggleServerButton = root.Q<Button>("toggle-server-button");

            SetupBindings();
        }

        private void SetupBindings()
        {
            _toggleServerButton.clicked += () => OnToggleServer?.Invoke();
        }

        public void Update(ServerControlsData data)
        {
            if (_lastData != null && _lastData.Equals(data))
            {
                return;
            }

            _lastData = data;

            UpdateToggleButton(data);
        }

        private void UpdateToggleButton(ServerControlsData data)
        {
            if (data.IsServerRunning)
            {
                _toggleServerButton.text = "Stop Server";
                _toggleServerButton.RemoveFromClassList("mcp-button--start");
                _toggleServerButton.AddToClassList("mcp-button--stop");
            }
            else
            {
                _toggleServerButton.text = "Start Server";
                _toggleServerButton.RemoveFromClassList("mcp-button--stop");
                _toggleServerButton.AddToClassList("mcp-button--start");
            }
        }
    }
}
