using System;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// View layer for McpEditorWindow in MVP architecture.
    /// Owns UI sections and forwards user interactions to presenter via events.
    /// Related: McpEditorWindow (presenter), McpEditorModel (model)
    /// </summary>
    public class McpEditorWindowUI : IDisposable
    {
        private const string UXML_RELATIVE_PATH = "Editor/UI/UIToolkit/McpEditorWindow.uxml";
        private const string USS_RELATIVE_PATH = "Editor/UI/UIToolkit/McpEditorWindow.uss";
        private const string GITHUB_ICON_RELATIVE_PATH = "Editor/UI/Setup/GitHub_Invertocat_White.png";

        private readonly VisualElement _root;

        private CliSetupSection _cliSetupSection;
        private ConnectedToolsSection _connectedToolsSection;
        private ToolSettingsSection _toolSettingsSection;

        private Foldout _configurationFoldout;
        private ScrollView _mainScrollView;
        private VisualElement _githubLinkRow;
        private Label _githubLinkLabel;
        private Image _githubLinkIcon;

        public event Action OnRefreshCliVersion;
        public event Action OnInstallCli;
        public event Action OnInstallSkills;
        public event Action OnRefreshSkillsState;
        public event Action<SkillsTarget> OnSkillsTargetChanged;
        public event Action<bool> OnGroupSkillsChanged;
        public event Action<bool> OnConfigurationFoldoutChanged;
        public event Action<bool> OnConnectedToolsFoldoutChanged;
        public event Action<bool> OnToolSettingsFoldoutChanged;
        public event Action<string, bool> OnToolToggled;
        public event Action<DynamicCodeSecurityLevel> OnSecurityLevelChanged;

        public McpEditorWindowUI(VisualElement root)
        {
            _root = root;
            LoadLayout();
            ApplyDebugStyle();
            InitializeSections();
        }

        private void ApplyDebugStyle()
        {
#if ULOOPMCP_DEBUG
            VisualElement mainContainer = _root.Q<VisualElement>("main-scroll-view");
            mainContainer?.AddToClassList("mcp-main-container--debug");
#endif
        }

        private void LoadLayout()
        {
            string uxmlPath = $"{McpConstants.PackageAssetPath}/{UXML_RELATIVE_PATH}";
            string ussPath = $"{McpConstants.PackageAssetPath}/{USS_RELATIVE_PATH}";

            VisualTreeAsset visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlPath);
            if (visualTree == null)
            {
                Debug.LogError($"Failed to load UXML from: {uxmlPath}");
                return;
            }

            visualTree.CloneTree(_root);

            StyleSheet styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(ussPath);
            if (styleSheet == null)
            {
                Debug.LogError($"Failed to load USS from: {ussPath}");
                return;
            }

            _root.styleSheets.Add(styleSheet);
        }

        private void InitializeSections()
        {
            _configurationFoldout = _root.Q<Foldout>("configuration-foldout");
            Debug.Assert(_configurationFoldout != null, "configuration-foldout must not be null");
            _configurationFoldout.RegisterValueChangedCallback(evt => OnConfigurationFoldoutChanged?.Invoke(evt.newValue));

            _cliSetupSection = new CliSetupSection(_root);
            _cliSetupSection.SetupBindings();
            _cliSetupSection.OnRefreshCliVersion += () => OnRefreshCliVersion?.Invoke();
            _cliSetupSection.OnInstallCli += () => OnInstallCli?.Invoke();
            _cliSetupSection.OnInstallSkills += () => OnInstallSkills?.Invoke();
            _cliSetupSection.OnRefreshSkillsState += () => OnRefreshSkillsState?.Invoke();
            _cliSetupSection.OnSkillsTargetChanged += value => OnSkillsTargetChanged?.Invoke(value);
            _cliSetupSection.OnGroupSkillsChanged += value => OnGroupSkillsChanged?.Invoke(value);

            _mainScrollView = _root.Q<ScrollView>("main-scroll-view");
            ConfigureScrollView();

            _connectedToolsSection = new ConnectedToolsSection(_root);
            _connectedToolsSection.OnFoldoutChanged += value => OnConnectedToolsFoldoutChanged?.Invoke(value);

            _toolSettingsSection = new ToolSettingsSection(_root);
            _toolSettingsSection.OnFoldoutChanged += value => OnToolSettingsFoldoutChanged?.Invoke(value);
            _toolSettingsSection.OnToolToggled += (toolName, enabled) => OnToolToggled?.Invoke(toolName, enabled);
            _toolSettingsSection.OnSecurityLevelChanged += value => OnSecurityLevelChanged?.Invoke(value);

            _githubLinkRow = _root.Q<VisualElement>("github-link-row");
            _githubLinkLabel = _root.Q<Label>("github-link-label");
            _githubLinkIcon = _root.Q<Image>("github-link-icon");
            Debug.Assert(_githubLinkRow != null, "github-link-row must not be null");
            Debug.Assert(_githubLinkLabel != null, "github-link-label must not be null");
            Debug.Assert(_githubLinkIcon != null, "github-link-icon must not be null");
            _githubLinkRow.RegisterCallback<ClickEvent>(_ => HandleOpenGitHub());
            _githubLinkRow.RegisterCallback<MouseEnterEvent>(_ => HandleGitHubHoverChanged(true));
            _githubLinkRow.RegisterCallback<MouseLeaveEvent>(_ => HandleGitHubHoverChanged(false));
            InitializeGitHubIcon();
        }

        private void ConfigureScrollView()
        {
            Debug.Assert(_mainScrollView != null, "mainScrollView must not be null");
            _mainScrollView.verticalScrollerVisibility = ScrollerVisibility.Auto;
            _mainScrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
        }

        private void InitializeGitHubIcon()
        {
            string iconPath = $"{McpConstants.PackageAssetPath}/{GITHUB_ICON_RELATIVE_PATH}";
            Texture2D iconTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath);
            Debug.Assert(iconTexture != null, $"GitHub icon not found at {iconPath}");
            _githubLinkIcon.image = iconTexture;
        }

        private static void HandleOpenGitHub()
        {
            Application.OpenURL(McpUIConstants.PROJECT_REPOSITORY_URL);
        }

        private void HandleGitHubHoverChanged(bool isHovered)
        {
            ViewDataBinder.ToggleClass(_githubLinkRow, "mcp-github-link--hover", isHovered);
            ViewDataBinder.ToggleClass(_githubLinkLabel, "mcp-github-link__label--hover", isHovered);
            ViewDataBinder.ToggleClass(_githubLinkIcon, "mcp-github-link__icon--hover", isHovered);
        }

        public void UpdateConnectedTools(ConnectedToolsData data)
        {
            _connectedToolsSection?.Update(data);
        }

        public void UpdateToolSettings(ToolSettingsSectionData data)
        {
            _toolSettingsSection?.Update(data);
        }

        public void UpdateSingleToolToggle(string toolName, bool enabled)
        {
            _toolSettingsSection?.UpdateSingleToggle(toolName, enabled);
        }

        public void UpdateConfigurationFoldout(bool show)
        {
            _configurationFoldout?.SetValueWithoutNotify(show);
        }

        public void UpdateCliSetup(CliSetupData data)
        {
            _cliSetupSection?.Update(data);
        }

        public void Dispose()
        {
            _cliSetupSection = null;
            _connectedToolsSection = null;
            _toolSettingsSection = null;
            _configurationFoldout = null;
        }
    }
}
