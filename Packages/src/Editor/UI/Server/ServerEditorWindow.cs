using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace io.github.hatayama.UnityCliLoop
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
        }

        private void RefreshUI()
        {
            if (_serverStatusSection == null)
            {
                return;
            }

            ServerStatusData statusData = CreateServerStatusData();
            _serverStatusSection.Update(statusData);

            ServerControlsData controlsData = CreateServerControlsData();
            _serverControlsSection.Update(controlsData);
        }

        private ServerStatusData CreateServerStatusData()
        {
            bool isRunning = McpServerController.IsServerRunning;
            string status = isRunning ? "Running" : "Stopped";
            Color statusColor = isRunning ? Color.green : Color.red;

            return new ServerStatusData(isRunning, status, statusColor);
        }

        private ServerControlsData CreateServerControlsData()
        {
            bool isRunning = McpServerController.IsServerRunning;
            return new ServerControlsData(isRunning);
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
            McpServerController.StartServer();
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
