using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Standalone window for server status monitoring and lifecycle control.
    /// Accessible from Window > Unity CLI Loop > Server.
    /// </summary>
    public class ServerEditorWindow : EditorWindow
    {
        private const string UXML_RELATIVE_PATH = "Editor/UI/Server/ServerEditorWindow.uxml";
        private const string USS_RELATIVE_PATH = "Editor/UI/Server/ServerEditorWindow.uss";

        private ServerStatusSection _serverStatusSection;
        private ServerControlsSection _serverControlsSection;
        private volatile bool _needsRepaint;

        [MenuItem("Window/Unity CLI Loop/Server", priority = 2)]
        public static void ShowWindow()
        {
            ServerEditorWindow window = GetWindow<ServerEditorWindow>("Server");
            window.Show();
        }

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
            McpBridgeServer.OnServerStarted += OnServerStateChanged;
            McpBridgeServer.OnServerStopping += OnServerStateChanged;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            McpBridgeServer.OnServerStarted -= OnServerStateChanged;
            McpBridgeServer.OnServerStopping -= OnServerStateChanged;
        }

        private void CreateGUI()
        {
            LoadLayout();
            InitializeSections();
            RefreshUI();
        }

        private void OnFocus()
        {
            RefreshUI();
        }

        private void LoadLayout()
        {
            string uxmlPath = $"{McpConstants.PackageAssetPath}/{UXML_RELATIVE_PATH}";
            VisualTreeAsset visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlPath);
            Debug.Assert(visualTree != null, $"UXML not found at {uxmlPath}");
            visualTree.CloneTree(rootVisualElement);

            string ussPath = $"{McpConstants.PackageAssetPath}/{USS_RELATIVE_PATH}";
            StyleSheet styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(ussPath);
            Debug.Assert(styleSheet != null, $"USS not found at {ussPath}");
            rootVisualElement.styleSheets.Add(styleSheet);
        }

        private void InitializeSections()
        {
            _serverStatusSection = new ServerStatusSection(rootVisualElement);

            _serverControlsSection = new ServerControlsSection(rootVisualElement);
            _serverControlsSection.OnToggleServer += ToggleServer;
            _serverControlsSection.OnPortChanged += OnPortChanged;
        }

        private void RefreshUI()
        {
            if (_serverStatusSection == null)
            {
                return;
            }

            SyncPortIfRunning();

            ServerStatusData statusData = CreateServerStatusData();
            _serverStatusSection.Update(statusData);

            ServerControlsData controlsData = CreateServerControlsData();
            _serverControlsSection.Update(controlsData);
        }

        private void SyncPortIfRunning()
        {
            if (!McpServerController.IsServerRunning)
            {
                return;
            }

            int actualPort = McpServerController.ServerPort;
            int savedPort = McpEditorSettings.GetCustomPort();

            if (savedPort != actualPort)
            {
                McpEditorSettings.SetCustomPort(actualPort);
            }
        }

        private ServerStatusData CreateServerStatusData()
        {
            (bool isRunning, int port, bool _) = McpServerController.GetServerStatus();
            string status = isRunning ? "Running" : "Stopped";
            Color statusColor = isRunning ? Color.green : Color.red;

            return new ServerStatusData(isRunning, port, status, statusColor);
        }

        private ServerControlsData CreateServerControlsData()
        {
            bool isRunning = McpServerController.IsServerRunning;
            int customPort = McpEditorSettings.GetCustomPort();

            bool hasPortWarning = false;
            string portWarningMessage = null;

            if (!isRunning)
            {
                if (!McpPortValidator.ValidatePort(customPort))
                {
                    hasPortWarning = true;
                    portWarningMessage = $"Port {customPort} is invalid. Port must be 1024 or higher and not a reserved system port.";
                }
                else if (NetworkUtility.IsPortInUse(customPort))
                {
                    hasPortWarning = true;
                    portWarningMessage = $"Port {customPort} is already in use. Please choose a different port or stop the other process using this port.";
                }
            }

            return new ServerControlsData(customPort, isRunning, !isRunning, hasPortWarning, portWarningMessage);
        }

        private void ToggleServer()
        {
            if (McpServerController.IsServerRunning)
            {
                McpServerController.StopServer();
            }
            else
            {
                StartServer();
            }

            RefreshUI();
        }

        private void StartServer()
        {
            int port = McpEditorSettings.GetCustomPort();

            if (!McpPortValidator.ValidatePort(port))
            {
                EditorUtility.DisplayDialog("Port Error",
                    $"Port must be between {McpServerConfig.MIN_PORT_NUMBER} and {McpServerConfig.MAX_PORT_NUMBER}",
                    "OK");
                return;
            }

            McpServerController.StartServer(port);
        }

        private void OnPortChanged(int port)
        {
            McpEditorSettings.SetCustomPort(port);
            RefreshUI();
        }

        private void OnServerStateChanged()
        {
            _needsRepaint = true;
        }

        private void OnEditorUpdate()
        {
            if (!_needsRepaint)
            {
                return;
            }

            _needsRepaint = false;
            RefreshUI();
        }
    }
}
