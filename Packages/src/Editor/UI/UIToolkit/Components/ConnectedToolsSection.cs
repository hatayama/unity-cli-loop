using System;
using System.Linq;
using UnityEngine.UIElements;

namespace io.github.hatayama.uLoopMCP
{
    public class ConnectedToolsSection
    {
        private readonly VisualElement _sectionContainer;
        private readonly Foldout _foldout;
        private readonly VisualElement _clientsList;

        private bool _hasLastValidData;
        private ConnectedToolsData _lastData;

        public event Action<bool> OnFoldoutChanged;

        public ConnectedToolsSection(VisualElement root)
        {
            _sectionContainer = root.Q<VisualElement>("connected-tools-section");
            _foldout = root.Q<Foldout>("connected-tools-foldout");
            _clientsList = root.Q<VisualElement>("clients-list");

            SetupBindings();
        }

        private void SetupBindings()
        {
            _foldout.RegisterValueChangedCallback(evt => OnFoldoutChanged?.Invoke(evt.newValue));
        }

        public void Update(ConnectedToolsData data)
        {
            ViewDataBinder.SetVisible(_sectionContainer, data.ShowSection);

            if (!data.ShowSection)
            {
                return;
            }

            if (data.ShowReconnectingUI && _hasLastValidData)
            {
                return;
            }

            ViewDataBinder.UpdateFoldout(_foldout, data.ShowFoldout);
            UpdateClientsList(data);
            _lastData = data;
        }

        private void UpdateClientsList(ConnectedToolsData data)
        {
            _clientsList.Clear();

            if (data.Clients == null || data.Clients.Count == 0)
            {
                _hasLastValidData = false;
                return;
            }

            System.Collections.Generic.List<ConnectedClient> validClients = data.Clients
                .Where(client => IsValidClientName(client.ClientName))
                .ToList();

            foreach (ConnectedClient client in validClients)
            {
                VisualElement clientItem = CreateClientItem(client);
                _clientsList.Add(clientItem);
            }

            _hasLastValidData = validClients.Count > 0;
        }

        private VisualElement CreateClientItem(ConnectedClient client)
        {
            VisualElement container = new VisualElement();
            container.AddToClassList("mcp-client");

            Label iconLabel = new Label(McpUIConstants.CLIENT_ICON);
            iconLabel.AddToClassList("mcp-client__icon");
            container.Add(iconLabel);

            Label nameLabel = new Label(client.ClientName);
            nameLabel.AddToClassList("mcp-client__name");
            container.Add(nameLabel);

            return container;
        }

        private bool IsValidClientName(string clientName)
        {
            return !string.IsNullOrEmpty(clientName) && clientName != McpConstants.UNKNOWN_CLIENT_NAME;
        }
    }
}
